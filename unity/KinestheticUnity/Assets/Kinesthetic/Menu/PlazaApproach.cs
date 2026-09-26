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
    /// headset: nothing turns. The rig is never yawed. On the Mac's flat view the camera turns to face the
    /// door before it walks, because a screen is not a head, and a walk that starts head-on reads as a walk.
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

        /// Seconds to turn through 90 degrees on a flat view, and the shortest and longest turn allowed.
        public const float TurnSecondsPerQuarter = .45f, MinTurnSeconds = .25f, MaxTurnSeconds = .9f;

        /// Carries `mover` through `portal` and leaves the view covered. `hide` is what should vanish as
        /// the walk starts — the ring of panes, which would otherwise sweep through the person's head.
        ///
        /// On a flat view (`turnToward`) the camera first turns to face the door, while the door swings,
        /// and only then walks — turning and sliding at once reads as a lurch from any angle but head-on.
        /// The look controller is switched off for the walk: it composes its drag on top of whatever is
        /// written to the camera, so with a dragged view the camera would never actually face the door.
        public static IEnumerator Enter(Portal portal, Transform mover, HeadFade fade, bool turnToward, params GameObject[] hide)
        {
            foreach (var go in hide) if (go) go.SetActive(false);
            var look = turnToward ? mover.GetComponent<DevFreeLook>() : null;
            if (look) look.enabled = false;
            portal.Open();

            Vector3 from = mover.position, to = portal.Stop;
            to.y = from.y;   // the floor stays where the rig's floor is; a camera keeps its eye height
            var fromRotation = mover.rotation;
            var flat = to - from; flat.y = 0;
            var toRotation = flat.sqrMagnitude > .0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : fromRotation;
            float turnSeconds = turnToward
                ? Mathf.Clamp(Quaternion.Angle(fromRotation, toRotation) / 90f * TurnSecondsPerQuarter, MinTurnSeconds, MaxTurnSeconds)
                : 0;
            float beat = Mathf.Max(BeatSeconds, turnSeconds + .15f);

            float t = 0;
            while (t < beat)
            {
                t += Time.unscaledDeltaTime;
                if (turnToward) mover.rotation = Quaternion.Slerp(fromRotation, toRotation, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / turnSeconds)));
                yield return null;
            }
            if (turnToward) mover.rotation = toRotation;

            t = 0;
            while (t < DashSeconds)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / DashSeconds));
                mover.position = Vector3.Lerp(from, to, u);
                fade.Tunnel = TunnelDepth * u;
                fade.Cover = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(CoverFrom, 1, u));
                yield return null;
            }
            mover.position = to;
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
