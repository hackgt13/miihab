using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Coach
{
    /// Headset: Alex's voice, heard from the coach inside the headset.
    ///
    /// The conversation is the Mac's (CoachVoice: its microphone, its one ElevenLabs session). This only listens: it
    /// opens the voice server's listen-only socket (voice/backend/listeners.ts) and plays what Alex says from the
    /// coach's head, through the Quest's own speakers. While it is connected the Mac goes quiet, so Alex is heard
    /// once — not also from AirPods strapped to the patient's wrist.
    ///
    /// The voice server holds the ElevenLabs key and listens on the Mac's loopback only, so the headset reaches it
    /// the one way it can: over the USB cable, where `adb reverse tcp:8769 tcp:8769` (start_tunnel.sh) makes the
    /// headset's own loopback the Mac. Off the cable it keeps retrying quietly and the Mac keeps speaking.
    public sealed class CoachVoiceListener : MonoBehaviour
    {
        public const string Url = "ws://127.0.0.1:8769/voice?role=listen";

        /// Adds the listener to a coach (once), sounding from its head bone when it has one.
        public static CoachVoiceListener Attach(Transform coach)
        {
            if (!coach) return null;
            var existing = coach.GetComponentInChildren<CoachVoiceListener>(true);
            if (existing) return existing;
            var head = coach.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head") ?? coach;
            return head.gameObject.AddComponent<CoachVoiceListener>();
        }

        public bool Connected { get; private set; }

        readonly ConcurrentQueue<string> inbox = new();
        CancellationTokenSource cancel;

        // Playback: a ring buffer the audio thread drains into a streaming clip, as CoachVoice does on the Mac.
        readonly object ringGate = new();
        readonly float[] ring = new float[16000 * 40]; int readAt, writeAt, queued;
        int outputRate = 16000;
        AudioSource source;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            // Mostly from the coach, not all: a fully spatial voice drops away when the patient turns to the mirror.
            source.spatialBlend = .5f; source.loop = true; source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 2; source.maxDistance = 20;
            StartPlayback();
        }

        void OnEnable() { cancel = new CancellationTokenSource(); _ = Run(cancel.Token); }

        void OnDisable() { cancel?.Cancel(); cancel = null; Connected = false; }

        async Task Run(CancellationToken stop)
        {
            var buffer = new byte[1 << 16];
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    await ws.ConnectAsync(new Uri(Url), stop);
                    inbox.Enqueue("{\"type\":\"connected\"}");
                    var message = new StringBuilder();
                    while (ws.State == WebSocketState.Open && !stop.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stop);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (result.EndOfMessage) { inbox.Enqueue(message.ToString()); message.Clear(); }
                    }
                }
                catch (Exception) { /* no cable, or the voice server is restarting: try again shortly */ }
                inbox.Enqueue("{\"type\":\"closed\"}");
                try { await Task.Delay(2000, stop); } catch (Exception) { return; }
            }
        }

        void Update()
        {
            while (inbox.TryDequeue(out var text))
            {
                JObject m; try { m = JObject.Parse(text); } catch (Exception) { continue; }
                switch ((string)m["type"])
                {
                    case "connected": Connected = true; break;
                    case "closed": Connected = false; Clear(); break;
                    case "audio_format":
                        var rate = Rate((string)m["output"], outputRate);
                        if (rate != outputRate) { outputRate = rate; StartPlayback(); }
                        break;
                    case "audio": Enqueue(Convert.FromBase64String((string)m["data"] ?? "")); break;
                    case "interrupt": Clear(); break;
                }
            }
        }

        static int Rate(string format, int fallback) =>
            format != null && format.StartsWith("pcm_") && int.TryParse(format.Substring(4), out var hz) ? hz : fallback;

        void StartPlayback()
        {
            source.Stop();
            source.clip = AudioClip.Create("Alex (headset)", outputRate * 2, 1, outputRate, true, OnAudioRead);
            source.Play();
        }

        void Clear() { lock (ringGate) { readAt = writeAt = queued = 0; } }

        void Enqueue(byte[] pcm)
        {
            lock (ringGate)
            {
                for (int i = 0; i + 1 < pcm.Length; i += 2)
                {
                    if (queued == ring.Length) { readAt = (readAt + 1) % ring.Length; queued--; }   // drop the oldest, never block
                    ring[writeAt] = (short)(pcm[i] | pcm[i + 1] << 8) / 32768f;
                    writeAt = (writeAt + 1) % ring.Length; queued++;
                }
            }
        }

        // Audio thread: drain the ring and pad with silence.
        void OnAudioRead(float[] data)
        {
            lock (ringGate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (queued > 0) { data[i] = ring[readAt]; readAt = (readAt + 1) % ring.Length; queued--; }
                    else data[i] = 0;
                }
            }
        }

        void OnDestroy() { cancel?.Cancel(); }
    }
}
