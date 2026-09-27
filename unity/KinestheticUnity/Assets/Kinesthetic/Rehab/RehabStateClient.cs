using System;
using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// Headset: renders the rehab studio the Mac is running (RehabStatePublisher). Poses the patient, the coach
    /// and each group member from the published bones, and feeds the mirror window when there is no group. Reps,
    /// the cue and every button are the Mac's boards, mirrored by UI/Remote onto the same world-space boards this
    /// scene carries; the one text it draws is each member's name tag, which is theirs. It measures nothing and
    /// decides nothing.
    public sealed class RehabStateClient : MonoBehaviour, IRehabView
    {
        public PoseRig rig;
        [Tooltip("Where the coach sits: seated from this scene's patient (CoachDemonstrator.SeatPose), so the Mac's world position is not trusted across two scenes.")]
        public Transform coachSeat;
        public string url = "ws://127.0.0.1:8767/rehab-state?role=client";
        LatestSocket socket; List<Transform> patientBones, coachBones; Kinesthetic.Coach.CoachDemonstrator coach;
        PeerAvatar group;
        readonly Dictionary<PoseRig, List<Transform>> memberBones = new();
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

        int lastUpright = -1;
        void Apply(JObject s)
        {
            if ((string)s["type"] != "rehab.state") return;
            side = (string)s["side"] ?? side; kind = (string)s["kind"] ?? kind;
            target = (float?)s["target"] ?? target; band = (float?)s["band"] ?? band;
            angle = s["angle"]?.Type is JTokenType.Float or JTokenType.Integer ? (float)s["angle"] : null;
            handoff = (bool?)s["handoff"] ?? false;
            // A set is starting with the patient sitting still and upright: zero the headset's head there, so the
            // lean the Mac draws (and measures) is from how they sat at the start of the set.
            if ((int?)s["upright"] is int upright && upright != lastUpright)
            {
                if (lastUpright >= 0) FindAnyObjectByType<SeatedHeadset>()?.RecenterNow();
                lastUpright = upright;
            }
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
            Group(s["partners"] as JArray);
            Trajectory(s["trajectory"] as JObject);
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

        /// The rest of the group in the seats the Mac gave them, exactly as the Mac draws them, beside the mirror. Seated from this scene's patient (PeerAvatar), so the Mac's world positions are not
        /// trusted here.
        void Group(JArray people)
        {
            if (people == null)
            {
                if (group) { Destroy(group); group = null; memberBones.Clear(); }
                return;
            }
            if (!group) { group = gameObject.AddComponent<PeerAvatar>(); group.view = this; }
            var shown = new List<(string, string, int)>();
            foreach (var p in people.OfType<JObject>()) shown.Add(((string)p["name"], (string)p["state"], (int?)p["mii"] ?? 0));
            group.Present(shown);
            for (int i = 0; i < group.Seats.Count && i < people.Count; i++)
            {
                var rig = group.Seats[i].Rig;
                if (!rig) continue;   // a seat is built before it is posed; its first frame may still be coming
                if (!memberBones.TryGetValue(rig, out var bones)) memberBones[rig] = bones = GolfStateFormat.Bones(rig);
                Pose(people[i]["pose"], bones);
            }
        }

        /// The curl's path the Mac is drawing, drawn here too; hidden when the Mac sends none.
        void Trajectory(JObject path)
        {
            var curl = GetComponent<CurlTrajectory>();
            if (!curl)
            {
                if (path == null) return;
                curl = gameObject.AddComponent<CurlTrajectory>();
                curl.view = this; curl.Remote = true;
            }
            curl.Apply(path);
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
