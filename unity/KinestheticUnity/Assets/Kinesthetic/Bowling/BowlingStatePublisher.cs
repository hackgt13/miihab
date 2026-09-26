using System;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Bowling
{
    public sealed class BowlingStatePublisher : MonoBehaviour
    {
        public string url = "ws://127.0.0.1:8767/bowling-state?role=host";
        BowlingGame game; LatestSocket socket; float next; long sequence;
        readonly string session = Guid.NewGuid().ToString("N");
        void Start() { game = GetComponent<BowlingGame>(); if (!game.renderOnly) socket = new LatestSocket(url); }
        static JArray V(Vector3 p) => new(p.x, p.y, p.z);
        static JArray Q(Quaternion q) => new(q.x, q.y, q.z, q.w);
        static JObject Body(Rigidbody body) => new() { ["p"] = V(body.transform.position), ["r"] = Q(body.transform.rotation), ["visible"] = body.gameObject.activeSelf };
        public string Snapshot()
        {
            game = game ? game : GetComponent<BowlingGame>();
            var pins = new JArray(); foreach (var pin in game.pins) pins.Add(Body(pin));
            var marks = new JArray(); var scores = new JArray();
            for (int i = 0; i < 10; i++) { marks.Add(game.Score.Marks(i)); scores.Add(game.Score.Cumulative(i)?.ToString() ?? "–"); }
            return new JObject { ["type"] = "bowling.state", ["session"] = session, ["seq"] = ++sequence,
                ["phase"] = game.Phase, ["cue"] = game.Cue, ["frame"] = game.Score.Frame + 1, ["roll"] = game.Score.BallNumber,
                ["aim"] = game.Phase == "Ready" ? game.Swing.Aim : game.LastAim, ["power"] = game.Phase == "Ready" ? game.Swing.Power : game.LastPower,
                ["connected"] = game.MotionReady, ["total"] = game.Score.Total, ["marks"] = marks, ["scores"] = scores,
                ["ball"] = Body(game.ball), ["pins"] = pins }.ToString(Newtonsoft.Json.Formatting.None);
        }
        void LateUpdate() { if (socket == null || !socket.connected || Time.unscaledTime < next) return; next = Time.unscaledTime + 1f / 30; socket.Send(Snapshot()); }
        void OnDestroy() => socket?.Dispose();
    }
}
