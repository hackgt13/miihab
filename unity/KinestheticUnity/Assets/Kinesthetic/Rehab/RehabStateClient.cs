using System;
using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// Headset: renders the rehab studio the Mac is running (RehabStatePublisher). Poses the patient, the coach
    /// and a group partner from the published bones, and feeds the mirror window when there is no partner. Reps,
    /// the cue and every button are the Mac's boards, mirrored by UI/Remote onto the same world-space boards this
    /// scene carries; the one text it draws is the partner's name tag, which is theirs. It measures nothing and
    /// decides nothing.
    public sealed class RehabStateClient : MonoBehaviour, IRehabView
    {
        public PoseRig rig;
        [Tooltip("Where the coach sits: seated from this scene's patient (CoachDemonstrator.SeatPose), so the Mac's world position is not trusted across two scenes.")]
        public Transform coachSeat;
        public string url = "ws://127.0.0.1:8767/rehab-state?role=client";
        LatestSocket socket; List<Transform> patientBones, coachBones, partnerBones; Kinesthetic.Coach.CoachDemonstrator coach;
        PeerAvatar partner; PoseRig partnerRig;
        float lastStateAt = -99;
        string side = "right", kind = "arm-elevation.v1"; float target = 80, band = 15; float? angle; bool handoff;
        RepFeel feel;
        public RepFeel Feel => Time.unscaledTime - lastStateAt < 1 ? feel : default;

        public PoseRig Rig => rig;
        public string Side => side;
        public float TargetDeg => target;
        public float BandDeg => band;
        public string ExerciseKind => kind;
        public float? ShownAngle => Time.unscaledTime - lastStateAt < 1 ? angle : null;
        public bool CoachHandingOff => handoff;

        // The scene's mirror is given this view by QuestRehabSetup, but an interface field is not serialised, so
        // on its own it wakes with none and switches itself off. Hand it over before any Start runs.
        void Awake()
        {
            if (GetComponent<MirrorPanel>() is MirrorPanel mirror && mirror.view == null) mirror.view = this;
        }

        void Start()
        {
            Application.runInBackground = true;
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            rig.Initialize(); rig.Apply(null);
            patientBones = GolfStateFormat.Bones(rig);
            // The same in-world mechanics the Mac runs, driven by the feel it publishes.
            Mechanics.RepMechanics.AttachAll(gameObject, this);
            socket = !Application.isEditor && config ? new LatestSocket(config.Url("/rehab-state?role=client")) : new LatestSocket(url);
        }

        void Update()
        {
            if (socket.Take(out var text))
            {
                try { Apply(JObject.Parse(text)); lastStateAt = Time.unscaledTime; } catch (Exception) { }
            }
        }

        void Apply(JObject s)
        {
            if ((string)s["type"] != "rehab.state") return;
            side = (string)s["side"] ?? side; kind = (string)s["kind"] ?? kind;
            target = (float?)s["target"] ?? target; band = (float?)s["band"] ?? band;
            angle = s["angle"]?.Type is JTokenType.Float or JTokenType.Integer ? (float)s["angle"] : null;
            handoff = (bool?)s["handoff"] ?? false;
            if (s["feel"] is JObject f)
                feel = new RepFeel
                {
                    InRep = (bool?)f["rep"] ?? false, Phase = (string)f["phase"] ?? "",
                    Speed = (float?)f["speed"] ?? 0, TempoSpeed = (float?)f["tempo"] ?? 0,
                    HasTempo = (bool?)f["hasTempo"] ?? false, HasHold = (bool?)f["hasHold"] ?? false,
                    HoldFraction = (float?)f["hold"] ?? 0, Holding = (bool?)f["holding"] ?? false, HoldMet = (bool?)f["met"] ?? false,
                    Hitches = (int?)f["hitches"] ?? 0, Streak = (int?)f["streak"] ?? 0, Fast = (bool?)f["fast"] ?? false,
                    Valid = (int?)f["valid"] ?? 0, Prescribed = (int?)f["prescribed"] ?? 0,
                };
            Pose(s["patient"], patientBones);
            Partner(s["partner"] as JObject);
            if (s["coach"] is JObject c)
            {
                if (!coach && (coach = FindAnyObjectByType<Kinesthetic.Coach.CoachDemonstrator>()))
                {
                    coach.enabled = false;   // the Mac's coach drives this one
                    coachBones = coach.model.GetComponentsInChildren<Transform>(true).ToList();
                }
                if (coach)
                {
                    if (coachSeat) coach.transform.SetPositionAndRotation(coachSeat.position, coachSeat.rotation);
                    else coach.transform.SetPositionAndRotation(GolfStateFormat.V(c["p"]), GolfStateFormat.Q(c["r"]));
                    Pose(c["pose"], coachBones);
                }
            }
        }

        /// The group partner where the mirror stands, exactly as the Mac draws them; the mirror when the Mac has
        /// none. Seated from this scene's patient (PeerAvatar), so the Mac's world position is not trusted here.
        void Partner(JObject p)
        {
            if (p == null)
            {
                if (partner) { Destroy(partner); partner = null; partnerBones = null; partnerRig = null; }
                if (!GetComponent<MirrorPanel>()) gameObject.AddComponent<MirrorPanel>().view = this;
                return;
            }
            if (!partner)
            {
                if (GetComponent<MirrorPanel>() is MirrorPanel mirror) Destroy(mirror);
                partner = gameObject.AddComponent<PeerAvatar>(); partner.view = this;
            }
            bool showing = (bool?)p["showing"] ?? false;
            partner.Present(showing, (string)p["name"], (string)p["state"], (int?)p["mii"] ?? 0);
            if (!showing || !partner.Rig) return;   // its copy is built on its first frame
            if (partnerRig != partner.Rig) { partnerRig = partner.Rig; partnerBones = GolfStateFormat.Bones(partnerRig); }
            Pose(p["pose"], partnerBones);
        }

        static void Pose(JToken pose, List<Transform> bones)
        {
            if (pose == null || bones == null || (int?)pose["n"] != bones.Count) return;   // a different rig: never half-apply
            var lp = (JArray)pose["lp"]; var lr = (JArray)pose["lr"];
            for (int k = 0; k < bones.Count; k++) { bones[k].localPosition = GolfStateFormat.V(lp[k]); bones[k].localRotation = GolfStateFormat.Q(lr[k]); }
        }

        void OnDestroy() => socket?.Dispose();
    }
}
