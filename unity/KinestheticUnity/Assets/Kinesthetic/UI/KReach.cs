using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// The reach, drawn as the movement instead of as a chart.
    ///
    /// A line graph of shoulder degrees is generic — it could be plotting anything, and "93°" does not
    /// feel like a distance until you see the arm sweep it. So this is an arc pivoting at the shoulder,
    /// read like a dial: a faint track for the range a shoulder has, a quiet band for where week one
    /// stopped, a solid band for everything gained since, a notch for the target and the arm at today.
    ///
    ///     <k:KReach name="range-fan" class="range-fan" />
    ///     fan.measured = true; fan.start = 41; fan.now = 63; fan.target = 85;
    ///
    /// The screen gives it a size. Every reading is an attribute rather than a painter the screen
    /// attaches, so the headset's replica — which receives attributes, never delegates — draws the same
    /// fan from the same numbers. Painted in code, so its colours come from Palette.cs.
    [UxmlElement]
    public partial class KReach : VisualElement
    {
        bool measuredValue;
        float nowValue, startValue, targetValue, ceilingValue = 120;

        /// False until a real session exists: nothing is drawn, because a fan at zero reads as a measurement.
        [UxmlAttribute]
        public bool measured { get => measuredValue; set { measuredValue = value; MarkDirtyRepaint(); } }

        /// Today's reach, in degrees of elevation.
        [UxmlAttribute]
        public float now { get => nowValue; set { nowValue = value; MarkDirtyRepaint(); } }

        /// Where week one stopped.
        [UxmlAttribute]
        public float start { get => startValue; set { startValue = value; MarkDirtyRepaint(); } }

        /// What is being reached for.
        [UxmlAttribute]
        public float target { get => targetValue; set { targetValue = value; MarkDirtyRepaint(); } }

        /// The top of the dial. Functional shoulder elevation, not the anatomical 180.
        [UxmlAttribute]
        public float ceiling { get => ceilingValue; set { ceilingValue = Mathf.Max(1, value); MarkDirtyRepaint(); } }

        public KReach()
        {
            AddToClassList("k-reach");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// Three bands and two marks, and nothing else. An earlier version drew guide spokes, a filled
        /// pie, an arm, a hand and a separate edge line on top of each other, which read as clutter.
        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 24 || r.height < 24 || !measuredValue) return;
            var p = ctx.painter2D;
            float ceilingDeg = ceilingValue;
            float now = Mathf.Clamp(nowValue, 0, ceilingDeg);
            float start = Mathf.Clamp(startValue, 0, ceilingDeg);
            float target = Mathf.Clamp(targetValue, 0, ceilingDeg);

            // Arm at the side points down; raising it sweeps towards horizontal and past it. Painter2D
            // measures from +X clockwise, so straight down is 90 and an elevation of E sits at 90 - E.
            // Drawing 0..ceiling covers a box `radius` wide and `radius * (1 + sin(ceiling - 90))` tall.
            float Ang(float e) => 90 - e;
            float tall = 1 + Mathf.Sin((ceilingDeg - 90) * Mathf.Deg2Rad);
            float radius = Mathf.Min(r.width, r.height / tall) * .96f;
            var pivot = new Vector2(r.x + (r.width - radius) * .5f,
                                    r.y + (r.height - radius * tall) * .5f + radius * (tall - 1));

            float thickness = radius * .17f;
            float mid = radius - thickness * .5f;

            void Band(float from, float to, Color colour)
            {
                if (to <= from) return;
                p.strokeColor = colour;
                p.lineWidth = thickness;
                p.lineCap = LineCap.Butt;
                p.BeginPath();
                p.Arc(pivot, mid, Angle.Degrees(Ang(to)), Angle.Degrees(Ang(from)));
                p.Stroke();
            }

            Band(0, ceilingDeg, Palette.Line.At(.20f));         // the range a shoulder has
            Band(0, start, Palette.Reference.At(.55f));         // where week one stopped
            Band(start, now, Palette.Progress);                 // everything gained since

            // The target, as a notch cut across the band rather than a line laid over it.
            float ta = Ang(target) * Mathf.Deg2Rad;
            var tdir = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
            p.strokeColor = Palette.Target; p.lineWidth = 4; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(pivot + tdir * (mid - thickness * .62f));
            p.LineTo(pivot + tdir * (mid + thickness * .62f));
            p.Stroke();

            // The arm. Without it the arc floats and the shoulder dot reads as a stray speck — this is
            // the line that makes the whole figure a reach rather than a gauge.
            float na = Ang(now) * Mathf.Deg2Rad;
            var ndir = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
            p.strokeColor = Palette.Ink; p.lineWidth = 6; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(pivot);
            p.LineTo(pivot + ndir * (mid - thickness * .5f));
            p.Stroke();

            p.fillColor = Palette.Ink;
            p.BeginPath(); p.Arc(pivot, 9, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();

            // The hand, sitting on the band at today's reach.
            p.fillColor = Palette.Panel;
            p.strokeColor = Palette.Progress; p.lineWidth = 5;
            p.BeginPath(); p.Arc(pivot + ndir * mid, thickness * .58f, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill(); p.Stroke();
        }
    }
}
