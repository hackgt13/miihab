using UnityEngine.UIElements;
namespace Kinesthetic.UI
{
    [UxmlElement]
    public partial class KText : Label
    {
        public enum Size { Title, Heading, Body, Caption }
        public enum Tone { Ink, Soft, OnDark, Success }
        Size sizeValue; Tone toneValue;
        [UxmlAttribute] public Size size { get => sizeValue; set { sizeValue = value; KStyles.Variant(this, "k-text", value); } }
        [UxmlAttribute] public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, "k-text", value); } }
        public KText() { AddToClassList("k-text"); KStyles.Attach(this, "Content"); pickingMode = PickingMode.Ignore; size = Size.Body; tone = Tone.Ink; }
    }
}
