using System;
using Kinesthetic;
using Kinesthetic.Rehab;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;

// The rehab studio in a headset, first person: you sit in the patient's wheelchair, the coach is on your right
// and the mirror window on your left. Generated from the Mac's Rehab scene; the headset measures nothing and
// renders what the Mac publishes (RehabStatePublisher → relay /rehab-state → RehabStateClient).
public static class QuestRehabSetup
{
    public const string SourcePath = "Assets/Kinesthetic/Rehab/Rehab.unity";
    public const string ScenePath = "Assets/Kinesthetic/Rehab/QuestRehab.unity";
    const int LocalHeadLayer = 30;

    [MenuItem("Kinesthetic/Quest/Create headset rehab scene")]
    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(source, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var session = UnityEngine.Object.FindAnyObjectByType<RehabSession>();
        session.enabled = false;                                   // no sensors, rules or network on the headset
        var doc = session.GetComponent<UIDocument>(); if (doc) doc.enabled = false;   // screen-space UI does not render in stereo
        foreach (var guide in new Component[] { session.targetOrb, session.liveMarker, session.targetBand, session.armGuide })
            if (guide) guide.gameObject.SetActive(false);
        foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            cam.gameObject.SetActive(false);                       // the Mac's view camera and its listener

        // You are the patient: their head would sit in front of the headset camera.
        var rig = session.rig;
        foreach (var r in rig.avatar.GetComponentsInChildren<Renderer>(true))
            if (r.name != "MiiBody") r.gameObject.layer = LocalHeadLayer;

        var seat = rig.transform.parent ? rig.transform.parent : rig.transform;
        var forward = Vector3.ProjectOnPlane(rig.transform.TransformDirection(Vector3.back), Vector3.up).normalized;   // the Mii faces local -Z
        var anchor = new GameObject("Patient seat anchor").transform;
        anchor.SetPositionAndRotation(seat.position, Quaternion.LookRotation(forward));
        var originGo = new GameObject("XR Origin"); originGo.transform.SetParent(anchor, false);
        var offset = new GameObject("Camera Offset"); offset.transform.SetParent(originGo.transform, false);
        var camGo = new GameObject("Headset camera"); camGo.transform.SetParent(offset.transform, false); camGo.tag = "MainCamera";
        camGo.transform.localPosition = new Vector3(0, 1.15f, 0);  // seated eye height until tracking takes over
        var headset = camGo.AddComponent<Camera>(); headset.nearClipPlane = .05f; headset.farClipPlane = 300;
        headset.cullingMask = ~(1 << LocalHeadLayer);
        camGo.AddComponent<AudioListener>();
        var pose = camGo.AddComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(new InputAction("Head position", binding: "<XRHMD>/centerEyePosition"));
        pose.rotationInput = new InputActionProperty(new InputAction("Head rotation", binding: "<XRHMD>/centerEyeRotation"));
        var origin = originGo.AddComponent<XROrigin>();
        origin.Origin = originGo; origin.CameraFloorOffsetObject = offset; origin.Camera = headset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        // Reps and the cue on a world-space panel ahead and a little low, where a seated patient reads comfortably.
        var hudGo = new GameObject("Headset studio panel");
        hudGo.transform.SetPositionAndRotation(seat.position + forward * 1.9f + Vector3.up * 1.3f, Quaternion.LookRotation(forward));
        var hud = hudGo.AddComponent<TextMesh>();
        hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hudGo.GetComponent<MeshRenderer>().sharedMaterial = hud.font.material;
        hud.fontSize = 96; hud.characterSize = .011f; hud.anchor = TextAnchor.MiddleCenter; hud.alignment = TextAlignment.Center;
        hud.color = Palette.Prussian70; hud.text = "Waiting for the RehabMii studio on the Mac…";

        // First person: coach ahead-right and mirror ahead-left, about 40 degrees off centre, inside the headset's view.
        var right = Vector3.Cross(Vector3.up, forward);
        var coachSeat = new GameObject("Coach seat (first person)").transform;
        var coachAt = seat.position + right * 1.0f + forward * 1.35f;
        coachSeat.SetPositionAndRotation(coachAt, Quaternion.LookRotation(Vector3.ProjectOnPlane(seat.position - coachAt, Vector3.up)));
        var client = new GameObject("Headset rehab-state client").AddComponent<RehabStateClient>();
        client.rig = rig; client.hud = hud; client.coachSeat = coachSeat;
        var mirror = client.gameObject.AddComponent<MirrorPanel>();   // the mirror on the left, fed from the Mac
        mirror.view = client;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Created " + ScenePath;
    }
}
