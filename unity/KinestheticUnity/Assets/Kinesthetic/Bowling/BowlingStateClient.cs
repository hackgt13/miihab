using System;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Bowling
{
    // Quest renders the Mac's resolved bodies; no raw sensor input or local simulation.
    public sealed class BowlingStateClient : MonoBehaviour
    {
        public BowlingGame game;
        public TextMesh hud, scoreboard;
        public string overrideUrl;
        LatestSocket socket;
        string session;
        long sequence = -1;
        float last = -10;
        void Start()
        {
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            if (string.IsNullOrEmpty(overrideUrl) && (!config || config.host == "127.0.0.1" || string.IsNullOrEmpty(config.token)))
            { hud.text = "Pair with your Mac first.\nKinesthetic → Quest → Write host config"; enabled = false; return; }
            string url = string.IsNullOrEmpty(overrideUrl) ? $"ws://{config.host}:{config.port}/bowling-state?role=client&token={Uri.EscapeDataString(config.token)}" : overrideUrl;
            socket = new LatestSocket(url);
        }
        public bool Apply(string text)
        {
            try
            {
                var p = JObject.Parse(text);
                if ((string)p["type"] == "bowling.host-disconnected") { session = null; sequence = -1; last = -10; return false; }
                if ((string)p["type"] != "bowling.state" || p["pins"] is not JArray pins || pins.Count != game.pins.Length) return false;
                string nextSession = (string)p["session"]; long seq = (long)p["seq"];
                if (string.IsNullOrEmpty(nextSession) || nextSession == session && seq <= sequence) return false;
                if (!ValidBody(p["ball"])) return false;
                foreach (var pin in pins) if (!ValidBody(pin)) return false;
                var avatar = p["avatar"] as JArray;
                if (avatar != null && game.avatar)
                {
                    game.avatar.Initialize();
                    if (avatar.Count != game.avatar.joints.Length) return false;
                    foreach (var joint in avatar) if (!ValidBody(joint)) return false;
                }
                var marks = p["marks"] as JArray; var scores = p["scores"] as JArray;
                if (marks?.Count != 10 || scores?.Count != 10) return false;
                session = nextSession; sequence = seq;
                SetBody(game.ball, p["ball"]);
                for (int i = 0; i < pins.Count; i++) SetBody(game.pins[i], pins[i]);
                if (avatar != null && game.avatar) for (int i = 0; i < avatar.Count; i++)
                {
                    game.avatar.joints[i].localPosition = GolfStateFormat.V(avatar[i]["p"]);
                    game.avatar.joints[i].localRotation = GolfStateFormat.Q(avatar[i]["r"]);
                }
                string phase = (string)p["phase"];
                hud.text = $"BOWLING  ·  FRAME {p["frame"]}  ·  BALL {p["roll"]}\n{p["cue"]}\n{BowlingHud.AimText((float)p["aim"])}   ·   {Mathf.RoundToInt((float)p["power"] * 100)}% power";
                if (phase == "Complete") hud.text = $"Nice bowling!\n{p["total"]} points\nPlay again on your Mac.";
                scoreboard.text = "REHABMII  /  BOWLING\n";
                for (int i = 0; i < 10; i++) scoreboard.text += $"{i + 1}: {marks[i]} ({scores[i]})    " + (i == 4 ? "\n" : "");
                scoreboard.text += $"\nTOTAL   {p["total"]}";
                if (game.aimLine)
                {
                    game.aimLine.enabled = phase == "Ready";
                    game.aimLine.SetPosition(0, new Vector3(0, .012f, .3f));
                    game.aimLine.SetPosition(1, new Vector3(0, .012f, .3f) + Quaternion.Euler(0, (float)p["aim"], 0) * Vector3.forward * 4.2f);
                }
                last = Time.unscaledTime;
                return true;
            }
            catch (Exception) { return false; }
        }
        static bool ValidBody(JToken body)
        {
            if (body?["p"] is not JArray pos || pos.Count != 3 || body["r"] is not JArray rot || rot.Count != 4) return false;
            foreach (var n in pos) if (!float.IsFinite((float)n)) return false;
            float norm = 0; foreach (var n in rot) { float v = (float)n; if (!float.IsFinite(v)) return false; norm += v * v; }
            return norm > .5f && norm < 1.5f;
        }
        static void SetBody(Rigidbody body, JToken data)
        {
            body.isKinematic = true; body.gameObject.SetActive((bool)data["visible"]);
            body.position = GolfStateFormat.V(data["p"]); body.rotation = GolfStateFormat.Q(data["r"]);
        }
        void Update()
        {
            if (socket == null) return;
            if (socket.Take(out string text)) Apply(text);
            if (Time.unscaledTime - last > 1)
            { hud.text = "Waiting for Bowling on your Mac…"; if (game.aimLine) game.aimLine.enabled = false; }
        }
        void OnDestroy() => socket?.Dispose();
    }
}
