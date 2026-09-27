using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A measured figure and what it is. Four sizes, named for how far away it is read from, where there were
    /// eight font sizes between 28 and 82px.
    ///
    ///     <k:KReadout value="112°" caption="best reach" size="Hero" />
    ///
    /// Tone is a verdict, as the palette defines it: a figure turns Good when it passes the plan, never
    /// because it is large.
    [UxmlElement]
    public partial class KReadout : VisualElement
    {
        public enum Size { Hero, Large, Regular, Compact }
        public enum Tone { Ink, Good, Target }

        const string Block = "k-readout";
        readonly Label figure = new Label(), label = new Label();
        Size sizeValue;
        Tone toneValue;
        bool punchValue; float punchStart = -1; IVisualElementScheduledItem punching;
        // The figure's leading number, so a punch fires only when the count goes up and not on every redraw.
        double lastNumber = double.NaN;

        [UxmlAttribute]
        public string value
        {
            get => figure.text;
            set
            {
                if (figure.text == value) return;
                figure.text = value;
                var number = Leading(value);
                if (punchValue && !double.IsNaN(number) && !double.IsNaN(lastNumber) && number > lastNumber) Punch();
                lastNumber = number;
            }
        }

        /// A count that punches when it goes up: the figure swells and shakes for a third of a second, as a score
        /// does when it is earned. Only ever on an increase, so a figure that merely redraws stays still. The
        /// motion is on the figure alone; the caption, tone and layout do not move.
        [UxmlAttribute]
        public bool punch { get => punchValue; set => punchValue = value; }

        static double Leading(string text)
        {
            if (string.IsNullOrEmpty(text)) return double.NaN;
            int end = 0;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || (end == 0 && text[end] == '-'))) end++;
            return end > 0 && double.TryParse(text.Substring(0, end), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
        }

        /// Swell to 1.22× with a little overshoot, and a two-cycle shake that dies out, over 340 ms.
        public void Punch()
        {
            const float duration = .34f;
            punchStart = UnityEngine.Time.unscaledTime;
            punching?.Pause();
            punching = figure.schedule.Execute(() =>
            {
                float t = UnityEngine.Mathf.Clamp01((UnityEngine.Time.unscaledTime - punchStart) / duration);
                // Ease out back: up fast, over the top, then settle.
                float back = 1 - UnityEngine.Mathf.Pow(1 - t, 3) * (1 + 2.2f * t);
                float swell = 1 + .22f * (1 - t) * UnityEngine.Mathf.Sin(back * UnityEngine.Mathf.PI * .5f + UnityEngine.Mathf.PI * .5f) ;
                float shake = 2.5f * (1 - t) * UnityEngine.Mathf.Sin(t * UnityEngine.Mathf.PI * 4);
                figure.style.scale = new StyleScale(new Scale(new UnityEngine.Vector2(swell, swell)));
                figure.style.translate = new StyleTranslate(new Translate(shake, 0));
                if (t >= 1)
                {
                    figure.style.scale = new StyleScale(new Scale(UnityEngine.Vector2.one));
                    figure.style.translate = new StyleTranslate(new Translate(0, 0));
                    punching.Pause();
                }
            }).Every(16);
        }

        [UxmlAttribute]
        public string caption
        {
            get => label.text;
            set { label.text = value; label.EnableInClassList(Block + "__caption--empty", string.IsNullOrEmpty(value)); }
        }

        [UxmlAttribute]
        public Size size { get => sizeValue; set { sizeValue = value; KStyles.Variant(this, Block, value); } }

        [UxmlAttribute]
        public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, Block, value); } }

        public KReadout()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Readout");
            pickingMode = PickingMode.Ignore;

            figure.pickingMode = label.pickingMode = PickingMode.Ignore;
            figure.AddToClassList(Block + "__value");
            label.AddToClassList(Block + "__caption");
            Add(figure);
            Add(label);

            caption = string.Empty;
            size = Size.Regular;
            tone = Tone.Ink;
        }
    }
}
