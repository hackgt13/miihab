using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;

namespace Kinesthetic
{
    /// The Quest's Touch controllers, as pointers for GazeDwell.
    ///
    /// A dwell alone asks a lot of a head: hold still on a small target for a second, and never rest your eyes
    /// on something you did not mean. So a controller adds two roads to the same press. Point it and pull the
    /// trigger, and whatever the beam is on is pressed at once. Or leave it in your lap, look at a button and
    /// pull — the gaze target is pressed without waiting out the ring. Either way GazeDwell still reports one
    /// name through Committed, and PressGate still turns it into one action.
    ///
    /// One instance per scene, made by the first GazeDwell that wants it, so every board reads the same rays
    /// and there is one beam per hand however many boards are standing. It runs before GazeDwell so a board
    /// always sees this frame's rays.
    ///
    /// Controls are found by name rather than through the OpenXR profile's types, so any controller that
    /// exposes an aim pose and a trigger works, and the Mac — which has no XRController at all — is untouched.
    [DefaultExecutionOrder(-100)]
    public sealed class ControllerPointer : MonoBehaviour
    {
        const float BeamLength = 1.5f, BeamWidth = .004f, MaxDistance = 12;

        public struct Pointer
        {
            public Ray ray;
            public bool tracked;
            public bool pulled;       // trigger or A/X went down this frame
            public bool onPane;       // the beam is on some pane, whether or not on a button
        }

        static ControllerPointer instance;
        static int users;

        readonly List<Pointer> pointers = new();
        readonly Dictionary<InputDevice, LineRenderer> beams = new();
        readonly List<InputDevice> seen = new();

        public static IReadOnlyList<Pointer> Pointers => instance ? instance.pointers : System.Array.Empty<Pointer>();

        /// A GazeDwell is listening. With none listening the beams are hidden, so a game scene shows no laser.
        public static void Acquire()
        {
            if (!instance)
            {
                // Outlives scene loads, like the navigation board that is one of its users.
                instance = new GameObject("Controller pointer").AddComponent<ControllerPointer>();
                DontDestroyOnLoad(instance.gameObject);
            }
            users++;
        }

        public static void Release() => users = Mathf.Max(0, users - 1);

        // Play mode may start without a domain reload, which would carry the last session's count in.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; users = 0; }

        /// The named Button a tracked controller is pointing at on `owner`'s pane, or null. `pulled` is whether
        /// that controller's trigger went down this frame.
        public static Button ButtonOn(GameObject owner, float maxDistance, out bool pulled)
        {
            pulled = false;
            foreach (var p in Pointers)
            {
                if (!p.tracked || !p.onPane) continue;
                var button = Panes.WorldPanelPick.NamedButtonOn(owner, p.ray, maxDistance);
                if (button == null) continue;
                pulled = p.pulled;
                return button;
            }
            return null;
        }

        /// Some tracked controller's beam is on a pane. The head then stops dwelling anywhere, so looking at
        /// one board while pointing at another never presses the one you are only looking at.
        public static bool Pointing
        {
            get
            {
                foreach (var p in Pointers) if (p.tracked && p.onPane) return true;
                return false;
            }
        }

        /// A trigger went down on a controller that is not pointing at any pane: the head chooses, the hand
        /// confirms. An untracked controller counts too — someone holding it still in their lap.
        public static bool PulledAtNothing
        {
            get
            {
                foreach (var p in Pointers) if (p.pulled && (!p.tracked || !p.onPane)) return true;
                return false;
            }
        }

        void LateUpdate()
        {
            pointers.Clear();
            seen.Clear();
            var cam = Camera.main;
            if (users == 0 || !cam) { HideBeams(); return; }

            // The controllers are tracked in the same space as the head, so the camera's parent — the rig's
            // floor offset — carries them into the world exactly as it carries the camera.
            var space = cam.transform.parent;
            foreach (var device in InputSystem.devices)
            {
                if (device is not XRController controller) continue;
                var position = controller.TryGetChildControl<Vector3Control>("pointerPosition") ?? controller.TryGetChildControl<Vector3Control>("devicePosition");
                var rotation = controller.TryGetChildControl<QuaternionControl>("pointerRotation") ?? controller.TryGetChildControl<QuaternionControl>("deviceRotation");
                if (position == null || rotation == null) continue;

                var trackedControl = controller.TryGetChildControl<ButtonControl>("isTracked");
                bool tracked = trackedControl == null || trackedControl.isPressed;
                var origin = position.ReadValue();
                var forward = rotation.ReadValue() * Vector3.forward;
                if (space) { origin = space.TransformPoint(origin); forward = space.TransformDirection(forward); }

                var pointer = new Pointer { ray = new Ray(origin, forward), tracked = tracked, pulled = Pulled(controller) };
                float length = BeamLength;
                // The same hit rule as picking, so the beam passes through an empty layer (the nav shell)
                // exactly where a press would.
                if (tracked)
                {
                    Panes.WorldPanelPick.Under<VisualElement>(pointer.ray, MaxDistance, Physics.DefaultRaycastLayers, out var pane, out _, out _);
                    if (pane)
                    {
                        pointer.onPane = true;
                        foreach (var collider in pane.GetComponentsInChildren<Collider>())
                            if (collider.Raycast(pointer.ray, out var hit, MaxDistance)) { length = hit.distance; break; }
                    }
                }
                pointers.Add(pointer);
                seen.Add(device);
                Beam(device, pointer, length);
            }

            foreach (var (device, beam) in beams) if (beam && !seen.Contains(device)) beam.enabled = false;
        }

        static readonly string[] Selects = { "triggerPressed", "primaryButton" };

        static bool Pulled(XRController controller)
        {
            foreach (var name in Selects)
                if (controller.TryGetChildControl<ButtonControl>(name) is { wasPressedThisFrame: true }) return true;
            return false;
        }

        void Beam(InputDevice device, Pointer pointer, float length)
        {
            if (!beams.TryGetValue(device, out var beam) || !beam)
            {
                var go = new GameObject("Controller beam");
                go.transform.SetParent(transform, false);
                beam = go.AddComponent<LineRenderer>();
                beam.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                beam.positionCount = 2;
                beam.useWorldSpace = true;
                beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                beam.receiveShadows = false;
                beam.startWidth = BeamWidth; beam.endWidth = BeamWidth * .5f;
                beams[device] = beam;
            }

            beam.enabled = pointer.tracked;
            if (!pointer.tracked) return;
            // Solid onto a pane, fading out into the room, so it is plain when the beam can press something.
            var near = Palette.Sand10.At(.9f);
            beam.startColor = near;
            beam.endColor = pointer.onPane ? near : Palette.Sand10.At(0);
            beam.SetPosition(0, pointer.ray.origin);
            beam.SetPosition(1, pointer.ray.GetPoint(length));
        }

        void HideBeams()
        {
            foreach (var beam in beams.Values) if (beam) beam.enabled = false;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
