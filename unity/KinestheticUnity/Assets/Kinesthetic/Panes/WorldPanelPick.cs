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
    /// The hit is normalised against the pane's own declared size in metres rather than against
    /// collider geometry, so it holds whatever collider UIDocument decides to maintain and
    /// whatever the panel's reference resolution is. GazeDwell.Under describes this step in its
    /// comment but does not perform it — it compares metres against pixels, which only agrees
    /// when a panel happens to be authored 1:1. Do not copy that arithmetic.
    ///
    /// UI Toolkit's y axis runs downward from the top-left; a transform's local space runs
    /// upward from its origin. The flip below is the whole reason this is fiddly.
    public static class WorldPanelPick
    {
        /// The element of type T under `ray` within `maxDistance`, or null. `panelPoint` is in
        /// panel coordinates; `normalised` is 0..1 from the bottom-left, which is what a Camera
        /// viewport and a RenderTexture both already expect.
        public static T Under<T>(Ray ray, float maxDistance, int layerMask,
                                 out Pane pane, out Vector2 panelPoint, out Vector2 normalised)
            where T : VisualElement
        {
            pane = null; panelPoint = default; normalised = default;
            if (!Physics.Raycast(ray, out var hit, maxDistance, layerMask)) return null;

            pane = hit.collider.GetComponentInParent<Pane>();
            if (!pane) return null;                                  // something else is in the way
            var root = pane.ContentRoot;
            if (root == null) return null;

            var metres = pane.WorldSize;
            if (metres.x <= 0 || metres.y <= 0) return null;
            var local = pane.transform.InverseTransformPoint(hit.point);
            normalised = new Vector2(local.x / metres.x + .5f, local.y / metres.y + .5f);

            var size = root.contentRect.size;
            if (size.x <= 0 || size.y <= 0) return null;              // not laid out yet this frame
            panelPoint = new Vector2(normalised.x * size.x, (1 - normalised.y) * size.y);

            foreach (var element in root.Query<T>().ToList())
                if (element.worldBound.Contains(panelPoint)) return element;
            return null;
        }
    }
}
