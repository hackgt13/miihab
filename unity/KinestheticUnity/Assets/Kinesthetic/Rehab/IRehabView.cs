namespace Kinesthetic.Rehab
{
    /// What the mirror window and the in-world mechanics need to draw: the patient rig, the plan's band, and how
    /// the rep in progress feels. On the Mac the rehab session supplies it; on a headset the state client does,
    /// from what the Mac published.
    public interface IRehabView
    {
        PoseRig Rig { get; }
        string Side { get; }
        float TargetDeg { get; }
        float BandDeg { get; }
        string ExerciseKind { get; }
        /// The measured angle the arm is showing, or null when nothing live is being measured.
        float? ShownAngle { get; }
        bool CoachHandingOff { get; }
        /// The rep in progress as the mechanics feel it (Mechanics/). Computed on the Mac, rendered anywhere.
        RepFeel Feel { get; }
    }

    /// How the rep is being made, right now — the input to a mechanic. The speeds are what make it a game:
    /// the tempo is the speed the plan asks for, and a mechanic answers to how far the arm is over it.
    /// The judgements (holding, met, hitches, fast) are the coordinator's, from its live quality readouts;
    /// the instantaneous speed is the presentation's, from the angle it is showing, and is never scored.
    [System.Serializable]
    public struct RepFeel
    {
        public bool InRep;            // a rep is being made
        public string Phase;          // raise | hold | lower, or empty between reps
        public float Speed;           // the segment's angular speed now, degrees per second, signed with the phase
        public float TempoSpeed;      // the prescribed speed for this phase, degrees per second (0 when there is no tempo)
        public bool HasTempo, HasHold;   // which qualities this exercise is judged on
        public float HoldFraction;    // how much of the hold target has been held, 0..1
        public bool Holding, HoldMet; // the arm is in the band right now; the hold target has been met this rep
        public int Hitches, Streak;   // hitches so far this rep; reps in a row made well
        public bool Fast;             // the coordinator says: slower
        public int Valid, Prescribed; // the set so far: reps counted, of how many

        /// How far over the tempo the arm is moving, 0 at or under it, 1 at double. The thing a balance answers to.
        public float OverSpeed => TempoSpeed <= 0 ? 0 : UnityEngine.Mathf.Clamp01(UnityEngine.Mathf.Abs(Speed) / TempoSpeed - 1);
    }
}
