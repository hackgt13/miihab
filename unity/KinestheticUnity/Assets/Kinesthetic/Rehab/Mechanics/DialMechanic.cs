using UnityEngine;

namespace Kinesthetic.Rehab.Mechanics
{
    /// The joint, as a needle. A line from a pivot that swings through exactly the angle the joint is at, on the
    /// side the limb is on: down at rest, out and up as the arm rises, the way the arm itself goes. The arc it
    /// sweeps carries the plan — the band from the target to the ceiling in jungle, the ceiling marked in coral —
    /// so the needle entering the band is the rep arriving, with no number to read. It rides in the HUD beside
    /// the reticle and answers the angle directly, which is the most immediate feedback the studio has.
    public sealed class DialMechanic : RepMechanic
    {
        // Nearer and larger than the reticle: at 0.8 m a 0.2 m needle is about three times the size the reticle's
        // elements are to the eye, with its pivot just off the centre line on the limb's side so the arc sweeps outward.
        const float Distance = .8f, Below = -.1f, Aside = .12f;    // pivot a touch above the eyeline: the arc sweeps down from it and must clear the dock
        const float Radius = .2f;

        Transform pivotDot, tip; LineRenderer needle, track, band, ceilingTick;
        Material tipMaterial, pivotMaterial;
        Vector3 place; Quaternion facing; bool placed; float alpha, shown;
        static readonly Color Chrome = Palette.Sand00, Climbing = Palette.Cerulean40, InBand = Palette.Jungle40, Over = Palette.Coral40;

        void Start()
        {
            tipMaterial = Ghost(); pivotMaterial = Ghost();
            tip = Primitive(PrimitiveType.Sphere, "Dial tip", Vector3.one * .04f, tipMaterial);
            pivotDot = Primitive(PrimitiveType.Sphere, "Dial pivot", Vector3.one * .025f, pivotMaterial);
            needle = Line("Dial needle", .014f);
            track = Line("Dial track", .006f);
            band = Line("Dial band", .02f);
            ceilingTick = Line("Dial ceiling", .012f);
            Visible(false);
        }

        protected override void Render(RepFeel feel, float? angle, float dt)
        {
            if (!needle) return;
            var eyes = Camera.main ? Camera.main.transform : null;
            bool show = eyes && angle is float;
            alpha = Mathf.Lerp(alpha, show ? 1 : 0, 1 - Mathf.Exp(-6 * dt));
            if (alpha < .02f) { Visible(false); return; }

            // The same easing as the reticle, so the two ride together; nearer, and offset to the side the limb is on.
            bool left = view.Side == "left";
            var target = eyes.position + eyes.forward * Distance + Vector3.down * Below;
            place = placed ? Vector3.Lerp(place, target, 1 - Mathf.Exp(-5 * dt)) : target;
            facing = Quaternion.LookRotation(place - eyes.position, Vector3.up);
            placed = true;
            Vector3 right = facing * Vector3.right, up = facing * Vector3.up, normal = facing * Vector3.forward;
            Vector3 outward = left ? -right : right;
            var pivot = place + outward * Aside + up * (Radius * .35f);

            // The needle follows the shown angle with no further smoothing: what the joint does, it does.
            shown = angle ?? shown;
            Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return -up * Mathf.Cos(a) + outward * Mathf.Sin(a); }
            float targetDeg = view.TargetDeg, ceiling = view.TargetDeg + view.BandDeg;
            bool inBand = shown >= targetDeg && shown <= ceiling, over = shown > ceiling;
            var needleColor = over ? Over : inBand ? InBand : Climbing;

            Arc(track, pivot, Dir, 0, 180, Radius, 36);
            track.startColor = track.endColor = Fade(Chrome, alpha * .22f);
            Arc(band, pivot, Dir, targetDeg, ceiling, Radius, 12);
            band.startColor = band.endColor = Fade(InBand, alpha * (inBand ? .95f : .6f));
            ceilingTick.positionCount = 2;
            ceilingTick.SetPosition(0, pivot + Dir(ceiling) * (Radius * .86f));
            ceilingTick.SetPosition(1, pivot + Dir(ceiling) * (Radius * 1.12f));
            ceilingTick.startColor = ceilingTick.endColor = Fade(Over, alpha * (over ? .95f : .55f));

            needle.positionCount = 2;
            needle.SetPosition(0, pivot);
            needle.SetPosition(1, pivot + Dir(shown) * Radius);
            needle.startColor = needle.endColor = Fade(needleColor, alpha * .95f);
            tip.position = pivot + Dir(shown) * Radius;
            tipMaterial.color = Fade(needleColor, alpha);
            pivotDot.position = pivot;
            pivotMaterial.color = Fade(Chrome, alpha * .7f);
            Visible(true);
        }

        static void Arc(LineRenderer line, Vector3 pivot, System.Func<float, Vector3> dir, float fromDeg, float toDeg, float radius, int steps)
        {
            line.positionCount = steps + 1;
            for (int i = 0; i <= steps; i++) line.SetPosition(i, pivot + dir(Mathf.Lerp(fromDeg, toDeg, i / (float)steps)) * radius);
        }

        void Visible(bool on)
        {
            if (needle) needle.enabled = on; if (track) track.enabled = on; if (band) band.enabled = on; if (ceilingTick) ceilingTick.enabled = on;
            if (tip) tip.GetComponent<Renderer>().enabled = on; if (pivotDot) pivotDot.GetComponent<Renderer>().enabled = on;
        }
    }
}
