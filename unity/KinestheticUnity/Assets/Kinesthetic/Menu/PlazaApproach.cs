using System.Collections;
using Kinesthetic.Shell;
using UnityEngine;

namespace Kinesthetic.Menu
{
    /// Walking into a venue: the one sequence both the Mac and the headset run when an activity is chosen.
    ///
    /// The door swings open first, and the person waits long enough to see it swing. Then they are carried
    /// in a straight line from where they stand to the spot inside the vestibule, over about a second, the
    /// edges of the view narrowing gently as they go. Straight is the comfortable way to move someone in a
    /// headset: nothing turns. The rig is never yawed. On the Mac's flat view the camera may turn to face
    /// the door as it goes, because a screen is not a head, and the turn is what makes the plaza sweep past.
    ///
    /// The view is not faded to dark and then swapped; it goes dark by entering the corridor. The vestibule
    /// is painted the fade's own colour, unlit, so as the doorway fills the view the fade rises with it over
    /// the last stretch of the walk and simply completes what the walls began. The caller swaps scenes on
    /// that covered frame and fades back up with Arrive.
    public static class PlazaApproach
    {
        public const float BeatSeconds = .6f, DashSeconds = 1.1f, HoldSeconds = .2f, ClearSeconds = 1.2f;
        public const float TunnelDepth = .45f;
        /// The point along the walk where the fade begins to rise: by then the doorway fills the view.
        public const float CoverFrom = .62f;

        /// Carries `mover` through `portal` and leaves the view covered. `hide` is what should vanish as
        /// the walk starts — the ring of panes, which would otherwise sweep through the person's head.
        public static IEnumerator Enter(Portal portal, Transform mover, HeadFade fade, bool turnToward, params GameObject[] hide)
        {
            foreach (var go in hide) if (go) go.SetActive(false);
            portal.Open();

            float t = 0;
            while (t < BeatSeconds) { t += Time.unscaledDeltaTime; yield return null; }

            Vector3 from = mover.position, to = portal.Stop;
            to.y = from.y;   // the floor stays where the rig's floor is; a camera keeps its eye height
            var fromRotation = mover.rotation;
            var flat = to - from; flat.y = 0;
            var toRotation = flat.sqrMagnitude > .0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : fromRotation;

            t = 0;
            while (t < DashSeconds)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / DashSeconds));
                mover.position = Vector3.Lerp(from, to, u);
                if (turnToward) mover.rotation = Quaternion.Slerp(fromRotation, toRotation, u);
                fade.Tunnel = TunnelDepth * u;
                fade.Cover = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(CoverFrom, 1, u));
                yield return null;
            }
            mover.position = to;
            if (turnToward) mover.rotation = toRotation;
            fade.Cover = 1;
            fade.Tunnel = 0;

            yield return Hold();
        }

        /// A beat at full cover before the swap on the way into a venue, so the dark reads as a place rather
        /// than a blink. The way back has no doorway and no beat: it fades and goes.
        public static IEnumerator Hold()
        {
            float t = 0;
            while (t < HoldSeconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        /// The other side of the cut: the new scene is up, and the view comes back.
        public static IEnumerator Arrive(HeadFade fade)
        {
            fade.Tunnel = 0;
            yield return fade.CoverTo(0, ClearSeconds);
        }
    }
}
