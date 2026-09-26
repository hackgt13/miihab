using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// One line of a conversation: a pair's thread on the friends pane, or a group session's chat.
    ///
    ///     new KMessage { side = KMessage.Side.Theirs, author = "Maya", said = "With you", time = "14:02" }
    ///
    /// `said` is one of the fixed encouragements, set in bold; `text` is a plain note under it. `author`
    /// shows only on someone else's message in a room with more than two people in it. `Room` is the
    /// room's own line — "June joined" — centred and quiet, never a balloon, because nobody said it.
    /// Anything else a message carries (a photo) goes in `Attachments`.
    [UxmlElement]
    public partial class KMessage : VisualElement
    {
        public enum Side { Mine, Theirs, Room }

        const string Block = "k-message";
        Side sideValue;
        readonly Label authorLabel, saidLabel, textLabel, timeLabel;

        public VisualElement Attachments { get; }

        [UxmlAttribute] public Side side { get => sideValue; set { sideValue = value; KStyles.Variant(this, Block, value); } }
        [UxmlAttribute] public string author { get => authorLabel.text; set => Set(authorLabel, value); }
        [UxmlAttribute] public string said { get => saidLabel.text; set => Set(saidLabel, value); }
        [UxmlAttribute] public string text { get => textLabel.text; set => Set(textLabel, value); }
        [UxmlAttribute] public string time { get => timeLabel.text; set => Set(timeLabel, value); }

        public KMessage()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Message");
            pickingMode = PickingMode.Ignore;
            authorLabel = Part("author"); saidLabel = Part("said"); textLabel = Part("text");
            Attachments = new VisualElement { pickingMode = PickingMode.Ignore };
            Add(Attachments);
            timeLabel = Part("time");
            side = Side.Theirs;
        }

        Label Part(string part)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(Block + "__" + part);
            label.AddToClassList("hidden");
            Add(label);
            return label;
        }

        static void Set(Label label, string value)
        {
            label.text = value ?? "";
            label.EnableInClassList("hidden", string.IsNullOrEmpty(value));
        }
    }
}
