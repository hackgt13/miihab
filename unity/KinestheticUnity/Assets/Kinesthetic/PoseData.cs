using System;
using Newtonsoft.Json;
using UnityEngine;

namespace Kinesthetic
{
    [Serializable] public class PosePoint
    {
        public int index;
        public float x = float.NaN, y = float.NaN, z = float.NaN, visibility = float.NaN;
    }
    [Serializable] public class PoseFrame
    {
        public int frameID;
        public string source;
        public double sourceMediaTimeMs, observedAtMonotonicMs, inferenceDurationMs;
        public bool subjectDetected;
        public PosePoint[] imageLandmarks, worldLandmarks;
    }
    [Serializable] public class PoseEnvelope
    {
        public string schemaVersion, type, sessionId;
        public long sequence;
        public PoseFrame payload;
    }
    public static class PoseJson
    {
        // Null/missing coordinates must stay invalid instead of becoming a plausible zero.
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
            NullValueHandling = NullValueHandling.Ignore, MaxDepth = 32 };
        public static T Read<T>(string text) => JsonConvert.DeserializeObject<T>(text, Settings);
    }
    [Serializable] public class PoseCapture
    {
        public string schemaVersion, sessionID;
        public PoseFrame[] frames;
    }

    public static class PoseMath
    {
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Visible(PoseFrame frame, int index)
        {
            if (frame == null || !frame.subjectDetected || frame.imageLandmarks == null ||
                frame.worldLandmarks == null || index >= frame.imageLandmarks.Length || index >= frame.worldLandmarks.Length) return false;
            var p = frame.imageLandmarks[index];
            var w = frame.worldLandmarks[index];
            return p != null && w != null && Finite(p.x) && Finite(p.y) && Finite(p.visibility) &&
                p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1 && p.visibility >= .5f &&
                Finite(w.x) && Finite(w.y) && Finite(w.z);
        }
        // Initial camera-view basis: image right +X, up +Y, away from camera +Z.
        // A human side/depth check is still required before clinical interpretation.
        public static Vector3 Position(PoseFrame frame, int index)
        {
            var p = frame.worldLandmarks[index];
            return new Vector3(p.x, -p.y, p.z);
        }
        public static bool Direction(PoseFrame frame, int from, int to, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!Visible(frame, from) || !Visible(frame, to)) return false;
            direction = Position(frame, to) - Position(frame, from);
            return direction.sqrMagnitude > 1e-8f;
        }
        public static bool Shoulder(PoseFrame frame, bool left, out float degrees)
        {
            degrees = 0;
            int shoulder = left ? 11 : 12, elbow = left ? 13 : 14, hip = left ? 23 : 24;
            if (!Direction(frame, shoulder, elbow, out var arm) || !Direction(frame, shoulder, hip, out var down)) return false;
            degrees = Vector3.Angle(down, arm);
            return Finite(degrees);
        }
        public static bool Elbow(PoseFrame frame, bool left, out float degrees)
        {
            degrees = 0;
            int shoulder = left ? 11 : 12, elbow = left ? 13 : 14, wrist = left ? 15 : 16;
            if (!Direction(frame, elbow, shoulder, out var upper) || !Direction(frame, elbow, wrist, out var forearm)) return false;
            if (!PlausibleArm(upper,forearm)) return false;
            degrees = 180 - Vector3.Angle(upper, forearm);
            return Finite(degrees);
        }
        public static bool PlausibleArm(Vector3 upper, Vector3 forearm)
        {
            // Broad geometry sanity check, not an anatomical measurement.
            float ratio=forearm.magnitude/Mathf.Max(upper.magnitude,1e-6f);
            return ratio>=.35f && ratio<=2f;
        }
    }
}
