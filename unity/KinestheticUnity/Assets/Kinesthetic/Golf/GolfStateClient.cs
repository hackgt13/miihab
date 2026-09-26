using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Golf
{
    /// Headset client: renders the host's state; head tracking stays local to the headset.
    public sealed class GolfStateClient : MonoBehaviour
    {
        public KinestheticGolf game;             // disabled in the headset scene; used only for scene references
        public Transform patientAnchor;          // XR origin parent: follows the patient's seat and faces the ball
        public TextMesh hud;
        public string url = "ws://127.0.0.1:8767/state?role=client";
        LatestSocket socket; List<Transform>[] bones; JObject state; float lastState = -99;
        Vector3 ballTarget; Quaternion ballRot = Quaternion.identity;

        void Start()
        {
            Application.runInBackground = true;
            var config = Path.Combine(Application.streamingAssetsPath, "quest-host.json");
            // In the editor the relay is on this machine; the baked LAN address is for the headset build.
            if (!Application.isEditor && File.Exists(config)) { var c = JObject.Parse(File.ReadAllText(config)); url = $"ws://{c["host"]}:{c["port"] ?? 8767}/state?role=client&token={Uri.EscapeDataString((string)c["token"] ?? "")}"; }
            foreach (var r in game.rigs) r.Initialize();
            bones = new[] { GolfStateFormat.Bones(game.rigs[0]), GolfStateFormat.Bones(game.rigs[1]) };
            game.ball.isKinematic = true;
            ballTarget = game.ball.position;
            socket = new LatestSocket(url);
        }

        void Update()
        {
            if (socket.Take(out var text))
            {
                try { var s = JObject.Parse(text); if ((string)s["type"] == "golf.state") { state = s; lastState = Time.unscaledTime; ApplyPoses(s); } }
                catch (Exception) { }
            }
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 25);
            game.ball.position = Vector3.Lerp(game.ball.position, ballTarget, k);
            game.ball.rotation = Quaternion.Slerp(game.ball.rotation, ballRot, k);
            FollowPatient();
            if (hud)
            {
                bool live = Time.unscaledTime - lastState < 1;
                hud.text = !socket.connected ? "Connecting to the Kinesthetic Mac…" : !live ? "Waiting for the game on the Mac…" :
                    $"{((int)state["active"] == 0 ? "YOUR TURN" : "FRIEND'S TURN")}\nYOU {state["strokes"][0]} — {state["strokes"][1]} FRIEND   ·   {state["yards"]} yds";
            }
        }

        void ApplyPoses(JObject s)
        {
            ballTarget = GolfStateFormat.V(s["ball"]["p"]); ballRot = GolfStateFormat.Q(s["ball"]["r"]);
            var players = (JArray)s["players"];
            for (int i = 0; i < 2 && i < players.Count; i++)
            {
                var p = players[i];
                game.players[i].SetPositionAndRotation(GolfStateFormat.V(p["p"]), GolfStateFormat.Q(p["r"]));
                game.rigs[i].transform.localPosition = GolfStateFormat.V(p["rig"]);
                var lp = (JArray)p["lp"]; var lr = (JArray)p["lr"];
                if ((int)p["n"] != bones[i].Count) continue;   // different avatar build: never misapply bones
                for (int b = 0; b < bones[i].Count; b++) { bones[i][b].localPosition = GolfStateFormat.V(lp[b]); bones[i][b].localRotation = GolfStateFormat.Q(lr[b]); }
                var club = p["club"];
                game.clubs[i].SetPositionAndRotation(GolfStateFormat.V(club["p"]), GolfStateFormat.Q(club["r"]));
                game.clubs[i].localScale = GolfStateFormat.V(club["s"]);
            }
        }

        // The headset sits at the patient's seat, facing their ball. It only re-anchors when the patient's seat moves
        // to a new lie (between turns). Never rotate or glide the view on its own mid-shot: that causes VR sickness.
        Vector3 anchoredSeat = new(float.NaN, 0, 0);
        void FollowPatient()
        {
            if (!patientAnchor) return;
            var seat = game.players[0].position;
            if (float.IsNaN(anchoredSeat.x) || Vector3.Distance(anchoredSeat, seat) > .5f)
            {
                var toBall = Vector3.ProjectOnPlane(game.ball.position - seat, Vector3.up);
                if (toBall.sqrMagnitude < .01f || toBall.magnitude > 6) toBall = Vector3.ProjectOnPlane(game.cup.position - seat, Vector3.up);
                patientAnchor.SetPositionAndRotation(seat, Quaternion.LookRotation(toBall.normalized, Vector3.up));
                anchoredSeat = seat;
                if (hud) { hud.transform.position = seat + patientAnchor.forward * 3.2f + Vector3.up * 2.1f; hud.transform.rotation = patientAnchor.rotation; }
            }
        }

        void OnDestroy() => socket?.Dispose();
    }
}
