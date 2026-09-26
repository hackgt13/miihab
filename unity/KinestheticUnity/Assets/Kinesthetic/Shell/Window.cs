using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Shell
{
    /// One panel standing in the ring around the person: the landing board, the activity gallery, friends,
    /// whatever comes next. A window is a world-space `UIDocument` and a name — it owns no layout of its own,
    /// so anything that can be a UXML can be a window without knowing the carousel exists.
    ///
    /// Windows are told when they come to face the person and when they turn away, which is the hook a panel
    /// wants for work it should not be doing while nobody is looking: friends polling the bridge, a chart
    /// re-drawing, audio. Nothing is torn down at the turn — `UIDocument.enabled = false` destroys the visual
    /// tree and rebuilds it on the way back, which would cost every panel its scroll position, its focus and
    /// its bindings for a repaint that UI Toolkit only performs when something actually changes. A panel that
    /// is genuinely expensive can opt out through `SetVisible`.
    [RequireComponent(typeof(UIDocument))]
    public sealed class Window : MonoBehaviour
    {
        [Tooltip("Stable name the rest of the app turns to: 'home', 'gallery', 'friends'.")]
        public string id;

        [Tooltip("Shown to the person on the way past — in the carousel's dots, and by anything that narrates.")]
        public string title;

        /// Raised when this window has finished turning to face the person, and when it has left.
        /// Presented fires once the turn completes, not when it starts, so a panel that refreshes on arrival
        /// does not do it while it is still edge-on and unreadable.
        public event Action Presented;
        public event Action Dismissed;

        /// Where in the ring this window stands. Assigned by the carousel; slot 0 is what the person sees first.
        public int Slot { get; internal set; }

        /// True between Presented and Dismissed.
        public bool IsFacing { get; private set; }

        UIDocument document;
        public UIDocument Document => document != null ? document : document = GetComponent<UIDocument>();

        internal void Face(bool facing)
        {
            if (IsFacing == facing) return;
            IsFacing = facing;
            if (facing) Presented?.Invoke(); else Dismissed?.Invoke();
        }

        /// Stop drawing this window entirely. Only for a panel heavy enough that the repaint is worth losing
        /// its state over — the visual tree is rebuilt from the UXML when it comes back, so whoever binds it
        /// has to be able to bind it twice.
        public void SetVisible(bool visible)
        {
            if (Document != null) Document.enabled = visible;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = visible;
        }
    }
}
