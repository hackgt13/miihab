using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class Pane : MonoBehaviour
    {
        const float PixelsPerMetre = 1000f;
        const float PanelPixelsPerUnit = 100f;
        UIDocument document;
        PanelSettings ownedSettings, originalSettings;
        IPaneContent content;
        VisualElement boundRoot;

        UIDocument Document => document ? document : document = GetComponent<UIDocument>();
        public VisualElement ContentRoot => Document.rootVisualElement;
        public string Title => content?.Title ?? gameObject.name;
        public Vector2 WorldSize
        {
            get
            {
                var size = ContentRoot?.worldBound.size ?? Vector2.zero;
                return new Vector2(Document.transform.TransformVector(Vector3.right * size.x).magnitude,
                    Document.transform.TransformVector(Vector3.up * size.y).magnitude);
            }
        }

        // An authored UIDocument works without managed content. Supplying content transfers its lifetime
        // to this host; disabling a pane preserves the content and rebinds its tree when it is shown again.
        public void SetContent(IPaneContent next)
        {
            if (ReferenceEquals(content, next)) return;
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
            ContentRoot?.Clear();
            content = next;
            BindContent();
        }

        void BindContent()
        {
            var root = ContentRoot;
            if (content == null || root == null || root.panel == null || root == boundRoot) return;
            root.Clear();
            content.Bind(root);
            boundRoot = root;
        }

        void Update()
        {
            BindContent();
            if (boundRoot?.panel != null) content?.Tick();
        }

        public bool TryProject(Vector3 worldPoint, out Vector2 panelPoint, out Vector2 normalised)
        {
            panelPoint = normalised = default;
            var root = ContentRoot;
            if (!isActiveAndEnabled || !Document.enabled || root?.panel == null) return false;
            var local = Document.transform.InverseTransformPoint(worldPoint);
            panelPoint = new Vector2(local.x, local.y);
            // World-space UITK already scales and flips its root transform. Undo that transform once
            // to obtain content pixels instead of assuming that element bounds are pixel coordinates.
            var point = root.WorldToLocal(panelPoint);
            var rect = root.contentRect;
            if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(point)) return false;
            normalised = new Vector2((point.x - rect.x) / rect.width, 1 - (point.y - rect.y) / rect.height);
            return true;
        }

        public bool SendPointer(Vector3 worldPoint, bool pressed)
        {
            if (boundRoot == null || content is not IPanePointerTarget target || !TryProject(worldPoint, out _, out var point)) return false;
            target.OnPointer(point, pressed);
            return true;
        }

        void OnDisable() => boundRoot = null;
        void ReleaseContent() { var previous = content; content = null; boundRoot = null; previous?.Dispose(); }
        void OnDestroy()
        {
            ReleaseContent();
            if (document && document.panelSettings == ownedSettings) document.panelSettings = originalSettings;
            if (ownedSettings) Destroy(ownedSettings);
        }
    }
}
