using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinesthetic.Panes
{
    /// Where the pointer is and whether it is pressed — nothing about what it hit.
    ///
    /// Keeping the ray source separate from the picking is what lets the same pane interactions
    /// work from a mouse on the Mac and from head gaze on a headset, which is the property
    /// GazeDwell established and this preserves. A pose-tracked wrist is a third source and
    /// needs no change here or in PaneHost: set source to External and write Ray each frame.
    ///
    /// The dwell reticle is Kinesthetic.GazeReticle, reused rather than reimplemented. It is
    /// procedural geometry parented to the camera because a screen-space panel does not render
    /// in stereo, so it is the one piece of feedback that survives a headset.
    public sealed class PanePointer : MonoBehaviour
    {
        public enum Source { Auto, Mouse, Gaze, External }

        public Source source = Source.Auto;
        public float dwellSeconds = 1.1f;
        public float dwellResetDegrees = 4;      // a shaky head should not cancel the dwell

        public Ray Ray { get; set; }
        public bool Pressed { get; private set; }
        public bool PressedThisFrame { get; private set; }

        GazeReticle reticle;
        Vector3 dwellDirection;
        float held;
        bool wasPressed;

        Source Resolved => source != Source.Auto ? source
            : Mouse.current != null ? Source.Mouse : Source.Gaze;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) { Pressed = PressedThisFrame = false; reticle?.Hide(); return; }

            bool pressed;
            switch (Resolved)
            {
                case Source.Mouse:
                    Ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                    pressed = Mouse.current.leftButton.isPressed;
                    reticle?.Hide();
                    break;

                case Source.Gaze:
                    Ray = new Ray(cam.transform.position, cam.transform.forward);
                    pressed = Dwell(cam);
                    break;

                default:                          // External: someone else writes Ray
                    pressed = Pressed;
                    break;
            }

            PressedThisFrame = pressed && !wasPressed;
            wasPressed = Pressed = pressed;
        }

        /// Gaze has no button, so a held dwell stands in for one. Resetting on a direction change
        /// rather than on a target change keeps this ignorant of what it is pointing at, which is
        /// the whole reason the source and the picking are separate.
        bool Dwell(Camera cam)
        {
            reticle ??= GazeReticle.Create();
            var direction = cam.transform.forward;
            if (Vector3.Angle(direction, dwellDirection) > dwellResetDegrees) { dwellDirection = direction; held = 0; }
            held += Time.unscaledDeltaTime;

            if (held < dwellSeconds) { reticle.Show(cam, held / dwellSeconds); return false; }
            reticle.Show(cam, 1);
            return true;
        }
    }
}
