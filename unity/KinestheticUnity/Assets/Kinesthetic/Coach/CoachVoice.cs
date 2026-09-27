using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Coach
{
    /// The coach's voice: Alex, the ElevenLabs voice PT behind voice/server.ts. While a rehab session runs, the
    /// Mac's microphone streams to it and Alex's replies play from the coach, whose mouth opens with the sound.
    /// Alex reads the plan and the measured reps through its tools; it never changes either.
    ///
    /// Half duplex on purpose: while Alex is speaking the microphone sends silence, so the coach does not hear
    /// itself through the speakers and interrupt its own sentence.
    [DefaultExecutionOrder(100)]   // after CoachDemonstrator poses the head, so the talking nod adds to it
    public sealed class CoachVoice : MonoBehaviour
    {
        public string url = "ws://127.0.0.1:8769/voice";
        public static CoachVoice Instance { get; private set; }

        /// Alex's latest sentence, for subtitles; empty when nothing was said recently.
        public string Line => Time.unscaledTime - lineAt < 7 ? line : "";
        public bool Connected { get; private set; }
        public bool Speaking => level > .03f;
        /// Connected, with a microphone, and not talking: what the patient says now reaches Alex.
        public bool Listening => Connected && mic && !Speaking && Time.unscaledTime - lastSpokeAt >= .4f;

        ClientWebSocket socket; CancellationTokenSource cancel;
        readonly ConcurrentQueue<string> inbox = new();
        readonly SemaphoreSlim sendGate = new(1, 1);
        string line = ""; float lineAt = -99;

        // Playback: a ring buffer the audio thread drains into a streaming clip.
        readonly object ringGate = new();
        float[] ring = new float[16000 * 40]; int readAt, writeAt, queued;
        int outputRate = 16000, inputRate = 16000;
        AudioSource source;
        volatile float outputRms; float level, lastSpokeAt = -99;

        // Microphone
        AudioClip mic; int micRead; string micDevice; float[] micChunk = new float[0];
        readonly System.Collections.Generic.List<float> micPending = new();

        Kinesthetic.Golf.MiiIdleLife miiLife;
        Transform head, mouthLocator, mouth;
        Vector3 mouthInHead; Quaternion mouthFacingInHead; bool mouthPlaced;

        void Awake() => Instance = this;

        void Start()
        {
            var bones = GetComponentsInChildren<Transform>(true);
            head = Array.Find(bones, t => t.name == "head");
            mouthLocator = Array.Find(bones, t => t.name == "mouth");
            miiLife = GetComponentInChildren<Kinesthetic.Golf.MiiIdleLife>();
            if (!miiLife) BuildMouth();
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = .35f; source.loop = true; source.playOnAwake = false;
        }

        /// Opens a conversation with Alex. `patientId` is stable per patient so Alex remembers them.
        /// Optional `mode` overrides the agent behaviour (e.g. "tutorial").
        public async void Begin(string patientId, string mode = null)
        {
            if (socket != null) return;

            // The voice server listens on the Mac only (it holds the ElevenLabs key), so the Mac connects to
            // localhost. A headset reaches it over the USB cable: `adb reverse tcp:8769 tcp:8769` makes the
            // headset's own loopback the Mac, as the relay's does for 8767.
            var connectUrl = url;

            cancel = new CancellationTokenSource();
            socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(new Uri(connectUrl), cancel.Token);
                var msg = new JObject { ["type"] = "session_start", ["patient_id"] = patientId };
                if (!string.IsNullOrEmpty(mode)) msg["mode"] = mode;
                await Send(msg.ToString());
                _ = Task.Run(Receive);
            }
            catch (Exception e) { Debug.LogWarning("Coach voice unavailable: " + e.Message); Close(); }
        }

        /// Tell Alex what just happened in the studio (a rep, a set, a plan change) without interrupting him. Sent as an
        /// ElevenLabs contextual update; notes made before the conversation opens wait for it.
        /// With `speak`, Alex answers it now (a rep that did not count, a set ending); otherwise he only knows it.
        public void Context(string text, bool speak = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (Connected && socket?.State == WebSocketState.Open) _ = Send(new JObject { ["type"] = "context", ["text"] = text, ["speak"] = speak }.ToString());
            else { pendingContext.Enqueue((text, speak)); while (pendingContext.Count > 6) pendingContext.Dequeue(); }
        }
        readonly System.Collections.Generic.Queue<(string text, bool speak)> pendingContext = new();

        /// Tells the server to speak a scripted line (tutorial mode).
        public void Cue(string step)
        {
            if (socket?.State == WebSocketState.Open)
                _ = Send(new JObject { ["type"] = "cue", ["step"] = step }.ToString());
        }

        public async void End()
        {
            if (socket == null) return;
            try { if (socket.State == WebSocketState.Open) await Send("{\"type\":\"session_end\"}"); } catch (Exception) { }
            Close();
        }

        void Close()
        {
            Connected = false;
            if (mic) { Microphone.End(micDevice); mic = null; }
            cancel?.Cancel();
            try { socket?.Abort(); } catch (Exception) { }
            socket?.Dispose(); socket = null;
            lock (ringGate) { readAt = writeAt = queued = 0; }
        }

        async Task Send(string text)
        {
            await sendGate.WaitAsync();
            try { await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, cancel.Token); }
            finally { sendGate.Release(); }
        }

        async Task Receive()
        {
            var buffer = new byte[1 << 16]; var message = new StringBuilder();
            try
            {
                while (socket != null && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage) { inbox.Enqueue(message.ToString()); message.Clear(); }
                }
            }
            catch (Exception) { }
            inbox.Enqueue("{\"type\":\"closed\"}");
        }

        void Update()
        {
            while (inbox.TryDequeue(out var text))
            {
                JObject m; try { m = JObject.Parse(text); } catch (Exception) { continue; }
                switch ((string)m["type"])
                {
                    case "session_started":
                        Connected = true; StartMicrophone(); StartPlayback();
                        while (pendingContext.Count > 0) { var (note, speak) = pendingContext.Dequeue(); Context(note, speak); }
                        break;
                    case "audio_format":
                        outputRate = Rate((string)m["output"], outputRate); inputRate = Rate((string)m["input"], inputRate);
                        if (Connected) { StartPlayback(); StartMicrophone(); }
                        break;
                    case "audio": Enqueue(Convert.FromBase64String((string)m["data"] ?? "")); break;
                    case "interrupt": lock (ringGate) { readAt = writeAt = queued = 0; } break;
                    case "transcript":
                        if ((string)m["role"] == "agent") { line = (string)m["text"] ?? ""; lineAt = Time.unscaledTime; }
                        break;
                    case "error": Debug.LogWarning("Coach voice: " + (string)m["message"]); break;
                    case "closed": if (socket != null) Close(); break;
                }
            }
            if (Connected) StreamMicrophone();
        }

        // "pcm_16000" → 16000. Anything else (e.g. ulaw) keeps the current rate.
        static int Rate(string format, int fallback) =>
            format != null && format.StartsWith("pcm_") && int.TryParse(format.Substring(4), out var hz) ? hz : fallback;

        void StartPlayback()
        {
            source.Stop();
            source.clip = AudioClip.Create("Alex", outputRate * 2, 1, outputRate, true, OnAudioRead);
            source.Play();
        }

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

        // Audio thread: drain the ring, pad with silence, and measure loudness for the mouth.
        void OnAudioRead(float[] data)
        {
            double sum = 0;
            lock (ringGate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    float v = 0;
                    if (queued > 0) { v = ring[readAt]; readAt = (readAt + 1) % ring.Length; queued--; }
                    data[i] = v; sum += v * v;
                }
            }
            outputRms = (float)Math.Sqrt(sum / Math.Max(1, data.Length));
        }

        void StartMicrophone()
        {
            if (mic) Microphone.End(micDevice);
            micDevice = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
            if (micDevice == null) { Debug.LogWarning("Coach voice: no microphone; Alex can speak but not hear."); return; }
            mic = Microphone.Start(micDevice, true, 2, inputRate);
            micRead = 0; micPending.Clear();
        }

        void StreamMicrophone()
        {
            if (!mic) return;
            int at = Microphone.GetPosition(micDevice);
            int available = (at - micRead + mic.samples) % mic.samples;
            if (available <= 0) return;
            if (micChunk.Length != available) micChunk = new float[available];
            int first = Mathf.Min(available, mic.samples - micRead);   // the clip loops; read up to its end, then from its start
            var head0 = new float[first]; mic.GetData(head0, micRead); Array.Copy(head0, micChunk, first);
            if (available > first) { var tail = new float[available - first]; mic.GetData(tail, 0); Array.Copy(tail, 0, micChunk, first, tail.Length); }
            micRead = at;
            // Half duplex: while Alex is talking (and just after), send silence so the speakers are not heard as the patient.
            bool muted = Time.unscaledTime - lastSpokeAt < .4f;
            foreach (var v in micChunk) micPending.Add(muted ? 0 : v);
            int chunk = inputRate / 10;   // 100 ms
            while (micPending.Count >= chunk)
            {
                var bytes = new byte[chunk * 2];
                for (int i = 0; i < chunk; i++)
                {
                    short s = (short)Mathf.Clamp(Mathf.RoundToInt(micPending[i] * 32767f), short.MinValue, short.MaxValue);
                    bytes[2 * i] = (byte)s; bytes[2 * i + 1] = (byte)(s >> 8);
                }
                micPending.RemoveRange(0, chunk);
                if (socket?.State == WebSocketState.Open)
                    _ = Send(new JObject { ["type"] = "audio", ["data"] = Convert.ToBase64String(bytes) }.ToString());
            }
        }

        // ------------------------------------------------------------------ mouth
        // The trainer model has no jaw bone or mouth shapes, only a mouth locator, so the open mouth is a small
        // dark shape at that point that grows with the voice and disappears when Alex is quiet.
        void BuildMouth()
        {
            if (!mouthLocator) return;
            var shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shape.name = "Talking mouth";
            Destroy(shape.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(.28f, .09f, .08f) };
            shape.GetComponent<Renderer>().sharedMaterial = material;
            shape.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mouth = shape.transform;
            mouth.gameObject.SetActive(false);
            // The locator sits inside the head. Find the real face surface in front of it with a ray against the face
            // mesh (a temporary collider), and keep that spot relative to the head so it follows nods.
            if (!head) return;
            var forward = transform.forward;
            foreach (var filter in head.GetComponentsInChildren<MeshFilter>(true))
            {
                var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
                Physics.SyncTransforms();
                bool hit = collider.Raycast(new Ray(mouthLocator.position + forward * .3f, -forward), out var surface, .6f);
                Destroy(collider);
                if (!hit) continue;
                mouthInHead = head.InverseTransformPoint(surface.point + surface.normal * .005f);
                mouthFacingInHead = Quaternion.Inverse(head.rotation) * Quaternion.LookRotation(surface.normal, Vector3.up);
                mouthPlaced = true;
                break;
            }
        }

        void LateUpdate()
        {
            float target = Mathf.Clamp01(outputRms * 7f);
            level = Mathf.Lerp(level, target, 1 - Mathf.Exp(-(target > level ? 30f : 12f) * Time.deltaTime));
            if (level > .03f) { lastSpokeAt = Time.unscaledTime; if (miiLife) miiLife.Surprise(.12f); }
            if (mouth && mouthLocator && head)
            {
                bool open = level > .03f;
                mouth.gameObject.SetActive(open);
                if (open)
                {
                    // Sit on the face surface, facing where the head faces; open taller with louder speech.
                    if (mouthPlaced) mouth.SetPositionAndRotation(head.TransformPoint(mouthInHead), head.rotation * mouthFacingInHead);
                    else mouth.SetPositionAndRotation(mouthLocator.position + transform.forward * .05f, Quaternion.LookRotation(transform.forward, Vector3.up));
                    float s = transform.lossyScale.y;
                    mouth.localScale = new Vector3(.04f, .008f + .045f * level, .014f) * s;
                }
            }
            // A small nod while talking, on top of whatever the demonstrator did with the head this frame.
            if (head && level > .03f) head.rotation = Quaternion.AngleAxis(Mathf.Sin(Time.time * 9f) * 2.2f * level, transform.right) * head.rotation;
        }

        void OnDestroy() { Close(); if (Instance == this) Instance = null; }
    }
}
