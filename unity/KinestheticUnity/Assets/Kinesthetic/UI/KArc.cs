using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A ring that fills as a fraction does: how much of a program is done, how many reps of a set. The ring
    /// is the context and a figure in the middle is the reading, so anything placed inside — usually a
    /// KReadout — is centred in it.
    ///
    ///     <k:KArc form="Segmented" segments="10" fraction="0.4" tone="Good" class="studio-reps">
    ///         <k:KReadout value="4" caption="of 10" size="Large" />
    ///     </k:KArc>
    ///
    /// The screen gives it a size; the arc draws itself to fit. Painted in code, so its colours come from
    /// Palette.cs, the C# half of the theme.
    [UxmlElement]
    public partial class KArc : VisualElement
    {
        public enum Form { Continuous, Segmented }
        public enum Tone { Progress, Good, Target }

        const string Block = "k-arc";
        Form formValue;
        Tone toneValue;
        int segmentCount = 10;
        float fill;

        [UxmlAttribute]
        public Form form { get => formValue; set { formValue = value; KStyles.Variant(this, Block, value); MarkDirtyRepaint(); } }

        [UxmlAttribute]
        public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, Block, value); MarkDirtyRepaint(); } }

        /// Segmented only: one arc per rep, up to 24 before they stop reading as separate.
        [UxmlAttribute]
        public int segments { get => segmentCount; set { segmentCount = Mathf.Clamp(value, 1, 24); MarkDirtyRepaint(); } }

        /// 0 to 1. Data, not style.
        [UxmlAttribute]
        public float fraction { get => fill; set { fill = Mathf.Clamp01(value); MarkDirtyRepaint(); } }

        public KArc()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Measures");
            generateVisualContent += Draw;
            form = Form.Continuous;
            tone = Tone.Progress;
        }

        Color Colour => toneValue == Tone.Good ? Palette.Good : toneValue == Tone.Target ? Palette.Target : Palette.Progress;

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size < 16) return;
            var painter = context.painter2D;

            if (formValue == Form.Continuous)
            {
                float thickness = Mathf.Max(10, size * .11f), radius = (size - thickness) * .5f;
                painter.lineWidth = thickness;
                painter.lineCap = LineCap.Butt;
                painter.strokeColor = Palette.Line;
                painter.BeginPath(); painter.Arc(rect.center, radius, Angle.Degrees(0), Angle.Degrees(360)); painter.Stroke();
                if (fill <= 0) return;
                painter.lineCap = LineCap.Round;
                painter.strokeColor = Colour;
                painter.BeginPath(); painter.Arc(rect.center, radius, Angle.Degrees(-90), Angle.Degrees(-90 + 360 * fill)); painter.Stroke();
                return;
            }

            float segmentThickness = Mathf.Max(6, size * .04f), segmentRadius = (size - segmentThickness) * .5f;
            float step = 360f / segmentCount, gap = segmentCount == 1 ? 0 : 6;
            float filled = fill * segmentCount;
            painter.lineWidth = segmentThickness;
            painter.lineCap = LineCap.Round;
            for (int i = 0; i < segmentCount; i++)
            {
                float begin = -90 + i * step + gap * .5f, end = -90 + (i + 1) * step - gap * .5f;
                painter.strokeColor = Palette.Line;
                painter.BeginPath(); painter.Arc(rect.center, segmentRadius, Angle.Degrees(begin), Angle.Degrees(end)); painter.Stroke();
                if (filled <= i) continue;
                painter.strokeColor = Colour;
                painter.BeginPath();
                painter.Arc(rect.center, segmentRadius, Angle.Degrees(begin), Angle.Degrees(Mathf.Lerp(begin, end, Mathf.Clamp01(filled - i))));
                painter.Stroke();
            }
        }
    }
}
