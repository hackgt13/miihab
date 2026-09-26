using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Kinesthetic.Shell;

/// Aims a ray at the centre of a named button on the facing pane and asks whether the pane's own
/// collider maths finds that same button. This is the exact path PanePointerInput and GazeDwell use.
public static class PickProbe
{
    public static void Main()
    {
        var carousel = Object.FindAnyObjectByType<PaneCarousel>();
        var cam = Camera.main;
        if (carousel == null || cam == null) { Debug.Log("PICK no carousel/camera"); return; }

        foreach (var slot in carousel.Slots)
        {
            var doc = slot.pane.GetComponent<UIDocument>();
            var pointer = slot.pane.GetComponent<PanePointerInput>();
            var root = doc?.rootVisualElement;
            if (root == null) { Debug.Log($"PICK {slot.id}: no root"); continue; }
            var buttons = root.Query<Button>().ToList().Where(b => !string.IsNullOrEmpty(b.name)).ToList();
            var collider = slot.pane.GetComponentInChildren<Collider>();
            Debug.Log($"PICK {slot.id}: pointer={(pointer == null ? "MISSING" : "present")} buttons={buttons.Count} " +
                      $"collider={(collider is BoxCollider bc ? bc.size.ToString("F1") : "none")}");

            foreach (var b in buttons.Take(2))
            {
                // Element centre, in the panel's own centred y-up units, straight to world.
                var centre = b.worldBound.center;
                var world = collider.transform.TransformPoint(new Vector3(centre.x, centre.y, 0));
                var ray = new Ray(cam.transform.position, (world - cam.transform.position).normalized);
                string hitName = "no hit";
                if (Physics.Raycast(ray, out var hit, 40f) && hit.collider.transform.IsChildOf(slot.pane))
                {
                    var local = hit.collider.transform.InverseTransformPoint(hit.point);
                    var p = new Vector2(local.x, local.y);
                    var found = buttons.FirstOrDefault(x => x.worldBound.Contains(p));
                    hitName = found?.name ?? "hit pane, no button";
                }
                Debug.Log($"PICK   aim '{b.name}' -> resolved '{hitName}' {(hitName == b.name ? "OK" : "MISMATCH")}");
            }
        }
    }
}
