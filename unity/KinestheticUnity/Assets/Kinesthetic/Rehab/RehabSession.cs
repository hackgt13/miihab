using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

using Kinesthetic.Activities;
using Kinesthetic.UI;
using Kinesthetic.Golf;

namespace Kinesthetic.Rehab
{
    // Patient-facing seated shoulder raise. The coordinator's measurement engine is the only source of
    // angles, rep counts and validity; this view renders them and the avatar never feeds back into them.
    public sealed class RehabSession : MonoBehaviour, IActivity, IRehabView
    {
        public PoseRig rig;
        [Tooltip("Drive the Mii from camera pose (MediaPipe). Off: measurement is IMU-only and the Mii's arm follows the AirPod angle.")]
        public bool useCameraPose;
        public bool autoStartSession = true, startServices = true;
        public string motionUrl = SensorHub.DefaultMotionUrl;
        public string wristMotionUrl = "ws://127.0.0.1:8767/bowling-motion?role=viewer";
        bool wristMotion;
        string SensorPlacement => wristMotion ? "wrist" : "handle";
        bool autoArmed, servicesStarting;
        long motionTicks; float stillSince = -1, enteredAt;
        string motionSession; long motionSequence = -1;
        bool MotionFresh => LivePoseClient.Fresh(motionTicks);
        public bool ReadyToBegin => useCameraPose ? Fresh : MotionFresh && stillSince >= 0 && Time.unscaledTime - stillSince >= 1.5f;
        public bool Calibrated => calibrated;
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
        public int Valid => valid;
        /// The line the patient is reading right now (published to a headset with the rest of the state).
        public string Cue { get; private set; } = "";
        PoseRig IRehabView.Rig => rig;
        string IRehabView.Side => side;
        float IRehabView.TargetDeg => targetDeg;
        float IRehabView.BandDeg => bandDeg;
        bool IRehabView.CoachHandingOff => coach && coach.HandingOff;
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
        Kinesthetic.Coach.CoachDemonstrator coach;
        bool voiceOn; string currentExerciseId;
        VisualElement screen, hudCoach, angleDetails; Label hudCoachLine;
        /// Stable per machine, so Alex (the voice PT) remembers this patient between sessions.
        static string PatientId
        {
            get
            {
                var id = PlayerPrefs.GetString("RehabMii.PatientId", "");
                if (string.IsNullOrEmpty(id)) { id = Guid.NewGuid().ToString(); PlayerPrefs.SetString("RehabMii.PatientId", id); PlayerPrefs.Save(); }
                return id;
            }
        }
        // Whether the measurement stream is live: camera frames, or AirPod-driven samples from the coordinator.
        bool Fresh => useCameraPose ? LivePoseClient.Fresh(poseTicks) : MotionFresh && Time.unscaledTime - lastSampleAt < .5f;
        string status = "Secure your AirPod. Rest your arm.";
        float flashUntil; Color flash;
        Label title, statusLabel, planLabel, summaryLabel; Button start; VisualElement summaryCard;
        Label sideLabel, progressNote, angleNote, cueTitle;
        KReadout repCount, angleReadout;
        KChip sensorStatus; KTag cueStep;
        KArc repRing; KMeter angleMeter;
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
            if (!useCameraPose && !GetComponent<MirrorPanel>()) gameObject.AddComponent<MirrorPanel>().view = this;
            // Third person to set the scene, first person during the set (mirror left, coach right).
            if (!GetComponent<StudioCamera>()) gameObject.AddComponent<StudioCamera>().session = this;
            // A headset renders this studio from what it publishes (RehabStateClient in QuestRehab).
            if (!GetComponent<RehabStatePublisher>()) gameObject.AddComponent<RehabStatePublisher>().session = this;
            autoArmed = autoStartSession; enteredAt = Time.unscaledTime;
            if (startServices) StartCoroutine(ConnectServices());
            BindUI();
        }

        IEnumerator ConnectServices()
        {
            if (servicesStarting) yield break;
            servicesStarting = true;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var script = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../scripts/start_demo_services.sh"));
            if (startServices && File.Exists(script))
            {
                System.Diagnostics.Process process = null;
                try { process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "/bin/zsh", Arguments = "\"" + script + "\" club", UseShellExecute = false, CreateNoWindow = true }); }
                catch (Exception) { sessionError = true; }
                while (process != null && !process.HasExited) yield return null;
                process?.Dispose();
            }
