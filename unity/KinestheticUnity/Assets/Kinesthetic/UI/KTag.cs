using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A small pill of metadata: a side, a status, a preview marker. The tone is a verdict and carries the
    /// palette's meaning — Good is jungle, Trouble is coral — so a tag never turns green for decoration.
    ///
    ///     <k:KTag text="RIGHT ARM" tone="Info" />
    [UxmlElement]
    public partial class KTag : Label
    {
        public enum Tone { Neutral, Info, Good, Trouble }

        const string Block = "k-tag";
        Tone toneValue;

        [UxmlAttribute]
        public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, Block, value); } }

        public KTag() : this(string.Empty) { }

        public KTag(string text) : base(text)
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Text");
            tone = Tone.Neutral;
        }
    }
}
