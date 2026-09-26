using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A line of text the person types: a message, a name. The screen gives it a width; the box, its edge,
    /// its lettering and the placeholder's are the component's.
    ///
    ///     <k:KField name="relay-text" placeholder="Write a message…" />
    ///
    /// World-space panels get no pointer events, so a field there is not clicked into: the screen calls
    /// Focus() when it wants typing, and reads Enter itself.
    [UxmlElement]
    public partial class KField : TextField
    {
        [UxmlAttribute]
        public string placeholder { get => textEdition.placeholder; set => textEdition.placeholder = value; }

        public KField()
        {
            AddToClassList("k-field");
            KStyles.Attach(this, "Field");
            textEdition.hidePlaceholderOnFocus = false;
        }
    }
}
