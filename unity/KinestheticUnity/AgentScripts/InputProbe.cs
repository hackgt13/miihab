using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

public static class InputProbe
{
    public static void Main()
    {
        Debug.Log($"INPUT EventSystem={(Object.FindAnyObjectByType<EventSystem>() == null ? "NONE" : "present")}");
        foreach (var doc in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = doc.gameObject;
            var cols = go.GetComponentsInChildren<Collider>(true);
            string colliders = string.Join(" | ", cols.Select(c =>
                $"{c.GetType().Name} on '{c.gameObject.name}' size={(c is BoxCollider b ? b.size.ToString("F2") : "?")} enabled={c.enabled}"));
            string comps = string.Join(",", go.GetComponents<Component>().Select(c => c.GetType().Name));
            Debug.Log($"INPUT '{go.name}' panel={(doc.panelSettings ? doc.panelSettings.name : "NONE")} comps=[{comps}] colliders=[{colliders}]");
        }
        foreach (var ps in Resources.FindObjectsOfTypeAll<PanelSettings>())
            Debug.Log($"INPUT panelSettings '{ps.name}' renderMode={ps.renderMode}");
    }
}
