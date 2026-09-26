using System;
using System.IO;
using System.Linq;
using Kinesthetic.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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
        var plaza = MenuPlazaBuilder.Build();
        Transform Anchor(string name) => plaza.GetComponentsInChildren<Transform>().First(t => t.name == name);
        Diorama();
        // The camera is parked at the plaza Viewpoint and left bare on purpose: position is
        // fixed, and look input is installed separately. Nothing here rotates it.
        var eye = Anchor("Viewpoint");
        var camera = new GameObject("Menu camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 46; camera.nearClipPlane = .05f; camera.farClipPlane = 600;
        camera.transform.SetPositionAndRotation(eye.position, eye.rotation);
        var sway = plaza.AddComponent<MenuSway>();
        sway.sway = plaza.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Sway ")).ToArray();
        Resident(Anchor("MiiStand"));
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

    // Late afternoon on the plaza: one warm key, a cool bounce off the lagoon, and just
    // enough fog that the far islands sit behind the course instead of on top of it.
    static void Diorama()
    {
        string skyPath = Root + "/Plaza/PlazaSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetColor("_SkyTint", new Color(.45f, .68f, .89f));
        sky.SetColor("_GroundColor", new Color(.79f, .77f, .70f));
        sky.SetFloat("_AtmosphereThickness", .74f); sky.SetFloat("_Exposure", 1.26f);
        sky.SetFloat("_SunSize", .035f); sky.SetFloat("_SunSizeConvergence", 6);
        EditorUtility.SetDirty(sky); RenderSettings.skybox = sky;

        var sun = new GameObject("Plaza sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.12f; sun.color = new Color(1, .96f, .88f);
        sun.shadows = LightShadows.Soft; sun.shadowStrength = .4f; sun.shadowBias = .04f;
        sun.transform.rotation = Quaternion.Euler(34, 26, 0); RenderSettings.sun = sun;
        var bounce = new GameObject("Lagoon bounce").AddComponent<Light>();
        bounce.type = LightType.Directional; bounce.intensity = .36f; bounce.color = new Color(.84f, .93f, 1);
        bounce.shadows = LightShadows.None; bounce.transform.rotation = Quaternion.Euler(-12, -142, 0);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.80f, .87f, .94f);
        RenderSettings.ambientEquatorColor = new Color(.83f, .81f, .74f);
        RenderSettings.ambientGroundColor = new Color(.58f, .55f, .47f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.81f, .87f, .90f);
        RenderSettings.fogStartDistance = 70; RenderSettings.fogEndDistance = 430;
    }

    // The patient Mii waits on the plaza in a resting seated pose — same rig the activities
    // drive, held at its idle so the menu shows who the app is for.
    static void Resident(Transform stand)
    {
        var slot = new GameObject("Waiting Mii").transform;
        // The avatar and chair art both face local -Z, so the slot turns away from its anchor.
        slot.SetPositionAndRotation(stand.position + Vector3.up * .062f, stand.rotation * Quaternion.Euler(0, 180, 0));
        var actor = new GameObject("Body pose").transform; actor.SetParent(slot, false);
        var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/Mii/KinestheticMii.glb"), actor);
        avatar.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var head = avatar.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "head"); if (head) head.localScale *= .78f;
        foreach (var a in avatar.GetComponentsInChildren<Animation>()) { a.playAutomatically = false; a.enabled = false; }
        foreach (var a in avatar.GetComponentsInChildren<Animator>()) a.enabled = false;
        var renders = avatar.GetComponentsInChildren<Renderer>(); var bounds = renders[0].bounds;
        foreach (var r in renders) bounds.Encapsulate(r.bounds);
        avatar.transform.localScale *= 1.75f / bounds.size.y;
        foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        var rig = actor.gameObject.AddComponent<Kinesthetic.PoseRig>();
        rig.avatar = avatar.transform; rig.seated = true; rig.usePresentationSpace = true;
        rig.Initialize(); rig.Apply(null);
        actor.position += Vector3.up * (stand.position.y + .12f - rig.RightAnkle.position.y);
        var chair = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/BlenderProps/MiiWheelchair.glb"), slot);
        chair.transform.localRotation = Quaternion.Euler(0, 180, 0);
        actor.gameObject.AddComponent<Kinesthetic.Golf.MiiIdleLife>(); // blink + breathing
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
