using UnityEngine;

namespace Kinesthetic.Rehab.Mechanics
{
    /// A pace to follow: a soft orb that travels the arc at the prescribed tempo, just beyond the hand. Raise
    /// with it, wait with it at the top, come down with it. It leads by a little when you are on tempo; when you
    /// outrun it the orb is behind you and turns coral, which is the whole message — the arm should never get
    /// ahead of the pace. It sits at rest between reps, and hides entirely for a movement with no tempo.
    ///
    /// The orb follows the rep's phases as the coordinator names them (RepFeel.Phase), so it holds while the
    /// patient holds, however long, and starts down from wherever the arm actually was when the lowering began.
    public sealed class PacerMechanic : RepMechanic
    {
        const float Ride = .12f;   // how far beyond the hand the orb rides, as a fraction of the segment
        Transform orb; LineRenderer wake; Material material;
        float pacerAngle; string phase = ""; bool wasInRep; float alpha;
        static readonly Color Pace = Palette.Sand00, Ahead = Palette.Coral40;

        /// The angle the pace is at right now, for anything that wants to measure the arm against it (the reticle).
        public float PacerAngle => pacerAngle;
        /// How far the arm is ahead of the pace in the direction of travel, in degrees; negative when behind.
        /// Zero at the top and when there is no pace to be ahead of.
        public float Lead { get; private set; }
        public bool Active => alpha > .02f;

        void Start()
        {
            material = Ghost(); material.color = Fade(Pace, 0);
            orb = Primitive(PrimitiveType.Sphere, "Pacer", Vector3.one * .05f, material);
            wake = Line("Pacer wake", .008f);
            orb.GetComponent<Renderer>().enabled = false; wake.enabled = false;
        }

        protected override void Render(RepFeel feel, float? angle, float dt)
        {
            if (!orb) return;
            bool show = feel.HasTempo && angle is float && feel.InRep && feel.TempoSpeed > 0;
            alpha = Mathf.Lerp(alpha, show ? 1 : 0, 1 - Mathf.Exp(-8 * dt));
            var renderer = orb.GetComponent<Renderer>();
            if (alpha < .02f) { renderer.enabled = false; wake.enabled = false; wasInRep = feel.InRep; Lead = 0; return; }
            float shown = angle ?? 0;

            // Where the pace is: it starts with the arm, climbs at the tempo to the target, waits, and descends at
            // the tempo from wherever the arm was when it began to come down.
            if (feel.InRep && !wasInRep) { pacerAngle = shown; phase = ""; }
            if (feel.Phase != phase)
            {
                if (feel.Phase == "lower") pacerAngle = Mathf.Max(pacerAngle, shown);
                phase = feel.Phase;
            }
            float top = view.TargetDeg + view.BandDeg * .35f;
            if (phase == "raise") pacerAngle = Mathf.Min(top, pacerAngle + feel.TempoSpeed * dt);
            else if (phase == "hold") pacerAngle = Mathf.Lerp(pacerAngle, Mathf.Clamp(shown, view.TargetDeg, top), 1 - Mathf.Exp(-3 * dt));
            else if (phase == "lower") pacerAngle = Mathf.Max(0, pacerAngle - feel.TempoSpeed * dt);
            wasInRep = feel.InRep;

            if (!Segment(pacerAngle, out var origin, out var direction, out var length)) { renderer.enabled = false; wake.enabled = false; return; }
            orb.position = origin + direction * length * (1 + Ride);
            renderer.enabled = true;

            // Ahead of the pace, in the direction of travel, by more than a few degrees: the arm is outrunning it.
            float ahead = phase == "lower" ? pacerAngle - shown : shown - pacerAngle;
            Lead = phase == "hold" ? 0 : ahead;
            bool outrun = phase != "hold" && ahead > 6;
            material.color = Fade(outrun ? Ahead : Pace, alpha * (outrun ? .85f : .55f));
            orb.localScale = Vector3.one * (.05f + .006f * Mathf.Sin(Time.unscaledTime * 5));

            // A short wake from the hand to the orb, so the eye reads them as one pair.
            if (Segment(shown, out var handOrigin, out var handDirection, out var handLength))
            {
                wake.enabled = true; wake.positionCount = 2;
                wake.SetPosition(0, handOrigin + handDirection * handLength * (1 + Ride * .4f));
                wake.SetPosition(1, orb.position);
                wake.startColor = wake.endColor = Fade(outrun ? Ahead : Pace, alpha * .35f);
            }
            else wake.enabled = false;
        }
    }
}
