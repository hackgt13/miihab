using System;
using System.Collections.Generic;
using UnityEngine;
using Kinesthetic.Golf;

namespace Kinesthetic.Activities
{
    /// <summary>
    /// One connection per sensor channel, for the lifetime of the app.
    ///
    /// Every activity used to construct its own LivePoseClient in Start() and its own reconnect loop,
    /// so each navigation tore down and re-established every channel, and the same forty-odd lines of
    /// reconnect were copied into each activity. The camera and the AirPods are attached to this
    /// machine either way; nothing about walking between scenes should disturb them.
    ///
    /// Holding the connections here also lets the menu show sensor status *before* entering an
    /// activity, which is what makes gallery gating honest rather than a guess.
    ///
    /// It spans two separate server processes. Pose is the bridge on 8766; club motion is the relay on
    /// 8767, which is a different process with its own lifecycle, so the two channels fail and recover
    /// independently and neither waits for the other.
    ///
    /// Take() consumes the pending message, so a channel has one reader: the activity that is loaded.
    /// The menu reads only connection state, never the payload.
    /// </summary>
    public sealed class SensorHub : MonoBehaviour
    {
        public const string DefaultPoseUrl = "ws://127.0.0.1:8766/pose?role=viewer";
        public const string DefaultMotionUrl = "ws://127.0.0.1:8767/golf?role=viewer";
        const float RetrySeconds = 3;

        public static SensorHub Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        /// <summary>Get the hub, creating it on first use. Survives scene loads.</summary>
        public static SensorHub Ensure()
        {
            if (Instance) return Instance;
            var host = new GameObject("Sensor hub");
            var hub = host.AddComponent<SensorHub>();
            return Instance = hub;
        }

        LivePoseClient pose;
        // Motion channels are per-URL because each game has its own: golf swings arrive on /golf and
        // bowling rolls on /bowling-motion, separate channels on the same relay process.
        readonly Dictionary<string, GolfMotionClient> motions = new();
        readonly Dictionary<string, float> motionRetryAt = new();
        readonly Dictionary<string, int> motionReconnects = new();
        float retryPoseAt;

        /// <summary>The pose channel. Never null after Awake, and reconnected automatically.</summary>
        public LivePoseClient Pose => pose;
        /// <summary>The club-motion channel, on the other relay process.</summary>
        public GolfMotionClient Motion => MotionFor(DefaultMotionUrl);

        /// <summary>
        /// The motion channel at this URL, opened on first ask and kept alive from then on. An
        /// activity names the channel it needs; the hub owns the socket and the reconnecting.
        /// </summary>
        public GolfMotionClient MotionFor(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (!motions.TryGetValue(url, out var client))
            {
                motions[url] = client = new GolfMotionClient(url);
                // A new socket gets its full retry window to connect; with the clock at 0 it was replaced (and
                // counted as a reconnect, i.e. tracking loss in the record) on the very next frame.
                motionRetryAt[url] = Time.unscaledTime + RetrySeconds;
            }
            return client;
        }
        public int MotionReconnectsFor(string url) => motionReconnects.TryGetValue(url, out var n) ? n : 0;

        public bool PoseConnected => pose != null && pose.Connected;
        /// <summary>How many times each channel has had to reconnect. An activity reads the delta
        /// across its own session to report tracking quality, rather than owning the socket to count.</summary>
        public int PoseReconnects { get; private set; }
        public int MotionReconnects => MotionReconnectsFor(DefaultMotionUrl);

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            pose = new LivePoseClient(DefaultPoseUrl);
            retryPoseAt = Time.unscaledTime + RetrySeconds;   // see MotionFor: time to connect before it counts as lost
        }

        void Update()
        {
            // The one reconnect loop in the app. Each channel retries on its own clock, because the
            // two relays are separate processes and one being down says nothing about the other.
            if (!PoseConnected && Time.unscaledTime > retryPoseAt)
            {
                pose?.Dispose();
                pose = new LivePoseClient(DefaultPoseUrl);
                retryPoseAt = Time.unscaledTime + RetrySeconds;
                PoseReconnects++;
            }
            // Every motion channel anyone has asked for, each on its own retry clock.
            foreach (var url in new List<string>(motions.Keys))
            {
                var client = motions[url];
                if (client != null && client.connected) continue;
                motionRetryAt.TryGetValue(url, out var due);
                if (Time.unscaledTime <= due) continue;
                client?.Dispose();
                motions[url] = new GolfMotionClient(url);
                motionRetryAt[url] = Time.unscaledTime + RetrySeconds;
                motionReconnects[url] = MotionReconnectsFor(url) + 1;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            pose?.Dispose();
            foreach (var client in motions.Values) client?.Dispose();
            motions.Clear();
        }
    }
}
