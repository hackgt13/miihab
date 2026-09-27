using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// One choice in a list, pressed as a whole: a group to join, a way to play. The row is the button,
    /// so nothing pressable ever sits inside another pressable thing. The screen puts the content in
    /// (KText, KTag, KMiiTag) and lays it out; the ground, edge, lip and hover are the component's.
    ///
    ///     <k:KOption name="lobby-solo" tone="Plain"><k:KText text="Play solo" size="Heading" /></k:KOption>
    ///
    /// `Highlight` is jungle, the palette's "good": the row has something in it for you — a friend is in
    /// that group. It is a verdict, never decoration. Hover and focus are cerulean for every tone, as on
    /// KButton, so a head and a mouse see the same thing.
    [UxmlElement]
    public partial class KOption : Button
    {
        public enum Tone { Plain, Highlight }

        const string Block = "k-option";
        Tone toneValue;
        bool hot, pressed;

        [UxmlAttribute]
        public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, Block, value); } }

        /// Hover, for a panel that has no pointer. Draws what :hover and :focus draw.
        public bool Hot { get => hot; set { hot = value; EnableInClassList("k-hot", value); } }

        /// Pressed, likewise. Draws what :active draws.
        public bool Pressed { get => pressed; set { pressed = value; EnableInClassList("k-pressed", value); } }

        public KOption() : this(null) { }

        public KOption(System.Action clicked) : base(clicked)
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Option");
            text = "";
            tone = Tone.Plain;
        }
    }
}
