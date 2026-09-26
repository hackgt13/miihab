using System;
using System.Collections.Generic;
using System.IO;
using Kinesthetic.UI.Remote;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// Fills the two Resources assets the remote UI resolves ids through: every .uss under Assets/Kinesthetic
/// and every Texture2D and Sprite there. Run it after adding a stylesheet or an image a board uses; a Quest
/// build carries whatever the assets held when it was made.
public static class RemoteUiRegistries
{
    const string Folder = "Assets/Kinesthetic/UI/Remote/Resources/RemoteUI";
    const string Search = "Assets/Kinesthetic";

    [MenuItem("Kinesthetic/Remote UI/Refresh registries")]
    public static string Refresh()
    {
        EnsureFolder(Folder);

        var sheets = LoadOrCreate<UiSheetRegistry>(Folder + "/UiSheetRegistry.asset");
        sheets.ids.Clear(); sheets.sheets.Clear();
        foreach (var path in Paths("t:StyleSheet"))
        {
            if (!path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase)) continue;   // .tss themes are not board sheets
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (!sheet) continue;
            sheets.ids.Add(UiSheetRegistry.IdForAssetPath(path));
            sheets.sheets.Add(sheet);
        }
        EditorUtility.SetDirty(sheets);

        var images = LoadOrCreate<UiImageRegistry>(Folder + "/UiImageRegistry.asset");
        images.ids.Clear(); images.images.Clear();
        foreach (var path in Paths("t:Texture2D"))
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture) continue;
            string id = UiSheetRegistry.IdForAssetPath(path);
            images.ids.Add(id);
            images.images.Add(texture);
            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (sub is Sprite sprite) { images.ids.Add(id + "#" + sprite.name); images.images.Add(sprite); }
        }
        EditorUtility.SetDirty(images);

        AssetDatabase.SaveAssets();
        UiSheetRegistry.Invalidate();
        UiImageRegistry.Invalidate();
        string summary = $"Remote UI registries: {sheets.ids.Count} stylesheets, {images.ids.Count} images.";
        Debug.Log(summary);
        return summary;
    }

    static IEnumerable<string> Paths(string filter)
    {
        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets(filter, new[] { Search })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.Ordinal);   // a stable order keeps the asset diff readable
        return paths;
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
