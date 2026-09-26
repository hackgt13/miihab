using UnityEngine;

namespace Kinesthetic.Bowling
{
    // Raw CoreMotion attitude is XYZW, with reference Z vertical. Only relative
    // attitude and angular speed are used. Heading is an estimate, not hand velocity.
    public sealed class BowlingSwing
    {
        Quaternion reference, stillReference;
        double last = -1, stillSince = -1, backSince;
        float previousAngle, maxAngle, peakSpeed;
        float olderSpeed, previousSpeed;
        bool backing, latched;
        public bool Calibrated { get; private set; }
        public float CalibrationProgress { get; private set; }
        public float Aim { get; private set; }
        public float Power { get; private set; }
        public float SwingAngle { get; private set; }
        // 0.8 rad/s is a valid gentle delivery. Full power is reachable at 4 rad/s;
        // stronger movement is capped, so there is no reward for a violent swing.
        public static float MapPower(float speed) => Mathf.InverseLerp(.8f, 4f, speed);
        public static float BallSpeed(float power) => Mathf.Lerp(4.2f, 9f, Mathf.Clamp01(power));
        public string Cue => !Calibrated ? "Face the pins. Hold your hand still." : backing ? "Now swing forward." : "Swing back, then forward.";
        public void Reset()
        {
            Calibrated = false; last = stillSince = -1; CalibrationProgress = Aim = Power = 0;
            Rearm();
        }
        public void Rearm() { backing = latched = false; maxAngle = peakSpeed = previousAngle = SwingAngle = olderSpeed = previousSpeed = 0; }

        public bool Sample(Quaternion attitude, Vector3 rate, double time, bool armed, out float aim, out float power)
        {
            aim = power = 0;
            if (time <= last) return false;
            if (last >= 0 && time - last > .25) Reset();
            last = time;
            attitude = attitude.normalized;
            float speed = rate.magnitude;
            // Three-sample median rejects a lone gyro spike without a long lag.
            float filteredSpeed = Mathf.Max(Mathf.Min(olderSpeed, previousSpeed), Mathf.Min(Mathf.Max(olderSpeed, previousSpeed), speed));
            olderSpeed = previousSpeed; previousSpeed = speed;
            if (!Calibrated)
            {
                if (speed > .22f || stillSince < 0 || Quaternion.Angle(stillReference, attitude) > 3)
                { stillSince = time; stillReference = attitude; }
                CalibrationProgress = Mathf.Clamp01((float)(time - stillSince));
                if (CalibrationProgress >= 1) { reference = attitude; Calibrated = true; Rearm(); }
                return false;
            }
            var delta = attitude * Quaternion.Inverse(reference);
            if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            float twistNorm = Mathf.Sqrt(delta.z * delta.z + delta.w * delta.w);
            if (twistNorm < .01f) { Rearm(); return false; }
            var twist = new Quaternion(0, 0, delta.z / twistNorm, delta.w / twistNorm);
            float heading = -Mathf.DeltaAngle(0, 2 * Mathf.Atan2(twist.z, twist.w) * Mathf.Rad2Deg);
            Aim = Mathf.Clamp(heading, -8, 8);
            float angle = Quaternion.Angle(Quaternion.identity, delta * Quaternion.Inverse(twist));
            SwingAngle = latched ? 0 : angle;
            Power = MapPower(filteredSpeed);
            if (!armed || latched) { previousAngle = angle; return false; }
            if (!backing && angle >= 18 && angle < 100 && speed > .35f)
            { backing = true; backSince = time; maxAngle = angle; peakSpeed = 0; }
            if (backing)
            {
                maxAngle = Mathf.Max(maxAngle, angle);
                if (angle < previousAngle) peakSpeed = Mathf.Max(peakSpeed, filteredSpeed);
                if (time - backSince > 3 || angle > 115) { Rearm(); previousAngle = angle; return false; }
                if (time - backSince >= .12 && maxAngle >= 18 && angle <= 12 && previousAngle > angle && filteredSpeed >= .8f)
                {
                    backing = false; latched = true;
                    SwingAngle = 0;
                    aim = Aim; power = Power = MapPower(peakSpeed);
                    return true;
                }
            }
            previousAngle = angle;
            return false;
        }
    }
}
