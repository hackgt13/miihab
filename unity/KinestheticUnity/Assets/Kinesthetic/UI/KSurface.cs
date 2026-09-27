using UnityEngine.UIElements;
namespace Kinesthetic.UI
{
    [UxmlElement]
    public partial class KSurface : VisualElement
    {
        public enum Tone { Paper, Glass, Scrim, Well }
        public enum Density { Compact, Comfortable, Flush }
        Tone toneValue; Density densityValue;
        [UxmlAttribute] public Tone tone { get => toneValue; set { toneValue = value; KStyles.Variant(this, "k-surface", value); } }
        [UxmlAttribute] public Density density { get => densityValue; set { densityValue = value; KStyles.Variant(this, "k-surface", value); } }
        public KSurface() { AddToClassList("k-surface"); KStyles.Attach(this, "Content"); tone = Tone.Paper; density = Density.Comfortable; }
    }
}
