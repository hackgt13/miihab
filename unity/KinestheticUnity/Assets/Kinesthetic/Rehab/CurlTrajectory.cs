using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// <summary>
    /// The biceps curl's path, standing where the mirror window stands for every other movement (ahead and to the
    /// left, a little nearer, turned 30° so the path's depth reads): the ideal arc (upper arm still, wrist on a circle about the
    /// elbow), the wrist's trail for the rep being made, the last few reps fading behind it in the colour of how well
    /// they went, the arm as it is right now, and how good the last rep was. It never counts reps: the studio's own
    /// counter is the one count.
    ///
    /// The coordinator sends it ten times a second; between those, the arm and the trail's tip glide toward the
    /// newest reading every frame, so it moves at the display's rate, not the network's.
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
        [Tooltip("How big the model arm is drawn against a real one: about the mirror window's size at its distance.")]
        public float scale = 1.25f;
        [Tooltip("Where the plate's centre stands, from the patient's hips: to their left and forward — a little nearer than the mirror (metres).")]
        public float offsetLeft = 1.3f, offsetForward = 1.8f;
        [Tooltip("Degrees the plate is turned from facing the patient, so the path's depth reads.")]
        public float turnDeg = 30;
        [Tooltip("How quickly the drawn arm catches up with the newest reading (per second).")]
        public float glide = 16;

        const string Url = "ws://127.0.0.1:8766/exercise?role=viewer";
        const int RecentReps = 3;

        ExerciseClient client;
        Transform root, model;
        LineRenderer ideal, live, upperArm, forearm;
        readonly List<LineRenderer> recent = new();
        Transform shoulderJoint, elbowJoint, wristJoint;
        TextMesh title, repLine, detailLine;
        string placedFor;   // the side it was placed for; it is placed once, not every frame
        // What the newest reading says, and what is drawn on the way to it.
        Vector3 elbowTarget, wristTarget, elbowShown, wristShown;
        bool armLive, armFresh;
        readonly List<Vector3> liveTarget = new();

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
            // Placed once, from the patient at rest, and again only if the working side changes: following the
            // hips every frame carried every breath and every rep's bounce of the avatar into the graph.
            if (root && root.gameObject.activeSelf && placedFor != view.Side) Place();
            if (root && root.gameObject.activeSelf && armLive) Glide();
        }

        /// Every frame: the arm eases toward the newest reading, and the trail's tip rides with the wrist.
        void Glide()
        {
            float k = armFresh ? 1 : 1 - Mathf.Exp(-glide * Time.deltaTime);
            armFresh = false;
            elbowShown = Vector3.Lerp(elbowShown, elbowTarget, k);
            wristShown = Vector3.Lerp(wristShown, wristTarget, k);
            Line(upperArm, new List<Vector3> { Vector3.zero, elbowShown });
            Line(forearm, new List<Vector3> { elbowShown, wristShown });
            elbowJoint.localPosition = elbowShown; wristJoint.localPosition = wristShown;
            if (liveTarget.Count > 0)
            {
                var trail = new List<Vector3>(liveTarget); trail[trail.Count - 1] = wristShown;
                Line(live, trail);
            }
        }

        /// Draw this payload (coordinator/exercise/curl-trace.ts `TracePayload`), or hide for null.
        public void Apply(JObject p)
        {
            Last = p;
            if (!root) return;
            root.gameObject.SetActive(p != null);
            if (p == null) return;

            // First the arm is measured (curl-trace.ts): one straight swing up to the ceiling and back down. Until
            // then the plate shows only that, with the whole swing as its guide.
            var calibration = p["calibration"] as JObject;
            string state = (string)calibration?["state"];
            if (state == "reach") { ShowReach(calibration, p["meta"] as JObject); return; }
            title.text = "YOUR CURL PATH";

            Line(ideal, Points(p["ideal"]));
            liveTarget.Clear(); liveTarget.AddRange(Points(p["live"]));
            if (liveTarget.Count < 2) live.enabled = false;

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
                elbowTarget = V(arm["elbow"]); wristTarget = V(arm["wrist"]);
                armFresh = !armLive;   // the first reading is drawn where it is, not glided in from nowhere
                armLive = true;
                elbowJoint.gameObject.SetActive(true); wristJoint.gameObject.SetActive(true);
            }

            var all = p["reps"] as JArray;
            var lastRep = all != null && all.Count > 0 ? all[all.Count - 1] as JObject : null;
            if (lastRep != null)
            {
                float score = (float?)lastRep["score"] ?? 0;
                repLine.text = $"Last rep: {score:0}%\n{Word(score)}";
                repLine.color = Verdict(score);
                detailLine.text = $"Path {(float?)lastRep["pathAccuracy"]:0}%   Smooth {(float?)lastRep["smoothness"]:0}%   Elbow drift {(float?)lastRep["elbowDriftDeg"]:0}°";
            }
            // Before the first rep: what the measuring swing found (or why an average arm stands in), then the curl.
            else
            {
                repLine.text = "Curl when you're ready"; repLine.color = Palette.Sand00;
                detailLine.text = Wrap((string)calibration?["instruction"] ?? "Follow the dotted arc", 52);
            }

        }

        /// The measuring swing: its instruction, and the arc a straight arm sweeps from hanging to overhead.
        void ShowReach(JObject calibration, JObject meta)
        {
            float reach = ((float?)meta?["upperArmM"] ?? .29f) + ((float?)meta?["forearmM"] ?? .26f);
            var arc = new List<Vector3>();
            for (int i = 0; i <= 36; i++) { float a = Mathf.PI * i / 36; arc.Add(new Vector3(Mathf.Sin(a) * reach, -Mathf.Cos(a) * reach, 0)); }
            Line(ideal, arc);
            live.enabled = upperArm.enabled = forearm.enabled = false;
            armLive = false; liveTarget.Clear();
            foreach (var line in recent) line.enabled = false;
            elbowJoint.gameObject.SetActive(false); wristJoint.gameObject.SetActive(false);
            title.text = "MEASURE YOUR ARM";
            repLine.text = "Arm straight · up and down"; repLine.color = Palette.Cerulean40;
            detailLine.text = Wrap((string)calibration?["instruction"] ?? "", 52);
        }

        /// TextMesh does not wrap: break at spaces so a line stays on the plate.
        static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width) return text ?? "";
            var sb = new System.Text.StringBuilder(); int line = 0;
            foreach (var word in text.Split(' '))
            {
                if (line > 0 && line + 1 + word.Length > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word); line += word.Length;
            }
            return sb.ToString();
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
            plate.transform.localPosition = new Vector3(.2f * scale, -.18f * scale, .02f);
            plate.transform.localScale = new Vector3(.74f * scale, .96f * scale, 1);
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

            title = Text("Title", "YOUR CURL PATH", .007f, Palette.Sand30, new Vector3(-.13f, .25f, 0), 48);
            repLine = Text("Rep", "", .011f, Palette.Sand00, new Vector3(-.13f, .1f, 0), 64);   // two lines, growing up
            detailLine = Text("Detail", "", .0065f, Palette.InkOnDark, new Vector3(-.13f, .06f, 0), 48);
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

        /// Where the mirror window stands (MirrorPanel: to the patient's left and forward, centred at its height),
        /// facing their eyes and then turned `turnDeg` so the path's depth reads — and still.
        void Place()
        {
            placedFor = view.Side;
            var rig = view.Rig;
            var hip = rig.Hip.position;
            var left = rig.transform.TransformDirection(Vector3.right);      // the Mii's anatomical left is local +X
            var forward = rig.transform.TransformDirection(Vector3.back);    // it faces local -Z
            var floor = (rig.transform.parent ? rig.transform.parent : rig.transform).position.y;
            var centre = hip + left * offsetLeft + forward * offsetForward;
            centre.y = floor + .77f;                                         // the mirror window's centre height
            var eye = hip + Vector3.up * .8f;
            var toEye = eye - centre; toEye.y = 0;
            var facing = Quaternion.LookRotation(-toEye.normalized, Vector3.up) * Quaternion.Euler(0, turnDeg, 0);
            // The plate's centre sits at (.2, -.18) of the model's shoulder, in model units.
            root.SetPositionAndRotation(centre - facing * new Vector3(.2f * scale, -.18f * scale, 0), facing);
        }
    }
}
