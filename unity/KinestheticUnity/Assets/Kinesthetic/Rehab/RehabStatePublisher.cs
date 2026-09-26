using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kinesthetic.Golf;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// Mac host: publishes the rehab studio as it is being rendered — the patient's, the coach's and a group partner's bone poses,
    /// the band, reps and cue — about 30 times a second on the relay's /rehab-state channel. A headset renders
    /// exactly this and computes nothing (RehabStateClient). Added at runtime by RehabSession.
    public sealed class RehabStatePublisher : MonoBehaviour
    {
        public string url = "ws://127.0.0.1:8767/rehab-state?role=host";
        public RehabSession session;
        LatestSocket socket; List<Transform> patientBones, coachBones, partnerBones;
        PoseRig partnerRig;
        Kinesthetic.Coach.CoachDemonstrator coach;
        long seq; float next;

        void Start()
        {
            // Nothing to publish without a session and its rig; say so once rather than once a frame.
            if (!session || !session.rig) { Debug.LogWarning("RehabStatePublisher has no session or rig to publish; disabled."); enabled = false; return; }
            patientBones = GolfStateFormat.Bones(session.rig);
            socket = new LatestSocket(url);
        }

        void LateUpdate()
        {
            if (socket == null || Time.unscaledTime < next || !socket.connected) return;
            next = Time.unscaledTime + 1f / 30;
            if (!coach && (coach = FindAnyObjectByType<Kinesthetic.Coach.CoachDemonstrator>()))
                coachBones = coach.model.GetComponentsInChildren<Transform>(true).ToList();
            var view = (IRehabView)session;
            var b = new StringBuilder(12288);
            b.Append("{\"type\":\"rehab.state\",\"seq\":").Append(++seq)
             .Append(",\"running\":").Append(session.IsRunning ? "true" : "false")
             .Append(",\"angle\":").Append(view.ShownAngle is float a ? a.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "null")
             .Append(",\"side\":\"").Append(view.Side).Append("\",\"kind\":\"").Append(view.ExerciseKind)
             .Append("\",\"target\":").Append(view.TargetDeg.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
             .Append(",\"band\":").Append(view.BandDeg.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
             .Append(",\"valid\":").Append(session.Valid).Append(",\"prescribed\":").Append(session.prescribedReps)
             .Append(",\"handoff\":").Append(view.CoachHandingOff ? "true" : "false")
             .Append(",\"cue\":").Append(Newtonsoft.Json.JsonConvert.ToString(session.Cue ?? ""))
             .Append(",\"feel\":"); Feel(b, view.Feel);
            b.Append(",\"patient\":");
            Pose(b, patientBones);
            if (coach && coachBones != null)
            {
                b.Append(",\"coach\":{\"p\":"); GolfStateFormat.Vec(b, coach.transform.position);
                b.Append(",\"r\":"); GolfStateFormat.Quat(b, coach.transform.rotation);
                b.Append(",\"pose\":"); Pose(b, coachBones); b.Append('}');
            }
            Partner(b);
            b.Append('}');
            socket.Send(b.ToString());
        }

        /// The group partner, while the patient is in a group (PeerAvatar stands in for the mirror): who, their
        /// state line, their Mii, and their bones as drawn here. Absent on your own, which is how a headset knows
        /// to keep its mirror. The headset seats them from its own patient, as it does the coach.
        void Partner(StringBuilder b)
        {
            var partner = session.GetComponent<PeerAvatar>();
            if (!partner) return;
            b.Append(",\"partner\":{\"showing\":").Append(partner.Showing ? "true" : "false");
            if (partner.Showing && partner.Rig)
            {
                if (partnerRig != partner.Rig) { partnerRig = partner.Rig; partnerBones = GolfStateFormat.Bones(partnerRig); }
                b.Append(",\"name\":").Append(Newtonsoft.Json.JsonConvert.ToString(partner.ShownName))
                 .Append(",\"state\":").Append(Newtonsoft.Json.JsonConvert.ToString(partner.ShownState))
                 .Append(",\"mii\":").Append(partner.ShownMii)
                 .Append(",\"pose\":"); Pose(b, partnerBones);
            }
            b.Append('}');
        }

        /// The rep as the mechanics feel it, so a headset's ball tips and pacer moves exactly as the Mac's do.
        static void Feel(StringBuilder b, RepFeel f)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            b.Append("{\"rep\":").Append(f.InRep ? "true" : "false")
             .Append(",\"phase\":\"").Append(f.Phase ?? "").Append("\",\"speed\":").Append(f.Speed.ToString("0.#", inv))
             .Append(",\"tempo\":").Append(f.TempoSpeed.ToString("0.#", inv))
             .Append(",\"hasTempo\":").Append(f.HasTempo ? "true" : "false").Append(",\"hasHold\":").Append(f.HasHold ? "true" : "false")
             .Append(",\"hold\":").Append(f.HoldFraction.ToString("0.##", inv))
             .Append(",\"holding\":").Append(f.Holding ? "true" : "false").Append(",\"met\":").Append(f.HoldMet ? "true" : "false")
             .Append(",\"hitches\":").Append(f.Hitches).Append(",\"streak\":").Append(f.Streak)
             .Append(",\"fast\":").Append(f.Fast ? "true" : "false")
             .Append(",\"valid\":").Append(f.Valid).Append(",\"prescribed\":").Append(f.Prescribed).Append('}');
        }

        static void Pose(StringBuilder b, List<Transform> bones)
        {
            b.Append("{\"n\":").Append(bones.Count).Append(",\"lp\":[");
            for (int k = 0; k < bones.Count; k++) { if (k > 0) b.Append(','); GolfStateFormat.Vec(b, bones[k].localPosition); }
            b.Append("],\"lr\":[");
            for (int k = 0; k < bones.Count; k++) { if (k > 0) b.Append(','); GolfStateFormat.Quat(b, bones[k].localRotation); }
            b.Append("]}");
        }

        void OnDestroy() => socket?.Dispose();
    }
}
