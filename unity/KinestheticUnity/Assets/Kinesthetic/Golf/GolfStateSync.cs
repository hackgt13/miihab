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
        readonly bool followDiscovery;
        string lastTarget, lastFailure;
        int attempts;
        static string Redact(string url) { if (url == null) return ""; var i = url.IndexOf("token="); return i < 0 ? url : url.Substring(0, i) + "token=…"; }
        public LatestSocket(string url) { _ = Task.Run(() => Run(() => url)); }
        /// `url` (QuestHostConfig.Url) is asked again before every connection attempt, and a connected socket moves
        /// when the relay announces itself at a different address (the Mac changed networks).
        public LatestSocket(Func<string> url) { followDiscovery = true; _ = Task.Run(() => Run(url)); }
        public void Send(string text) { lock (gate) outgoing = text; }
        public bool Take(out string text) { lock (gate) { text = incoming; incoming = null; return text != null; } }
        async Task Run(Func<string> url)
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    var target = url(); lastTarget = target;
                    if (++attempts <= 12) Debug.LogWarning($"Relay attempt {attempts}: {Redact(target)}");
                    using var ws = new ClientWebSocket();
                    // An address that drops packets holds a connect for the TCP timeout (minutes), and on device the
                    // cancellation token does not interrupt it. Race it against 3 s and abort the socket on a loss.
                    using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token))
                    {
                        var connecting = ws.ConnectAsync(new Uri(target), attempt.Token);
                        if (await Task.WhenAny(connecting, Task.Delay(3000, cancel.Token)).ConfigureAwait(false) != connecting)
                        {
                            attempt.Cancel(); ws.Abort();
                            _ = connecting.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // observe the late failure
                            throw new TimeoutException("no answer in 3 s");
                        }
                        await connecting.ConfigureAwait(false);
                    }
                    connected = true;
                    Debug.LogWarning("Relay connected: " + Redact(target));
                    lastFailure = null;
                    var checkedAt = DateTime.UtcNow;
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
                        // The relay announced itself somewhere else (the Mac changed networks): move there.
                        if (followDiscovery && (DateTime.UtcNow - checkedAt).TotalSeconds > 1)
                        {
                            checkedAt = DateTime.UtcNow;
                            // A connection over the USB cable is kept; only a network connection follows the Mac.
                            var announced = RelayDiscovery.Host;
                            if (announced != null && !target.Contains("//" + QuestHostConfig.Loopback + ":") &&
                                !target.Contains("//" + announced + ":")) break;
                        }
                    }
                }
                catch (Exception e)
                {
                    // Once per distinct failure, so a headset that cannot reach the Mac says why.
                    var failure = Redact(lastTarget) + ": " + e.GetBaseException().GetType().Name + " " + e.GetBaseException().Message;
                    if (failure != lastFailure) { lastFailure = failure; Debug.LogWarning("Relay connection failed, " + failure); }
                }
                connected = false;
                try { await Task.Delay(1000, cancel.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }
        public void Dispose() => cancel.Cancel();
    }

}
