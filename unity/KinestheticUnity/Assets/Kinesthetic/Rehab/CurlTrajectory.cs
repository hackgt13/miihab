using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// <summary>
    /// The biceps curl's path, drawn in the studio beside the patient's working arm while they curl: the ideal arc
    /// (upper arm still, wrist on a circle about the elbow), the wrist's trail for the rep being made, the last few
    /// reps fading behind it in the colour of how well they went, the arm as it is right now, and the scores — the
    /// rep's and the set's.
    ///
    /// It draws what the coordinator computes (coordinator/exercise/curl-trace.ts, `exercise.trajectory` on
    /// /exercise), which rebuilds the curl from the two AirPods: each gives a yaw-free tilt, a model arm places the
    /// elbow and wrist. Nothing here measures or decides anything, and nothing here changes a count.
    ///
    /// On the Mac it reads /exercise itself; on a headset RehabStateClient hands it the same payload out of the
    /// state the Mac publishes (Apply). RehabSession adds it; it stays hidden until a curl set sends a path.
    ///
    /// Colours carry the palette's meaning: cerulean is the measured path now, jungle a rep that went well, coral one
    /// that needs work, sand the ideal and the arm.
    /// </summary>
    public sealed class CurlTrajectory : MonoBehaviour
    {
        public IRehabView view;
        [Tooltip("How much bigger than a real arm the model is drawn, so it reads from the seat.")]
        public float scale = 1.3f;
        [Tooltip("Where the model's shoulder floats, from the patient's hips: to their working side, forward and up (metres).")]
        public float offsetSide = .72f, offsetForward = .55f, offsetUp = .95f;

        const string Url = "ws://127.0.0.1:8766/exercise?role=viewer";
        const int RecentReps = 3;

        ExerciseClient client;
        Transform root, model;
        LineRenderer ideal, live, upperArm, forearm;
        readonly List<LineRenderer> recent = new();
        Transform shoulderJoint, elbowJoint, wristJoint;
        TextMesh title, repLine, detailLine, setLine;
        float swivel;

        /// The last payload drawn, for the publisher to carry to a headset. Null when there is nothing to show.
        public JObject Last { get; private set; }
        /// True on a headset: the payload is handed over, never read.
        public bool Remote;

        void Start()
        {
            if (view?.Rig == null) { enabled = false; return; }
            view.Rig.Initialize();
            Build();
            if (!Remote) client = new ExerciseClient(Url);
            root.gameObject.SetActive(false);
        }

        void OnDestroy() { client?.Dispose(); if (root) Destroy(root.gameObject); }

        void Update()
        {
            while (client != null && client.Take(out var text))
            {
                JObject m; try { m = JObject.Parse(text); } catch { continue; }
                switch ((string)m["type"])
                {
                    // A new set clears the last one's path; a set of anything but a curl sends none and stays hidden.
                    case "exercise.started": Apply(null); break;
                    case "exercise.trajectory": Apply(m["payload"] as JObject); break;
                }
            }
            if (!root || !root.gameObject.activeSelf) return;
            Place();
        }

        /// Draw this payload (coordinator/exercise/curl-trace.ts `TracePayload`), or hide for null.
        public void Apply(JObject p)
        {
            Last = p;
            if (!root) return;
            root.gameObject.SetActive(p != null);
            if (p == null) return;

            Line(ideal, Points(p["ideal"]));
            Line(live, Points(p["live"]));

            var reps = p["recent"] as JArray ?? new JArray();
            for (int i = 0; i < recent.Count; i++)
            {
                // Newest last; the older a rep, the fainter.
                int k = reps.Count - recent.Count + i;
                var line = recent[i];
                if (k < 0 || !(reps[k] is JObject r)) { line.enabled = false; continue; }
                float fade = .35f + .55f * (i + 1f) / recent.Count;
                var c = Verdict((float?)r["score"] ?? 0).At(fade);
                line.startColor = line.endColor = c;
                Line(line, Points(r["path"]));
            }

            if (p["arm"] is JObject arm)
            {
                var e = V(arm["elbow"]); var w = V(arm["wrist"]);
                Line(upperArm, new List<Vector3> { Vector3.zero, e });
                Line(forearm, new List<Vector3> { e, w });
                elbowJoint.localPosition = e; wristJoint.localPosition = w;
                upperArm.enabled = forearm.enabled = true;
                elbowJoint.gameObject.SetActive(true); wristJoint.gameObject.SetActive(true);
            }

            var all = p["reps"] as JArray;
            var lastRep = all != null && all.Count > 0 ? all[all.Count - 1] as JObject : null;
            if (lastRep != null)
            {
                float score = (float?)lastRep["score"] ?? 0;
                repLine.text = $"Rep {(int?)lastRep["rep"]}  ·  {score:0}  {Word(score)}";
                repLine.color = Verdict(score);
                detailLine.text = $"Path {(float?)lastRep["pathAccuracy"]:0}%   Smooth {(float?)lastRep["smoothness"]:0}%   Elbow drift {(float?)lastRep["elbowDriftDeg"]:0}°";
            }
            else { repLine.text = "Curl when you're ready"; repLine.color = Palette.Sand00; detailLine.text = "Follow the dotted arc"; }

            if (p["set"] is JObject set)
                setLine.text = $"Set {(float?)set["score"]:0}  ·  {(int?)set["reps"]} reps  ·  path {(float?)set["pathAccuracy"]:0}%";
            else setLine.text = "";
        }

        static string Word(float score) => score >= 85 ? "Strong" : score >= 70 ? "Fair" : "Needs work";
        static Color Verdict(float score) => score >= 85 ? Palette.Good : score >= 70 ? Palette.Cerulean40 : Palette.Attention;

        // ---- building ------------------------------------------------------------------------------------------

        void Build()
        {
            root = new GameObject("Curl trajectory").transform;
            model = new GameObject("Model arm space").transform;
            model.SetParent(root, false);
            model.localScale = Vector3.one * scale;

            // A dark glass plate behind the drawing, so the lines read against any wall.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Plate"; Destroy(plate.GetComponent<Collider>());
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(.2f * scale, -.245f * scale, .02f);
            plate.transform.localScale = new Vector3(.74f * scale, 1.04f * scale, 1);
            plate.GetComponent<Renderer>().sharedMaterial = Unlit(Palette.PanelDark.At(.55f));
            plate.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // A faint grid in the curl's plane, every 10 cm of arm, so the space has a scale.
            for (float x = -.1f; x <= .5001f; x += .1f) Grid(new Vector3(x, -.62f, 0), new Vector3(x, .05f, 0));
            for (float y = -.6f; y <= .0501f; y += .1f) Grid(new Vector3(-.1f, y, 0), new Vector3(.5f, y, 0));

            ideal = MakeLine("Ideal arc", .012f, Palette.Sand00.At(.55f));
            for (int i = 0; i < RecentReps; i++) recent.Add(MakeLine($"Rep trail {i + 1}", .014f, Palette.Cerulean40));
            live = MakeLine("Wrist now", .022f, Palette.Cerulean40);
            upperArm = MakeLine("Upper arm", .03f, Palette.Sand00);
            forearm = MakeLine("Forearm", .03f, Palette.Sand00);
            shoulderJoint = Joint("Shoulder"); elbowJoint = Joint("Elbow"); wristJoint = Joint("Wrist");
            elbowJoint.gameObject.SetActive(false); wristJoint.gameObject.SetActive(false);
            upperArm.enabled = forearm.enabled = false;

            title = Text("Title", "YOUR CURL PATH", .007f, Palette.Sand30, new Vector3(-.13f, .22f, 0), 48);
            repLine = Text("Rep", "", .011f, Palette.Sand00, new Vector3(-.13f, .15f, 0), 64);
            detailLine = Text("Detail", "", .0065f, Palette.InkOnDark, new Vector3(-.13f, .1f, 0), 48);
            setLine = Text("Set", "", .0065f, Palette.Sand30, new Vector3(-.13f, -.73f, 0), 48);
        }

        void Grid(Vector3 a, Vector3 b)
        {
            var g = MakeLine("Grid", .003f, Palette.Sand00.At(.12f));
            Line(g, new List<Vector3> { a, b });
        }

        LineRenderer MakeLine(string name, float width, Color color)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(model, false);
            line.useWorldSpace = false; line.widthMultiplier = width * scale; line.numCapVertices = 4; line.numCornerVertices = 2;
            // Sprites/Default honours vertex colour alpha, as the mirror's ghosts do.
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.positionCount = 0;
            return line;
        }

        Transform Joint(string name)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = name;
            Destroy(orb.GetComponent<Collider>());
            orb.transform.SetParent(model, false); orb.transform.localScale = Vector3.one * .035f;
            orb.GetComponent<Renderer>().sharedMaterial = Unlit(Palette.Sand00);
            orb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return orb.transform;
        }

        TextMesh Text(string name, string content, float size, Color color, Vector3 at, int fontSize)
        {
            var text = new GameObject(name).AddComponent<TextMesh>();
            text.transform.SetParent(model, false);
            text.transform.localPosition = at;
            text.transform.localScale = Vector3.one * size;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = fontSize; text.anchor = TextAnchor.LowerLeft; text.alignment = TextAlignment.Left;
            text.text = content; text.color = color;
            return text;
        }

        static Material Unlit(Color c) { var m = new Material(Shader.Find("Sprites/Default")); m.color = c; return m; }

        static void Line(LineRenderer line, List<Vector3> points)
        {
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            line.enabled = points.Count > 1;
        }

        static List<Vector3> Points(JToken token)
        {
            var list = new List<Vector3>();
            if (token is JArray a) foreach (var p in a) list.Add(V(p));
            return list;
        }

        /// The coordinator's body frame — x forward, y up, z sideways — laid into this plate: x across, y up, z into it.
        static Vector3 V(JToken p) => p is JArray a && a.Count == 3 ? new Vector3((float)a[0], (float)a[1], -(float)a[2]) : Vector3.zero;

        /// Beside the working arm, facing the patient's eyes, turning slowly back and forth so the path's depth reads.
        void Place()
        {
            var rig = view.Rig;
            var hip = rig.Hip.position;
            var left = rig.transform.TransformDirection(Vector3.right);      // the Mii's anatomical left is local +X
            var forward = rig.transform.TransformDirection(Vector3.back);    // it faces local -Z
            var side = view.Side == "left" ? left : -left;
            var at = hip + side * offsetSide + forward * offsetForward + Vector3.up * offsetUp;
            var eye = hip + Vector3.up * .8f;
            var toEye = eye - at; toEye.y = 0;
            swivel += Time.deltaTime;
            var facing = Quaternion.LookRotation(-toEye.normalized, Vector3.up) * Quaternion.Euler(0, 18 * Mathf.Sin(swivel * .5f), 0);
            root.SetPositionAndRotation(at, facing);
        }
    }
}
