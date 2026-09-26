using System;
using System.IO;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Menu;
using Kinesthetic.Shell;
using Kinesthetic.UI.Remote;
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
        var panel = Panel("MenuPanel", 0, world: true);
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
        // The camera is parked at the plaza Viewpoint and left bare on purpose: position is fixed and look
        // input is installed separately. It only starts facing the menu board so the first frame reads.
        var eye = Anchor("Viewpoint");
        var board = Anchor("MenuBoard");
        var camera = new GameObject("Menu camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 46; camera.nearClipPlane = .05f; camera.farClipPlane = 600;
        camera.transform.SetPositionAndRotation(eye.position, Quaternion.LookRotation(board.position - eye.position, Vector3.up));
        var sway = plaza.AddComponent<MenuSway>();
        sway.sway = plaza.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Sway ")).ToArray();
        Resident(Anchor("MiiStand"));
        // The menu is geometry standing on the plaza, not an overlay painted over it. That is what lets a
        // moving head work at all: a screen-space panel is composited to the backbuffer after the camera
        // renders, so it stays glued to the screen wherever the camera looks, and never reaches a stereo eye.
        // A world-space panel lays its pixels out first and the transform maps them to metres; worldSpaceSize
        // alone does not govern the result. It lays out 100 panel pixels per world unit — confirmed from the
        // collider UIDocument maintains, which comes out 16x9 for a 1600x900 panel — so scale from that to the
        // width the plaza wants. Element rects are in those same units, which is how GazeDwell resolves them.
        const float pixelsPerUnit = 100f, wantedWidth = 3.7f;

        // Deliberately NOT carrying Kinesthetic.Panes.Pane onto these. It reuses whatever BoxCollider is
        // already on the object — which here is the 16x9 one UIDocument maintains for its own panel — and
        // resizes it to its worldSize, 1.2 x 0.78 m. That would shrink the board's pickable area to a patch
        // in the middle, and both the dwell and the pointer resolve elements through that collider.
        // Every pane is a board on the wire (UI/Remote): the Mac mirrors its tree to the headset's copy of
        // this scene, where a pane with the same id shows it and sends its presses back. The id is the slot's.
        GameObject Pane(string label, string uxml, bool board_, string boardId)
        {
            var go = board_
                ? new GameObject(label, typeof(UIDocument), typeof(MainMenuController), typeof(GazeDwell), typeof(PanePointerInput))
                : new GameObject(label, typeof(UIDocument), typeof(GazeDwell), typeof(PanePointerInput));
            go.AddComponent<RemoteBoard>().id = boardId;
            var d = go.GetComponent<UIDocument>();
            d.panelSettings = panel;
            d.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            d.worldSpaceSize = new Vector2(1600, 900);   // panel pixels, not metres; the transform maps them
            d.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/" + uxml);
            go.transform.localScale = Vector3.one * (wantedWidth / (d.worldSpaceSize.x / pixelsPerUnit));
            return go;
        }

        // Four panes standing in a ring around the viewpoint rather than four layers stacked on one board.
        // Coaching is the first turn to the LEFT, the gallery the second, and friends a turn to the RIGHT,
        // named as angles because that is the design rather than a consequence of the order they were
        // adopted in. Nothing behind: a seated person should never be asked to turn around, and the ring
        // brings panes to them.
        //
        // Coaching takes the slot next to the board because the two are one reading: the board says what to
        // do today, and coaching says what plan that came from. The gallery moved one slot further out and
        // lost nothing by it — every pane also has a button on the board (BuildPaneLinks), so the gallery is
        // still one press from home however far round it stands, and only the arrows count slots.
        var rig = new GameObject("Menu carousel", typeof(PaneCarousel));
        var carousel = rig.GetComponent<PaneCarousel>();
        // 60 degrees, not 90, so the neighbours show an edge instead of hiding behind the board — and not
        // 45, which looks closer but is worse: the board fills +/-27.9 deg of a +/-38.2 deg view, so a
        // neighbour at 45 puts its near edge at 17 deg, inside the board's silhouette and further away, and
        // the board simply covers it. An edge only clears the board past twice the pane's own half-angle
        // (55.7 deg) and leaves the screen past 66.1, so the window is narrow and 60 sits in the middle of
        // it. 62, which is the smallest spacing that leaves the arrows reachable. Measured on the scene: both
        // arrows sit centred at 30.5 degrees with their outer edge at 32.9, so a neighbour whose near edge
        // is at spacing minus 27.9 only stops covering them past 60.8. 62 clears with 1.2 degrees to spare
        // and still shows 4.1 degrees of the neighbouring pane. The sliver is the gap between the pane's
        // edge and the frame's, so it
        // widens as the panes come closer — 9 degrees at 57 against 6 at 60 — and 57 still keeps 1.3 degrees
        // of clearance past the board's own edge, where 56 leaves only 0.3.
        carousel.spacingDegrees = 62;
        var menu = Pane("RehabMii activity menu", "MainMenu.uxml", true, "menu.home");
        var coaching = Pane("Coaching", "Coaching.uxml", false, "menu.coaching");
        var gallery = Pane("Activity gallery", "Gallery.uxml", false, "menu.gallery");
        var friendsPane = Pane("Friends", "Friends.uxml", false, "menu.friends");
        carousel.Frame(eye.position, board.position);
        carousel.Adopt(
            new PaneCarousel.Slot("home", "Today", menu.transform, 0),
            new PaneCarousel.Slot("coaching", "Coaching", coaching.transform, -62),
            new PaneCarousel.Slot("gallery", "Activities", gallery.transform, -124),
            new PaneCarousel.Slot("friends", "Friends", friendsPane.transform, 62));

        // The arrows ride their own panel, wider than the panes and a little further out, so the facing pane
        // answers the gaze everywhere in front of it and the arrows answer only past its edge.
        var chrome = CarouselChrome.Stand(carousel, Panel("CarouselPanel", 1, world: true),
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Kinesthetic/Shell/CarouselChrome.uxml"));
        chrome.gameObject.AddComponent<RemoteBoard>().id = "menu.chrome";
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        ConfigureMacBuildScenes();
        return "Main menu created. Golf, Movement Studio and Bowling are included; menu is the default launch scene.";
    }

    [MenuItem("Kinesthetic/Menu/Configure Mac build scenes")]
    public static string ConfigureMacBuildScenes()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        foreach (string required in new[] { AdaptiveGolfSceneSetup.ScenePath, RehabSceneSetup.ScenePath, BowlingSceneSetup.ScenePath, TutorialSceneSetup.ScenePath })
        {
            var entry = scenes.FirstOrDefault(s => s.path == required);
            if (entry == null) scenes.Add(new EditorBuildSettingsScene(required, true)); else entry.enabled = true;
        }
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        return "Mac build scenes configured. MainMenu boots first; Golf, Movement Studio, Bowling and the intro are enabled.";
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
        actor.gameObject.AddComponent<MenuResident>();                 // pose the rig once in the player
        actor.gameObject.AddComponent<Kinesthetic.Golf.MiiIdleLife>(); // blink + breathing
    }

    static AudioClip Audio(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".wav");
    static PanelSettings Panel(string name, float order, bool world = false)
    {
        string path = Root + "/" + name + ".asset";
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (!panel)
        {
            panel = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Kinesthetic/Rehab/RehabPanel.asset"));
            panel.name = name; AssetDatabase.CreateAsset(panel, path);
        }
        panel.referenceResolution = new Vector2Int(1600,900);
        // A world-space panel has no screen to scale against: its pixels are laid out once and then mapped to
        // metres by worldSpaceSize. Screen-space panels keep scaling with the window as before.
        panel.scaleMode = world ? PanelScaleMode.ConstantPixelSize : PanelScaleMode.ScaleWithScreenSize;
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f;
        // A world-space panel renders as geometry, and UIDocument maintains a collider for it — that collider
        // is what GazeDwell picks against. Screen-space stays for in-activity chrome that is still Mac-only.
        panel.renderMode = world ? PanelRenderMode.WorldSpace : PanelRenderMode.ScreenSpaceOverlay;
        panel.sortingOrder = order; EditorUtility.SetDirty(panel); return panel;
    }
}
