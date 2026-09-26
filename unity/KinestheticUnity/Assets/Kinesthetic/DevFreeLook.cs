using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace Kinesthetic
{
    /// Mac-side look-around for every scene: left-drag to pan, right-click to recentre.
    ///
    /// Installs itself onto Camera.main after each scene load, so it needs no scene wiring and no
    /// regenerated scenes. It never attaches, and disables itself if already attached, once an XRHMD
    /// exists — on a headset TrackedPoseDriver keeps sole ownership of the camera pose.
    ///
    /// Composes with whatever already drives the camera rather than fighting it. The golf scenes call
    /// spectator.transform.LookAt(...) every frame (KinestheticGolf, KeyboardGolfGame); the drag is
    /// applied as an offset on top of that result, so ball-follow framing still works. Cameras nothing
    /// else drives (main menu, Movement Studio, the headset camera on macOS) compose from a fixed rest
    /// pose instead.
    ///
    /// Previews composition and sightlines only: one eye, Metal, editor scripting backend. It is not
    /// evidence that anything works on the headset.
    public sealed class DevFreeLook : MonoBehaviour
    {
        public float degreesPerPixel = .15f;
        public float pitchLimit = 85;

        float yaw, pitch;
        Quaternion baseRotation, lastWritten;
        bool written;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Attach();
            SceneManager.sceneLoaded += (scene, mode) => Attach();
        }

        static void Attach()
        {
            if (HeadsetPresent()) return;
            var cam = Camera.main;
            if (cam && !cam.GetComponent<DevFreeLook>()) cam.gameObject.AddComponent<DevFreeLook>();
        }

        // Re-checked every frame, not just once: a headset can be connected after the scene loads.
        static bool HeadsetPresent()
        {
            foreach (var device in InputSystem.devices) if (device is XRHMD) return true;
            return false;
        }

        void Awake() => baseRotation = transform.localRotation;

        void LateUpdate()
        {
            if (HeadsetPresent()) { transform.localRotation = baseRotation; enabled = false; return; }
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Still exactly what we wrote last frame => nothing else drives this camera, so keep the same
            // rest pose. Changed => a scene script set it this frame and becomes the new base. That is what
            // keeps the drag additive on a LookAt camera instead of compounding on a static one.
            var current = transform.localRotation;
            if (!written || current != lastWritten) baseRotation = current;

            if (mouse.rightButton.wasPressedThisFrame) yaw = pitch = 0;
            else if (mouse.leftButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * degreesPerPixel;
                pitch = Mathf.Clamp(pitch - delta.y * degreesPerPixel, -pitchLimit, pitchLimit);
            }

            lastWritten = baseRotation * Quaternion.Euler(pitch, yaw, 0);
            transform.localRotation = lastWritten;
            written = true;
        }
    }
}
