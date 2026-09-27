using System;
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

        /// The turn on a flat view runs at one angular speed, so its length is its angle: half a second per
        /// quarter turn, and a turn too small to see still takes a beat rather than snapping.
        public const float TurnSecondsPerQuarter = .5f, MinTurnSeconds = .2f;
        /// The longest the beat stretches waiting for the next scene to finish loading, and the longest
        /// frame the walk's clock will believe: a hitch mid-walk becomes a slightly longer walk, not a skip.
        public const float MaxReadyWaitSeconds = 3f, MaxFrameSeconds = 1f / 30;

        static float Dt() => Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);

        /// A headset never turns smoothly, but a person pressing from a pane off to one side would otherwise
        /// be carried sideways toward a door they cannot see. So past this angle the rig snaps to face the
        /// door under a blink: dark, turned, back, with no motion in between for the inner ear to object to.
        public const float SnapThresholdDegrees = 15f, BlinkSeconds = .12f, BlinkClearSeconds = .25f;

        /// Carries `mover` through `portal` and leaves the view covered. `hide` is what should vanish as
        /// the walk starts — the ring of panes, which would otherwise sweep through the person's head.
        ///
        /// On a flat view (`turnToward`) the camera turns to face the door once it stands open, and only
        /// then walks — turning and sliding at once reads as a lurch from any angle but head-on.
        /// The look controller is switched off for the walk: it composes its drag on top of whatever is
        /// written to the camera, so with a dragged view the camera would never actually face the door.
        ///
        /// `ready` says whether the next scene has finished loading. The beat lasts until it has, so the
        /// loading's main-thread work lands while nothing moves and the walk itself runs on quiet frames.
        public static IEnumerator Enter(Portal portal, Transform mover, HeadFade fade, bool turnToward, Func<bool> ready, params GameObject[] hide)
        {
            foreach (var go in hide) if (go) go.SetActive(false);
            var look = turnToward ? mover.GetComponent<DevFreeLook>() : null;
            if (look) look.enabled = false;
            // The frame that kicked all this off — the load, the fade's first draw — is a long one; it is
            // not on the clock. Nothing moves in it.
            yield return null;

            var eye = Camera.main ? Camera.main.transform : mover;
            if (!turnToward) yield return Snap(mover, eye, fade, portal.Stop);
            portal.Open();   // after the snap, so the whole swing is seen

            // The walk carries the rig so that the *eyes* end up over the stop: a tracked head may stand off
            // its rig's origin by half a metre, and it is the head that must end inside the vestibule.
            Vector3 from = mover.position, to = portal.Stop;
            var offset = mover.position - eye.position; offset.y = 0;
            to += offset;
            to.y = from.y;   // the floor stays where the rig's floor is; a camera keeps its eye height
            var fromRotation = mover.rotation;
            var flat = to - from; flat.y = 0;
            var toRotation = flat.sqrMagnitude > .0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : fromRotation;

            // 1. The beat: the door swings while the next scene loads. Nothing moves, so the loading's long
            //    frames land where they cannot be seen as motion.
            float t = 0;
            while (t < BeatSeconds || (ready != null && !ready() && t < BeatSeconds + MaxReadyWaitSeconds))
            {
                t += Dt();
                yield return null;
            }

            // 2. The turn, on a flat view only, at one angular speed, on quiet frames.
            if (turnToward)
            {
                float turnSeconds = Mathf.Max(MinTurnSeconds, Quaternion.Angle(fromRotation, toRotation) / 90f * TurnSecondsPerQuarter);
                t = 0;
                while (t < turnSeconds)
                {
                    t += Dt();
                    mover.rotation = Quaternion.Slerp(fromRotation, toRotation, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / turnSeconds)));
                    yield return null;
                }
                mover.rotation = toRotation;
            }

            // 3. The walk.
            t = 0;
            while (t < DashSeconds)
            {
                t += Dt();
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

        /// Blink, turn the rig about the head so the head faces `target`, blink back. Nothing for a turn too
        /// small to matter.
        static IEnumerator Snap(Transform mover, Transform eye, HeadFade fade, Vector3 target)
        {
            var facing = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            var toTarget = target - eye.position; toTarget.y = 0;
            if (facing.sqrMagnitude < .0001f || toTarget.sqrMagnitude < .0001f) yield break;
            float delta = Vector3.SignedAngle(facing, toTarget, Vector3.up);
            if (Mathf.Abs(delta) < SnapThresholdDegrees) yield break;
            yield return fade.CoverTo(1, BlinkSeconds);
            mover.RotateAround(eye.position, Vector3.up, delta);   // the head stays where it is; the world turns under it
            yield return null;
            yield return fade.CoverTo(0, BlinkClearSeconds);
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
