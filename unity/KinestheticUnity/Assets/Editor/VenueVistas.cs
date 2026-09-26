using System.IO;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Activities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// The picture on the back wall of every doorway's vestibule: what the venue looks like from the patient's
/// eyes, taken once here and shown as a still while the person walks in. A fake prerender rather than a live
/// portal — one texture per venue, no second camera at runtime — and it earns its place because the cut to
/// the real scene then lands on a view the person was already looking at.
///
/// Captured from the scene's own camera — the composed view its author framed, and the one the Mac shows
/// on arrival — or, for a scene with no camera, from its EyeAnchor. Not the eye in every case: in edit mode
/// nothing has posed the avatar yet, and the seat's eyes sit inside its head. Rerun after a venue's scene
/// changes its look; MenuPlazaBuilder reads the PNGs from Plaza/Vistas by venue name and falls back to the
/// plain shade for a venue without one.
public static class VenueVistas
{
    public const string Directory = "Assets/Kinesthetic/Menu/Plaza/Vistas";
    // The vestibule's back wall is 1.7 x 2.22 m (MenuPlazaBuilder.Doorway); the picture has its shape.
    public const int Width = 920, Height = 1200;
    const float FieldOfView = 58;

    public static string Path(string venue) => $"{Directory}/{venue}.png";

    [MenuItem("Kinesthetic/Menu/Capture venue vistas")]
    public static string Capture()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        var open = SceneManager_ActivePath();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (s.isDirty && !string.IsNullOrEmpty(s.path)) EditorSceneManager.SaveScene(s);
        }
        System.IO.Directory.CreateDirectory(Directory);
        ActivityCatalog.Invalidate();
        var taken = new System.Collections.Generic.List<string>();
        foreach (var entry in ActivityCatalog.All)
        {
            string scenePath = AssetDatabase.FindAssets("t:Scene " + entry.Scene)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == entry.Scene);
            if (scenePath == null) { Debug.LogWarning($"Venue vistas: no scene '{entry.Scene}' for '{entry.Venue}'; its doorway keeps the plain shade."); continue; }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Shoot(entry.Venue);
            taken.Add(entry.Venue);
        }
        if (!string.IsNullOrEmpty(open)) EditorSceneManager.OpenScene(open, OpenSceneMode.Single);
        AssetDatabase.Refresh();
        return "Captured vistas for: " + string.Join(", ", taken);
    }

    static string SceneManager_ActivePath() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

    static void Shoot(string venue)
    {
        var anchor = Object.FindAnyObjectByType<EyeAnchor>();
        var spectator = Camera.main ?? Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault();
        var go = new GameObject("Vista camera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = spectator ? Mathf.Max(spectator.fieldOfView, FieldOfView) : FieldOfView; cam.nearClipPlane = .05f; cam.farClipPlane = spectator ? spectator.farClipPlane : 800;
        cam.clearFlags = spectator ? spectator.clearFlags : CameraClearFlags.Skybox;
        cam.backgroundColor = spectator ? spectator.backgroundColor : Color.black;
        cam.cullingMask = ~(1 << FirstPersonView.OwnBodyLayer);
        if (spectator) go.transform.SetPositionAndRotation(spectator.transform.position, spectator.transform.rotation);
        else if (anchor)
        {
            // Out of the patient's own head, the way FirstPersonView does at runtime: everything of their
            // avatar but the torso goes on the culled layer. The scene is reopened unsaved, so this sticks
            // to the picture and not to the scene.
            if (anchor.ownBody)
                foreach (var r in anchor.ownBody.GetComponentsInChildren<Renderer>(true))
                    if (r.name != "MiiBody") r.gameObject.layer = FirstPersonView.OwnBodyLayer;
            // The same rest pose FirstPersonView gives the eyes: at the seat, level, facing the target.
            var seat = anchor.follow ? anchor.follow : anchor.transform;
            go.transform.position = seat.position + Vector3.up * anchor.eyeHeight;
            var toTarget = anchor.lookAt ? Vector3.ProjectOnPlane(anchor.lookAt.position - go.transform.position, Vector3.up) : Vector3.zero;
            go.transform.rotation = toTarget.sqrMagnitude > .0001f ? Quaternion.LookRotation(toTarget.normalized, Vector3.up) : seat.rotation;
        }

        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = target;
        cam.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        picture.Apply();
        RenderTexture.active = previous;
        cam.targetTexture = null;
        File.WriteAllBytes(Path(venue), picture.EncodeToPNG());
        Object.DestroyImmediate(picture);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(go);

        string path = Path(venue);
        AssetDatabase.ImportAsset(path);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
            importer.mipmapEnabled = true; importer.streamingMipmaps = false;
            importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }
}
