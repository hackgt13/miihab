using System;
using System.Collections;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Kinesthetic.Rehab
{
    // Patient-facing seated shoulder raise. The coordinator's measurement engine is the only source of
    // angles, rep counts and validity; this view renders them and the avatar never feeds back into them.
    public sealed class RehabSession : MonoBehaviour
    {
        public PoseRig rig;
        public Transform targetOrb, liveMarker;
        public LineRenderer targetBand, armGuide;
        public string poseUrl = "ws://127.0.0.1:8766/pose?role=viewer";
        public string exerciseUrl = "ws://127.0.0.1:8766/exercise?role=viewer";
        public string bridge = "http://127.0.0.1:8766";
        [Header("Clinician-approved plan (demo fixture)")]
        public string side = "right";
        public float targetDeg = 80, bandDeg = 20;
        public int prescribedReps = 8, planVersion = 1;

        LivePoseClient pose; ExerciseClient exercise;
        long poseTicks, poseSequence = -1; string poseSession;
        float retryPoseAt;
        bool running, calibrated;
        int attempted, valid;
        float? liveAngle; string phase = "idle";
        string status = "Sit tall, rest your arms, and start when you're ready.";
        float flashUntil; Color flash;
        Label title, reps, angle, statusLabel, planLabel, summaryLabel; Button start; VisualElement summaryCard;
        Label sideLabel, repGoal, targetLabel, progressNote, angleNote, cameraStatus, cueTitle, cueSymbol;
        VisualElement repRing, cameraChip, cueIcon;
        ProgressBar angleMeter;
        int paintedReps = -1, paintedGoal = -1;
        string coachingNote = "";
        static readonly Color Idle = new(.85f, .9f, .95f, .55f), Active = new(1f, .86f, .3f, .9f),
            Good = new(.35f, .95f, .5f, .95f), Bad = new(1f, .42f, .35f, .95f);

        void Start()
        {
            Application.runInBackground = true;
            rig.Initialize(); rig.Apply(null);
            pose = new LivePoseClient(poseUrl);
            exercise = new ExerciseClient(exerciseUrl);
            BindUI();
        }

        // UIDocument can build its tree after this component's Start, so bind whenever the tree appears
        // (and again if it is rebuilt) instead of assuming it exists at startup.
        bool BindUI()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var button = root?.Q<Button>("start");
            if (button == null) return false;
            if (button == start) return true;
            title = root.Q<Label>("title"); reps = root.Q<Label>("reps"); angle = root.Q<Label>("angle");
            statusLabel = root.Q<Label>("status"); planLabel = root.Q<Label>("plan"); summaryLabel = root.Q<Label>("summary");
            summaryCard = root.Q("summary-card"); start = button;
            sideLabel = root.Q<Label>("side-label"); repGoal = root.Q<Label>("rep-goal");
            targetLabel = root.Q<Label>("target-label"); progressNote = root.Q<Label>("progress-note");
            angleNote = root.Q<Label>("angle-note"); cameraStatus = root.Q<Label>("camera-status");
            cueTitle = root.Q<Label>("cue-title"); cueSymbol = root.Q<Label>("cue-symbol");
            repRing = root.Q("rep-ring"); cameraChip = root.Q("camera-chip"); cueIcon = root.Q("cue-icon");
            angleMeter = root.Q<ProgressBar>("angle-meter");
            repRing.generateVisualContent += DrawRepRing;
            paintedReps = paintedGoal = -1;
            root.Q<Button>("summary-close").clicked += () => summaryCard.AddToClassList("hidden");
            start.clicked += () => { if (running) StartCoroutine(Stop()); else StartCoroutine(Begin()); };
            start.text = running ? "Finish set" : "Start session  ›";
            UpdatePlanLabels();
            summaryCard.AddToClassList("hidden");
            StartCoroutine(RefreshPlan());
            return true;
        }

        IEnumerator RefreshPlan()
        {
            using var planRequest = UnityWebRequest.Get(bridge + "/api/plans/active");
            planRequest.timeout = 5;
            yield return planRequest.SendWebRequest();
            if (planRequest.result == UnityWebRequest.Result.Success) ApplyPlan(JObject.Parse(planRequest.downloadHandler.text));
        }

        IEnumerator Begin()
        {
            start.SetEnabled(false); summaryCard.AddToClassList("hidden");
            status = "Starting camera…";
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var script = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../scripts/start_camera_session.sh"));
            if (File.Exists(script))
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "/bin/zsh", Arguments = "\"" + script + "\"", UseShellExecute = false, CreateNoWindow = true });
                while (process != null && !process.HasExited) yield return null;
                process?.Dispose();
            }
