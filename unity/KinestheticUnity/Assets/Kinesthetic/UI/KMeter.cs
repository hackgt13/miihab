using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A straight measure. Pips for a count a person can check — "4 of 5 sessions" — and a bar for a
    /// continuous value such as an angle against its target. The screen gives it a width; its height is
    /// fixed here.
    ///
    ///     <k:KMeter form="Pips" count="5" filled="4" />
    ///     <k:KMeter form="Bar" fraction="0.62" />
    ///
    /// The track is the same colour as KArc's, so a ring and a bar on one panel read as one system.
    [UxmlElement]
    public partial class KMeter : VisualElement
    {
        public enum Form { Pips, Bar }
        public enum Tone { Progress, Good, Target }

        const string Block = "k-meter";
        const float PipGap = 7;
        Form formValue;
        Tone toneValue;
        int pipCount = 5, filledCount;
        float fill;

        [UxmlAttribute]
        public Form form { get => formValue; set { formValue = value; KStyles.Variant(this, Block, value); MarkDirtyRepaint(); } }

        [UxmlAttribute]
        public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, Block, value); MarkDirtyRepaint(); } }

        /// Pips only.
        [UxmlAttribute]
        public int count { get => pipCount; set { pipCount = Mathf.Clamp(value, 1, 14); MarkDirtyRepaint(); } }

        /// Pips only.
        [UxmlAttribute]
        public int filled { get => filledCount; set { filledCount = Mathf.Max(0, value); MarkDirtyRepaint(); } }

        /// Bar only: 0 to 1.
        [UxmlAttribute]
        public float fraction { get => fill; set { fill = Mathf.Clamp01(value); MarkDirtyRepaint(); } }

        public KMeter()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Measures");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            form = Form.Pips;
            tone = Tone.Progress;
        }

        Color Colour => toneValue == Tone.Good ? Palette.Good : toneValue == Tone.Target ? Palette.Target : Palette.Progress;

        void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width < 4 || rect.height < 2) return;
            var painter = context.painter2D;

            if (formValue == Form.Pips)
            {
                float cell = (rect.width - PipGap * (pipCount - 1)) / pipCount;
                for (int i = 0; i < pipCount; i++)
                    Pill(painter, rect.x + i * (cell + PipGap), rect.y, cell, rect.height, i < filledCount ? Colour : Palette.Line);
                return;
            }

            Pill(painter, rect.x, rect.y, rect.width, rect.height, Palette.Line);
            if (fill > 0) Pill(painter, rect.x, rect.y, rect.width * fill, rect.height, Colour);
        }

        // A rectangle with fully rounded ends. Never narrower than it is tall, so a small value still reads
        // as a dot rather than collapsing.
        static void Pill(Painter2D painter, float x, float y, float width, float height, Color colour)
        {
            float r = height * .5f;
            width = Mathf.Max(width, height);
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x + r, y));
            painter.LineTo(new Vector2(x + width - r, y));
            painter.Arc(new Vector2(x + width - r, y + r), r, Angle.Degrees(-90), Angle.Degrees(90));
            painter.LineTo(new Vector2(x + r, y + height));
            painter.Arc(new Vector2(x + r, y + r), r, Angle.Degrees(90), Angle.Degrees(270));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
