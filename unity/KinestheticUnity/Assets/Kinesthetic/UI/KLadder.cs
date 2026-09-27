using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// Where a target stands inside the limits a clinician approved, as a ladder with one rung per level.
    /// A bar would say "62% of the way", which is a number nobody was given; the rungs say "level 2 of
    /// 11", which is the number on the plan, and the gap to the end says how much room is left before
    /// the clinician has to decide again.
    ///
    ///     <k:KLadder name="envelope-ladder" class="envelope-ladder" />
    ///     ladder.rungs = 11; ladder.pitch = .1f; ladder.fraction = .2f;
    ///
    /// The screen gives it a width and a height. The readings are attributes, so the headset's replica
    /// draws the same ladder from the same numbers. Painted in code; colours come from Palette.cs.
    [UxmlElement]
    public partial class KLadder : VisualElement
    {
        int rungCount;
        float pitchValue = .1f, fill;

        /// How many levels there are. Zero draws nothing: a plan with no envelope has no ladder.
        [UxmlAttribute]
        public int rungs { get => rungCount; set { rungCount = Mathf.Clamp(value, 0, 64); MarkDirtyRepaint(); } }

        /// The distance between rungs, as a fraction of the whole track.
        [UxmlAttribute]
        public float pitch { get => pitchValue; set { pitchValue = Mathf.Clamp01(value); MarkDirtyRepaint(); } }

        /// Where today's target sits along the track, 0 to 1.
        [UxmlAttribute]
        public float fraction { get => fill; set { fill = Mathf.Clamp01(value); MarkDirtyRepaint(); } }

        public KLadder()
        {
            AddToClassList("k-ladder");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 40 || r.height < 12 || rungCount <= 0) return;
            var painter = ctx.painter2D;

            float pad = 8, left = r.x + pad, right = r.xMax - pad;
            float mid = r.y + r.height * .5f, thickness = Mathf.Min(18, r.height * .5f);
            float At(float f) => left + Mathf.Clamp01(f) * (right - left);
            float here = At(fill);

            void Track(float from, float to, Color colour)
            {
                if (to <= from) return;
                painter.strokeColor = colour; painter.lineWidth = thickness; painter.lineCap = LineCap.Round;
                painter.BeginPath(); painter.MoveTo(new(from, mid)); painter.LineTo(new(to, mid)); painter.Stroke();
            }

            Track(left, right, Palette.Reference.At(.24f));    // everything the envelope allows
            Track(left, here, Palette.Progress);               // everything already asked for

            // One rung per level, so the steps are countable rather than implied.
            for (int i = 0; i < rungCount; i++)
            {
                float f = i * pitchValue;
                float x = At(f);
                bool reached = f <= fill + .0001f;
                painter.strokeColor = reached ? Palette.Panel.At(.75f) : Palette.Reference.At(.45f);
                painter.lineWidth = 2; painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.MoveTo(new(x, mid - thickness * .32f));
                painter.LineTo(new(x, mid + thickness * .32f));
                painter.Stroke();
            }

            // Today's target, as a marker sitting on the track.
            painter.strokeColor = Palette.Target; painter.lineWidth = 4; painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(new(here, mid - thickness * .95f));
            painter.LineTo(new(here, mid + thickness * .95f));
            painter.Stroke();

            painter.fillColor = Palette.Panel;
            painter.strokeColor = Palette.Target; painter.lineWidth = 4;
            painter.BeginPath(); painter.Arc(new(here, mid), thickness * .5f, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill(); painter.Stroke();
        }
    }
}
