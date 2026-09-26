using System;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// The one button. A screen picks a tone, a size and a shape; colour, radius, padding, the lip along the
    /// bottom and how it answers a pointer are decided here and in Button.uss, once.
    ///
    ///     <k:KButton text="Start session" tone="Primary" size="Large" />
    ///
    /// Hover and focus are one look for every tone, so a person driving this with their head sees what a
    /// person with a mouse sees. A world-space panel gets no pointer events at all — panel.Pick returns null
    /// there, see GazeDwell — so :hover never fires on the plaza board or inside a pane. Whatever resolves
    /// the ray sets Hot and Pressed instead, and they draw exactly what :hover and :active draw.
    [UxmlElement]
    public partial class KButton : Button
    {
        public enum Tone { Primary, Secondary, Ghost, Quiet }
        public enum Size { Small, Medium, Large }
        public enum Shape { Pill, Round }

        const string Block = "k-button";
        Tone toneValue;
        Size sizeValue;
        Shape shapeValue;
        bool hot, pressed;

        [UxmlAttribute]
        public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, Block, value); } }

        [UxmlAttribute]
        public Size size { get => sizeValue; set { sizeValue = value; KStyles.Variant(this, Block, value); } }

        /// Round drops the label for a single glyph: an aim arrow, a close cross.
        [UxmlAttribute]
        public Shape shape { get => shapeValue; set { shapeValue = value; KStyles.Variant(this, Block, value); } }

        /// Hover, for a panel that has no pointer. Draws what :hover and :focus draw.
        public bool Hot { get => hot; set { hot = value; EnableInClassList("k-hot", value); } }

        /// Pressed, likewise. Draws what :active draws.
        public bool Pressed { get => pressed; set { pressed = value; EnableInClassList("k-pressed", value); } }

        public KButton() : this(null) { }

        public KButton(Action clicked) : base(clicked)
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Button");
            tone = Tone.Secondary;
            size = Size.Medium;
            shape = Shape.Pill;
        }
    }
}
