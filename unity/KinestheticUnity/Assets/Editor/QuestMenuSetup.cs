using System;
using System.Linq;
using Kinesthetic.Golf;
using Kinesthetic.Menu;
using Kinesthetic.Shell;
using Kinesthetic.UI.Remote;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// The plaza in a headset: you sit at the plaza's viewpoint, the menu's panes stand round you as replicas
// filled by the Mac's trees over /ui, and every activity is entered by walking through its building's
// door (PlazaApproach, on the Mac's cue). Generated from the Mac's MainMenu scene; the headset runs none of
// the menu's logic — the Mac keeps the coordinator, the navigation and the ring, and this copy follows.
public static class QuestMenuSetup
{
    public const string ScenePath = "Assets/Kinesthetic/Menu/QuestMenu.unity";

    [MenuItem("Kinesthetic/Quest/Create headset menu scene")]
    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var source = EditorSceneManager.OpenScene(MainMenuSetup.ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(source, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        QuestRigBuilder.DisableSceneCameras();                       // the Mac's menu camera and its listener

        // The Mac's menu logic — the coordinator's figures, the navigation, the panes' own content, the
        // arrows' handlers — stays on the Mac. What is left here is geometry: panes that show what the Mac
        // mirrors and send presses back, and a ring that turns when the Mac says it turned (QuestMenuMirror).
        foreach (var controller in UnityEngine.Object.FindObjectsByType<MainMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(controller);
        foreach (var chrome in UnityEngine.Object.FindObjectsByType<CarouselChrome>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(chrome);

        // Replica rather than Auto, so playing this scene on the Mac previews the headset's side and never
        // mirrors these boards out as a second host. The board shows the waiting card until the Mac connects.
        var waiting = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(QuestRehabSetup.WaitingPath);
        if (!waiting) throw new InvalidOperationException("Missing " + QuestRehabSetup.WaitingPath);
        var boards = UnityEngine.Object.FindObjectsByType<RemoteBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (boards.Length == 0) throw new InvalidOperationException("MainMenu.unity has no boards; run Kinesthetic/Menu/Create main menu first.");
        foreach (var board in boards)
        {
            board.role = RemoteBoard.Role.Replica;
            var document = board.GetComponent<UIDocument>();
            document.enabled = true;
            document.visualTreeAsset = board.id == "menu.home" ? waiting : null;
        }

        // The rig sits where the Mac's camera sat: the plaza's own Viewpoint, facing the board. The anchor is
        // on the floor and is what PlazaApproach carries through a door; the tracked camera rides under it.
        var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var eye = all.FirstOrDefault(t => t.name == "Viewpoint") ?? throw new InvalidOperationException("The plaza has no Viewpoint anchor.");
        var board_ = all.FirstOrDefault(t => t.name == "MenuBoard") ?? throw new InvalidOperationException("The plaza has no MenuBoard anchor.");
        var anchor = new GameObject("Plaza seat anchor").transform;
        var floor = eye.position; floor.y = 0;
        var forward = board_.position - eye.position; forward.y = 0;
        anchor.SetPositionAndRotation(floor, Quaternion.LookRotation(forward.normalized, Vector3.up));
        QuestRigBuilder.Build(anchor, eye.position.y, farClip: 600, CameraClearFlags.Skybox, ~0);

        // One light pass and no shadow map: the plaza is several hundred renderers in stereo on a mobile
        // GPU, and the shadow pass and the second directional light are each another full pass of it.
        foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.name == "Lagoon bounce") { UnityEngine.Object.DestroyImmediate(light.gameObject); continue; }
            light.shadows = LightShadows.None;
        }

        // The wire's client, the ring's follower and the scene follower. All three bootstrap themselves on
        // Android; carrying them in the scene means the Mac can play it as a stand-in headset too.
        new GameObject("Headset remote UI client").AddComponent<RemoteUiClient>();
        new GameObject("Headset menu mirror").AddComponent<QuestMenuMirror>();
        new GameObject("Quest activity follower").AddComponent<QuestActivityFollower>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        // Additive, like every other setup here: the combined Quest build orders the list itself.
        var builds = EditorBuildSettings.scenes.ToList();
        if (!builds.Any(s => s.path == ScenePath)) builds.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = builds.ToArray();
        AssetDatabase.SaveAssets();
        return "Created " + ScenePath;
    }
}
