using System;
using Kinesthetic;
using Kinesthetic.Rehab;
using Kinesthetic.UI.Boards;
using Kinesthetic.UI.Remote;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// The rehab studio in a headset, first person: you sit in the patient's wheelchair, the coach is on your right
// and the mirror window on your left. Generated from the Mac's Rehab scene; the headset measures nothing and
// renders what the Mac publishes — the bodies over /rehab-state (RehabStatePublisher → RehabStateClient) and
// the boards over /ui (RemoteUiHost → RemoteUiClient), onto the same boards at the same stations.
public static class QuestRehabSetup
{
    public const string SourcePath = "Assets/Kinesthetic/Rehab/Rehab.unity";
    public const string ScenePath = "Assets/Kinesthetic/Rehab/QuestRehab.unity";
    public const string WaitingPath = "Assets/Kinesthetic/UI/Boards/Waiting.uxml";

    [MenuItem("Kinesthetic/Quest/Create headset rehab scene")]
    public static string Create()
    {
        RehabSceneSetup.RequireIdleEditor("Create headset rehab scene");
        var source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(source, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var session = UnityEngine.Object.FindAnyObjectByType<RehabSession>();
        session.enabled = false;                                   // no sensors, rules or network on the headset
        foreach (var guide in new Component[] { session.targetOrb, session.liveMarker, session.targetBand, session.armGuide })
            if (guide) guide.gameObject.SetActive(false);
        QuestRigBuilder.DisableSceneCameras();                     // the Mac's view camera and its listener

        var rig = session.rig;
        QuestRigBuilder.HideOwnHead(rig.avatar);

        // The boards stay where the Mac put them and keep drawing — as Replicas, filled by the Mac's tree.
        // Explicitly Replica rather than Auto, so playing this scene on the Mac previews the headset's side
        // and never mirrors these boards out as a second host. The dock's own tree becomes the waiting
        // card the patient sees until the Mac connects; the others say nothing until then.
        var waiting = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WaitingPath);
        if (!waiting) throw new InvalidOperationException("Missing " + WaitingPath);
        var seat = UnityEngine.Object.FindAnyObjectByType<SeatRig>();
        if (!seat) throw new InvalidOperationException("Rehab.unity has no SeatRig; run Kinesthetic/Rehab/Create shoulder raise scene first.");
        foreach (var board in UnityEngine.Object.FindObjectsByType<RemoteBoard>(FindObjectsInactive.Include))
        {
            board.role = RemoteBoard.Role.Replica;
            var document = board.GetComponent<UIDocument>();
            document.enabled = true;
            document.visualTreeAsset = board.id == "rehab.dock" ? waiting : null;
        }

        var headset = QuestRigBuilder.Build(seat.transform, seat.eyeHeight, farClip: 300, CameraClearFlags.SolidColor, ~(1 << QuestRigBuilder.LocalHeadLayer));
        headset.backgroundColor = Palette.Cerulean10;   // the studio's own sky, as on the Mac

        // The coach's seat, from this scene's own patient — the same pose the Mac seats its coach at.
        var forward = Vector3.ProjectOnPlane(seat.transform.forward, Vector3.up).normalized;
        var coachPose = Kinesthetic.Coach.CoachDemonstrator.SeatPose(seat.transform.position, forward);
        var coachSeat = new GameObject("Coach seat (first person)").transform;
        coachSeat.SetPositionAndRotation(coachPose.position, coachPose.rotation);
        var client = new GameObject("Headset rehab-state client").AddComponent<RehabStateClient>();
        client.rig = rig; client.coachSeat = coachSeat;
        var mirror = client.gameObject.AddComponent<MirrorPanel>();   // the mirror on the left, fed from the Mac
        mirror.view = client;

        // The boards' client. RemoteUiClient bootstraps itself on Android; carrying one in the scene means
        // the Mac can play this scene as a stand-in headset too, and the bootstrap then finds this one.
        new GameObject("Headset remote UI client").AddComponent<RemoteUiClient>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Created " + ScenePath;
    }
}
