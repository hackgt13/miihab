using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies coordinator/activities.json into Resources so Unity and the coordinator read one list.
///
/// Generated, never hand-edited — the same rule as every other asset in this project. Editing the
/// copy is pointless because the next sync overwrites it; edit coordinator/activities.json.
/// </summary>
public static class ActivityCatalogSync
{
    const string Destination = "Assets/Kinesthetic/Activities/Resources/Activities/activities.json";

    [MenuItem("Kinesthetic/Activities/Sync catalog from coordinator")]
    public static string Sync()
    {
        var repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        var source = Path.Combine(repo, "coordinator/activities.json");
        if (!File.Exists(source)) throw new FileNotFoundException("Activity catalog not found", source);
        Directory.CreateDirectory(Path.GetDirectoryName(Destination));
        File.Copy(source, Destination, true);
        AssetDatabase.ImportAsset(Destination);
        Kinesthetic.Activities.ActivityCatalog.Invalidate();
        var count = Kinesthetic.Activities.ActivityCatalog.All.Length;
        return $"Synced {count} activities from coordinator/activities.json.";
    }
}
