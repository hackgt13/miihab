using UnityEngine;

namespace Kinesthetic.Rehab.Mechanics
{
    /// The main metric, in front of the eyes: a transparent reticle that follows the view, with a bubble the
    /// patient keeps in the centre. The centre is the pace. Ahead of it (moving faster than the tempo) the bubble
    /// rises; behind it the bubble sinks; at the top the bubble is the arm's place in the band, so sagging under
    /// the target or drifting over the ceiling moves it too. Keep it centred and the rep is being made the way the
    /// plan asks. A hitch knocks it sideways. The hold fills an arc around the centre, and the set's reps are the
    /// segments of the outer ring — so the count is here as well, not off on a board.
    ///
    /// Head-relative, not head-locked: it eases toward a point in front of the view rather than being pinned
    /// to it, which is what stays comfortable on a headset. Drawn with lines and a sphere in the palette, so it
    /// renders in stereo like everything else in the world; no screen-space UI.
    public sealed class ReticleMechanic : RepMechanic
    {
        const float Distance = 1.25f, Below = .12f;                 // in front of the eyes, a little under the eyeline
        const float OuterRadius = .11f, HoldRadius = .06f, CentreRadius = .03f, Travel = .085f;
        const float LeadFullDeg = 14f;                               // this far ahead of the pace puts the bubble at the edge

        Transform bubble; LineRenderer outer, centre, holdArc; LineRenderer[] repSegments = new LineRenderer[0];
        Material bubbleMaterial;
        Vector3 place; Quaternion facing; bool placed;
        Vector2 offset; float knock, knockVelocity, outsideFor, alpha; int hitches; bool wasInRep;
        static readonly Color Chrome = Palette.Sand00, Bubble = Palette.Cerulean40, Good = Palette.Jungle40, Off = Palette.Coral40;

        void Start()
        {
            bubbleMaterial = Ghost();
            bubble = Primitive(PrimitiveType.Sphere, "Reticle bubble", Vector3.one * .022f, bubbleMaterial);
            outer = Line("Reticle ring", .004f); centre = Line("Reticle centre", .005f); holdArc = Line("Reticle hold", .009f);
            centre.loop = true; outer.loop = true;
        }

        Transform Eyes => Camera.main ? Camera.main.transform : null;

