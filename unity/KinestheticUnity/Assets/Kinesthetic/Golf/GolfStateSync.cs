using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Golf
{
    // Unity on the Mac is the only golf authority. It publishes what it rendered (ball, both avatars' bones,
    // clubs, turn/score) and headsets only reproduce it — they never simulate a competing shot.
    static class GolfStateFormat
    {
        public static List<Transform> Bones(PoseRig rig) => new(rig.avatar.GetComponentsInChildren<Transform>(true));
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public static void Vec(StringBuilder b, Vector3 v) => b.Append('[').Append(v.x.ToString("0.####", C)).Append(',').Append(v.y.ToString("0.####", C)).Append(',').Append(v.z.ToString("0.####", C)).Append(']');
        public static void Quat(StringBuilder b, Quaternion q) => b.Append('[').Append(q.x.ToString("0.####", C)).Append(',').Append(q.y.ToString("0.####", C)).Append(',').Append(q.z.ToString("0.####", C)).Append(',').Append(q.w.ToString("0.####", C)).Append(']');
        public static Vector3 V(JToken t) => new((float)t[0], (float)t[1], (float)t[2]);
        public static Quaternion Q(JToken t) => new((float)t[0], (float)t[1], (float)t[2], (float)t[3]);
    }

    // Keeps only the newest outgoing/incoming message; network work stays off the main thread.
    sealed class LatestSocket : IDisposable
    {
        readonly CancellationTokenSource cancel = new();
        readonly object gate = new();
        string outgoing, incoming;
        public volatile bool connected;
        public LatestSocket(string url) { _ = Task.Run(() => Run(url)); }
        public void Send(string text) { lock (gate) outgoing = text; }
        public bool Take(out string text) { lock (gate) { text = incoming; incoming = null; return text != null; } }
        async Task Run(string url)
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    await ws.ConnectAsync(new Uri(url), cancel.Token).ConfigureAwait(false); connected = true;
                    var receive = Task.Run(async () => {
                        var buffer = new byte[64 * 1024];
                        while (ws.State == WebSocketState.Open) {
                            using var m = new MemoryStream(); WebSocketReceiveResult r;
                            do { r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).ConfigureAwait(false);
                                 if (r.MessageType == WebSocketMessageType.Close) return; m.Write(buffer, 0, r.Count); } while (!r.EndOfMessage);
                            lock (gate) incoming = Encoding.UTF8.GetString(m.ToArray());
                        }
                    });
                    while (ws.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                    {
                        string text; lock (gate) { text = outgoing; outgoing = null; }
                        if (text != null) await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, cancel.Token).ConfigureAwait(false);
                        else await Task.Delay(5, cancel.Token).ConfigureAwait(false);
                        if (receive.IsCompleted) break;
                    }
                }
                catch (Exception) { }
                connected = false;
                try { await Task.Delay(1000, cancel.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }
        public void Dispose() => cancel.Cancel();
    }

}
