using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.UI.Boards;
using Kinesthetic.UI.Remote;
using Kinesthetic.Visit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// The therapist visit in a headset, first person: you sit in the patient's seat, the whiteboard ahead-left and
// Alex standing ahead-right, exactly where the Mac put them. Generated from the Mac's TherapistVisit scene; the
// headset fetches nothing and speaks nothing — the whiteboard, the speech balloon and the dock's buttons are the
// Mac's boards, mirrored over /ui (RemoteUiHost → RemoteUiClient) onto the same boards at the same stations, and
// a press here (Next, Hear it again) goes back to the Mac, which is running the visit.
public static class QuestVisitSetup
{
    public const string SourcePath = TherapistVisitSetup.ScenePath;
    public const string ScenePath = "Assets/Kinesthetic/Visit/QuestVisit.unity";

    [MenuItem("Kinesthetic/Quest/Create headset visit scene")]
    public static string Create()
    {
        RehabSceneSetup.RequireIdleEditor("Create headset visit scene");
        var source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(source, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var visit = UnityEngine.Object.FindAnyObjectByType<TherapistVisit>();
        if (!visit || !visit.seat) throw new InvalidOperationException("TherapistVisit.unity has no visit or seat; run Kinesthetic/Visit/Create therapist visit scene first.");
        visit.enabled = false;                                     // the Mac talks to the coordinator and the voice; this copy is posed by its boards
        QuestRigBuilder.DisableSceneCameras();                     // the Mac's eye camera and its listener

        // The boards stay where the Mac put them and keep drawing — as Replicas, filled by the Mac's trees.
        // Explicitly Replica rather than Auto, so playing this scene on the Mac previews the headset's side and
        // never mirrors these boards out as a second host. The dock shows the waiting card until the Mac connects.
        var waiting = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(QuestRehabSetup.WaitingPath);
        if (!waiting) throw new InvalidOperationException("Missing " + QuestRehabSetup.WaitingPath);
        var boards = UnityEngine.Object.FindObjectsByType<RemoteBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (boards.Length == 0) throw new InvalidOperationException("TherapistVisit.unity has no boards.");
        foreach (var board in boards)
        {
            board.role = RemoteBoard.Role.Replica;
            var document = board.GetComponent<UIDocument>();
            document.enabled = true;
            document.visualTreeAsset = board.id == "visit.dock" ? waiting : null;
        }

        // The patient's own eyes: the seat is the floor point and the facing, the eye height is the seat's.
        var seat = visit.seat;
        var headset = QuestRigBuilder.Build(seat.transform, seat.eyeHeight, farClip: 300, CameraClearFlags.SolidColor, ~0);
        headset.backgroundColor = Palette.Cerulean10;              // the studio's own sky, as on the Mac

        // The boards' client. RemoteUiClient bootstraps itself on Android; carrying one in the scene means the
        // Mac can play this scene as a stand-in headset too, and the bootstrap then finds this one.
        new GameObject("Headset remote UI client").AddComponent<RemoteUiClient>();

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
