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
            if (!pane || !pane.TryProject(hit.point, out panelPoint, out normalised)) return null;
            var picked = Pick(pane.ContentRoot, panelPoint);
            for (var element = picked; element != null; element = element.parent)
                if (element is T target) return target.enabledInHierarchy ? target : null;
            return null;
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
