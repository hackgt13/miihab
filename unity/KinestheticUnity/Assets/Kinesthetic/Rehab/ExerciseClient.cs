using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kinesthetic.Rehab
{
    // Receives the coordinator's /exercise stream (samples, rep events, summaries) off the main thread.
    // Unlike pose, every event matters, so messages are queued rather than overwritten.
    public sealed class ExerciseClient : IDisposable
    {
        readonly object gate = new();
        readonly Queue<string> queue = new();
        readonly CancellationTokenSource cancel = new();
        ClientWebSocket socket;
        public volatile bool connected;
        public ExerciseClient(string url) { _ = Task.Run(() => Run(url)); }
        public bool Take(out string text) { lock (gate) return queue.TryDequeue(out text); }
        async Task Run(string url)
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket(); socket = ws;
                    await ws.ConnectAsync(new Uri(url), cancel.Token).ConfigureAwait(false); connected = true;
                    var buffer = new byte[16 * 1024];
                    while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                    {
                        using var message = new MemoryStream(); WebSocketReceiveResult result;
                        do {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("closed");
                            if (message.Length + result.Count > 256 * 1024) throw new IOException("Exercise message too large");
                            message.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);
                        lock (gate) { if (queue.Count >= 2048) queue.Dequeue(); queue.Enqueue(Encoding.UTF8.GetString(message.ToArray())); }
                    }
                }
                catch (Exception) { connected = false; }
                try { await Task.Delay(1000, cancel.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }
        public void Dispose() { cancel.Cancel(); socket?.Abort(); }
    }
}
