using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// One floating surface in a venue: a rectangle of UI standing in the world, with chrome and
    /// a content area. It owns everything spatial — transform, size, collider, focus — and knows
    /// nothing about what is inside it.
    ///
    /// Chrome is authored in UXML rather than as 3D meshes, which is only safe because picking
    /// belongs to PanePointer and never to UI Toolkit's event system. Dragging by the grip is a
    /// raycast against this pane's collider, so it cannot be broken by the panel.Pick hole that
    /// world-space panels have.
    ///
    /// The collider is ours rather than the one UIDocument maintains for its panel: sizing a box
    /// to WorldSize is deterministic, and it means picking does not depend on undocumented
    /// behaviour of a feature that shipped one Unity version ago.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Pane : MonoBehaviour
    {
        public const float Thickness = .02f;               // metres; gives the grip something to hit

        [SerializeField] Vector2 worldSize = new(1.2f, .78f);

        public Vector2 WorldSize => worldSize;
        public IPaneContent Content { get; private set; }
        public string Id { get; set; }
        public bool Focused { get; private set; }

        /// The panel root, including chrome — the pointer picks against all of it.
        public VisualElement ContentRoot { get; private set; }

        UIDocument document;
        VisualElement frame, contentArea, grip;
        Label title;
        Button close;
        BoxCollider box;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            box = gameObject.GetComponent<BoxCollider>();
            if (!box) box = gameObject.AddComponent<BoxCollider>();
        }

        /// Called by the host once the UIDocument has a panel and a tree. Separate from Awake
        /// because rootVisualElement is not reliably available until the document is enabled.
        public void Bind(IPaneContent content)
        {
            Content = content;
            if (content != null) worldSize = content.PreferredSize;
            Resize(worldSize);

            ContentRoot = document.rootVisualElement;
            if (ContentRoot == null) return;

            frame = ContentRoot.Q("pane-frame") ?? ContentRoot;
            contentArea = ContentRoot.Q("pane-content");
            grip = ContentRoot.Q("pane-grip");
            title = ContentRoot.Q<Label>("pane-title");
            close = ContentRoot.Q<Button>("pane-close");

            if (title != null && content != null) title.text = content.Title;
            if (close != null) close.clicked += Close;

            // Chrome must not swallow the pick: the pointer resolves elements by rect, so an
            // area that reports itself as hittable would shadow the content underneath it.
            if (contentArea != null) contentArea.pickingMode = PickingMode.Ignore;

            content?.Bind(contentArea ?? ContentRoot);
            SetFocused(false);
        }

        public void Resize(Vector2 metres)
        {
            worldSize = new Vector2(Mathf.Max(.1f, metres.x), Mathf.Max(.1f, metres.y));
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = worldSize;
            if (box) { box.size = new Vector3(worldSize.x, worldSize.y, Thickness); box.center = Vector3.zero; }
        }

        public void SetFocused(bool focused)
        {
            Focused = focused;
            frame?.EnableInClassList("pane-focused", focused);
        }

        /// Highlight whatever the pointer is over, so dwell has something to aim at.
        public void Highlight(VisualElement element)
        {
            grip?.EnableInClassList("pane-hot", element != null && element == grip);
            close?.EnableInClassList("pane-hot", element != null && element == close);
        }

        public bool IsGrip(VisualElement element) => element != null && element == grip;

        public void Tick() => Content?.Tick();

        public void Close()
        {
            Content?.Dispose(); Content = null;
            if (PaneHost.Instance) PaneHost.Instance.Forget(this);
            Destroy(gameObject);
        }

        void OnDestroy() { Content?.Dispose(); Content = null; }
    }
}
