using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

/// The headset's rig, once: an XROrigin parked at the patient's seat with a tracked centre-eye camera
/// under it, and the patient's own head moved off that camera's layers. Golf, bowling and rehab all
/// build through this.
///
/// The anchor is the seat: its position is the floor point under the patient and its forward is where
/// they face. SeatedHeadset keeps the tracked camera at `seatedEyeHeight` above it, facing that way,
/// wherever the wearer stands in their room — the patient's own eyes, not a view of the patient.
public static class QuestRigBuilder
{
    /// The layer the patient's own head is drawn on, culled from the headset camera. The same one
    /// FirstPersonView uses for the Mac's eye camera, so a scene hides the head once for both.
    public const int LocalHeadLayer = 30;

    public static Camera Build(Transform anchor, float seatedEyeHeight, float farClip, CameraClearFlags clear, int cullingMask)
    {
        var originGo = new GameObject("XR Origin"); originGo.transform.SetParent(anchor, false);
        var offset = new GameObject("Camera Offset"); offset.transform.SetParent(originGo.transform, false);
        var camGo = new GameObject("Headset camera"); camGo.transform.SetParent(offset.transform, false); camGo.tag = "MainCamera";
        camGo.transform.localPosition = new Vector3(0, seatedEyeHeight, 0);
        var camera = camGo.AddComponent<Camera>();
        camera.nearClipPlane = .05f; camera.farClipPlane = farClip;
        camera.clearFlags = clear; camera.cullingMask = cullingMask;
        camGo.AddComponent<AudioListener>();
        var pose = camGo.AddComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(new InputAction("Head position", binding: "<XRHMD>/centerEyePosition"));
        pose.rotationInput = new InputActionProperty(new InputAction("Head rotation", binding: "<XRHMD>/centerEyeRotation"));
        var origin = originGo.AddComponent<XROrigin>();
        origin.Origin = originGo; origin.CameraFloorOffsetObject = offset; origin.Camera = camera;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        // First person: keep the tracked eyes at the patient's seated eyes, whatever the wearer's room.
        var seated = originGo.AddComponent<Kinesthetic.SeatedHeadset>();
        seated.seat = anchor; seated.head = camGo.transform; seated.eyeHeight = seatedEyeHeight;
        return camera;
    }

    /// You are the patient: their head would sit in front of the headset camera. Everything of the avatar
    /// but the torso goes on the culled layer, so they see their body and not the inside of their own head.
    public static void HideOwnHead(Transform avatar)
    {
        foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            if (r.name != "MiiBody") r.gameObject.layer = LocalHeadLayer;
    }

    /// The Mac's view cameras and their listeners: a headset renders through its own.
    public static void DisableSceneCameras()
    {
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            cam.gameObject.SetActive(false);
    }
}
