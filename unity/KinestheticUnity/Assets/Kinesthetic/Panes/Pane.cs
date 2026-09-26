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
        /// Upstream's world-panel work calls this SetContent; this half has always called it Bind. Same
        /// operation, two names that met in a merge — aliased rather than renamed, because renaming would
        /// break whichever side was not looking.
        public void SetContent(IPaneContent content) => Bind(content);

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

        /// Hand a world point to content that wants raw pointer input, as a 0..1 fraction of the pane.
        /// Content that does not implement IPanePointerTarget simply does not hear about it.
        public bool SendPointer(Vector3 worldPoint, bool pressed)
        {
            if (ContentRoot == null || Content is not IPanePointerTarget target ||
                !TryProject(worldPoint, out _, out var point)) return false;
            target.OnPointer(point, pressed);
            return true;
        }

        /// Where a world point lands on this pane, in panel coordinates and as a 0..1 fraction.
        ///
        /// Carried over from the world-panel-integration work upstream, which is also the answer to the
        /// question Panes/README left open about WorldPanelPick and GazeDwell disagreeing. Neither guess was
        /// right: a world-space panel already scales and flips its own root transform, so the fix is to undo
        /// that transform once with WorldToLocal rather than to assume element bounds are pixels (GazeDwell)
        /// or to normalise a second time against declared metres (the earlier WorldPanelPick).
        public bool TryProject(Vector3 worldPoint, out Vector2 panelPoint, out Vector2 normalised)
        {
            panelPoint = normalised = default;
            var root = ContentRoot;
            if (!isActiveAndEnabled || document == null || !document.enabled || root?.panel == null) return false;
            var local = document.transform.InverseTransformPoint(worldPoint);
            panelPoint = new Vector2(local.x, local.y);
            var point = root.WorldToLocal(panelPoint);
            var rect = root.contentRect;
            if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(point)) return false;
            normalised = new Vector2((point.x - rect.x) / rect.width, 1 - (point.y - rect.y) / rect.height);
            return true;
        }

    }
}
