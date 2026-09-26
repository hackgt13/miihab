using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kinesthetic.UI.Boards
{
    /// The patient's seat, as the thing boards stand around. Its transform is the seat anchor: the floor
    /// point under the patient, facing the way they face — the same pose QuestRehabSetup parks the XR
    /// origin at, so a board placed from here lands in the same spot for the Mac's eye camera and for a
    /// tracked headset. The eye point is `eyeHeight` straight up from it, which is EyeAnchor's convention
    /// too; EyeAnchor itself is not reused because in rehab it sits on the Mii's actor, whose forward is
    /// the back of the Mii, and its facing follows a moving target rather than the chair.
    ///
    /// A placement is remembered, not just applied: the boards are children of the seat and the list is
    /// what a verification reads back to check that every board is at its station, and what `Restand`
    /// uses if the seat is ever moved.
    public sealed class SeatRig : MonoBehaviour
    {
        [Serializable]
        public struct Placement
        {
            public Transform board;
            public Station station;
        }

        [Tooltip("Seated eyes above the seat's floor point, metres. 1.15 is what every rig here assumes.")]
        public float eyeHeight = 1.15f;

        [SerializeField] List<Placement> placements = new();

        public IReadOnlyList<Placement> Placements => placements;

        public Vector3 EyePoint => transform.position + Vector3.up * eyeHeight;

        /// The facing, levelled: a seat anchor is never pitched, since pitching the rig tilts the wearer's world.
        public Quaternion Facing
        {
            get
            {
                var flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                return flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
            }
        }

        /// Where a board at `station` stands: out from the eyes along the station's heading, turned to face
        /// back down it. A world-space panel's front is its local +Z pointing away from the viewer, measured
        /// against the authored menu board, so the heading itself is the board's rotation.
        public Pose PoseAt(Station station)
        {
            var heading = Facing * station.Heading;
            // Where it stands comes from the heading; how it is held comes from the tilt, about the board's
            // own sideways axis once it faces the eye. Pitching its +Z down turns the face the reader sees up.
            var held = heading * Quaternion.Euler(station.tiltDegrees, 0, 0);
            return new Pose(EyePoint + heading * (Vector3.forward * station.distanceMetres), held);
        }

        /// Stand `board` at `station` as a child of the seat, and remember that it lives there.
        public void Stand(Transform board, Station station)
        {
            board.SetParent(transform, true);
            var pose = PoseAt(station);
            board.SetPositionAndRotation(pose.position, pose.rotation);
            int at = placements.FindIndex(p => p.board == board);
            var placement = new Placement { board = board, station = station };
            if (at >= 0) placements[at] = placement; else placements.Add(placement);
        }

        /// Put every remembered board back at its station, for a seat that moved.
        public void Restand()
        {
            foreach (var placement in placements)
                if (placement.board) { var pose = PoseAt(placement.station); placement.board.SetPositionAndRotation(pose.position, pose.rotation); }
        }

        /// A world point as the seat sees it: yaw clockwise from the facing, pitch above the horizontal,
        /// metres from the eyes. The inverse of PoseAt, for checking where something actually stands.
        public void Bearing(Vector3 worldPoint, out float yawDegrees, out float pitchDegrees, out float distanceMetres)
        {
            var local = Quaternion.Inverse(Facing) * (worldPoint - EyePoint);
            distanceMetres = local.magnitude;
            yawDegrees = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            pitchDegrees = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
        }
    }
}
