using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kinesthetic.Golf
{
    [Serializable] public class ClubMotionPacket
    {
        public string type, playerId, sourceId, sessionId;
        public long sequence;
        public double sensorTime;
        public float[] quaternion, rotationRate;
    }
    public sealed class GolfMotionClient : IDisposable
    {
        readonly object gate = new();
        readonly Queue<(string, long)> queue = new();
        readonly CancellationTokenSource cancel = new();
        ClientWebSocket socket;
        public volatile bool connected;
        public GolfMotionClient(string url) { _ = Task.Run(() => Run(url)); }
        public bool Take(out string text, out long ticks)
        {
            lock(gate) { if(queue.Count == 0) {text=null;ticks=0;return false;} (text,ticks)=queue.Dequeue(); return true; }
        }
        async Task Run(string url)
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket(); socket=ws;
                    await ws.ConnectAsync(new Uri(url),cancel.Token).ConfigureAwait(false); connected=true;
                    byte[] buffer = new byte[4096];
                    while(ws.State==WebSocketState.Open && !cancel.IsCancellationRequested)
                    {
                        using var message=new MemoryStream(); WebSocketReceiveResult result;
                        do {
                            result=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),cancel.Token).ConfigureAwait(false);
                            if(result.MessageType!=WebSocketMessageType.Text || message.Length+result.Count>8192) throw new IOException("Invalid motion message");
                            message.Write(buffer,0,result.Count);
                        } while(!result.EndOfMessage);
                        lock(gate) { if(queue.Count>=128) queue.Clear(); queue.Enqueue((Encoding.UTF8.GetString(message.ToArray()),Stopwatch.GetTimestamp())); }
                    }
                } catch(Exception) { connected=false; }
                lock(gate) queue.Clear();
                try { await Task.Delay(1000,cancel.Token).ConfigureAwait(false); } catch(OperationCanceledException) { break; }
            }
        }
        public void Dispose() { cancel.Cancel(); socket?.Abort(); }
    }
}