        protected override void Render(RepFeel feel, float? angle, float dt)
        {
            if (!bubble) return;
            var eyes = Eyes;
            bool show = eyes && angle is float;
            alpha = Mathf.Lerp(alpha, show ? 1 : 0, 1 - Mathf.Exp(-6 * dt));
            if (alpha < .02f) { Visible(false); return; }

            // Ease toward a spot in front of the eyes, and face them. First frame snaps so it does not fly in.
            var target = eyes.position + eyes.forward * Distance + Vector3.down * Below;
            place = placed ? Vector3.Lerp(place, target, 1 - Mathf.Exp(-5 * dt)) : target;
            facing = Quaternion.LookRotation(place - eyes.position, Vector3.up);
            placed = true;
            Vector3 right = facing * Vector3.right, up = facing * Vector3.up, normal = facing * Vector3.forward;

            // Where the bubble wants to be. Vertical is the measure that matters: the lead over the pace while
            // moving, the place in the band while holding. Horizontal is only ever a knock from a hitch.
            var pacer = GetComponent<PacerMechanic>();
            float vertical = 0;
            if (feel.InRep && feel.Phase == "hold" || feel.Holding)
            {
                float centreDeg = view.TargetDeg + view.BandDeg * .5f, half = Mathf.Max(1, view.BandDeg * .5f);
                vertical = Mathf.Clamp(((angle ?? centreDeg) - centreDeg) / half, -1.4f, 1.4f) * .7f;
            }
            else if (feel.InRep && pacer && pacer.Active) vertical = Mathf.Clamp(pacer.Lead / LeadFullDeg, -1.4f, 1.4f);
            else if (feel.InRep && feel.HasTempo) vertical = feel.OverSpeed * 1.2f;
            if (feel.InRep && !wasInRep) hitches = 0;
            if (feel.Hitches > hitches) { hitches = feel.Hitches; knockVelocity += (hitches % 2 == 0 ? 1 : -1) * 1.6f; }
            wasInRep = feel.InRep;
            knockVelocity -= knock * 60 * dt; knockVelocity *= Mathf.Exp(-7 * dt); knock += knockVelocity * dt;
            offset = Vector2.Lerp(offset, new Vector2(knock * .5f, vertical), 1 - Mathf.Exp(-9 * dt));
            var bubbleAt = place + (right * offset.x + up * offset.y) * Travel;
            bool centred = offset.magnitude * Travel < CentreRadius;
            outsideFor = centred || !feel.InRep ? 0 : outsideFor + dt;

            // Draw. The chrome is faint; the bubble carries the colour: cerulean in play, jungle when centred at the
            // top or the hold is met, coral when it has been out of the centre for a moment.
            bubble.position = bubbleAt;
            float scale = .022f + (feel.HoldMet ? .006f : 0);
            bubble.localScale = Vector3.one * scale;
            var bubbleColor = feel.HoldMet || (centred && feel.Holding) ? Good : outsideFor > .35f ? Off : Bubble;
            bubbleMaterial.color = Fade(bubbleColor, alpha * (feel.InRep ? .95f : .5f));

            Ring(centre, place, normal, up, CentreRadius, 1);
            centre.startColor = centre.endColor = Fade(centred && feel.InRep ? Good : Chrome, alpha * (feel.InRep ? .75f : .35f));
            Ring(outer, place, normal, up, OuterRadius, 1);
            outer.startColor = outer.endColor = Fade(Chrome, alpha * .22f);

            bool showHold = feel.HasHold && feel.InRep && (feel.Holding || feel.HoldMet) && feel.HoldFraction > 0;
            holdArc.enabled = showHold;
            if (showHold)
            {
                Ring(holdArc, place, normal, up, HoldRadius, feel.HoldMet ? 1 : feel.HoldFraction);
                holdArc.startColor = holdArc.endColor = Fade(feel.HoldMet ? Good : Bubble, alpha * .9f);
            }
            Reps(feel, normal, up, right);
            Visible(true);
        }

        /// The set's count as segments of the outer ring: one per prescribed rep, lit as they are counted.
        void Reps(RepFeel feel, Vector3 normal, Vector3 up, Vector3 right)
        {
            int n = Mathf.Clamp(feel.Prescribed, 0, 30);
            if (repSegments.Length != n)
            {
                foreach (var s in repSegments) if (s) Destroy(s.gameObject);
                repSegments = new LineRenderer[n];
                for (int i = 0; i < n; i++) repSegments[i] = Line($"Rep {i + 1}", .012f);
            }
            const float gapDeg = 5f;
            for (int i = 0; i < n; i++)
            {
                float span = 360f / n, start = -90 + i * span + gapDeg * .5f, end = start + span - gapDeg;
                var line = repSegments[i]; line.positionCount = 6;
                for (int k = 0; k < 6; k++)
                {
                    float a = Mathf.Lerp(start, end, k / 5f);
                    line.SetPosition(k, place + Quaternion.AngleAxis(-a, normal) * up * OuterRadius);
                }
                bool lit = i < feel.Valid;
                line.startColor = line.endColor = Fade(lit ? Good : Chrome, alpha * (lit ? .95f : .3f));
                line.enabled = true;
            }
        }

        void Visible(bool on)
        {
            if (bubble) bubble.GetComponent<Renderer>().enabled = on;
            if (outer) outer.enabled = on; if (centre) centre.enabled = on;
            if (!on && holdArc) holdArc.enabled = false;
            if (!on) foreach (var s in repSegments) if (s) s.enabled = false;
        }
    }
}
