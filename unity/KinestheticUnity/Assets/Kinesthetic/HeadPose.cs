using System;
using System.Globalization;
using System.Text;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace Kinesthetic
{
    /// The headset is the third sensor beside the two AirPods: where the patient's head is and which way it faces,
    /// with no controllers. It sends its pose to the Mac as an offset from the seated eye point, in the seat's own
    /// frame (x right, y up, z forward), so "6 cm forward, 2 cm left" means the same in every scene. The Mac leans
    /// the torso and turns the head with it (PoseRig.ApplyHeadPose) and sends the finished body back.
    public sealed class HeadPoseSender : MonoBehaviour
    {
        public Transform seat, head;
        public float eyeHeight = 1.15f;
        const float Interval = 1f / 30;
        LatestSocket socket; float nextAt; long seq;

        void Start()
        {
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            if (Application.isEditor || !config || string.IsNullOrEmpty(config.token)) { enabled = false; return; }
            socket = new LatestSocket(config.Url("/head?role=producer"));
        }

        void LateUpdate()
        {
            if (socket == null || !seat || !head || !XRSettings.isDeviceActive || Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + Interval;
            var flat = Vector3.ProjectOnPlane(seat.forward, Vector3.up);
            var frame = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
            var toSeat = Quaternion.Inverse(frame);
            var p = toSeat * (head.position - (seat.position + Vector3.up * eyeHeight));
            var q = toSeat * head.rotation;
            var c = CultureInfo.InvariantCulture;
            socket.Send(new StringBuilder(160).Append("{\"type\":\"head.pose\",\"seq\":").Append(++seq)
                .Append(",\"p\":[").Append(p.x.ToString("0.####", c)).Append(',').Append(p.y.ToString("0.####", c)).Append(',').Append(p.z.ToString("0.####", c))
                .Append("],\"q\":[").Append(q.x.ToString("0.#####", c)).Append(',').Append(q.y.ToString("0.#####", c)).Append(',')
                .Append(q.z.ToString("0.#####", c)).Append(',').Append(q.w.ToString("0.#####", c)).Append("]}").ToString());
        }

        void OnDestroy() => socket?.Dispose();
    }

    /// The Mac's reading of the headset's head pose, from the relay's /head channel. Fresh for half a second after
    /// the last pose; stale means no headset, and the body sits upright as before.
    public sealed class HeadPoseFeed : MonoBehaviour
    {
        public const string Url = "ws://127.0.0.1:8767/head?role=viewer";
        public static HeadPoseFeed Instance { get; private set; }
        GolfMotionClient client;
        Vector3 offset; Quaternion rotation = Quaternion.identity; float at = -99;

        public static HeadPoseFeed Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("Head pose feed"); DontDestroyOnLoad(go);
            return go.AddComponent<HeadPoseFeed>();
        }

        void Awake() { Instance = this; client = new GolfMotionClient(Url); }

        void Update()
        {
            while (client.Take(out var text, out _))
            {
                try
                {
                    var m = JObject.Parse(text);
                    if ((string)m["type"] == "head.disconnected") { at = -99; continue; }
                    if ((string)m["type"] != "head.pose" || m["p"] is not JArray p || m["q"] is not JArray q) continue;
                    offset = new Vector3((float)p[0], (float)p[1], (float)p[2]);
                    rotation = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
                    at = Time.unscaledTime;
                }
                catch (Exception) { }
            }
        }

        /// The head's offset from the seated eye point and its orientation, in the seat's frame, if a headset is live.
        public bool TryGet(out Vector3 headOffset, out Quaternion headRotation)
        {
            headOffset = offset; headRotation = rotation;
            return Time.unscaledTime - at < .5f;
        }

        void OnDestroy() { client?.Dispose(); if (Instance == this) Instance = null; }
    }
}
