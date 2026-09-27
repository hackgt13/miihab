using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Golf
{
    /// Relay address and pairing token baked into a headset build by
    /// Kinesthetic → Quest → Write host config.
    ///
    /// A Resources asset rather than StreamingAssets on purpose: on Android, StreamingAssets lives
    /// inside the compressed APK, so Application.streamingAssetsPath is a jar: URL and File.Exists
    /// against it is always false. Reading it that way left the headset silently falling back to
    /// loopback and never finding the Mac.
    public sealed class QuestHostConfig : ScriptableObject
    {
        /// Path for Resources.Load; the asset lives at Assets/Kinesthetic/Golf/Resources/Golf/.
        public const string ResourcePath = "Golf/QuestHostConfig";

        public string host = "127.0.0.1";
        public int port = 8767;
        public string token = "";

        /// The relay URL for `path` (which may carry its own query), resolved at each connection attempt. Attempts
        /// alternate: the headset's own loopback first, which reaches the Mac over the USB cable after
        /// `adb reverse tcp:8767 tcp:8767` (and is refused at once when there is no cable); then the Mac on the
        /// network — the one that announced itself, else the address baked into the build. Venue Wi-Fi can pass
        /// the announcement broadcast yet drop every connection to the address it names, so an announcement is
        /// never trusted to the exclusion of the cable.
        public Func<string> Url(string path)
        {
            RelayDiscovery.Listen(token);
            var separator = path.Contains("?") ? "&" : "?";
            int attempt = 0;
            return () =>
            {
                var target = attempt++ % 2 == 0 ? Loopback : RelayDiscovery.Host ?? host;
                return $"ws://{target}:{port}{path}{separator}token={Uri.EscapeDataString(token ?? "")}";
            };
        }

        public const string Loopback = "127.0.0.1";
    }

    /// Hears the relay's once-a-second UDP announcement (coordinator/golf-relay.ts) and remembers the Mac that sent
    /// it. An announcement counts only if its proof is the HMAC of the sender's address under our pairing token,
    /// so another machine on the network cannot pose as the Mac and collect the token.
    public static class RelayDiscovery
    {
        public const int Port = 8768;
        static volatile string host;
        static int started;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject multicastLock;   // held for the app's life; Android may drop broadcasts without it
#endif
        public static string Host => host;

        public static void Listen(string token)
        {
            if (string.IsNullOrEmpty(token) || Interlocked.Exchange(ref started, 1) == 1) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "miihab-relay");
                multicastLock.Call("acquire");
            }
            catch (Exception e) { Debug.LogWarning("Relay discovery: no multicast lock (" + e.Message + ")"); }
#endif
            var key = Encoding.UTF8.GetBytes(token);
            new Thread(() =>
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                    using var hmac = new HMACSHA256(key);
                    while (true)
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var bytes = udp.Receive(ref from);
                        try
                        {
                            var m = JObject.Parse(Encoding.UTF8.GetString(bytes));
                            var sender = from.Address.ToString();
                            if ((string)m["service"] != "miihab-relay") continue;
                            var expected = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(sender))).Replace("-", "").ToLowerInvariant();
                            if ((string)m["proof"] != expected) continue;
                            if (host != sender) Debug.Log("Relay discovery: the Mac is at " + sender);
                            host = sender;
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception e) { Debug.LogWarning("Relay discovery stopped: " + e.Message); }
            }) { IsBackground = true, Name = "Relay discovery" }.Start();
        }
    }
}
