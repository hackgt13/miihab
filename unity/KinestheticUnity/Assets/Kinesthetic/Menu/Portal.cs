using UnityEngine;

namespace Kinesthetic.Menu
{
    /// A doorway in the plaza that an activity is entered through. One per catalog venue, placed by
    /// MenuPlazaBuilder on the building that stands for that venue: the studio pavilion's door, the first-tee
    /// gatehouse, the lanes hut. It knows three things — the leaf that swings, the spot inside the vestibule
    /// where the person comes to rest, and which venue it is — and PlazaApproach does the rest.
    ///
    /// The vestibule behind the leaf is a short corridor painted the fade's own shade in every direction, so
    /// once the head is inside it the view is already dark on every side but the one it came in by, and the
    /// fade only has to finish the job. That is what lets the cut to the next scene land on a covered frame
    /// without anyone having to look a particular way.
    public sealed class Portal : MonoBehaviour
    {
        public string venue;

        [Tooltip("The door leaf's pivot, at its hinged edge. Null for an opening with no leaf.")]
        public Transform hinge;

        [Tooltip("Where the person comes to rest, on the floor inside the vestibule.")]
        public Transform stop;

        [Tooltip("Degrees the leaf swings to. Negative swings inward, out of the walker's way.")]
        public float openDegrees = -92;
        public float openSeconds = .9f;

        float angle, target;

        public bool IsOpen => Mathf.Approximately(angle, openDegrees);
        public bool IsClosed => Mathf.Approximately(angle, 0);
        public Vector3 Stop => stop ? stop.position : transform.position;

        public static Portal Find(string venue)
        {
            if (string.IsNullOrEmpty(venue)) return null;
            foreach (var portal in FindObjectsByType<Portal>(FindObjectsSortMode.None))
                if (portal.venue == venue) return portal;
            return null;
        }

        public void Open() => target = openDegrees;
        public void Close() => target = 0;

        void Update()
        {
            if (!hinge || Mathf.Approximately(angle, target)) return;
            float rate = Mathf.Abs(openDegrees) / Mathf.Max(.01f, openSeconds);
            angle = Mathf.MoveTowards(angle, target, Time.unscaledDeltaTime * rate);
            hinge.localRotation = Quaternion.Euler(0, angle, 0);
        }
    }
}
