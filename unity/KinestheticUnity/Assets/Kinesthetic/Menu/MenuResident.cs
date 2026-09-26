using UnityEngine;

namespace Kinesthetic.Menu
{
    /// The Mii on the plaza is scenery: no pose client drives it, so nothing calls PoseRig.Apply at
    /// runtime. PoseRig resolves LeftHand, RightHand and the rest through a bone dictionary that only
    /// Initialize() fills, and that had run in the editor when the scene was authored — the dictionary
    /// is not serialised, so in a player every one of those lookups returns null and MiiIdleLife
    /// dereferences a null hand on each LateUpdate.
    ///
    /// Posing it once on Awake settles both: the avatar takes its authored resting pose in the player
    /// rather than the imported bind pose, and the idle blink and breathing find the bones they expect.
    [RequireComponent(typeof(PoseRig))]
    public sealed class MenuResident : MonoBehaviour
    {
        void Awake() => GetComponent<PoseRig>().Apply(null);
    }
}
