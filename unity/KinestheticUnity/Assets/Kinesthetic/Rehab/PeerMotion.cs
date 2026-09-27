using System;
using UnityEngine;
using Kinesthetic.Activities;
using Kinesthetic.Golf;

namespace Kinesthetic.Rehab
{
    /// <summary>
    /// How the other person in a group session is moving, as an angle for their Mii to show.
    ///
    /// Live: the second Mac's AirPods, which the relay hands over as `friend` on /golf while the patient is in a
    /// group (golf-relay.ts `route`). The angle is the same yaw-free tilt the coordinator measures the patient with
    /// (exercise/imu-tilt.ts): the angle between the world vertical in the AirPod's frame now and at rest, rest
    /// being the first second it was held still. It is presentation only — nobody scores it, and nothing here is
    /// sent anywhere. The patient's own readers all filter to `patient`, so this stream never reaches their record.
    ///
    /// Fake: a sample person with no live stream raises in a steady, human-paced loop toward the plan's target,
    /// each one offset by their Mii so two fakes are never in step. They are labelled sample wherever they appear.
    /// </summary>
    public sealed class PeerMotion
    {
        // Its own reader: a relay channel is consumed by whoever takes from it (SensorHub), and the patient's
        // watch reads the plain /golf viewer. The relay ignores the extra parameter.
        const string Url = "ws://127.0.0.1:8767/golf?role=viewer&reader=peer";
        const float StillRadS = .35f, CalibrateSeconds = 1f;

        long ticks, sequence = -1;
        string session;
        Vector3 rest, vertical;
        bool calibrated;
        float stillSince = -1;

        /// The second Mac is streaming right now.
        public bool Live => Kinesthetic.LivePoseClient.Fresh(ticks);
        /// Live, but not yet held still long enough to know where rest is.
        public bool Calibrating => Live && !calibrated;

        public void Read()
        {
            var motion = SensorHub.Instance?.MotionFor(Url);
            while (motion != null && motion.Take(out var text, out var t))
            {
                ClubMotionPacket p;
                try { p = JsonUtility.FromJson<ClubMotionPacket>(text); } catch (Exception) { continue; }
                if (p == null || p.type != "club.motion" || p.playerId != "friend") continue;
                if (p.quaternion?.Length != 4 || p.rotationRate?.Length != 3) continue;
                if (p.sessionId != session) { session = p.sessionId; sequence = -1; calibrated = false; stillSince = -1; }
                if (p.sequence <= sequence) continue;
                sequence = p.sequence; ticks = t;
                vertical = VerticalInDevice(p.quaternion);
                float speed = new Vector3(p.rotationRate[0], p.rotationRate[1], p.rotationRate[2]).magnitude;
                if (!float.IsFinite(speed) || speed > StillRadS) stillSince = -1;
                else if (stillSince < 0) stillSince = Time.unscaledTime;
                if (!calibrated && stillSince >= 0 && Time.unscaledTime - stillSince >= CalibrateSeconds) { rest = vertical; calibrated = true; }
            }
        }

        /// The live angle in degrees, or null when there is no calibrated stream to read one from.
        public float? LiveAngle => Live && calibrated ? Vector3.Angle(rest, vertical) : null;

        /// A fake person's raise: up over 2 s, held 2 s, down over 3 s, rested 2.5 s — the tempo the plan coaches.
        public static float FakeAngle(float targetDeg, int seed, float time)
        {
            const float Raise = 2f, Hold = 2f, Lower = 3f, Rest = 2.5f, Cycle = Raise + Hold + Lower + Rest;
            float t = (time + seed * 2.3f) % Cycle;
            float top = targetDeg * (.92f + .06f * ((seed * 7) % 3));   // near the target, a little different each
            if (t < Raise) return top * Smooth(t / Raise);
            if ((t -= Raise) < Hold) return top;
            if ((t -= Hold) < Lower) return top * (1 - Smooth(t / Lower));
            return 0;
        }

        static float Smooth(float x) => x * x * (3 - 2 * x);

        /// The world's vertical in the device frame, for a CoreMotion attitude [x, y, z, w] — verticalInDevice in
        /// coordinator/exercise/imu-tilt.ts, so the partner's angle reads the way the patient's does.
        static Vector3 VerticalInDevice(float[] q)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            return new Vector3(2 * (x * z - w * y), 2 * (y * z + w * x), w * w - x * x - y * y + z * z).normalized;
        }
    }
}
