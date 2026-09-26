using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

/// The headset's rig, once: an XROrigin parked at the patient's seat with a tracked centre-eye camera
/// under it, and the patient's own head moved off that camera's layers. QuestRehabSetup builds through
/// this; QuestSceneSetup (golf) still carries its own copy of the same steps and can adopt this later.
///
/// The anchor is the seat: its position is the floor point under the patient and its forward is where
/// they face. With a floor tracking origin the headset's real height replaces `seatedEyeHeight` the
/// moment tracking starts; the value only places the camera before that, and in the editor.
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
