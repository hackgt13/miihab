using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Shell
{
    /// Where panes stand, and how you get to the one you want. The panes themselves, and everything inside
    /// them, belong to `Kinesthetic.Panes` — this holds no opinion about what a pane shows. It takes a
    /// transform, stands it at an angle, and turns the ring. That is the whole contract, and it is why there
    /// is no window type in here: a second noun for the thing `Panes` already calls a pane would be two names
    /// for one object, and the two halves would start growing toward each other.
    ///
    /// **The ring turns; the person does not.** That is a clinical decision rather than a stylistic one. The
    /// people using this are seated, often in a chair that does not swivel, frequently with the shoulder or
    /// neck that is the reason they are here. So the gallery really does live 90° to the left — the panes are
    /// world-space and a headset user can simply look at it — but nobody is ever *required* to turn. Pressing
    /// "choose activity" brings it to them.
    ///
    /// Turning the furniture rather than the camera is also what keeps this comfortable in a headset: moving
    /// someone's viewpoint for them is the reliable way to make them sick. The menu camera stays parked,
    /// which is what `MainMenuSetup` already intends ("position is fixed").
    ///
    /// Slots are fixed angles, not list positions: at the default 90° spacing four panes sit at the cardinal
    /// points and every neighbour is exactly one quarter-turn away, so "left" means the same thing every
    /// time. A fifth pane is a design decision — the spacing drops to 72° and the right angles go — which is
    /// why nothing here makes that choice silently.
    public sealed class PaneCarousel : MonoBehaviour
    {
        /// One standing place in the ring. A value, not an object: the carousel needs somewhere to put a
        /// pane and a name to reach it by, and nothing else.
        [Serializable]
        public struct Slot
        {
            public string id;        // what the rest of the app turns to: "home", "gallery", "friends"
            public string title;     // shown in the dots' tooltip, and by anything that narrates
            public Transform pane;

            public Slot(string id, string title, Transform pane) { this.id = id; this.title = title; this.pane = pane; }
        }

        [Tooltip("Degrees between neighbouring slots. 90 keeps every pane a clean quarter-turn away.")]
        public float spacingDegrees = 90f;

        [Tooltip("Metres from the person to each pane. 3.5 is the plaza's own Viewpoint→MenuBoard distance, " +
                 "measured rather than chosen; Frame() resets it from whatever scene this lands in.")]
        public float radius = 3.5f;

        [Tooltip("Seconds for one turn. Below ~0.3 the ring snaps and the eye loses which way it went; above " +
                 "~0.6 someone who knows where they are going is waiting for the furniture.")]
        public float turnSeconds = .45f;

        [Tooltip("True if a panel's front is its local +Z. Measured against the authored menu board, whose +Z " +
                 "points away from the Viewpoint — so a pane turned out along its slot already faces inward.")]
        public bool faceOutward = true;

        [Tooltip("Stop drawing panes nobody is facing. Off by default: disabling a UIDocument destroys its " +
                 "visual tree and rebuilds it on the way back, costing the pane its scroll position, its " +
                 "focus and its bindings — to save a repaint UI Toolkit only performs when something changes.")]
        public bool cullHidden;

        /// The slot now facing the person. Changes the moment a turn starts, so chrome moves with the
        /// movement rather than after it.
        public Slot Current => slots.Count == 0 ? default : slots[index];
        public int CurrentIndex => index;
        public IReadOnlyList<Slot> Slots => slots;
        public bool IsTurning => turning;

        /// Raised as a turn begins, with the slot being turned to.
        public event Action<Slot> Changed;

        /// Raised when the turn finishes. A pane that should only work while someone is looking at it —
        /// polling the bridge, redrawing a chart, playing audio — hangs off this rather than off Changed,
        /// so it does not wake up while it is still edge-on and unreadable.
        public event Action<Slot> Settled;

        /// Raised for the slot being left, as the turn begins.
        public event Action<Slot> Leaving;

        readonly List<Slot> slots = new();
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

        /// Stand these panes in the ring, in order, starting with the one the person faces. Calling it again
        /// rebuilds the ring, which is what a data-driven gallery wants when the catalog changes underneath.
        public void Adopt(params Slot[] adopted)
        {
            EnsureRing();
            slots.Clear();
            foreach (var slot in adopted)
            {
                if (slot.pane == null) continue;
                slots.Add(slot);
                Place(slots.Count - 1);
            }
            index = 0;
            ring.localRotation = Quaternion.identity;
            turning = false;
            Cull();
            if (slots.Count > 0) { Changed?.Invoke(Current); Settled?.Invoke(Current); }
        }

        /// Out along the slot's heading, turned to face back down it.
        void Place(int slot)
        {
            float yaw = slot * spacingDegrees;
            var heading = Quaternion.Euler(0, yaw, 0);
            var pane = slots[slot].pane;
            pane.SetParent(ring, false);
            pane.localPosition = heading * (Vector3.forward * radius);
            pane.localRotation = faceOutward ? heading : heading * Quaternion.Euler(0, 180, 0);
        }

        /// Turn to a pane by name. An unknown name is ignored rather than thrown: the caller is usually a
        /// button, and a pane that is not there should cost a dead press, not the panel.
        public bool Show(string id)
        {
            int found = slots.FindIndex(s => s.id == id);
            if (found >= 0) TurnTo(found);
            return found >= 0;
        }

        public void Next() => TurnTo(index + 1);
        public void Previous() => TurnTo(index - 1);

        void TurnTo(int wanted)
        {
            if (slots.Count == 0) return;
            int target = ((wanted % slots.Count) + slots.Count) % slots.Count;
            if (target == index && !turning) return;

            // The ring turns the short way round, so "two to the left" never becomes "two and a bit to the
            // right". Signed degrees rather than slot arithmetic keeps the wrap honest: slot 3 to slot 0 is
            // +90, not -270.
            float current = ring.localEulerAngles.y;
            float delta = Mathf.DeltaAngle(current, -target * spacingDegrees);

            Leaving?.Invoke(Current);
            from = current;
            to = current + delta;
            elapsed = 0;
            turning = true;
            index = target;
            Cull();
            Changed?.Invoke(Current);
        }

        void Update()
        {
            if (!turning) return;

            // Unscaled: a menu that stops turning because something paused the game is a menu you cannot
            // leave. Cubic ease-out — fastest at the start, so the turn reads as an answer to the press
            // rather than as an animation that began on its own.
            elapsed += Time.unscaledDeltaTime;
            float t = turnSeconds <= 0 ? 1 : Mathf.Clamp01(elapsed / turnSeconds);
            float eased = 1 - Mathf.Pow(1 - t, 3);
            ring.localRotation = Quaternion.Euler(0, Mathf.LerpUnclamped(from, to, eased), 0);

            if (t < 1) return;
            turning = false;
            Cull();
            Settled?.Invoke(Current);
        }

        /// Everything is drawn while the ring is moving, because a pane turning past is the point. Only when
        /// it settles does anything get switched off, and only if someone asked for that.
        void Cull()
        {
            if (!cullHidden) return;
            for (int i = 0; i < slots.Count; i++)
            {
                bool visible = turning || i == index;
                var pane = slots[i].pane;
                var document = pane.GetComponent<UIDocument>();
                if (document != null) document.enabled = visible;
                foreach (var collider in pane.GetComponentsInChildren<Collider>(true)) collider.enabled = visible;
            }
        }

        /// Stand the ring where the person is and size it to what they are already looking at, so panes
        /// arrive at the distance the scene was authored for rather than at a number typed into this file.
        public void Frame(Vector3 eye, Vector3 firstPane)
        {
            transform.position = eye;
            var flat = firstPane - eye; flat.y = 0;
            if (flat.sqrMagnitude > .0001f)
            {
                radius = flat.magnitude;
                transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }
            for (int i = 0; i < slots.Count; i++) Place(i);
        }
    }
}
