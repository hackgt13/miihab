using System;
using System.Collections;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

using Kinesthetic.Activities;

namespace Kinesthetic.Rehab
{
    // Patient-facing seated shoulder raise. The coordinator's measurement engine is the only source of
    // angles, rep counts and validity; this view renders them and the avatar never feeds back into them.
    public sealed class RehabSession : MonoBehaviour, IActivity
    {
        public PoseRig rig;
        [Tooltip("Drive the Mii from camera pose (MediaPipe). Off: measurement is IMU-only and the Mii's arm follows the AirPod angle.")]
        public bool useCameraPose;
        public Transform targetOrb, liveMarker;
        public LineRenderer targetBand, armGuide;
        public string exerciseUrl = "ws://127.0.0.1:8766/exercise?role=viewer";
        public string bridge = "http://127.0.0.1:8766";
        [Header("Clinician-approved plan (demo fixture)")]
        public string side = "right";
        public float targetDeg = 80, bandDeg = 20;
        public int prescribedReps = 8, planVersion = 1;

        LivePoseClient pose => SensorHub.Instance?.Pose;
        ExerciseClient exercise;
        long poseTicks, poseSequence = -1; string poseSession;
        bool running, calibrated;
        public bool IsRunning => running;
        /// The angle the Mii's arm is showing right now, or null when nothing live is being measured.
        public float? ShownAngle => !useCameraPose && running && Fresh && liveAngle.HasValue ? shownAngle : null;
        public string ExerciseKind => exerciseKind;
        bool startingSession, stoppingSession, sessionError, summaryReceived;
        public bool IsBusy => startingSession || stoppingSession;
        // IActivity. The shell drives this without knowing it is a therapy session.
        public string ActivityId => "rehab.studio";
        public event Action<string> Completed;
        /// <summary>Leaving must fail if /exercise/stop does not answer, or the set is lost.</summary>
        public IEnumerator RequestExit(Action<bool> succeeded) => FinishSession(succeeded);
        int attempted, valid;
        float? liveAngle; string phase = "idle";
        float lastSampleAt = -99, shownAngle; string exerciseKind = "arm-elevation.v1";
        // Whether the measurement stream is live: camera frames, or AirPod-driven samples from the coordinator.
        bool Fresh => useCameraPose ? LivePoseClient.Fresh(poseTicks) : Time.unscaledTime - lastSampleAt < .5f;
        string status = "Rest your arms. Press Start session.";
        float flashUntil; Color flash;
        Label title, reps, angle, statusLabel, planLabel, summaryLabel; Button start; VisualElement summaryCard;
        Label sideLabel, repGoal, targetLabel, progressNote, angleNote, cameraStatus, cueTitle, cueSymbol;
        VisualElement repRing, cameraChip, cueIcon;
        ProgressBar angleMeter;
        int paintedReps = -1, paintedGoal = -1;
        string coachingNote = "";
        // The band around the measured arm: cerulean at rest, sand while the rep is being made,
        // green once the target is reached, and coral only when something is wrong and has to be seen.
        static readonly Color Idle = Palette.Cerulean20.At(.55f), Active = Palette.Sand30.At(.9f),
            Good = Palette.Good.At(.95f), Bad = Palette.Attention.At(.95f);

