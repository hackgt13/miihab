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

        [UxmlAttribute]
        public string value { get => figure.text; set => figure.text = value; }

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
