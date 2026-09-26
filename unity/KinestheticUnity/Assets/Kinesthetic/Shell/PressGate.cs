using System.Collections.Generic;
using UnityEngine;

namespace Kinesthetic.Shell
{
    /// One press is one action, however many roads it arrives by.
    ///
    /// A world-space panel ends up with several input paths on purpose: UI Toolkit's own `clicked` event, a
    /// raycast from the cursor, a gaze dwell, and a keyboard submit on whatever is focused. Each of them is
    /// correct on its own and none of them knows about the others, so a single physical press can deliver
    /// two or three times. Trying to elect one true path is the wrong fix — it means deleting an input
    /// somebody needs, and on this product the somebody is usually the person who can only use that one.
    ///
    /// So the paths stay, and the rule lives at the single point they converge: a press is accepted unless
    /// the same element has already been pressed in this frame — which is duplication, not intent — or
    /// within the cooldown, which is a bounce. Nothing in a menu wants a rapid repeat of the same button;
    /// everything in it is ruined by one arriving twice. Launching an activity twice is the case that
    /// actually costs something, and it is the one the IsTurning guard on the carousel never covered.
    public sealed class PressGate
    {
        readonly float cooldownSeconds;
        readonly Dictionary<string, float> nextAllowed = new();
        int frame = -1;
        string last;

        /// 0.25 s is long enough to swallow a bounce or a second path landing a frame late, and short enough
        /// that a person deliberately pressing the same button again is never told no.
        public PressGate(float cooldownSeconds = .25f) => this.cooldownSeconds = cooldownSeconds;

        public bool Accept(string element)
        {
            if (string.IsNullOrEmpty(element)) return false;

            // The same element twice in one frame is one press arriving by two roads.
            if (frame == Time.frameCount && element == last) return false;

            // Unscaled, so a menu still answers while something has the game paused.
            if (nextAllowed.TryGetValue(element, out float until) && Time.unscaledTime < until) return false;

            frame = Time.frameCount;
            last = element;
            nextAllowed[element] = Time.unscaledTime + cooldownSeconds;
            return true;
        }

        /// Forget everything, for when a panel is rebound and its old elements are gone.
        public void Clear()
        {
            nextAllowed.Clear();
            frame = -1;
            last = null;
        }
    }
}