        void Start()
        {
            Application.runInBackground = true;
            rig.Initialize(); rig.Apply(null);
            SensorHub.Ensure();
            exercise = new ExerciseClient(exerciseUrl);
            // The mirror window is added here, so the generated scene needs no change.
            if (!useCameraPose && !GetComponent<MirrorPanel>()) gameObject.AddComponent<MirrorPanel>().session = this;
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
            root.Q<Button>("summary-menu").clicked += () => Kinesthetic.Menu.ActivityNavigation.Ensure().OpenReturn();
            start.clicked += () => { if (IsBusy) return; if (running) StartCoroutine(Stop()); else StartCoroutine(Begin()); };
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
            startingSession = true; sessionError = false; summaryReceived = false;
            start.SetEnabled(false); summaryCard.AddToClassList("hidden");
            status = useCameraPose ? "Starting camera…" : "Connecting to your AirPod…";
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var script = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../scripts/start_camera_session.sh"));
            if (useCameraPose && File.Exists(script))
            {
                System.Diagnostics.Process process = null;
                try { process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "/bin/zsh", Arguments = "\"" + script + "\"", UseShellExecute = false, CreateNoWindow = true }); }
                catch (Exception) { sessionError = true; }
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
            startingSession = false;
            start.SetEnabled(true);
            if (request.result != UnityWebRequest.Result.Success) { sessionError = true; status = useCameraPose ? "Check that camera capture is open, then press Start to try again." : "Check the measurement service is running, then press Start to try again."; yield break; }
            sessionError = false;
            running = true; calibrated = false; attempted = valid = 0; liveAngle = null;
            status = useCameraPose ? "Hold still with your arms relaxed · calibrating" : "Hold the handle still, arm resting · calibrating";
            start.text = "Finish set";
        }

        void ApplyPlan(JObject plan)
        {
            // The plan's derived v1 view is the first measured prescription: the shoulder raise this scene runs.
            var e = plan["exercise"] as JObject; if (e == null) return;
            planVersion = plan["version"]?.Value<int>() ?? planVersion;
            side = (string)e["side"] ?? side;
            targetDeg = e["targetDeg"]?.Value<float>() ?? targetDeg;
            var ceiling = e["targetMaxDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? e["targetMaxDeg"].Value<float>() : (float?)null;
            if (ceiling > targetDeg) bandDeg = ceiling.Value - targetDeg;   // the drawn band is the plan's safe band
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

        IEnumerator Stop() { yield return FinishSession(); }

        public IEnumerator FinishSession(Action<bool> completed = null)
        {
            if (stoppingSession) { while (stoppingSession) yield return null; completed?.Invoke(!running); yield break; }
            stoppingSession = true; sessionError = false; start.SetEnabled(false);
            status = "Finishing your session…";
            using var request = new UnityWebRequest(bridge + "/exercise/stop", "POST") { downloadHandler = new DownloadHandlerBuffer(), timeout = 5 };
            yield return request.SendWebRequest();
            stoppingSession = false; start.SetEnabled(true);
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (summaryReceived) { completed?.Invoke(true); yield break; }
                sessionError = true;
                status = "Couldn't finish the session · check the measurement service and retry";
                completed?.Invoke(false);
                yield break;
            }
            // The HTTP response also carries the summary if the exercise stream was interrupted.
            JObject summary = null;
            try { summary = JObject.Parse(request.downloadHandler.text); } catch (Exception) { }
            if (summary?["attempted"] != null) ShowSummary(summary);
            running = false; start.text = "Start session  ›";
            completed?.Invoke(true);
        }

        void Update()
        {
            ReadPose();
            if (!BindUI()) return;
            ReadExercise();
            DrawGuides();
            reps.text = valid.ToString();
            angle.text = running && Fresh && liveAngle.HasValue ? $"{liveAngle.Value:0}°" : "—";
            statusLabel.text = status;
            UpdateStudioUI();
        }

        void UpdateStudioUI()
        {
            bool fresh = Fresh;
            cameraChip.EnableInClassList("connected", fresh);
            cameraStatus.text = useCameraPose ? (fresh ? "Camera connected" : running ? "Looking for you…" : "Camera on standby")
                : fresh ? "AirPod connected" : running ? "Waiting for your AirPod…" : "AirPod on standby";
            angleMeter.value = fresh && liveAngle.HasValue && running ? Mathf.Clamp01(liveAngle.Value / Mathf.Max(1, targetDeg)) * 100 : 0;
            angleNote.text = !running ? "Your range appears when you begin" : !fresh || !liveAngle.HasValue ? (useCameraPose ? "Waiting for a clear view of your arm" : "Waiting for the AirPod on your handle") : "Measured from your live movement";
            progressNote.text = valid >= prescribedReps ? "Your set is complete" : valid == 0 ? "Take your time." : $"{prescribedReps - valid} more · take your time";
            bool attention = sessionError || running && !fresh;
            bool reached = running && fresh && liveAngle.HasValue && liveAngle.Value >= targetDeg;
            cueIcon.EnableInClassList("attention", attention);
            cueIcon.EnableInClassList("good", reached || valid >= prescribedReps);
            cueTitle.text = stoppingSession ? "Saving your session" : startingSession ? "Getting ready…" : sessionError ? (running ? "Let's finish saving your session" : "Let's get you connected") : !running ? (valid >= prescribedReps ? "Set complete." : "Sit comfortably.") : attention ? (useCameraPose ? "Let's get you in view" : "Let's find your AirPod") : !calibrated ? "Rest your arm." : reached ? "Hold gently, then lower" : "Raise, hold, lower.";
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
                painter.strokeColor = Palette.Slate30;
                float begin = -90 + i * step + gap * .5f, end = -90 + (i + 1) * step - gap * .5f;
                painter.BeginPath(); painter.Arc(rect.center, rect.width * .43f, Angle.Degrees(begin), Angle.Degrees(end)); painter.Stroke();
                if (filled <= i) continue;
                painter.strokeColor = Palette.Good;
                painter.BeginPath(); painter.Arc(rect.center, rect.width * .43f, Angle.Degrees(begin), Angle.Degrees(Mathf.Lerp(begin, end, Mathf.Clamp01(filled - i)))); painter.Stroke();
            }
        }

