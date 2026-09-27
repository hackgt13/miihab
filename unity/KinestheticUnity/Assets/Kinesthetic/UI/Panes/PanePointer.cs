using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinesthetic.Panes
{
    /// Where the pointer is and whether it is pressed — nothing about what it hit.
    ///
    /// Keeping the ray source separate from the picking is what lets the same pane interactions
    /// work from a mouse on the Mac and from a Touch controller on a headset. A pose-tracked wrist
    /// is a third source and needs no change here or in PaneHost: set source to External and write
    /// Ray each frame.
    ///
    /// The controller's beam and trigger are Kinesthetic.ControllerPointer's, shared with every
    /// board, so a pane and a board answer the same hand the same way.
    public sealed class PanePointer : MonoBehaviour
    {
        public enum Source { Auto, Mouse, Controller, External }

        public Source source = Source.Auto;

        public Ray Ray { get; set; }
        public bool Pressed { get; private set; }
        public bool PressedThisFrame { get; private set; }

        bool wasPressed;

        Source Resolved => source != Source.Auto ? source
            : Mouse.current != null ? Source.Mouse : Source.Controller;

        void OnEnable() => ControllerPointer.Acquire();
        void OnDisable() => ControllerPointer.Release();

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) { Pressed = PressedThisFrame = false; return; }

            bool pressed;
            switch (Resolved)
            {
                case Source.Mouse:
                    Ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                    pressed = Mouse.current.leftButton.isPressed;
                    break;

                case Source.Controller:
                    pressed = false;
                    foreach (var p in ControllerPointer.Pointers)
                    {
                        if (!p.tracked) continue;
                        Ray = p.ray;
                        pressed = p.pulled;       // a pull is one frame: the trigger is a click, not a hold
                        break;
                    }
                    break;

                default:                          // External: someone else writes Ray
                    pressed = Pressed;
                    break;
            }

            PressedThisFrame = pressed && !wasPressed;
            wasPressed = Pressed = pressed;
        }
    }
}
