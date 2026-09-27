using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;

namespace Kinesthetic
{
    /// A board's headset input: the Touch controllers. Point a controller at a button and pull the trigger
    /// (or A/X) to press it. ControllerPointer reads the controllers and draws the beams; this resolves the
    /// beam to a button on its own board.
    ///
    /// It used to be a head-gaze dwell — a ray from the centre of the view that pressed after a second — and
    /// keeps the name because scenes and setup code reference the component. The dwell is gone: it pressed
    /// what people only looked at, and a ring made in one scene broke the boards that outlive a scene load.
    ///
    /// Nothing here synthesises UI Toolkit events: it reports the element's name and the board maps that to
    /// the same action its clicked handler runs, exactly as PanePointerInput does for the Mac's mouse.
    ///
    /// Elements are resolved by Panes.WorldPanelPick, not by panel.Pick: Pick returns null on a world-space
    /// panel at every point, including ones well inside the root. WorldPanelPick projects the hit through the
    /// panel's own transform once (Pane.TryProject) and picks in paint order, so a modal's shade blocks the
    /// buttons under it and a disabled button is never committed.
    public sealed class GazeDwell : MonoBehaviour
    {
        public float maxDistance = 12;

        /// The element under the beam changed; null when the beam left the last one.
        public event Action<string> Entered;
        public event Action<string> Committed;

        /// Whether a headset is driving the view (or the Mac's look-drag, DevFreeLook, is steered off its
        /// rest pose). BoardSet enables this component only then; with no controllers it reports nothing.
        public static bool HeadDriven(Camera cam)
        {
            foreach (var device in InputSystem.devices) if (device is XRHMD) return true;
            var look = cam ? cam.GetComponent<DevFreeLook>() : null;
            return look && look.Steered;
        }

        string hot;

        void Awake() => Panes.WorldPanelPick.MakePickable(gameObject);

        void OnEnable() => ControllerPointer.Acquire();

        void OnDisable()
        {
            ControllerPointer.Release();
            Leave();
        }

        void LateUpdate()
        {
            var pointed = ControllerPointer.ButtonOn(gameObject, maxDistance, out bool pulled);
            if (pointed == null) { Leave(); return; }

            if (pointed.name != hot) { hot = pointed.name; Entered?.Invoke(hot); }
            if (!pulled) return;
            // A replica board (UI/Remote) has no handlers of its own: its press crosses to the Mac instead.
            if (Kinesthetic.UI.Remote.RemoteBoard.Intercepts(gameObject, pointed)) return;
            Committed?.Invoke(pointed.name);
        }

        /// The beam left whatever it was on: subscribers hear a null so a highlight does not stick.
        void Leave()
        {
            if (hot == null) return;
            hot = null;
            Entered?.Invoke(null);
        }
    }
}
