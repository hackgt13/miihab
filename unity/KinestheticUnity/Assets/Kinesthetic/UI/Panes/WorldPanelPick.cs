using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// Ray to element, in one place.
    ///
    /// UI Toolkit's own picking is unavailable here: panel.Pick returns null on a world-space
    /// panel at every point, including well inside the root, which is why GazeDwell resolves
    /// elements by their own rects instead. That workaround should exist once, not once per
    /// panel, so every world-space surface in the project can share it.
    ///
    /// Pane projects through the UIDocument and its root transform, including their scale and
    /// rotation. Picking follows paint order so a modal shade blocks controls underneath it.
    ///
    /// The nearest collider answers, with one exception: a pane whose root ignores picking is a layer,
    /// not a surface — the navigation board that rides in front of the view, empty until a dialog is up —
    /// and where nothing on it is picked the ray carries on to the next collider. A shade or a card on it
    /// is picked, and blocks, as a modal should.
    public static class WorldPanelPick
    {
        static readonly RaycastHit[] hits = new RaycastHit[8];

        /// The element of type T under `ray` within `maxDistance`, or null. `panelPoint` is in
        /// panel coordinates; `normalised` is 0..1 from the bottom-left, which is what a Camera
        /// viewport and a RenderTexture both already expect.
        public static T Under<T>(Ray ray, float maxDistance, int layerMask,
                                 out Pane pane, out Vector2 panelPoint, out Vector2 normalised)
            where T : VisualElement
        {
            pane = null; panelPoint = default; normalised = default;
            int count = Physics.RaycastNonAlloc(ray, hits, maxDistance, layerMask);
            System.Array.Sort(hits, 0, count, ByDistance.Instance);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                pane = hit.collider.GetComponentInParent<Pane>();
                if (!pane || !pane.TryProject(hit.point, out panelPoint, out normalised)) return null;
                var root = pane.ContentRoot;
                var picked = Pick(root, panelPoint);
                if (picked == null && root.pickingMode == PickingMode.Ignore) continue;   // a layer, empty here
                for (var element = picked; element != null; element = element.parent)
                    if (element is T target) return target.enabledInHierarchy ? target : null;
                return null;
            }
            pane = null; panelPoint = default; normalised = default;
            return null;
        }

        sealed class ByDistance : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly ByDistance Instance = new();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        /// The enabled, named Button under `ray` on `owner`'s own pane, or null. Every menu input —
        /// GazeDwell's dwell, PanePointerInput's cursor, CarouselChrome's click — asks this, so a head and
        /// a mouse land on the same name and PressGate turns that name into one action.
        ///
        /// The nearest collider answers, and nothing else is consulted: a pane standing in front of
        /// `owner` makes this null for `owner`. That is the whole hit-test rule between the ring and its
        /// arrows — the chrome panel sits wider and further back, so the facing pane wins every ray in
        /// front of it and the arrows answer only past its edge.
        public static Button NamedButtonOn(GameObject owner, Ray ray, float maxDistance)
        {
            var button = Under<Button>(ray, maxDistance, Physics.DefaultRaycastLayers, out var pane, out _, out _);
            return pane && pane.gameObject == owner && !string.IsNullOrEmpty(button?.name) ? button : null;
        }

        /// Picking projects through a Pane, so anything picked needs one. With no content set a Pane is
        /// pure projection: it changes no size, no settings and no collider.
        public static void MakePickable(GameObject owner)
        {
            if (!owner.GetComponent<Pane>()) owner.AddComponent<Pane>();
        }

        static VisualElement Pick(VisualElement element, Vector2 point)
        {
            if (element.resolvedStyle.display == DisplayStyle.None || !element.visible) return null;
            bool inside = element.ContainsPoint(element.WorldToLocal(point));
            if (!inside) return null;
            for (int i = element.hierarchy.childCount - 1; i >= 0; i--)
            {
                var hit = Pick(element.hierarchy[i], point);
                if (hit != null) return hit;
            }
            return inside && element.pickingMode == PickingMode.Position ? element : null;
        }
    }
}
