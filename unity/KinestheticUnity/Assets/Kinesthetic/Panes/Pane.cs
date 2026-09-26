using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// PLACEHOLDER — WorldPanelPick (ff2c0a9) needs a Pane component, but its source file was not committed,
    /// so master did not compile. This is the smallest shape WorldPanelPick reads: a world-space UIDocument's
    /// content root and the pane's size in metres. Replace it with the real Pane when that lands.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Pane : MonoBehaviour
    {
        [Tooltip("Width and height of the pane in metres.")]
        public Vector2 worldSize = new(1.2f, .8f);
        public Vector2 WorldSize => worldSize;
        public VisualElement ContentRoot => GetComponent<UIDocument>()?.rootVisualElement;
    }
}
