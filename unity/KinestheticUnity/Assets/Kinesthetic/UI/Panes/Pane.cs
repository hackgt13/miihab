using System;
using UnityEngine;
using Kinesthetic.UI;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// One floating surface in a venue: a world-space UIDocument that owns the lifetime of the content
    /// shown on it. This is the only pane type in the project. Where it stands is PaneCarousel's job;
    /// what it shows is IPaneContent's; everything between — size, settings, binding, ticking, chrome,
    /// focus, and turning a world point into a panel point — is here.
    ///
    /// Size. A world-space panel lays its pixels out first and its transform maps them to metres, at
    /// 100 panel pixels per unit (MainMenuSetup measured this from the collider UIDocument maintains).
    /// So a pane lays out at PixelsPerMetre and scales itself down; handing metres straight to
    /// worldSpaceSize lays the panel out a pixel wide.
    ///
    /// Settings. Content changes worldSpaceSize and scale mode, so a pane works on its own copy of the
    /// PanelSettings it was given and never on the shared asset.
    ///
    /// Lifetime. Supplying content hands its lifetime to the pane: replacing it disposes the old one
    /// exactly once, destroying the pane disposes the last. Disabling the pane or its document keeps the
    /// content, and it is bound again when the tree comes back — a UIDocument that is disabled throws
    /// its visual tree away.
    ///
    /// Chrome is optional. With Pane.uxml the content goes into pane-content under a title bar; with no
    /// tree asset it fills the root, which is how an authored document or a test uses a pane.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Pane : MonoBehaviour
    {
        const float PixelsPerMetre = 1000f;
        /// Panel pixels per world unit, measured from the collider UIDocument maintains. BoardBuilder and
        /// NavigationBoard scale by the same figure.
        public const float PanelPixelsPerUnit = 100f;

        UIDocument document;
        PanelSettings ownedSettings, originalSettings;
        VisualElement boundRoot, contentHost, frame, grip;
        Button close;

        UIDocument Document => document ? document : document = GetComponent<UIDocument>();

        public string Id { get; set; }
        public IPaneContent Content { get; private set; }
        public string Title => Content?.Title ?? gameObject.name;
        public bool Focused { get; private set; }

        /// Whether content ticks. A host turns this off for panes nobody is facing; unlike disabling the
        /// component, it keeps the bound tree, so nothing is rebuilt when the pane is faced again.
        public bool Ticking { get; set; } = true;

        /// The whole panel, chrome included — what the pointer picks against.
        public VisualElement ContentRoot => Document.rootVisualElement;

        /// The size the pane is actually drawn at, in metres, including any scale above it.
        public Vector2 WorldSize
        {
            get
            {
                var size = ContentRoot?.worldBound.size ?? Vector2.zero;
                return new Vector2(Document.transform.TransformVector(Vector3.right * size.x).magnitude,
                                   Document.transform.TransformVector(Vector3.up * size.y).magnitude);
            }
        }

        public void SetContent(IPaneContent next)
        {
            if (ReferenceEquals(Content, next)) return;
            if (next != null)
            {
                var size = next.PreferredSize;
                if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
                    throw new ArgumentOutOfRangeException(nameof(next), "Pane content must request a positive finite size.");
                if (!Document.panelSettings) throw new InvalidOperationException("Assign the pane's PanelSettings first.");
                if (!ownedSettings)
                {
                    originalSettings = Document.panelSettings;
                    ownedSettings = Instantiate(Document.panelSettings);
                    ownedSettings.name = "Pane settings";
                    ownedSettings.renderMode = PanelRenderMode.WorldSpace;
                    ownedSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                    Document.panelSettings = ownedSettings;
                }
                Document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
                Document.worldSpaceSize = size * PixelsPerMetre;
                transform.localScale = Vector3.one * (PanelPixelsPerUnit / PixelsPerMetre);
            }
            ReleaseContent();
            contentHost?.Clear();
            Content = next;
            Bind();
        }

        /// Attach chrome and content to the current tree, once per tree. Runs again after the document
        /// has thrown its tree away and built a new one.
        void Bind()
        {
            var root = ContentRoot;
            if (Content == null || root == null || root.panel == null || root == boundRoot) return;

            frame = root.Q("pane-frame");
            grip = root.Q("pane-grip");
            contentHost = root.Q("pane-content") ?? root;
            var title = root.Q<Label>("pane-title");
            if (title != null) title.text = Content.Title;
            close = root.Q<Button>("pane-close");
            if (close != null) { close.clicked -= Close; close.clicked += Close; }

            contentHost.Clear();
            Content.Bind(contentHost);
            boundRoot = root;
            SetFocused(Focused);
        }

        void Update()
        {
            Bind();
            if (Ticking && boundRoot?.panel != null) Content?.Tick();
        }

        public void SetFocused(bool focused)
        {
            Focused = focused;
            frame?.EnableInClassList("k-pane--focused", focused);
            frame?.EnableInClassList("k-pane--idle", !focused);
        }

        /// Hover comes from the raycast, not from :hover, which never fires on a world-space panel. The
        /// grip takes the same k-hot a KButton answers to; the close button is one, so it is told directly.
        public void Highlight(VisualElement element)
        {
            grip?.EnableInClassList("k-hot", element != null && element == grip);
            if (close is KButton button) button.Hot = element != null && element == close;
        }

        public bool IsGrip(VisualElement element) => element != null && grip != null && element == grip;

        public void Close()
        {
            if (PaneHost.Instance) PaneHost.Instance.Forget(this);
            Destroy(gameObject);
        }

        /// Where a world point lands on this pane: in panel coordinates, and as a 0..1 fraction of the
        /// content area from the bottom-left (outside 0..1 when the point is on the chrome).
        ///
        /// A world-space panel already scales and flips its root, so the world point is taken into the
        /// document's space and then through the root's own transform once. Assuming element bounds are
        /// metres, or pixels, is what both earlier guesses in this project got wrong.
        public bool TryProject(Vector3 worldPoint, out Vector2 panelPoint, out Vector2 normalised)
        {
            panelPoint = normalised = default;
            var root = ContentRoot;
            if (!isActiveAndEnabled || !Document.enabled || root?.panel == null) return false;
            var local = Document.transform.InverseTransformPoint(worldPoint);
            panelPoint = new Vector2(local.x, local.y);
            if (!root.contentRect.Contains(root.WorldToLocal(panelPoint))) return false;

            var host = contentHost ?? root;
            var point = host.WorldToLocal(panelPoint);
            var rect = host.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return false;
            normalised = new Vector2((point.x - rect.x) / rect.width, 1 - (point.y - rect.y) / rect.height);
            return true;
        }

        /// Hand a world point to content that wants raw pointer input. Only points on the content area
        /// are sent; chrome is the pane's, not the content's.
        public bool SendPointer(Vector3 worldPoint, bool pressed)
        {
            if (boundRoot == null || Content is not IPanePointerTarget target ||
                !TryProject(worldPoint, out _, out var point) || !Inside(point)) return false;
            target.OnPointer(point, pressed);
            return true;
        }

        internal static bool Inside(Vector2 normalised) =>
            normalised.x >= 0 && normalised.x <= 1 && normalised.y >= 0 && normalised.y <= 1;

        void OnDisable() => boundRoot = null;

        void ReleaseContent() { var previous = Content; Content = null; boundRoot = null; previous?.Dispose(); }

        void OnDestroy()
        {
            ReleaseContent();
            if (document && document.panelSettings == ownedSettings) document.panelSettings = originalSettings;
            if (ownedSettings) Destroy(ownedSettings);
        }
    }
}
