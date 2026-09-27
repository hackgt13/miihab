using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A person you know, small: their face and their name in a pill. Marks where a friend is — in a group
    /// on the lobby's list — so the face does the recognising before the name is read.
    ///
    ///     new KMiiTag { variant = friend.mii, text = friend.displayName }
    ///
    /// Always jungle-edged: it only ever names a friend, which is good news on the row it sits in.
    [UxmlElement]
    public partial class KMiiTag : VisualElement
    {
        const string Block = "k-mii-tag";
        readonly KMiiFace face;
        readonly Label label;

        [UxmlAttribute] public int variant { get => face.variant; set => face.variant = value; }
        [UxmlAttribute] public string text { get => label.text; set => label.text = value; }

        public KMiiTag()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Option");
            pickingMode = PickingMode.Ignore;
            face = new KMiiFace();
            face.AddToClassList(Block + "__face");
            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(Block + "__name");
            Add(face); Add(label);
        }
    }
}
