using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kinesthetic.Shell
{
    /// The windows stand in a ring around the person and the ring turns; the person does not.
    ///
    /// That is the whole design decision, and it is a clinical one rather than a stylistic one. The people
    /// using this are seated, often in a chair they cannot swivel, frequently with a shoulder or a neck that
    /// is the reason they are here. A layout that puts the gallery over your left shoulder reads beautifully
    /// in a design tool and asks a patient to perform a movement they may be in rehab for. So the gallery
    /// really does live 90° to the left — the panels are in world space and a headset user can simply look at
    /// it — but nobody is ever *required* to turn. Pressing "choose activity" brings it to them.
    ///
    /// Turning the ring rather than the camera is also what keeps this comfortable in a headset. Moving a
    /// person's viewpoint for them is the reliable way to make them sick; moving furniture around a stationary
    /// viewer is not. The camera in the menu scene is parked deliberately (`MainMenuSetup`: "position is
    /// fixed") and this preserves that.
    ///
    /// Slots are fixed angles, not list positions: with the default 90° spacing, four windows sit at the
    /// cardinal points and every neighbour is exactly one quarter-turn away, so "left" and "right" mean the
    /// same thing every time. A fifth window is a design decision — either the spacing drops to 72° and the
    /// right angles go, or something earns its place — which is why adding one is deliberately not automatic.
    public sealed class WindowCarousel : MonoBehaviour
    {
        [Tooltip("Degrees between neighbouring slots. 90 keeps every window a clean quarter-turn away.")]
        public float spacingDegrees = 90f;

        [Tooltip("Metres from the person to each window. Set from the scene's own eye→board distance.")]
        public float radius = 3.2f;

        [Tooltip("Seconds for one turn. Below ~0.3 the ring snaps and the eye loses which way it went; above " +
                 "~0.6 a person who knows where they are going is waiting for the furniture.")]
        public float turnSeconds = .45f;

        [Tooltip("A window's own transform decides which way its face points. True if a panel's front is its " +
                 "local +Z; the menu board's anchor is authored facing the viewpoint, so verify against it.")]
        public bool faceOutward = true;

        /// The window now facing the person. Changes the moment a turn starts, so anything driving chrome —
        /// the dots, a title — updates with the movement rather than after it.
        public Window Current => windows.Count == 0 ? null : windows[index];

        /// Raised when the facing window changes, at the start of the turn.
        public event Action<Window> Changed;

        /// Raised when a turn finishes, with the window that settled.
        public event Action<Window> Settled;

        public IReadOnlyList<Window> Windows => windows;
        public bool IsTurning => turning;

        readonly List<Window> windows = new();
        Transform ring;
        int index;
        float from, to, elapsed;
        bool turning;

        void Awake() => EnsureRing();

        void EnsureRing()
        {
            if (ring != null) return;
            var existing = transform.Find("Ring");
            ring = existing != null ? existing : new GameObject("Ring").transform;
            ring.SetParent(transform, false);
        }

        /// Stand these windows in the ring, in order, starting at the slot the person faces. Call once; call
        /// it again and the ring is rebuilt from scratch, which is what a data-driven gallery wants when the
        /// catalog changes underneath it.
        public void Adopt(params Window[] adopted)
        {
            EnsureRing();
            windows.Clear();
            for (int slot = 0; slot < adopted.Length; slot++)
            {
                var window = adopted[slot];
                if (window == null) continue;
                window.Slot = slot;
                windows.Add(window);
                Place(window);
            }
            index = 0;
            ring.localRotation = Quaternion.identity;
            turning = false;
            for (int i = 0; i < windows.Count; i++) windows[i].Face(i == index);
            if (Current != null) { Changed?.Invoke(Current); Settled?.Invoke(Current); }
        }

        /// Put one window at its slot: out along the slot's heading, turned to face back down it.
        void Place(Window window)
        {
            float yaw = window.Slot * spacingDegrees;
            var heading = Quaternion.Euler(0, yaw, 0);
            window.transform.SetParent(ring, false);
            window.transform.localPosition = heading * (Vector3.forward * radius);
            window.transform.localRotation = faceOutward ? heading : heading * Quaternion.Euler(0, 180, 0);
        }

        /// Turn to a window by name. Unknown names are ignored rather than thrown: the caller is usually a
        /// button, and a missing window should cost a dead press, not the whole panel.
        public bool Show(string id)
        {
            int found = windows.FindIndex(w => w.id == id);
            if (found < 0) return false;
            TurnTo(found);
            return true;
        }

        public void Next() => TurnTo(index + 1);
        public void Previous() => TurnTo(index - 1);

        void TurnTo(int wanted)
        {
            if (windows.Count == 0) return;
            int target = ((wanted % windows.Count) + windows.Count) % windows.Count;
            if (target == index && !turning) return;

            // The ring turns the short way round, which for four windows means "two to the left" never
            // becomes "two and a bit to the right". Working in signed degrees rather than in slots keeps the
            // wrap at the seam honest: from slot 3 to slot 0 is +90, not -270.
            float current = ring.localEulerAngles.y;
            float wantedYaw = -target * spacingDegrees;
            float delta = Mathf.DeltaAngle(current, wantedYaw);

            from = current;
            to = current + delta;
            elapsed = 0;
            turning = true;
            index = target;
            Changed?.Invoke(Current);
        }

        void Update()
        {
            if (!turning) return;

            // Unscaled: a menu that stops turning because something paused the game is a menu you cannot
            // leave. The ease is a cubic out — fastest at the start, so the movement reads as a response to
            // the press rather than as an animation that began on its own.
            elapsed += Time.unscaledDeltaTime;
            float t = turnSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed / turnSeconds);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            ring.localRotation = Quaternion.Euler(0, Mathf.LerpUnclamped(from, to, eased), 0);

            if (t < 1) return;
            turning = false;
            for (int i = 0; i < windows.Count; i++) windows[i].Face(i == index);
            Settled?.Invoke(Current);
        }

        /// Stand the ring where the person is and size it to what they are already looking at, so the windows
        /// arrive at the distance the board was authored for instead of at a number typed into this file.
        public void Frame(Vector3 eye, Vector3 firstWindow)
        {
            transform.position = eye;
            var flat = firstWindow - eye; flat.y = 0;
            if (flat.sqrMagnitude > .0001f)
            {
                radius = flat.magnitude;
                transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }
            foreach (var window in windows) Place(window);
        }
    }
}
