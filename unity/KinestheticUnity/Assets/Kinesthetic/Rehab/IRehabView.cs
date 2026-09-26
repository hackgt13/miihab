namespace Kinesthetic.Rehab
{
    /// What the mirror window needs to draw: the patient rig it reflects, and the plan's band. On the Mac the
    /// rehab session supplies it; on a headset the state client does, from what the Mac published.
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
    }
}
