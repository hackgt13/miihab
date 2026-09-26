using UnityEngine;

namespace Kinesthetic.UI.Boards
{
    /// A board that rides with the view instead of standing at its station: a heads-up readout, kept at its
    /// station's pitch and distance from the eyes but measured from wherever the view is looking, and eased
    /// there rather than pinned, so it drifts after a head turn the way a comfortable HUD does and never
    /// judders with the head. The station still sets its density (BoardBuilder), so its type is the same size
    /// to the eye as anything else at that distance. Works from Camera.main, which is the Mac's view camera
    /// and the headset's tracked eye alike, and mirrors to a headset as any board does.
    public sealed class ViewFollow : MonoBehaviour
    {
        public Station station;
        [Tooltip("How quickly the board catches up with the view; lower drifts more.")]
        public float ease = 6f;
        bool placed;

        void LateUpdate()
        {
            var eyes = Camera.main ? Camera.main.transform : null;
            if (!eyes) return;
            // The station's heading, in the view's frame: yaw about the view's up, pitch above its forward.
            var level = Quaternion.LookRotation(Vector3.ProjectOnPlane(eyes.forward, Vector3.up).sqrMagnitude > 1e-4f
                ? Vector3.ProjectOnPlane(eyes.forward, Vector3.up) : eyes.forward, Vector3.up);
            var heading = level * station.Heading;
            var target = eyes.position + heading * (Vector3.forward * station.distanceMetres);
            var facing = Quaternion.LookRotation(target - eyes.position, Vector3.up) * Quaternion.Euler(-station.tiltDegrees, 0, 0);
            float k = placed ? 1 - Mathf.Exp(-ease * Time.unscaledDeltaTime) : 1;
            transform.position = Vector3.Lerp(transform.position, target, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, facing, k);
            placed = true;
        }
    }
}
