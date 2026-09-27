using System;
using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Tutorial
{
    /// Headset: renders the intro the Mac is running (TutorialStatePublisher). Spawns the same Alex from the same
    /// prefab, seats them where the Mac did, and poses every bone from the published state. Alex's own
    /// demonstrator and voice are switched off here: the Mac demonstrates and speaks, and this copy is posed.
    /// It measures nothing and decides nothing.
    public sealed class TutorialStateClient : MonoBehaviour
    {
        public string url = "ws://127.0.0.1:8767/tutorial-state?role=client";
        const string CoachPrefab = "Coach/TrainerCoach";
        LatestSocket socket; List<Transform> bones; Kinesthetic.Coach.CoachDemonstrator coach;

        void Start()
        {
            Application.runInBackground = true;
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            socket = !Application.isEditor && config ? new LatestSocket(config.Url("/tutorial-state?role=client")) : new LatestSocket(url);
        }

        void Update()
        {
            if (socket.Take(out var text))
            {
                try { Apply(JObject.Parse(text)); } catch (Exception) { }
            }
        }

        void Apply(JObject s)
        {
            if ((string)s["type"] != "tutorial.state" || s["coach"] is not JObject c) return;
            if (!coach && !Spawn()) return;
            coach.transform.SetPositionAndRotation(GolfStateFormat.V(c["p"]), GolfStateFormat.Q(c["r"]));
            if (c["pose"] is JObject pose && (int?)pose["n"] == bones.Count)   // a different rig: never half-apply
            {
                var lp = (JArray)pose["lp"]; var lr = (JArray)pose["lr"];
                for (int k = 0; k < bones.Count; k++) { bones[k].localPosition = GolfStateFormat.V(lp[k]); bones[k].localRotation = GolfStateFormat.Q(lr[k]); }
            }
        }

        /// Alex, from the prefab the Mac's sequencer spawns, posed by the Mac rather than by their own demonstrator.
        bool Spawn()
        {
            var prefab = Resources.Load<GameObject>(CoachPrefab);
            if (!prefab) { Debug.LogError("Tutorial: the coach prefab is missing at Resources/" + CoachPrefab); enabled = false; return false; }
            var go = Instantiate(prefab); go.name = "Coach (posed by the Mac)";
            coach = go.GetComponent<Kinesthetic.Coach.CoachDemonstrator>();
            if (!coach || !coach.model) { Debug.LogError("Tutorial: the coach prefab has no CoachDemonstrator with a model."); enabled = false; return false; }
            coach.demonstrating = false; coach.enabled = false;
            foreach (var voice in go.GetComponentsInChildren<Kinesthetic.Coach.CoachVoice>(true)) voice.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            bones = coach.model.GetComponentsInChildren<Transform>(true).ToList();
            return true;
        }

        void OnDestroy() => socket?.Dispose();
    }
}
