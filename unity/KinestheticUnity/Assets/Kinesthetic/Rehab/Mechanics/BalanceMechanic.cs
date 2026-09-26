using UnityEngine;

namespace Kinesthetic.Rehab.Mechanics
{
    /// A ball balanced on a small tray at the hand. Move at the tempo and it sits still; outrun the tempo and the
    /// tray tips and the ball slides toward the rim, and if you keep going it falls off. Hold in the band and a
    /// ring fills around the tray; when the hold is met the ball glows and settles. A hitch makes it hop.
    ///
    /// This is the "balancing a feather" of the studio: the patient's speed and steadiness ARE the game input, and
    /// the feedback is physical and continuous rather than a number. The tilt answers to the instantaneous speed
    /// (RepFeel.OverSpeed) so it feels immediate; the coordinator's own "slower" tips it too, so a drop always
    /// agrees with what the record will say. Nothing here is scored: a drop costs nothing but the sight of it, and
    /// the ball is back on the tray for the next rep.
    public sealed class BalanceMechanic : RepMechanic
    {
        const float TrayRadius = .075f, BallRadius = .032f, Rim = .05f, RingRadius = .1f;
        const float TiltDeg = 28f, SlideResponse = 2.6f, Gravity = 4.5f;

        Transform tray, ball; LineRenderer holdRing; Renderer ballRenderer, trayRenderer;
        Material ballMaterial, trayMaterial;
        float slide, hop, hopVelocity, tilt;
        bool dropped, wasInRep, wasMet; int hitches; float droppedAt; Vector3 dropVelocity, dropPosition;
        static readonly Color Ball = Palette.Sand00, Tray = Palette.Cerulean30, Met = Palette.Jungle40, Dropped = Palette.Coral40;

        void Start()
        {
            trayMaterial = Lit(Tray); ballMaterial = Lit(Ball);
            tray = Primitive(PrimitiveType.Cylinder, "Balance tray", new Vector3(TrayRadius * 2, .004f, TrayRadius * 2), trayMaterial);
            ball = Primitive(PrimitiveType.Sphere, "Balance ball", Vector3.one * BallRadius * 2, ballMaterial);
            trayRenderer = tray.GetComponent<Renderer>(); ballRenderer = ball.GetComponent<Renderer>();
            holdRing = Line("Hold ring", .012f);
            Show(false);
        }

        void Show(bool on) { if (trayRenderer) trayRenderer.enabled = on; if (ballRenderer) ballRenderer.enabled = on; if (holdRing) holdRing.enabled = on; }

        protected override void Render(RepFeel feel, float? angle, float dt)
        {
            if (!tray) return;
            if (angle is not float deg || !Along(deg, out var hand, out var tangent)) { Show(false); return; }
            var up = Vector3.up;
            var center = hand + up * .02f;

            // A new rep puts the ball back; the hold met and a hitch are moments, felt once each.
            if (feel.InRep && !wasInRep) { dropped = false; slide = 0; hop = 0; hopVelocity = 0; hitches = 0; }
            if (feel.Hitches > hitches) { hitches = feel.Hitches; hopVelocity = .55f; }
            if (feel.HoldMet && !wasMet) Kinesthetic.Menu.ActivityNavigation.Instance?.PlaySelect();
            wasInRep = feel.InRep; wasMet = feel.HoldMet;

            // The tray tips with how far over the tempo the arm is; the ball slides backward against the motion,
            // as it would on a tray that is being accelerated too hard. Holding and resting keep it level.
            bool moving = feel.InRep && feel.HasTempo && feel.Phase != "hold" && !feel.Holding;
            float over = moving ? Mathf.Max(feel.OverSpeed, feel.Fast ? .6f : 0) : 0;
            tilt = Mathf.Lerp(tilt, over, 1 - Mathf.Exp(-6 * dt));
            var slideDir = -tangent * (feel.Phase == "lower" ? -1 : 1);
            if (!dropped)
            {
                // Over the tempo the ball creeps toward the rim; at the tempo it eases back to the middle.
                slide += (over > .05f ? over * SlideResponse * Rim : -slide * 3) * dt;
                slide = Mathf.Clamp(slide, 0, Rim * 1.4f);
                if (slide > Rim)
                {
                    dropped = true; droppedAt = Time.unscaledTime;
                    dropPosition = center + up * BallRadius + slideDir * slide;
                    dropVelocity = slideDir * .35f + up * .15f;
                    Kinesthetic.Menu.ActivityNavigation.Instance?.PlayBack();
                }
            }
            hopVelocity -= Gravity * dt; hop = Mathf.Max(0, hop + hopVelocity * dt); if (hop == 0 && hopVelocity < 0) hopVelocity = 0;

            var tiltAxis = Vector3.Cross(up, slideDir).normalized;
            tray.position = center;
            tray.rotation = Quaternion.AngleAxis(tilt * TiltDeg, tiltAxis);
            Show(true);

            if (dropped)
            {
                // The ball falls away, slowly enough to be seen, and fades; the tray waits empty for the next rep.
                float t = Time.unscaledTime - droppedAt;
                dropVelocity += Vector3.down * Gravity * dt; dropPosition += dropVelocity * dt;
                ball.position = dropPosition;
                ballMaterial.color = Fade(Dropped, Mathf.Clamp01(1.4f - t));
                ballRenderer.enabled = t < 1.4f;
                if (!feel.InRep && t > 1.6f) { dropped = false; slide = 0; }
            }
            else
            {
                ball.position = center + tray.rotation * (up * BallRadius + slideDir * slide) + up * hop;
                float glow = feel.HoldMet ? 1 : feel.Holding ? feel.HoldFraction * .6f : 0;
                ballMaterial.color = Color.Lerp(Color.Lerp(Ball, Dropped, Mathf.Clamp01(slide / Rim) * .8f), Met, glow);
            }
            trayMaterial.color = Color.Lerp(Tray, Met, feel.HoldMet ? 1 : 0);

            // The hold: a ring around the tray that fills as the hold target is approached, jungle once it is met.
            bool showHold = feel.HasHold && feel.InRep && (feel.Holding || feel.HoldMet) && feel.HoldFraction > 0;
            holdRing.enabled = showHold;
            if (showHold)
            {
                Ring(holdRing, center + up * .005f, up, tangent, RingRadius, feel.HoldMet ? 1 : feel.HoldFraction);
                var c = feel.HoldMet ? Met : Palette.Cerulean40;
                holdRing.startColor = holdRing.endColor = Fade(c, .95f);
            }
        }
    }
}