#endif
            servicesStarting = false;
        }

        void ReadReadiness()
        {
            if (!useCameraPose)
            {
                var motion = SensorHub.Instance?.MotionFor(wristMotion ? wristMotionUrl : motionUrl);
                while (motion != null && motion.Take(out var text, out var ticks))
                {
                    ClubMotionPacket packet;
                    try { packet = JsonUtility.FromJson<ClubMotionPacket>(text); } catch (Exception) { continue; }
                    if (packet == null || packet.type != (wristMotion ? "bowling.motion" : "club.motion") || packet.playerId != "patient" || !LivePoseClient.Fresh(ticks)) continue;
                    if (packet.sessionId != motionSession) { motionSession = packet.sessionId; motionSequence = -1; stillSince = -1; }
                    if (packet.sequence <= motionSequence || packet.rotationRate?.Length != 3 || packet.quaternion?.Length != 4) continue;
                    motionSequence = packet.sequence; motionTicks = ticks;
                    float speed = new Vector3(packet.rotationRate[0], packet.rotationRate[1], packet.rotationRate[2]).magnitude;
                    if (!float.IsFinite(speed) || speed > .35f) stillSince = -1;
                    else if (stillSince < 0) stillSince = Time.unscaledTime;
                }
                if (!MotionFresh || Time.timeScale == 0) stillSince = -1;
            }
            if (autoArmed && !running && !IsBusy && !sessionError && Time.timeScale > 0 && exercise?.connected == true && ReadyToBegin)
                StartCoroutine(Begin());
        }

        // UIDocument can build its tree after this component's Start, so bind whenever the tree appears
        // (and again if it is rebuilt) instead of assuming it exists at startup.
        bool BindUI()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var button = root?.Q<Button>("start");
            if (button == null) return false;
            if (button == start) return true;
            title = root.Q<Label>("title"); repCount = root.Q<KReadout>("rep-count"); angleReadout = root.Q<KReadout>("angle-readout");
            statusLabel = root.Q<Label>("status"); planLabel = root.Q<Label>("plan"); summaryLabel = root.Q<Label>("summary");
            summaryCard = root.Q("summary-card"); start = button;
            sideLabel = root.Q<Label>("side-label"); progressNote = root.Q<Label>("progress-note");
            angleNote = root.Q<Label>("angle-note"); sensorStatus = root.Q<KChip>("sensor-status");
            cueTitle = root.Q<Label>("cue-title"); cueStep = root.Q<KTag>("cue-step");
            screen = root.Q("studio-screen"); hudCoach = root.Q("hud-coach"); angleDetails = root.Q("angle-details");
            hudCoachLine = root.Q<Label>("hud-coach-line");
            repRing = root.Q<KArc>("rep-ring"); angleMeter = root.Q<KMeter>("angle-meter");
            root.Q<Button>("summary-close").clicked += () => summaryCard.AddToClassList("hidden");
            root.Q<Button>("summary-menu").clicked += () => Kinesthetic.Menu.ActivityNavigation.Ensure().OpenReturn();
            root.Q<Button>("view-toggle").clicked += () => GetComponent<StudioCamera>()?.ToggleView();
            start.clicked += () => {
                if (IsBusy) return;
                if (running) StartCoroutine(Stop());
                else if (ReadyToBegin && exercise?.connected == true) StartCoroutine(Begin());
                else {
                    sessionError = false; summaryReceived = false; valid = attempted = 0;
                    summaryCard.AddToClassList("hidden"); autoArmed = true; enteredAt = Time.unscaledTime;
                    StartCoroutine(ConnectServices());
                }
            };
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
            autoArmed = false; startingSession = true; sessionError = false; summaryReceived = false;
            currentExerciseId = "pending";   // ignore the stream until the coordinator says which session is ours
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
            if (!useCameraPose && !ReadyToBegin) { startingSession = false; autoArmed = true; yield break; }
            var body = new JObject { ["planVersion"] = planVersion }.ToString();
            using var request = new UnityWebRequest(bridge + "/exercise/start", "POST") {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)), downloadHandler = new DownloadHandlerBuffer(), timeout = 5 };
            yield return request.SendWebRequest();
            startingSession = false;
            start.SetEnabled(true);
            if (request.result != UnityWebRequest.Result.Success) { sessionError = true; status = useCameraPose ? "Check that camera capture is open, then press Start to try again." : "Couldn’t connect. Press Retry connection."; yield break; }
            sessionError = false;
            // Only this session's messages count from here: starting one closes any session still open on the
            // coordinator, and that one's summary must not be taken for ours.
            try
            {
                var reply = JObject.Parse(request.downloadHandler.text);
                currentExerciseId = (string)reply["exerciseId"]; exerciseKind = (string)reply["exerciseKind"] ?? exerciseKind; shownAngle = 0;
            }
            catch (Exception) { currentExerciseId = null; }
            running = true; calibrated = false; attempted = valid = 0; liveAngle = null;
            status = useCameraPose ? "Hold still with your arms relaxed · calibrating" : $"Keep your {SensorPlacement} still · calibrating";
            start.text = "Finish set";
        }

        void ApplyPlan(JObject plan)
        {
            // The plan's derived v1 view is the first measured prescription: the shoulder raise this scene runs.
            var e = plan["exercise"] as JObject; if (e == null) return;
            var prescription = (plan["activities"] as JArray)?.OfType<JObject>().FirstOrDefault(a => !string.IsNullOrEmpty((string)a["exerciseKind"]));
            exerciseKind = (string)prescription?["exerciseKind"] ?? exerciseKind;
            bool nextWrist = (string)prescription?["params"]?["imuSource"] == "wrist";
            if (nextWrist != wristMotion) { wristMotion = nextWrist; motionTicks = 0; motionSequence = -1; motionSession = null; stillSince = -1; }
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
            title.text = exerciseKind == "elbow-flexion.v1" ? "Elbow bends" : "Shoulder raises";
            sideLabel.text = $"{side.ToUpperInvariant()} ARM";
            planLabel.text = $"{prescribedReps} reps · {targetDeg:0}° target";
            planLabel.tooltip = string.IsNullOrWhiteSpace(coachingNote) ? $"Prescribed plan v{planVersion}" : coachingNote;
            repCount.caption = $"of {prescribedReps} reps";
            angleReadout.caption = $"Target {targetDeg:0}°";
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
            running = false; autoArmed = false; start.text = "Practice again";
            completed?.Invoke(true);
        }

        void Update()
        {
            ReadPose();
            if (!BindUI()) return;
            ReadExercise();
            ReadReadiness();
            DrawGuides();
            // While the coach demonstrates and hands over, the cue is theirs; the measurement status follows after.
            coach ??= FindAnyObjectByType<Kinesthetic.Coach.CoachDemonstrator>();
            statusLabel.text = Cue = running && coach && coach.Demonstrating ? "Watch Alex. Raise, hold, lower."
                : running && coach && coach.HandingOff ? "Your turn. Follow your mirror." : status;
            UpdateStudioUI();
            UpdatePlayHud();
        }

        void UpdatePlayHud()
        {
            var voice = Kinesthetic.Coach.CoachVoice.Instance;
            if (voice && running && calibrated && !voiceOn) { voice.Begin(PatientId); voiceOn = true; }
            else if (voice && !running && voiceOn) { voice.End(); voiceOn = false; }
            screen.EnableInClassList("playing", running);
            screen.EnableInClassList("compact", screen.resolvedStyle.width < 1400);
            var said = running && voice ? voice.Line : "";
            hudCoachLine.text = said;
            hudCoach.EnableInClassList("hidden", string.IsNullOrEmpty(said));
        }

        void UpdateStudioUI()
        {
            var cameraRig = GetComponent<StudioCamera>();
            screen.Q<Button>("view-toggle").text = cameraRig && cameraRig.InSeatedView ? "Wide view" : "Seated view";
            bool fresh = useCameraPose ? Fresh : MotionFresh;
            bool live = running && Fresh && liveAngle.HasValue;
            bool reached = live && liveAngle.Value >= targetDeg && liveAngle.Value <= targetDeg + bandDeg;
            bool over = live && liveAngle.Value > targetDeg + bandDeg;
            sensorStatus.state = sessionError || running && !fresh ? KChip.State.Trouble : fresh ? KChip.State.Good : KChip.State.Live;
            sensorStatus.text = useCameraPose ? (fresh ? "Camera connected" : "Connecting camera…")
                : fresh ? "AirPod connected" : "Connecting AirPod…";
            repCount.value = valid.ToString();
            repRing.segments = prescribedReps; repRing.fraction = (float)valid / Mathf.Max(1, prescribedReps);
            progressNote.text = valid >= prescribedReps ? "Set complete" : valid == 0 ? "One at a time." : $"{prescribedReps - valid} to go";
            angleDetails.EnableInClassList("hidden", !running || !calibrated);
            angleReadout.value = live ? $"{liveAngle.Value:0}°" : "—";
            angleReadout.tone = over ? KReadout.Tone.Target : reached ? KReadout.Tone.Good : KReadout.Tone.Ink;
            angleMeter.fraction = live ? liveAngle.Value / Mathf.Max(1, targetDeg) : 0;
            angleMeter.tone = over ? KMeter.Tone.Target : reached ? KMeter.Tone.Good : KMeter.Tone.Progress;
            angleNote.text = !live ? "Waiting for movement" : over ? "Ease down gently" : reached ? "In your target" : "Raise slowly";
            cueStep.tone = reached ? KTag.Tone.Good : sessionError || over ? KTag.Tone.Trouble : KTag.Tone.Info;
            cueStep.text = stoppingSession ? "SAVING" : !running ? "GET READY" : !calibrated ? "HOLD STILL" : coach && coach.Demonstrating ? "WATCH" : "YOUR TURN";
            cueTitle.text = stoppingSession ? "Saving your set…" : sessionError ? "Let’s reconnect." : startingSession ? "Hold still." : !running
                ? summaryReceived ? "Well done today." : !fresh ? "Secure your AirPod." : ReadyToBegin ? "Ready. Let’s begin." : "Rest your arm."
                : !fresh ? "Let’s find your AirPod." : !calibrated ? "Hold still to calibrate." : coach && coach.Demonstrating ? "Watch Alex." : over ? "Lower gently." : reached ? "Hold. Then lower slowly." : "Raise, hold, lower.";
            if (!running && !startingSession && !sessionError)
                statusLabel.text = Cue = summaryReceived ? "Your session is saved." : !fresh ? $"Connect the AirPod on your {SensorPlacement}." : ReadyToBegin ? "Starting automatically…" : $"Keep your {SensorPlacement} still. We’ll begin automatically.";
            start.text = stoppingSession ? "Saving…" : running ? "Finish set" : startingSession ? "Starting…" : summaryReceived ? "Practice again" : sessionError ? "Retry connection" : "Connecting…";
            bool retry = sessionError || (!fresh || exercise?.connected != true) && Time.unscaledTime - enteredAt > 8;
            if (retry && !running && !IsBusy) start.text = "Retry connection";
            start.EnableInClassList("hidden", !running && !summaryReceived && !retry);
            start.SetEnabled(!IsBusy);
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
                if (currentExerciseId != null && (string)m["exerciseId"] != currentExerciseId) continue;   // an earlier session closing
                switch ((string)m["type"])
                {
                    case "exercise.started": exerciseKind = (string)p["exerciseKind"] ?? exerciseKind; shownAngle = 0; UpdatePlanLabels(); break;
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
                case "tracking.lost": status = useCameraPose ? "Move back into view — shoulders, elbows and hips" : "AirPod disconnected. Hold still while it reconnects."; Flash(Bad); break;
                case "tracking.recovered": if (calibrated) status = "Connected. Continue when ready."; break;
                case "rep.completed":
                    attempted++;
                    if (e["valid"]?.Value<bool>() == true) { valid++; status = valid >= prescribedReps ? "That's the set — great work" : $"Rep {valid} counted ✓  Lower slowly, then go again"; Flash(Good); }
                    else
                    {
                        status = (string)e["reason"] switch {
                            "did_not_reach_target" => "Almost — reach a little higher and hold",
                            "trunk_compensation" => "Keep your chest facing forward — that one didn't count",
                            "tracking_lost" => useCameraPose ? "Tracking paused. Try again." : "AirPod disconnected. Try again.",
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
            running = false; autoArmed = false; start.text = "Practice again";
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
            root.Q<KReadout>("summary-valid").value = valid.ToString();
            root.Q<KReadout>("summary-attempted").value = attempted.ToString();
            root.Q<KReadout>("summary-peak").value = median;
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
            var to = (p["to"] as JObject)?["targetDeg"]?.Value<float>();   // null on a hold
            var goodSessions = p["goodSessions"] as JObject;
            int good = goodSessions?["count"]?.Value<int>() ?? 0, needed = goodSessions?["needed"]?.Value<int>() ?? 0;
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
