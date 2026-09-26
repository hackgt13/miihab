using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// The small capitals above a heading. One size and one tracking everywhere; the only choice is what it
    /// sits on. Write the copy in capitals — USS has no text-transform.
    ///
    ///     <k:KEyebrow text="TODAY'S EXERCISE" />
    [UxmlElement]
    public partial class KEyebrow : Label
    {
        public enum Backdrop { Light, Dark }

        const string Block = "k-eyebrow";
        Backdrop backdropValue;

        [UxmlAttribute]
        public Backdrop backdrop { get => backdropValue; set { backdropValue = value; KStyles.Variant(this, Block, value); } }

        public KEyebrow() : this(string.Empty) { }

        public KEyebrow(string text) : base(text)
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Text");
            backdrop = Backdrop.Light;
        }
    }
}