#endif
            // The physician's active plan decides the target; this session pins that version.
            yield return RefreshPlan();
            var body = new JObject { ["planVersion"] = planVersion }.ToString();
            using var request = new UnityWebRequest(bridge + "/exercise/start", "POST") {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)), downloadHandler = new DownloadHandlerBuffer(), timeout = 5 };
            yield return request.SendWebRequest();
            start.SetEnabled(true);
            if (request.result != UnityWebRequest.Result.Success) { status = "Couldn't reach the measurement service · press Start to retry"; yield break; }
            running = true; calibrated = false; attempted = valid = 0; liveAngle = null;
            status = "Hold still with your arms relaxed · calibrating";
            start.text = "Finish set";
        }

        void ApplyPlan(JObject plan)
        {
            var e = plan["exercise"] as JObject; if (e == null) return;
            planVersion = plan["version"]?.Value<int>() ?? planVersion;
            side = (string)e["side"] ?? side;
            targetDeg = e["targetDeg"]?.Value<float>() ?? targetDeg;
            prescribedReps = e["prescribedReps"]?.Value<int>() ?? prescribedReps;
            coachingNote = (string)plan["coachingNote"] ?? "";
            UpdatePlanLabels();
        }

        void UpdatePlanLabels()
        {
            title.text = "Shoulder raises";
            sideLabel.text = $"{side.ToUpperInvariant()} ARM";
            planLabel.text = $"{prescribedReps} repetitions · reach {targetDeg:0}°" +
                (string.IsNullOrWhiteSpace(coachingNote) ? "" : $"\n{coachingNote}");
            planLabel.tooltip = $"Prescribed plan v{planVersion}";
            repGoal.text = $"of {prescribedReps} repetitions";
            targetLabel.text = $"Target {targetDeg:0}°";
        }

        IEnumerator Stop()
        {
            using var request = new UnityWebRequest(bridge + "/exercise/stop", "POST") { downloadHandler = new DownloadHandlerBuffer(), timeout = 5 };
            yield return request.SendWebRequest();
            // The summary arrives on the exercise stream; this only ends the session.
            running = false; start.text = "Start session  ›";
        }

        void Update()
        {
            ReadPose();
            if (!BindUI()) return;
            ReadExercise();
            DrawGuides();
            reps.text = valid.ToString();
            angle.text = running && LivePoseClient.Fresh(poseTicks) && liveAngle.HasValue ? $"{liveAngle.Value:0}°" : "—";
            statusLabel.text = status;
            UpdateStudioUI();
        }

        void UpdateStudioUI()
        {
            bool fresh = LivePoseClient.Fresh(poseTicks);
            cameraChip.EnableInClassList("connected", fresh);
            cameraStatus.text = fresh ? "Camera connected" : running ? "Looking for you…" : "Camera on standby";
            angleMeter.value = fresh && liveAngle.HasValue && running ? Mathf.Clamp01(liveAngle.Value / Mathf.Max(1, targetDeg)) * 100 : 0;
            angleNote.text = !running ? "Your range appears when you begin" : !fresh || !liveAngle.HasValue ? "Waiting for a clear view of your arm" : "Measured from your live movement";
            progressNote.text = valid >= prescribedReps ? "Your set is complete" : valid == 0 ? "One good movement at a time" : $"{prescribedReps - valid} more · take your time";
            bool attention = running && !fresh;
            bool reached = running && fresh && liveAngle.HasValue && liveAngle.Value >= targetDeg;
            cueIcon.EnableInClassList("attention", attention);
            cueIcon.EnableInClassList("good", reached || valid >= prescribedReps);
            cueTitle.text = !start.enabledSelf ? "Getting the studio ready" : !running ? (valid >= prescribedReps ? "A little stronger, one set at a time" : "Make yourself comfortable") : attention ? "Let's get you in view" : !calibrated ? "Find your resting position" : reached ? "Hold gently, then lower" : "Move at your own pace";
            cueSymbol.text = attention ? "!" : reached || valid >= prescribedReps ? "✓" : !running || !calibrated ? "1" : phase == "rep" ? "2" : "3";
            if (paintedReps != valid || paintedGoal != prescribedReps)
            {
                paintedReps = valid; paintedGoal = prescribedReps; repRing.MarkDirtyRepaint();
            }
        }

        // One arc per prescribed rep. The coordinator remains the source of every filled segment.
        void DrawRepRing(MeshGenerationContext context)
        {
            var rect = repRing.contentRect;
            if (rect.width <= 0 || rect.height <= 0) return;
            var painter = context.painter2D;
            painter.lineWidth = 7; painter.lineCap = LineCap.Round;
            int segments = Mathf.Clamp(prescribedReps, 1, 24);
            float step = 360f / segments, gap = segments == 1 ? 0 : 6;
            float filled = Mathf.Clamp01((float)valid / Mathf.Max(1, prescribedReps)) * segments;
            for (int i = 0; i < segments; i++)
            {
                painter.strokeColor = new Color(.87f, .93f, .90f);
                float begin = -90 + i * step + gap * .5f, end = -90 + (i + 1) * step - gap * .5f;
                painter.BeginPath(); painter.Arc(rect.center, rect.width * .43f, Angle.Degrees(begin), Angle.Degrees(end)); painter.Stroke();
                if (filled <= i) continue;
                painter.strokeColor = new Color(.22f, .68f, .62f);
                painter.BeginPath(); painter.Arc(rect.center, rect.width * .43f, Angle.Degrees(begin), Angle.Degrees(Mathf.Lerp(begin, end, Mathf.Clamp01(filled - i)))); painter.Stroke();
            }
        }

        void ReadPose()
        {
            if (pose != null && pose.Take(out var text, out var ticks))
            {
                try
                {
                    var packet = PoseJson.Read<PoseEnvelope>(text);
                    if (packet?.type == "pose.frame" && packet.schemaVersion == "kinesthetic.session.v1")
                    {
                        if (poseSession != packet.sessionId) { poseSession = packet.sessionId; poseSequence = -1; rig.ResetTracking(); }
                        if (packet.sequence > poseSequence) { poseSequence = packet.sequence; poseTicks = ticks; rig.Apply(packet.payload); }
                    }
                    else poseTicks = 0;
                }
                catch (Exception) { poseTicks = 0; }
            }
            if (!LivePoseClient.Fresh(poseTicks)) rig.Apply(null);
            if (pose?.Connected != true && Time.unscaledTime > retryPoseAt)
            { pose?.Dispose(); pose = new LivePoseClient(poseUrl); retryPoseAt = Time.unscaledTime + 3; }
        }

        void ReadExercise()
        {
            while (exercise != null && exercise.Take(out var text))
            {
                JObject m; try { m = JObject.Parse(text); } catch (Exception) { continue; }
                var p = m["payload"] as JObject; if (p == null) continue;
                switch ((string)m["type"])
                {
                    case "exercise.sample":
                        phase = (string)p["phase"] ?? phase;
                        liveAngle = p["valid"]?.Value<bool>() == true && p["angleDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? p["angleDeg"].Value<float>() : null;
                        break;
                    case "exercise.event": OnEvent(p); break;
                    case "exercise.summary": ShowSummary(p); break;
                }
            }
        }

        // Deterministic cue layer. The voice coach explains and converses on top of these; it never times them.
        void OnEvent(JObject e)
        {
            switch ((string)e["type"])
            {
                case "calibration.complete": calibrated = true; status = $"Raise your {side} arm to the glowing target"; break;
                case "rep.started": status = "Keep going — up to the target"; break;
                case "target.reached": status = "Hold it there… good"; Flash(Good); break;
                case "tracking.lost": status = "Move back into view — shoulders, elbows and hips"; Flash(Bad); break;
                case "tracking.recovered": if (calibrated) status = "Back in view · continue when ready"; break;
                case "rep.completed":
                    attempted++;
                    if (e["valid"]?.Value<bool>() == true) { valid++; status = valid >= prescribedReps ? "That's the set — great work" : $"Rep {valid} counted ✓  Lower slowly, then go again"; Flash(Good); }
                    else
                    {
                        status = (string)e["reason"] switch {
                            "did_not_reach_target" => "Almost — reach a little higher and hold",
                            "trunk_compensation" => "Keep your chest facing forward — that one didn't count",
                            "tracking_lost" => "I lost sight of your arm — that one didn't count",
                            "too_fast" => "Slow it down and control the movement",
                            _ => "That one didn't count" };
                        Flash(Bad);
                    }
                    if (valid >= prescribedReps && running) StartCoroutine(Stop());
                    break;
            }
        }

        void ShowSummary(JObject s)
        {
            running = false; start.text = "Start session  ›";
            var median = s["medianValidPeakDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? $"{s["medianValidPeakDeg"].Value<float>():0}°" : "—";
            var reasons = s["invalidReasons"] as JObject; var notes = new StringBuilder();
            if (reasons != null) foreach (var r in reasons) notes.Append($"\n{r.Value} × {r.Key.Replace('_', ' ')}");
            summaryLabel.text = $"{s["valid"]} of {s["attempted"]} reps counted · target {targetDeg:0}°\nMedian peak {median}{notes}\n\nSent to your care team as session evidence.";
            summaryCard.RemoveFromClassList("hidden");
            status = "Session saved";
        }

        void Flash(Color c) { flash = c; flashUntil = Time.unscaledTime + .6f; }

        // Guides live in the arm's arc: the band is the prescribed range, the marker is the measured angle.
        void DrawGuides()
        {
            if (!rig || !targetBand) return;
            targetBand.enabled = running;
            targetOrb.gameObject.SetActive(running);
            var left = side == "left";
            var shoulder = left ? rig.LeftUpperArm.position : rig.RightUpperArm.position;
            var across = (rig.RightUpperArm.position - rig.LeftUpperArm.position).normalized * (left ? -1 : 1);
            var down = (rig.Hip.position - (rig.RightUpperArm.position + rig.LeftUpperArm.position) * .5f).normalized;
            across = Vector3.ProjectOnPlane(across, down).normalized;
            float reach = Vector3.Distance(rig.RightUpperArm.position, rig.RightForearm.position) +
                          Vector3.Distance(rig.RightForearm.position, rig.RightHand.position);
            Vector3 At(float deg, float r) { float a = deg * Mathf.Deg2Rad; return shoulder + (down * Mathf.Cos(a) + across * Mathf.Sin(a)) * r; }

            const int n = 24; targetBand.positionCount = n;
            for (int i = 0; i < n; i++) targetBand.SetPosition(i, At(targetDeg + bandDeg * i / (n - 1), reach));
            armGuide.positionCount = 2; armGuide.SetPosition(0, shoulder);
            armGuide.SetPosition(1, At(liveAngle ?? 0, reach * .95f));
            bool showMeasured = liveAngle.HasValue && running && LivePoseClient.Fresh(poseTicks);
            armGuide.enabled = showMeasured;
            targetOrb.position = At(targetDeg + bandDeg * .5f, reach);
            targetOrb.localScale = Vector3.one * (.08f + .008f * Mathf.Sin(Time.unscaledTime * 4));
            liveMarker.gameObject.SetActive(showMeasured);
            if (liveAngle.HasValue) liveMarker.position = At(liveAngle.Value, reach);

            var color = Time.unscaledTime < flashUntil ? flash : !running ? Idle : phase == "rep" ? Active : Idle;
            if (showMeasured && liveAngle.Value >= targetDeg) color = Good;
            targetBand.startColor = targetBand.endColor = color;
            targetOrb.GetComponent<Renderer>().material.color = color;
        }

        void OnDestroy() { pose?.Dispose(); exercise?.Dispose(); }
    }
}
