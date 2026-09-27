using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Tutorial;
using Kinesthetic.UI.Remote;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The intro in a headset, first person: you sit where the Mac seats the patient, in the movement studio, and
// Alex sits across from you doing the arm lift the Mac's sequencer is running. Generated from the Mac's Tutorial
// scene; the headset runs no sequencer, reads no sensor and speaks nothing — Alex is posed from what the Mac
// publishes (TutorialStatePublisher → TutorialStateClient), and the voice plays on the Mac.
public static class QuestTutorialSetup
{
    public const string SourcePath = TutorialSceneSetup.ScenePath;
    public const string ScenePath = "Assets/Kinesthetic/Tutorial/QuestTutorial.unity";
    /// The Mac's camera: the patient's seated eyes at the origin, facing +Z (TutorialSequencer.SetupScene).
    const float EyeHeight = 1.03f;

    [MenuItem("Kinesthetic/Quest/Create headset tutorial scene")]
    public static string Create()
    {
        RehabSceneSetup.RequireIdleEditor("Create headset tutorial scene");
        var source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(source, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // The sequencer is the Mac's: it spawns the coach, reads the sensors and cues the voice. None of that here.
        foreach (var sequencer in UnityEngine.Object.FindObjectsByType<TutorialSequencer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(sequencer.gameObject);
        QuestRigBuilder.DisableSceneCameras();                     // the Mac's camera and its listener

        // The patient's seat: the origin, facing the windows, as the Mac's sequencer places its camera.
        var anchor = new GameObject("Patient seat anchor").transform;
        anchor.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var headset = QuestRigBuilder.Build(anchor, EyeHeight, farClip: 200, CameraClearFlags.SolidColor, ~0);
        headset.backgroundColor = Palette.Cerulean10;              // the studio's own sky, as on the Mac

        new GameObject("Headset tutorial-state client").AddComponent<TutorialStateClient>();
        // The navigation layer is a board on both machines; its client bootstraps on Android, and carrying one
        // here lets the Mac play this scene as a stand-in headset too.
        new GameObject("Headset remote UI client").AddComponent<RemoteUiClient>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        var builds = EditorBuildSettings.scenes.ToList();
        if (!builds.Any(s => s.path == ScenePath)) builds.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = builds.ToArray();
        AssetDatabase.SaveAssets();
        return "Created " + ScenePath;
    }
}
