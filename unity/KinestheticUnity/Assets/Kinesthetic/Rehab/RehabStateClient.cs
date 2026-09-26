using System;
using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// Headset: renders the rehab studio the Mac is running (RehabStatePublisher). Poses the patient and the
    /// coach from the published bones and feeds the mirror window. Reps, the cue and every button are the
    /// Mac's boards, mirrored by UI/Remote onto the same world-space boards this scene carries — so this
    /// draws no text of its own. It measures nothing and decides nothing.
    public sealed class RehabStateClient : MonoBehaviour, IRehabView
    {
        public PoseRig rig;
        [Tooltip("Where the coach sits: seated from this scene's patient (CoachDemonstrator.SeatPose), so the Mac's world position is not trusted across two scenes.")]
        public Transform coachSeat;
        public string url = "ws://127.0.0.1:8767/rehab-state?role=client";
        LatestSocket socket; List<Transform> patientBones, coachBones; Kinesthetic.Coach.CoachDemonstrator coach;
        float lastStateAt = -99;
        string side = "right", kind = "arm-elevation.v1"; float target = 80, band = 15; float? angle; bool handoff;

        public PoseRig Rig => rig;
        public string Side => side;
        public float TargetDeg => target;
        public float BandDeg => band;
        public string ExerciseKind => kind;
        public float? ShownAngle => Time.unscaledTime - lastStateAt < 1 ? angle : null;
        public bool CoachHandingOff => handoff;

        void Start()
        {
            Application.runInBackground = true;
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            rig.Initialize(); rig.Apply(null);
            patientBones = GolfStateFormat.Bones(rig);
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
            Pose(s["patient"], patientBones);
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

        static void Pose(JToken pose, List<Transform> bones)
        {
            if (pose == null || bones == null || (int?)pose["n"] != bones.Count) return;   // a different rig: never half-apply
            var lp = (JArray)pose["lp"]; var lr = (JArray)pose["lr"];
            for (int k = 0; k < bones.Count; k++) { bones[k].localPosition = GolfStateFormat.V(lp[k]); bones[k].localRotation = GolfStateFormat.Q(lr[k]); }
        }

        void OnDestroy() => socket?.Dispose();
    }
}
