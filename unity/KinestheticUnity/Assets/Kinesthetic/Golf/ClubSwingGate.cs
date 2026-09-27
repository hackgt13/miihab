using UnityEngine;

namespace Kinesthetic.Golf
{
    // A return through the calibrated address band is a swing candidate.
    // It is not a measured clubhead position or physical impact.
    public sealed class ClubSwingGate
    {
        public bool Calibrated { get; private set; }
        public Quaternion Reference { get; private set; }
        public string Source { get; private set; }
        public string Stage { get; private set; } = "Calibrate club";
        Vector3 axis;
        bool backed, fired;
        float previous, peakSpeed;
        double lastTime = -1;
        public void Reset()
        {
            Calibrated = false; backed = false; fired = false; lastTime = -1;
            previous = 0; peakSpeed = 0; Stage = "Calibrate club";
        }
        public void Calibrate(Quaternion attitude, string source, double timestamp)
        {
            Reset(); Reference = attitude.normalized; Source = source;
            Calibrated = true; lastTime = timestamp; Stage = "Ready · take your swing";
        }
        public bool Sample(Quaternion attitude, Vector3 rate, string source, double timestamp, out float speed)
        {
            speed = 0;
            if (!Calibrated || fired) return false;
            if (source != Source) { Reset(); return false; }
            if (timestamp <= lastTime) return false;
            if (timestamp - lastTime > .25) { Reset(); return false; }
            lastTime = timestamp;
            var relative = Quaternion.Inverse(Reference) * attitude.normalized;
            relative.ToAngleAxis(out float angle, out var direction);
            if (angle > 180) { angle = 360-angle; direction = -direction; }
            if (!backed)
            {
                if (angle >= 12 && angle <= 150)   // a short backswing counts: this is rehab, not a tour swing
                { backed = true; axis = direction; previous = angle; peakSpeed = rate.magnitude; Stage = "Swing back through address"; }
                return false;
            }
            float signed = angle * Vector3.Dot(direction, axis);
            peakSpeed = Mathf.Max(peakSpeed, rate.magnitude);
            if (signed > 170 || signed < -70) { Reset(); return false; }
            bool crossing = previous > 6 && signed <= 6 && signed >= -40 && rate.magnitude >= .4f;
            previous = signed;
            if (!crossing) return false;
            speed = peakSpeed; fired = true; Stage = "Checking virtual contact";
            return true;
        }
    }
}
