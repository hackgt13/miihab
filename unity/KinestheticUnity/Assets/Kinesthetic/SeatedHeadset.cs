using UnityEngine;
using UnityEngine.XR;

namespace Kinesthetic
{
    /// Puts the wearer's eyes where the patient's are. A floor-origin XR rig parked at the seat otherwise shows
    /// the world from wherever the wearer happens to stand in their room, at standing height: above and behind
    /// the wheelchair, a third-person view of oneself. This moves the rig (never the tracked camera) so the
    /// camera sits `eyeHeight` above the seat, turned to the seat's facing.
    ///
    /// The head still moves freely: turning and leaning are the wearer's own. The rig re-centres when the
    /// seat moves or turns (golf moves the patient to each new lie), and when the head drifts more than
    /// `driftMetres` from the seat, as it does if the wearer stands up or walks off.
    public sealed class SeatedHeadset : MonoBehaviour
    {
        public Transform seat;              // floor point under the patient, facing where they face
        public Transform head;              // the tracked camera
        public float eyeHeight = 1.15f;     // seated eyes above the seat's floor point; every rig here uses 1.15
        public float driftMetres = .5f;

        Vector3 placedSeat; Quaternion placedFacing; bool placed;

        void LateUpdate()
        {
            if (!seat || !head || !XRSettings.isDeviceActive) return;   // no headset: the camera stays where it was built
            var facing = Flat(seat.forward);
            var eye = seat.position + Vector3.up * eyeHeight;
            bool seatMoved = !placed || (seat.position - placedSeat).sqrMagnitude > .0025f || Quaternion.Angle(facing, placedFacing) > 2;
            if (seatMoved || (head.position - eye).sqrMagnitude > driftMetres * driftMetres) Recenter(eye, facing);
        }

        /// Zero the head where it is now: the eyes back at the seated eye point, facing the seat's way. Asked for when
        /// a set starts, with the patient sitting still and upright.
        public void RecenterNow()
        {
            if (!seat || !head) return;
            Recenter(seat.position + Vector3.up * eyeHeight, Flat(seat.forward));
        }

        /// Turn the rig about the head so the head looks along the seat's facing, then carry the head to the eyes.
        public void Recenter(Vector3 eye, Quaternion facing)
        {
            var look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (look.sqrMagnitude > 1e-4f)
                transform.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(look, facing * Vector3.forward, Vector3.up));
            transform.position += eye - head.position;
            placed = true; placedSeat = seat.position; placedFacing = facing;
        }

        static Quaternion Flat(Vector3 forward)
        {
            var flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            return flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
        }
    }
}
