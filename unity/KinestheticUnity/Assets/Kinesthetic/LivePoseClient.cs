using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kinesthetic
{
    // Network work stays off Unity's main thread. One latest-message slot bounds latency.
    public sealed class LivePoseClient : IDisposable
    {
        readonly ClientWebSocket socket = new ClientWebSocket();
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly object gate = new object();
        string pending;
        long pendingTicks;
        string status = "Connecting to the local pose bridge…";
        bool connected;
        public bool Connected { get { lock (gate) return connected; } }
        public string Status { get { lock (gate) return status; } }
        public LivePoseClient(string address) { _ = Task.Run(() => Run(address)); }
        public static bool Fresh(long ticks) => ticks > 0 &&
            (Stopwatch.GetTimestamp() - ticks) / (double)Stopwatch.Frequency <= .25;
        public bool Take(out string text, out long ticks)
        {
            lock (gate) { text = pending; ticks = pendingTicks; pending = null; return text != null; }
        }
        async Task Run(string address)
        {
            try
            {
                await socket.ConnectAsync(new Uri(address), cancellation.Token).ConfigureAwait(false);
                lock (gate) { connected = true; status = "Connected · waiting for camera observations"; }
                var buffer = new byte[16 * 1024];
                while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token).ConfigureAwait(false);
                        if (received.MessageType == WebSocketMessageType.Close) return;
                        if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > 128 * 1024)
                            throw new InvalidDataException("Unexpected pose message.");
                        message.Write(buffer, 0, received.Count);
                    } while (!received.EndOfMessage);
                    var text = Encoding.UTF8.GetString(message.ToArray());
                    lock (gate) { pending = text; pendingTicks = Stopwatch.GetTimestamp(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { lock (gate) status = "Pose bridge disconnected: " + e.Message; }
            finally
            {
                lock (gate) { connected = false; pending = null; if (!status.StartsWith("Pose bridge disconnected")) status = "Pose bridge disconnected"; }
                socket.Dispose(); cancellation.Dispose();
            }
        }
        public void Dispose()
        {
            try { cancellation.Cancel(); socket.Abort(); } catch (ObjectDisposedException) { }
        }
    }
}
