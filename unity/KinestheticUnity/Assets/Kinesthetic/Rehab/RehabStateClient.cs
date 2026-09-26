using System;
using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// Headset: renders the rehab studio the Mac is running (RehabStatePublisher). Poses the patient and the
    /// coach from the published bones, feeds the mirror window, and shows reps and the cue on a world-space
    /// panel (screen-space UI does not render in a headset). It measures nothing and decides nothing.
    public sealed class RehabStateClient : MonoBehaviour, IRehabView
    {
        public PoseRig rig;
        public TextMesh hud;
        [Tooltip("Where the coach sits in first person (in view to the right). The Mac's placement suits its own camera, not the patient's eyes.")]
        public Transform coachSeat;
        public string url = "ws://127.0.0.1:8767/rehab-state?role=client";
        LatestSocket socket; List<Transform> patientBones, coachBones; Kinesthetic.Coach.CoachDemonstrator coach;
        float lastStateAt = -99;
        string side = "right", kind = "arm-elevation.v1", cue = ""; float target = 80, band = 15; float? angle; bool handoff;
        int valid, prescribed;

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
            if (!Application.isEditor && config)
                url = $"ws://{config.host}:{config.port}/rehab-state?role=client&token={Uri.EscapeDataString(config.token ?? "")}";
            rig.Initialize(); rig.Apply(null);
            patientBones = GolfStateFormat.Bones(rig);
            socket = new LatestSocket(url);
        }

        void Update()
        {
            if (socket.Take(out var text))
            {
                try { Apply(JObject.Parse(text)); lastStateAt = Time.unscaledTime; } catch (Exception) { }
            }
            if (hud) hud.text = Time.unscaledTime - lastStateAt > 2 ? "Waiting for the RehabMii studio on the Mac…"
                : $"{valid} of {prescribed}\n{cue}";
        }

        void Apply(JObject s)
        {
            if ((string)s["type"] != "rehab.state") return;
            side = (string)s["side"] ?? side; kind = (string)s["kind"] ?? kind; cue = (string)s["cue"] ?? "";
            target = (float?)s["target"] ?? target; band = (float?)s["band"] ?? band;
            angle = s["angle"]?.Type is JTokenType.Float or JTokenType.Integer ? (float)s["angle"] : null;
            valid = (int?)s["valid"] ?? 0; prescribed = (int?)s["prescribed"] ?? 0; handoff = (bool?)s["handoff"] ?? false;
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
