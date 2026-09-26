using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace Kinesthetic
{
    /// Builds the patient's-eye camera from whatever EyeAnchor a scene provides, at runtime, in every scene,
    /// with no scene wiring — the same self-installing trick DevFreeLook uses, and for the same reason:
    /// scenes are regenerated from Assets/Editor/*SceneSetup.cs, so a camera authored into a scene drifts
    /// the moment that setup runs again, while one installed at runtime cannot.
    ///
    /// Yields entirely on a headset. QuestGolf already has the real rig: XROrigin at the seat anchor with
    /// TrackedPoseDriver on centre-eye. This is the Mac stand-in, and it renders one eye on Metal, so it
    /// previews framing and sightlines only — it is not evidence of anything on device.
    public sealed class FirstPersonView : MonoBehaviour
    {
        public const int OwnBodyLayer = 30;   // the layer QuestSceneSetup culls from the headset camera

        EyeAnchor anchor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Build();
            SceneManager.sceneLoaded += (scene, mode) => Build();
        }

        static void Build()
        {
            if (HeadsetPresent()) return;
            var found = Object.FindAnyObjectByType<EyeAnchor>();
            if (!found) return;
            var spectator = Camera.main;                        // the view the scene shipped with

            var go = new GameObject("Patient eye view");
            var view = go.AddComponent<FirstPersonView>();
            view.anchor = found;
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = found.fieldOfView;
            cam.nearClipPlane = .05f;                           // you are inside a head; clip close
            cam.farClipPlane = spectator ? spectator.farClipPlane : 800;
            cam.clearFlags = spectator ? spectator.clearFlags : CameraClearFlags.Skybox;
            cam.backgroundColor = spectator ? spectator.backgroundColor : Color.black;
            cam.cullingMask = (spectator ? spectator.cullingMask : ~0) & ~(1 << OwnBodyLayer);
            go.AddComponent<AudioListener>();

            HideOwnBody(found);
            view.Place();
            ViewDirector.Register(spectator, cam);
        }

        // The same rule the headset uses: everything of the patient's own avatar moves to the culled layer
        // except the torso, so they see their body and not the inside of their own head.
        static void HideOwnBody(EyeAnchor anchor)
        {
            if (!anchor.ownBody) return;
            foreach (var r in anchor.ownBody.GetComponentsInChildren<Renderer>(true))
                if (r.name != "MiiBody") r.gameObject.layer = OwnBodyLayer;
        }

        // Update, not LateUpdate: DevFreeLook composes its drag in LateUpdate by treating any rotation that
        // changed this frame as the new rest pose. Writing the seat pose first is what keeps the drag additive
        // instead of being overwritten by it on alternating frames.
        void Update() => Place();

        void Place()
        {
            if (!anchor) { Destroy(gameObject); return; }
            var seat = anchor.follow ? anchor.follow : anchor.transform;
            transform.position = seat.position + Vector3.up * anchor.eyeHeight;
            if (!anchor.lookAt) { transform.rotation = seat.rotation; return; }

            // Face the target's direction, but level. The ball sits on the ground a metre in front of eyes
            // over a metre up, so aiming straight at it pitches the view into the turf and loses the horizon,
            // the fairway and the flag. Flattening keeps the rest pose level and leaves looking down to the
            // look input — which is also what a headset needs, since pitching the rig tilts the wearer's world.
            var toTarget = Vector3.ProjectOnPlane(anchor.lookAt.position - transform.position, Vector3.up);
            transform.rotation = toTarget.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
                : seat.rotation;
        }

        // Re-checked rather than cached: a headset can be connected after the scene loads.
        static bool HeadsetPresent()
        {
            foreach (var device in InputSystem.devices) if (device is XRHMD) return true;
            return false;
        }
    }
}
