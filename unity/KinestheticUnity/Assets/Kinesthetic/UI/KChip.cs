using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A device or connection and whether it is there: a dot that carries the state, and a line of copy.
    /// One chip for the camera in the studio, the AirPods on the course and the lanes' sensor, where there
    /// used to be three.
    ///
    ///     <k:KChip text="AirPods connected" state="Good" />
    [UxmlElement]
    public partial class KChip : VisualElement
    {
        public enum State { Idle, Live, Good, Trouble }

        const string Block = "k-chip";
        readonly Label label = new Label();
        State stateValue;

        [UxmlAttribute]
        public string text { get => label.text; set => label.text = value; }

        [UxmlAttribute]
        public State state { get => stateValue; set { stateValue = value; KStyles.Variant(this, Block, value); } }

        public KChip()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Chip");
            pickingMode = PickingMode.Ignore;

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList(Block + "__dot");
            Add(dot);
            label.pickingMode = PickingMode.Ignore;
            label.AddToClassList(Block + "__label");
            Add(label);

            state = State.Idle;
        }
    }
}
