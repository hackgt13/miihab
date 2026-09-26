using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// Something a person in the venue is saying: a paper balloon with a tail pointing back at the speaker.
    /// The screen puts the words inside (usually a KText) and says which side the speaker stands on; the
    /// balloon, its edge and its tail are the component's.
    ///
    ///     <k:KBubble tail="Left"><k:KText name="line" size="Heading" /></k:KBubble>
    ///
    /// The tail is painted in code, so its colours come from Palette.cs; the balloon's fill and edge are the
    /// same two values in Bubble.uss, so the join does not show.
    [UxmlElement]
    public partial class KBubble : VisualElement
    {
        public enum Tail { Left, Center, Right, None }

        const string Block = "k-bubble";
        const float TailWidth = 34, TailDepth = 30, TailInset = 56, Edge = 2;
        Tail tailValue;

        [UxmlAttribute]
        public Tail tail { get => tailValue; set { tailValue = value; KStyles.Variant(this, Block, value); MarkDirtyRepaint(); } }

        public KBubble()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Bubble");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            tail = Tail.Left;
        }

        void Draw(MeshGenerationContext context)
        {
            if (tailValue == Tail.None) return;
            var size = layout.size;   // local space: the balloon's own box, from its top-left
            if (size.x <= 0) return;
            float bottom = size.y - Edge;
            float root = tailValue switch { Tail.Left => TailInset, Tail.Right => size.x - TailInset, _ => size.x / 2 };
            float tip = tailValue switch { Tail.Left => root - TailWidth * .9f, Tail.Right => root + TailWidth * .9f, _ => root };
            var p = context.painter2D;
            p.lineJoin = LineJoin.Round;
            p.fillColor = Palette.Panel; p.strokeColor = Palette.Line; p.lineWidth = Edge;
            p.BeginPath();
            p.MoveTo(new(root - TailWidth / 2, bottom));
            p.LineTo(new(tip, bottom + TailDepth));
            p.LineTo(new(root + TailWidth / 2, bottom));
            p.Fill();
            p.BeginPath();
            p.MoveTo(new(root - TailWidth / 2, bottom + Edge / 2));
            p.LineTo(new(tip, bottom + TailDepth));
            p.LineTo(new(root + TailWidth / 2, bottom + Edge / 2));
            p.Stroke();
        }
    }
}
