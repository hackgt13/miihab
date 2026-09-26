using System;
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
        GolfMotionClient motion;
        float retryPoseAt, retryMotionAt;

        /// <summary>The pose channel. Never null after Awake, and reconnected automatically.</summary>
        public LivePoseClient Pose => pose;
        /// <summary>The club-motion channel, on the other relay process.</summary>
        public GolfMotionClient Motion => motion;

        public bool PoseConnected => pose != null && pose.Connected;
        public bool MotionConnected => motion != null && motion.connected;
        public string PoseStatus => pose?.Status ?? "Pose bridge not started";
        /// <summary>How many times each channel has had to reconnect. An activity reads the delta
        /// across its own session to report tracking quality, rather than owning the socket to count.</summary>
        public int PoseReconnects { get; private set; }
        public int MotionReconnects { get; private set; }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            pose = new LivePoseClient(DefaultPoseUrl);
            motion = new GolfMotionClient(DefaultMotionUrl);
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
            if (!MotionConnected && Time.unscaledTime > retryMotionAt)
            {
                motion?.Dispose();
                motion = new GolfMotionClient(DefaultMotionUrl);
                retryMotionAt = Time.unscaledTime + RetrySeconds;
                MotionReconnects++;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            pose?.Dispose();
            motion?.Dispose();
        }
    }
}
