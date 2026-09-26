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

    /// How far above `seat` the patient's eyes are: the centre of the Mii's own eye meshes. A fixed 1.15 m put
    /// the headset about 18 cm above a seated Mii's eyes, looking down on its own head. Falls back to 40% up the
    /// head (skull base to top of the hair) for a model without eye meshes.
    public static float EyeHeight(Transform avatar, Transform seat)
    {
        float sum = 0; int n = 0;
        foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("Mii_eye.")) { sum += r.bounds.center.y; n++; }
        if (n > 0) return sum / n - seat.position.y;
        float skull = 0, top = float.MinValue;
        foreach (var t in avatar.GetComponentsInChildren<Transform>(true)) if (t.name == "head") skull = t.position.y;
        foreach (var r in avatar.GetComponentsInChildren<Renderer>(true)) top = Mathf.Max(top, r.bounds.max.y);
        return skull + (top - skull) * .4f - seat.position.y;
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
