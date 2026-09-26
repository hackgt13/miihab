using UnityEngine;

namespace Kinesthetic
{
    /// Where the patient's eyes are in a scene, and nothing else. FirstPersonView finds this at runtime and
    /// builds the camera from it, so no scene carries a first-person camera of its own — scenes here are
    /// code-generated, and anything written into one is destroyed by the next Create().
    ///
    /// Ignored on a headset: an XRHMD means TrackedPoseDriver owns the head pose and QuestGolf's XR rig is
    /// already parked at the seat.
    public sealed class EyeAnchor : MonoBehaviour
    {
        public Transform follow;          // the seat, when it moves between lies; null pins the eyes here
        public float eyeHeight = 1.15f;   // seated eyes above the seat's floor
        public Transform lookAt;          // what the view faces at rest; null looks along the anchor's forward
        public Transform ownBody;         // the patient's own avatar: hidden from their own eyes, torso excepted
        public float fieldOfView = 63;    // wider than a spectator's — this is a view from inside a head
    }
}
