using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kinesthetic.Shell
{
    /// The mouse, for a pane standing in the world.
    ///
    /// A world-space UI Toolkit panel receives no pointer events at all: `panel.Pick` returns null on one,
    /// and without an EventSystem nothing feeds it anyway. So every button inside a pane is inert to a mouse
    /// — no hover, no click — however ordinary its UXML looks. That is not a bug in the panel; it is what a
    /// world-space panel is. Input has to be raycast at it.
    ///
    /// This reports exactly what `GazeDwell` reports — the name of the element under the pointer, and the
    /// name of the one that was pressed — so a pane's owner subscribes to the two identically and a mouse and
    /// a head end at the same action. That is the whole reason this is shaped like a duplicate of GazeDwell
    /// rather than folded into it: one resolves a ray from the centre of the view after a dwell, the other
    /// from the cursor on a click, and the only thing they should share is the vocabulary.
    ///
    /// The maths is GazeDwell's, and it is not obvious: a world-space panel lays its elements out in the
    /// panel's own units, centred and y-up, which is exactly the space the hit point lands in once it is put
    /// back into the collider's local space. So the hit converts straight to element coordinates with no
    /// pixel scaling — confirmed on this project's own 1600x900 panels, whose element rects come back as
    /// 16x9 centred on zero.
    [RequireComponent(typeof(UIDocument))]
    public sealed class PanePointerInput : MonoBehaviour
    {
        public float maxDistance = 12;

        /// The element under the cursor changed. The subscriber decides what hovering means.
        public event Action<string> Entered;

        /// The element under the cursor was pressed.
        public event Action<string> Committed;

        UIDocument document;
        string hot;

        void Awake() => document = GetComponent<UIDocument>();

        void Update()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null || document == null) return;

            string under = Under(cam, mouse.position.ReadValue(), out var button);
            if (under != hot)
            {
                hot = under;
                if (under != null)
                {
                    // Focus is the hover: this project's stylesheets pair :hover with :focus on everything
                    // that reacts, so focusing what the cursor is over restores the highlight a world-space
                    // panel cannot produce on its own — and it is the same highlight the keyboard gives.
                    button?.Focus();
                    Entered?.Invoke(under);
                }
            }

            if (under != null && mouse.leftButton.wasPressedThisFrame) Committed?.Invoke(under);
        }

        /// The name of the Button under this screen point on this pane, or null.
        string Under(Camera cam, Vector2 screenPoint, out Button found)
        {
            found = null;
            var ray = cam.ScreenPointToRay(screenPoint);
            if (!Physics.Raycast(ray, out var hit, maxDistance)) return null;
            if (!hit.collider.transform.IsChildOf(transform)) return null;   // a nearer pane owns this one
            var root = document.rootVisualElement;
            if (root == null) return null;

            var local = hit.collider.transform.InverseTransformPoint(hit.point);
            var point = new Vector2(local.x, local.y);
            foreach (var button in root.Query<Button>().ToList())
                if (!string.IsNullOrEmpty(button.name) && button.worldBound.Contains(point))
                {
                    found = button;
                    return button.name;
                }
            return null;
        }
    }
}
