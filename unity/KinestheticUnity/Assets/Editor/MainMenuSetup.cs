using System;
using System.IO;
using System.Linq;
using Kinesthetic.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class MainMenuSetup
{
    const string Root = "Assets/Kinesthetic/Menu";
    public const string ScenePath = Root + "/MainMenu.unity";

    [MenuItem("Kinesthetic/Menu/Create main menu")]
    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before creating the menu.");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if(scene.isDirty && !string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        }
        Directory.CreateDirectory(Root + "/Resources/Menu"); AssetDatabase.Refresh();
        foreach(string name in new[]{"Golf", "Studio"})
        {
            var texture = AssetImporter.GetAtPath(Root + "/Art/" + name + ".png") as TextureImporter;
            if (!texture) throw new InvalidOperationException("Missing activity artwork: " + name);
            texture.textureType = TextureImporterType.Default; texture.mipmapEnabled = false;
            texture.npotScale = TextureImporterNPOTScale.None;
            texture.maxTextureSize = 2048; texture.textureCompression = TextureImporterCompression.CompressedHQ;
            texture.wrapMode = TextureWrapMode.Clamp; texture.SaveAndReimport();
        }
        foreach(string name in new[]{"MorningPlay", "Hover", "Select", "Back"})
        {
            var importer = AssetImporter.GetAtPath(Root + "/Audio/" + name + ".wav") as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing menu audio: " + name);
            var settings = importer.defaultSampleSettings;
            settings.loadType = name == "MorningPlay" ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = name == "MorningPlay" ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = .75f;
            importer.defaultSampleSettings = settings; importer.forceToMono = name != "MorningPlay";
            importer.SaveAndReimport();
        }
        var panel = Panel("MenuPanel", 0);
        var navigationPanel = Panel("NavigationPanel", 200);
        var navigation = new GameObject("Activity navigation", typeof(UIDocument), typeof(ActivityNavigation));
        var doc = navigation.GetComponent<UIDocument>(); doc.panelSettings = navigationPanel;
        doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/Navigation.uxml");
        var router = navigation.GetComponent<ActivityNavigation>();
        router.menuMusic = Audio("MorningPlay"); router.hoverSound = Audio("Hover"); router.selectSound = Audio("Select"); router.backSound = Audio("Back");
        PrefabUtility.SaveAsPrefabAsset(navigation, Root + "/Resources/Menu/ActivityNavigation.prefab");
        UnityEngine.Object.DestroyImmediate(navigation);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Menu camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
        camera.cullingMask = 0; camera.allowHDR = false;
        var menu = new GameObject("RehabMii activity menu", typeof(UIDocument), typeof(MainMenuController));
        var menuDoc = menu.GetComponent<UIDocument>(); menuDoc.panelSettings = panel;
        menuDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/MainMenu.uxml");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        foreach (string required in new[] { AdaptiveGolfSceneSetup.ScenePath, RehabSceneSetup.ScenePath })
        {
            var entry = scenes.FirstOrDefault(s => s.path == required);
            if (entry == null) scenes.Add(new EditorBuildSettingsScene(required, true)); else entry.enabled = true;
        }
        EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
        return "Main menu created. Golf and Movement Studio are included; menu is the default launch scene.";
    }
    static AudioClip Audio(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".wav");
    static PanelSettings Panel(string name, float order)
    {
        string path = Root + "/" + name + ".asset";
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (!panel)
        {
            panel = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Kinesthetic/Rehab/RehabPanel.asset"));
            panel.name = name; AssetDatabase.CreateAsset(panel, path);
        }
        panel.referenceResolution = new Vector2Int(1600,900); panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f;
        panel.sortingOrder = order; EditorUtility.SetDirty(panel); return panel;
    }
}