        void ReadPose()
        {
            if (!useCameraPose) { DriveFromImu(); return; }
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
        }

        // IMU-only: the Mii rests, and during a session the measured arm follows the AirPod angle (smoothed for
        // display; the scored value is the coordinator's).
        void DriveFromImu()
        {
            rig.Apply(null);
            if (!running || !Fresh || !liveAngle.HasValue) return;
            shownAngle = Mathf.Lerp(shownAngle, liveAngle.Value, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
            bool curl = exerciseKind == "elbow-flexion.v1";
            rig.ApplyImuArm(side == "left", curl ? null : shownAngle, curl ? shownAngle : null);
        }

        void ReadExercise()
        {
            while (exercise != null && exercise.Take(out var text))
            {
                JObject m; try { m = JObject.Parse(text); } catch (Exception) { continue; }
                var p = m["payload"] as JObject; if (p == null) continue;
                switch ((string)m["type"])
                {
                    case "exercise.started": exerciseKind = (string)p["exerciseKind"] ?? exerciseKind; shownAngle = 0; break;
                    case "exercise.sample":
                        phase = (string)p["phase"] ?? phase;
                        liveAngle = p["valid"]?.Value<bool>() == true && p["angleDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? p["angleDeg"].Value<float>() : null;
                        if (liveAngle.HasValue) lastSampleAt = Time.unscaledTime;
                        break;
                    case "exercise.event": OnEvent(p); break;
                    case "exercise.summary": ShowSummary(p); break;
                    case "exercise.progression": ShowProgression(p); break;
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
                case "tracking.lost": status = useCameraPose ? "Move back into view — shoulders, elbows and hips" : "Lost your AirPod for a moment — keep it on the handle"; Flash(Bad); break;
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
            if (summaryReceived) return;
            summaryReceived = true; sessionError = false;
            // The server decided this set was over and recorded it; tell whoever is driving us.
            Completed?.Invoke((string)s["exerciseId"] ?? "");
            running = false; start.text = "Start session  ›";
            valid = s["valid"]?.Value<int>() ?? 0; attempted = s["attempted"]?.Value<int>() ?? 0;
            var median = s["medianValidPeakDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? $"{s["medianValidPeakDeg"].Value<float>():0}°" : "—";
            var reasons = s["invalidReasons"] as JObject; var notes = new StringBuilder();
            if (reasons != null) foreach (var r in reasons)
            {
                string reason = r.Key switch {
                    "did_not_reach_target" => "Below the target range", "trunk_compensation" => "Chest moved from resting position",
                    "tracking_lost" => useCameraPose ? "Camera view interrupted" : "AirPod signal interrupted", "too_fast" => "Movement was too quick",
                    _ => r.Key.Replace('_', ' ') };
                notes.Append($"{r.Value} · {reason}\n");
            }
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Q<Label>("summary-title").text = attempted == 0 ? "Ready for another day" : valid >= prescribedReps ? "Your set is complete" : "Practice, at your pace";
            root.Q<Label>("summary-subtitle").text = attempted == 0 ? "No repetitions were recorded this time." : "Your session summary.";
            root.Q<Label>("summary-valid").text = valid.ToString();
            root.Q<Label>("summary-attempted").text = attempted.ToString();
            root.Q<Label>("summary-peak").text = median;
            root.Q<Label>("summary-plan").text = $"{side.ToUpperInvariant()} ARM  ·  TARGET {targetDeg:0}°  ·  {prescribedReps} REPS";
            root.Q<Label>("summary-progress")?.AddToClassList("hidden");   // filled by the progression verdict that follows
            summaryLabel.text = notes.Length > 0 ? notes.ToString().TrimEnd() : attempted == 0 ? "Return to the studio when you're ready to begin." : "Nice work.";
            root.Q<Label>("summary-saved").text = s["simulated"]?.Value<bool>() == true ? "Demo session · simulated movement" : "Session saved · available to your care team";
            summaryCard.RemoveFromClassList("hidden");
            root.Q<Button>("summary-close").Focus();
            Kinesthetic.Menu.ActivityNavigation.Ensure().PlaySelect();
            status = "Session saved";
        }

        // The progression rules' verdict on this session (coordinator/progression.ts), in the patient's words.
        // Arrives just after the summary; a level change inside the clinician's envelope has already applied.
        void ShowProgression(JObject p)
        {
            var label = GetComponent<UIDocument>().rootVisualElement.Q<Label>("summary-progress");
            if (label == null) return;
            string decision = (string)p["decision"];
            bool applied = (string)p["status"] == "applied";
            var to = p["to"]?["targetDeg"]?.Value<float>();
            int good = p["goodSessions"]?["count"]?.Value<int>() ?? 0, needed = p["goodSessions"]?["needed"]?.Value<int>() ?? 0;
            var (text, tone) = decision switch
            {
                "progress" when applied => ($"Level up! Next session your target is {to:0}°", "up"),
                "progress" => ($"Ready for {to:0}° · your care team will confirm the next level", "up"),
                "regress" when applied => ($"Next session eases back to {to:0}° to let your arm settle", "easy"),
                "regress" => ("Your care team will look at easing the target a little", "easy"),
                "clinician_review" => ("Great progress · your care team will set your next goal", "up"),
                _ when needed > 0 && good > 0 => ($"{good} of {needed} good sessions toward the next level", ""),
                _ when needed > 0 => ($"{needed} good sessions in a row unlock the next level", ""),
                _ => ("", ""),
            };
            label.text = text;
            label.EnableInClassList("hidden", text.Length == 0);
            label.EnableInClassList("up", tone == "up");
            label.EnableInClassList("easy", tone == "easy");
            if (tone == "up" && applied) { Flash(Good); Kinesthetic.Menu.ActivityNavigation.Ensure().PlaySelect(); }
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
            // With the IMU, guides follow the same plane the arm is drawn in (PoseRig.ImuArmDirection).
            Vector3 At(float deg, float r) { float a = deg * Mathf.Deg2Rad;
                return shoulder + (useCameraPose ? down * Mathf.Cos(a) + across * Mathf.Sin(a) : rig.ImuArmDirection(left, deg)) * r; }

            const int n = 24; targetBand.positionCount = n;
            for (int i = 0; i < n; i++) targetBand.SetPosition(i, At(targetDeg + bandDeg * i / (n - 1), reach));
            armGuide.positionCount = 2; armGuide.SetPosition(0, shoulder);
            armGuide.SetPosition(1, At(liveAngle ?? 0, reach * .95f));
            bool showMeasured = liveAngle.HasValue && running && Fresh;
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

        void OnDestroy() { exercise?.Dispose(); }   // the hub owns the pose channel
    }
}
