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
    /// Mac host: publishes the authoritative rendered state ~30 times per second.
    public sealed class GolfStatePublisher : MonoBehaviour
    {
        public string url = "ws://127.0.0.1:8767/state?role=host";
        KinestheticGolf game; LatestSocket socket; List<Transform>[] bones; long seq; float next;
        void Start()
        {
            game = GetComponent<KinestheticGolf>();
            bones = new[] { GolfStateFormat.Bones(game.rigs[0]), GolfStateFormat.Bones(game.rigs[1]) };
            socket = new LatestSocket(url);
        }
        void LateUpdate()
        {
            if (Time.unscaledTime < next || !socket.connected) return;
            next = Time.unscaledTime + 1f / 30;
            var b = new StringBuilder(8192);
            b.Append("{\"type\":\"golf.state\",\"seq\":").Append(++seq).Append(",\"phase\":\"").Append(game.Phase)
             .Append("\",\"active\":").Append(game.activePlayer).Append(",\"strokes\":[").Append(game.Strokes[0]).Append(',').Append(game.Strokes[1])
             .Append("],\"yards\":").Append(Mathf.RoundToInt(Vector3.ProjectOnPlane(game.cup.position - game.ball.position, Vector3.up).magnitude * 1.0936f))
             .Append(",\"ball\":{\"p\":"); GolfStateFormat.Vec(b, game.ball.position); b.Append(",\"r\":"); GolfStateFormat.Quat(b, game.ball.rotation);
            b.Append("},\"players\":[");
            for (int i = 0; i < 2; i++)
            {
                if (i > 0) b.Append(',');
                b.Append("{\"p\":"); GolfStateFormat.Vec(b, game.players[i].position); b.Append(",\"r\":"); GolfStateFormat.Quat(b, game.players[i].rotation);
                b.Append(",\"rig\":"); GolfStateFormat.Vec(b, game.rigs[i].transform.localPosition);
                b.Append(",\"n\":").Append(bones[i].Count).Append(",\"lp\":[");
                for (int k = 0; k < bones[i].Count; k++) { if (k > 0) b.Append(','); GolfStateFormat.Vec(b, bones[i][k].localPosition); }
                b.Append("],\"lr\":[");
                for (int k = 0; k < bones[i].Count; k++) { if (k > 0) b.Append(','); GolfStateFormat.Quat(b, bones[i][k].localRotation); }
                b.Append("],\"club\":{\"p\":"); GolfStateFormat.Vec(b, game.clubs[i].position); b.Append(",\"r\":"); GolfStateFormat.Quat(b, game.clubs[i].rotation);
                b.Append(",\"s\":"); GolfStateFormat.Vec(b, game.clubs[i].localScale); b.Append("}}");
            }
            b.Append("]}");
            socket.Send(b.ToString());
        }
        void OnDestroy() => socket?.Dispose();
    }

}
