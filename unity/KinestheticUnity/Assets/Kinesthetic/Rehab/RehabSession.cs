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
using Kinesthetic.UI.Boards;
using Kinesthetic.Golf;

namespace Kinesthetic.Rehab
{
    // Patient-facing seated shoulder raise. The coordinator's measurement engine is the only source of
    // angles, rep counts and validity; this view renders them and the avatar never feeds back into them.
    //
    // The screen is several world-space boards standing at stations around the seat (RehabSceneSetup), and
    // `boards` is the one root they are queried through: the element names are the ones the single
    // document had, so nothing below knows which board a label is on.
    public sealed class RehabSession : MonoBehaviour, IActivity, IRehabView
    {
        public PoseRig rig;
        public BoardSet boards;
        [Tooltip("Drive the Mii from camera pose (MediaPipe). Off: measurement is IMU-only and the Mii's arm follows the AirPod angle.")]
        public bool useCameraPose;
        public bool autoStartSession = true, startServices = true;
        public string motionUrl = SensorHub.DefaultMotionUrl;
        public string wristMotionUrl = "ws://127.0.0.1:8767/bowling-motion?role=viewer";
        bool wristMotion;
        // The movement tile for the kind being measured: its body model draws the joint, its tag says where the
        // tracker goes ("AIRPODS IN YOUR EARS" → "ears"). Every library kind has one, so a studio opened on its own
        // draws a curl as a curl.
        ActivityEntry Modelled => ActivityCatalog.MovementFor(exerciseKind);
        BodyModel Body => Modelled?.Body;
        // Where each tracker is, from the card's tag: "AIRPODS ON WRIST AND CHEST" → ["wrist", "chest"].
        string[] Placements
        {
            get
            {
                var tag = Modelled?.CardTag?.ToLowerInvariant();
                if (tag == null) return new[] { wristMotion ? "wrist" : "handle" };
                int at = Mathf.Max(tag.LastIndexOf(" on ", StringComparison.Ordinal), tag.LastIndexOf(" in ", StringComparison.Ordinal));
                var places = at < 0 ? tag : tag.Substring(at + 4);
                return places.Replace("the ", "").Replace("your ", "").Split(new[] { " and " }, StringSplitOptions.None);
            }
        }
        string SensorPlacement => Placements[0];
        /// The tracker still missing, so the status line names the one to fix.
        string MissingPlacement => TwoImu && limb.Fresh && !second.Fresh && Placements.Length > 1 ? Placements[1] : SensorPlacement;
        string AllPlacements => string.Join(" and ", Placements);
        // A movement tile from the gallery (activities.json group "movement"): this studio measures that one kind, as
        // the plan prescribes it or, when the plan does not, as a practice set at the library's defaults. The
        // coordinator decides which (GET /api/prescription); null when the studio was opened as itself.
        ActivityEntry movement;
        string movementLabel, movementPosture;
        bool practice;
        bool autoArmed, servicesStarting;
        float enteredAt;
        // One watch per AirPod pair. A two-AirPod movement (the catalog's requires "ref", coordinator/exercise/two-imu.ts)
        // needs both live and still before a set may start: the limb is the wrist pair unless the plan pins it to the
        // club pair, and the neighbouring segment is the other one — the coordinator reads them the same way.
        readonly MotionWatch limb = new(), second = new();
        string imuSource;
        bool TwoImu => Modelled?.Requires.Contains("ref") == true;
        bool LimbOnWrist => TwoImu ? imuSource != "club" : wristMotion;
        bool MotionFresh => limb.Fresh && (!TwoImu || second.Fresh);
        public bool ReadyToBegin => useCameraPose ? Fresh : limb.Still && (!TwoImu || second.Still);
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
        RepFeel IRehabView.Feel => feel;
        bool startingSession, stoppingSession, sessionError, summaryReceived;
        public bool IsBusy => startingSession || stoppingSession;
        // IActivity. The shell drives this without knowing it is a therapy session.
        public string ActivityId => movement?.Id ?? "rehab.studio";
        public event Action<string> Completed;
        /// <summary>Leaving must fail if /exercise/stop does not answer, or the set is lost.</summary>
        public IEnumerator RequestExit(Action<bool> succeeded) => FinishSession(succeeded);
        int attempted, valid;
        float? liveAngle; string phase = "idle";
        float lastSampleAt = -99, shownAngle; string exerciseKind = "arm-elevation.v1";
        Kinesthetic.Coach.CoachDemonstrator coach;
        bool voiceOn; string currentExerciseId;
        VisualElement hudCoach; Label hudCoachLine; Button viewToggle, summaryClose, summaryMenu;
        int boundGeneration = -1;
        // Held, so a rebind after one board rebuilds can take them off the boards that did not (-= then +=):
        // a handler added twice fires twice, and a toggle that fires twice does nothing.
        Action onSummaryClose, onSummaryMenu, onViewToggle, onStart;
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
        Label statusLabel; Button start; KSheet summaryCard;
        Label cueTitle;
        // The prescription, handed over on arrival. What it says lives in the plan; when it is put down is
        // what starts the set.
        readonly ActivityBriefing briefing = new();
        bool briefedWarning;
        KReadout repCount;
        KChip sensorStatus; KTag cueStep;
        string coachingNote = "";
        // Rep qualities (coordinator/exercise/quality.ts): how well a counted rep is made — the hold at the top,
        // the tempo of each phase, hitches, the streak. The coordinator judges; this renders. Their configs are
        // plan params (holdTargetMs, raiseMs, lowerMs), live readouts ride every sample, verdicts ride rep.completed.
        float planHoldMs, holdTargetMs = 2000, raiseMs = 2000, lowerMs = 3000;
        JObject liveQuality;            // this sample's readouts by quality id; null between reps
        int streak; float bestHoldMs; int holdMetRep;
        KArc holdRing; KReadout holdReadout, bestHoldReadout; KTag streakTag;   // the dock's hold ring; the crown's figures
        bool HoldPrescribed => planHoldMs > 0;
        float HoldTargetMs => Mathf.Max(holdTargetMs, planHoldMs);
        // The rep as the in-world mechanics feel it (Mechanics/): the coordinator's live judgements plus the speed
        // of the angle being shown. Published to a headset with the rest of the state.
        RepFeel feel; float speedDegS;
        System.Collections.Generic.HashSet<string> qualityIds;   // which qualities this session is judged on; null until it starts
        // The band around the measured arm: cerulean at rest, sand while the rep is being made,
        // green once the target is reached, and coral only when something is wrong and has to be seen.
        static readonly Color Idle = Palette.Cerulean20.At(.55f), Active = Palette.Sand30.At(.9f),
            Good = Palette.Good.At(.95f), Bad = Palette.Attention.At(.95f);

        void Start()
        {
            Application.runInBackground = true;
            rig.Initialize(); rig.Apply(null);
            if (!boards) boards = FindAnyObjectByType<BoardSet>();
            SensorHub.Ensure();
            exercise = new ExerciseClient(exerciseUrl);
            // The mirror window is added here, so the generated scene needs no change. In a group session the other
            // person sits where it stands instead (PeerAvatar): you watch them move, not yourself. Joining or leaving
            // a group mid-visit swaps one for the other.
            ArrangeCompany();
            var groups = Kinesthetic.Menu.GroupPanel.Instance;
            if (groups) groups.RoomChanged += ArrangeCompany;
            // The patient's own eyes (mirror left, coach right); C glides out to the wide shot.
            if (!GetComponent<StudioCamera>()) gameObject.AddComponent<StudioCamera>().session = this;
            // A headset renders this studio from what it publishes (RehabStateClient in QuestRehab).
            if (!GetComponent<RehabStatePublisher>()) gameObject.AddComponent<RehabStatePublisher>().session = this;
            // The in-world mechanics: the ball balanced on the hand, the pace to follow. They read this view's Feel.
            Mechanics.RepMechanics.AttachAll(gameObject, this);
            // Not armed yet. The set begins when the patient puts the briefing down, not when the sensor
            // happens to hold still for a second and a half.
            autoArmed = false; enteredAt = Time.unscaledTime;
            var launched = Kinesthetic.Menu.ActivityNavigation.Current();
            movement = launched != null && launched.IsMovement ? launched : null;
            if (movement != null) { exerciseKind = movement.MovementKind ?? exerciseKind; movementLabel = movement.DisplayName; }
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
                bool wrist = LimbOnWrist;
                limb.Read(wrist ? wristMotionUrl : motionUrl, wrist ? "bowling.motion" : "club.motion");
                if (TwoImu) second.Read(wrist ? motionUrl : wristMotionUrl, wrist ? "club.motion" : "bowling.motion");
                else second.Reset();
            }
            if (autoArmed && !running && !IsBusy && !sessionError && Time.timeScale > 0 && exercise?.connected == true && ReadyToBegin)
                StartCoroutine(Begin());
        }

        // A UIDocument can build its tree after this component's Start, so bind whenever every board has
        // one (and again if any is rebuilt) instead of assuming they exist at startup.
        bool BindUI()
        {
            if (!boards || !boards.Live) return false;
            var root = boards;
            var button = root.Q<Button>("start");
            if (button == null) return false;
            if (button == start && boundGeneration == boards.Generation) return true;
            boundGeneration = boards.Generation;
            repCount = root.Q<KReadout>("rep-count");
            statusLabel = root.Q<Label>("status");
            summaryCard = root.Q<KSheet>("summary-card");
            sensorStatus = root.Q<KChip>("sensor-status");
            cueTitle = root.Q<Label>("cue-title"); cueStep = root.Q<KTag>("cue-step");
            hudCoach = root.Q("hud-coach");
            hudCoachLine = root.Q<Label>("hud-coach-line");
            holdRing = root.Q<KArc>("hold-ring"); holdReadout = root.Q<KReadout>("hold-readout");
            streakTag = root.Q<KTag>("streak-tag"); bestHoldReadout = root.Q<KReadout>("best-hold");
            // The summary's two ways on: another set straight away (what the dock's Practice again does), or done
            // for today, straight back to the menu — the set is already saved, so there is nothing to confirm.
            onSummaryClose ??= () => { summaryCard.Dismiss(); onStart(); };
            onSummaryMenu ??= () => Kinesthetic.Menu.ActivityNavigation.Ensure().ReturnToMenuNow();
            onViewToggle ??= () => GetComponent<StudioCamera>()?.ToggleView();   // the Mac's camera only
            onStart ??= () => {
                if (IsBusy) return;
                if (running) StartCoroutine(Stop());
                else if (ReadyToBegin && exercise?.connected == true) StartCoroutine(Begin());
                else {
                    sessionError = false; summaryReceived = false; valid = attempted = 0;
                    summaryCard.Hide(); autoArmed = true; enteredAt = Time.unscaledTime;
                    StartCoroutine(ConnectServices());
                }
            };
            summaryClose = Rebind(summaryClose, root.Q<Button>("summary-close"), onSummaryClose);
            summaryMenu = Rebind(summaryMenu, root.Q<Button>("summary-menu"), onSummaryMenu);
            viewToggle = Rebind(viewToggle, root.Q<Button>("view-toggle"), onViewToggle);
            start = Rebind(start, button, onStart);
            // Mounted after every rebuild, in whatever state it was already in — a board that remade its
            // tree must not hand a dismissed sheet back to someone mid-set. A venue with no briefing host
            // has nothing waiting to be read, so it arms straight away instead of never starting. Say so
            // out loud: a studio silently missing its prescription looks exactly like one that never had
            // the feature, and the cause is almost always a scene that predates the brief board.
            if (!briefing.Bind(root.Q(ActivityBriefing.HostName), boards.BoardWith(ActivityBriefing.HostName)?.gameObject, BeginFromBriefing))
            {
                if (!briefedWarning) Debug.LogWarning($"No '{ActivityBriefing.HostName}' host on any board, so there is no " +
                    "prescription to read and the set arms on arrival. Run Kinesthetic \u2192 Rehab \u2192 Create shoulder " +
                    "raise scene; it refuses while any open scene has unsaved changes, so save those first.");
                briefedWarning = true;
                BeginFromBriefing();
            }
            UpdatePlanLabels();
            summaryCard.Hide();
            StartCoroutine(RefreshPlan());
            return true;
        }

        /// Move a handler from the button it was on to the one the boards answer with now. The same button
        /// gets it once: -= before += is what makes a rebind of an unchanged board a no-op.
        static Button Rebind(Button previous, Button current, Action handler)
        {
            if (previous != null) previous.clicked -= handler;
            if (current != null) { current.clicked -= handler; current.clicked += handler; }
            return current;
        }

        IEnumerator RefreshPlan()
        {
            using var planRequest = UnityWebRequest.Get(bridge + "/api/plans/active");
            planRequest.timeout = 5;
            yield return planRequest.SendWebRequest();
            if (planRequest.result == UnityWebRequest.Result.Success) ApplyPlan(JObject.Parse(planRequest.downloadHandler.text));
            if (movement == null) yield break;
            using var request = UnityWebRequest.Get(bridge + "/api/prescription?activityId=" + UnityWebRequest.EscapeURL(movement.Id));
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) ApplyMovement(JObject.Parse(request.downloadHandler.text));
        }

        /// The movement's own prescription replaces the plan's first one: its kind, its band, its dose.
        void ApplyMovement(JObject reply)
        {
            if (reply["prescription"] is not JObject x) return;
            practice = reply["practice"]?.Value<bool>() == true;
            exerciseKind = (string)x["exerciseKind"] ?? exerciseKind;
            movementLabel = (string)reply["label"] ?? movementLabel;
            movementPosture = (string)reply["posture"];
            var p = x["params"] as JObject;
            imuSource = (string)p?["imuSource"];
            bool nextWrist = imuSource == "wrist";
            if (nextWrist != wristMotion) { wristMotion = nextWrist; limb.Reset(); second.Reset(); }
            side = (string)p?["side"] ?? side;
            targetDeg = Num(p?["targetDeg"]) ?? targetDeg;
            if (Num(p?["targetMaxDeg"]) is float ceiling && ceiling > targetDeg) bandDeg = ceiling - targetDeg;
            prescribedReps = x["targetCount"]?.Value<int>() ?? prescribedReps;
            planHoldMs = Num(p?["holdMs"]) ?? 0;
            holdTargetMs = Num(p?["holdTargetMs"]) ?? holdTargetMs;
            raiseMs = Num(p?["raiseMs"]) ?? raiseMs; lowerMs = Num(p?["lowerMs"]) ?? lowerMs;
            if (practice) coachingNote = (string)x["note"] ?? "";
            UpdatePlanLabels();
        }

        IEnumerator Begin()
        {
            autoArmed = false; startingSession = true; sessionError = false; summaryReceived = false;
            currentExerciseId = "pending";   // ignore the stream until the coordinator says which session is ours
            start.SetEnabled(false); summaryCard.Hide();
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
            var startBody = new JObject { ["planVersion"] = planVersion };
            if (movement != null) startBody["activityId"] = movement.Id;
            var body = startBody.ToString();
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
            liveQuality = null; streak = 0; bestHoldMs = 0; holdMetRep = 0;
            status = useCameraPose ? "Hold still with your arms relaxed · calibrating" : $"Keep your {AllPlacements} still · calibrating";
            start.text = "Finish set";
        }

        void ApplyPlan(JObject plan)
        {
            // The plan's derived v1 view is the first measured prescription: the shoulder raise this scene runs.
            var e = plan["exercise"] as JObject; if (e == null) return;
            var prescription = (plan["activities"] as JArray)?.OfType<JObject>().FirstOrDefault(a => !string.IsNullOrEmpty((string)a["exerciseKind"]));
            exerciseKind = (string)prescription?["exerciseKind"] ?? exerciseKind;
            imuSource = (string)prescription?["params"]?["imuSource"];
            bool nextWrist = imuSource == "wrist";
            if (nextWrist != wristMotion) { wristMotion = nextWrist; limb.Reset(); second.Reset(); }
            planVersion = plan["version"]?.Value<int>() ?? planVersion;
            side = (string)e["side"] ?? side;
            targetDeg = e["targetDeg"]?.Value<float>() ?? targetDeg;
            var ceiling = e["targetMaxDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? e["targetMaxDeg"].Value<float>() : (float?)null;
            if (ceiling > targetDeg) bandDeg = ceiling.Value - targetDeg;   // the drawn band is the plan's safe band
            prescribedReps = e["prescribedReps"]?.Value<int>() ?? prescribedReps;
            coachingNote = (string)plan["coachingNote"] ?? "";
            // The qualities' configs are ordinary prescription params, filled with their defaults on read.
            var q = prescription?["params"] as JObject;
            planHoldMs = Num(q?["holdMs"]) ?? e["holdMs"]?.Value<float>() ?? planHoldMs;
            holdTargetMs = Num(q?["holdTargetMs"]) ?? holdTargetMs;
            raiseMs = Num(q?["raiseMs"]) ?? raiseMs; lowerMs = Num(q?["lowerMs"]) ?? lowerMs;
            UpdatePlanLabels();
        }

        /// "RIGHT ARM  ·  " for a limb, "RIGHT SIDE  ·  " for a side bend, nothing for a nod or a forward bend.
        string SideLabel
        {
            get
            {
                var b = Body; string s = side.ToUpperInvariant();
                if (b == null) return $"{s} ARM  ·  ";
                return b.Segment switch
                {
                    "arm" or "forearm" => $"{s} ARM  ·  ",
                    "thigh" or "shank" or "leg" => $"{s} LEG  ·  ",
                    _ => Mathf.Abs(b.Toward.x) > .5f ? $"{s} SIDE  ·  " : "",
                };
            }
        }

        static float? Num(JToken t) => t?.Type is JTokenType.Float or JTokenType.Integer ? t.Value<float>() : null;
        static string Seconds(float ms) => $"{ms / 1000:0.#} s";

        void UpdatePlanLabels()
        {
            if (repCount != null) repCount.caption = $"of {prescribedReps}";
            briefing.Describe(Prescription());
        }

        /// The plan as a sheet of paper, which is what it is. Every line is a field the coordinator sent;
        /// the coaching note is the only copy on it written by a person, and it used to live in a tooltip
        /// on a world-space board, where nothing hovers and nobody ever read it.
        ///
        /// There is deliberately no clinician name, clinic, date or signature. None of those is measured,
        /// and a prescription that invents them is a forged record rather than a nice touch.
        Briefing Prescription() => new()
        {
            eyebrow = practice ? "PRACTICE · NOT IN YOUR PLAN" : $"PRESCRIBED PLAN · V{planVersion}",
            title = Named(movementLabel, Modelled?.DisplayName, "Shoulder raises"),
            subtitle = Named(movementPosture, string.IsNullOrEmpty(side) ? "Seated" : $"{char.ToUpperInvariant(side[0])}{side.Substring(1)} arm, seated"),
            // The hold and the tempo are what the set is judged on beyond the count, so they are read before it.
            lines = new[]
            {
                // Where the tracker goes comes first: the reading is only as good as the strap.
                new BriefingLine("Wear it", useCameraPose ? "Camera" : Modelled?.Wear ?? $"AirPod on your {SensorPlacement}"),
                new BriefingLine("Repetitions", prescribedReps.ToString()),
                new BriefingLine("Move to", $"{targetDeg:0}°"),
                new BriefingLine("Stay under", $"{targetDeg + bandDeg:0}°"),
                new BriefingLine("Hold at the end", HoldPrescribed ? Seconds(HoldTargetMs) : "No hold"),
                new BriefingLine("Tempo", $"{Seconds(raiseMs)} out · {Seconds(lowerMs)} back"),
            },
            note = coachingNote,
            noteFrom = "FROM YOUR CARE TEAM",
            action = "Begin my set",
        };

        /// The first of these that actually says something. A field the service sends as "" is absent, not
        /// a value, and `??` keeps it: that is how the sheet came to render with no title and no posture on
        /// it, since a briefing hides copy that is blank rather than leaving a gap where it would be.
        static string Named(params string[] options) => Array.Find(options, o => !string.IsNullOrWhiteSpace(o));

        /// The sheet is down. Only from here may the studio start itself, once the sensor is ready.
        void BeginFromBriefing()
        {
            briefing.Dismiss();
            autoArmed = autoStartSession;
            enteredAt = Time.unscaledTime;
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
            UpdateFeel();
            // While the coach demonstrates and hands over, the cue is theirs; the measurement status follows after.
            coach ??= FindAnyObjectByType<Kinesthetic.Coach.CoachDemonstrator>();
            statusLabel.text = Cue = running && coach && coach.Demonstrating ? "Watch Alex. Raise, hold, lower."
                : running && coach && coach.HandingOff ? "Your turn. Follow your mirror." : status;
            UpdateStudioUI();
            UpdatePlayHud();
        }

        /// The rep in progress as the mechanics feel it: the coordinator's live quality readouts, the speed of the
        /// shown angle, and the speed the plan asks for in this phase (target over the raise time on the way up,
        /// the top of the band over the lowering time on the way down).
        void UpdateFeel()
        {
            var tempo = liveQuality?["tempo"] as JObject; var hold = liveQuality?["hold"] as JObject; var control = liveQuality?["control"] as JObject;
            bool inRep = running && calibrated && liveQuality != null && !(coach && coach.Demonstrating);
            string phase = inRep ? (string)tempo?["phase"] ?? (hold?["holding"]?.Value<bool>() == true ? "hold" : "raise") : "";
            bool hasTempo = qualityIds?.Contains("tempo") ?? true, hasHold = qualityIds?.Contains("hold") ?? HoldPrescribed;
            feel = new RepFeel
            {
                InRep = inRep, Phase = phase, Speed = speedDegS,
                TempoSpeed = !hasTempo ? 0 : phase == "lower" ? (targetDeg + bandDeg * .5f) / Mathf.Max(.1f, lowerMs / 1000f) : targetDeg / Mathf.Max(.1f, raiseMs / 1000f),
                HasTempo = hasTempo, HasHold = hasHold,
                HoldFraction = hold?["fraction"]?.Value<float>() ?? 0,
                Holding = hold?["holding"]?.Value<bool>() == true, HoldMet = hold?["met"]?.Value<bool>() == true,
                Hitches = control?["hitches"]?.Value<int>() ?? 0, Streak = streak,
                Fast = (string)tempo?["guidance"] == "slower",
                Valid = valid, Prescribed = prescribedReps,
            };
        }

        void UpdatePlayHud()
        {
            var voice = Kinesthetic.Coach.CoachVoice.Instance;
            if (voice && running && calibrated && !voiceOn) { voice.Begin(PatientId); voiceOn = true; }
            else if (voice && !running && voiceOn) { voice.End(); voiceOn = false; }
            var said = running && voice ? voice.Line : "";
            hudCoachLine.text = said;
            hudCoach.EnableInClassList("hidden", string.IsNullOrEmpty(said));
        }

        void UpdateStudioUI()
        {
            // While the summary is up it is the only board: the dock's chip, cue and button and the crown's count
            // all say again what the card says.
            bool summaryOpen = summaryCard != null && summaryCard.Presented;
            boards.Q("dock-board")?.EnableInClassList("hidden", summaryOpen);
            boards.Q("crown-board")?.EnableInClassList("hidden", summaryOpen || !running);
            var cameraRig = GetComponent<StudioCamera>();
            viewToggle.text = cameraRig && cameraRig.InSeatedView ? "Wide view" : "Seated view";
            bool fresh = useCameraPose ? Fresh : MotionFresh;
            bool live = running && Fresh && liveAngle.HasValue;
            bool reached = live && liveAngle.Value >= targetDeg && liveAngle.Value <= targetDeg + bandDeg;
            bool over = live && liveAngle.Value > targetDeg + bandDeg;
            sensorStatus.state = sessionError || running && !fresh ? KChip.State.Trouble : fresh ? KChip.State.Good : KChip.State.Live;
            sensorStatus.text = useCameraPose ? (fresh ? "Camera connected" : "Connecting camera…")
                : fresh ? (TwoImu ? "Both AirPods connected" : "AirPod connected") : TwoImu ? "Connecting AirPods…" : "Connecting AirPod…";
            // The crown: the count (it punches when it goes up), the run of reps made well, and the best hold. The
            // angle itself is the band's to show, and the hold in progress and the pace are the reticle's.
            if (repCount != null)
            {
                repCount.value = valid.ToString();
                repCount.tone = valid >= prescribedReps && prescribedReps > 0 ? KReadout.Tone.Good : KReadout.Tone.Ink;
            }
            if (streakTag != null)
            {
                streakTag.text = streak >= 2 ? $"{streak} IN A ROW" : "";
                streakTag.EnableInClassList("hidden", streak < 2);
            }
            if (bestHoldReadout != null)
            {
                bestHoldReadout.EnableInClassList("hidden", !HoldPrescribed);
                bestHoldReadout.value = bestHoldMs > 0 ? $"{bestHoldMs / 1000:0.0} s" : "—";
                bestHoldReadout.caption = $"best hold · aim {Seconds(HoldTargetMs)}";
                bestHoldReadout.tone = bestHoldMs >= HoldTargetMs ? KReadout.Tone.Good : KReadout.Tone.Ink;
            }
            // The rep in progress, as the qualities see it: which phase, whether the hold clock is running, and a
            // nudge when the arm is coming down faster than the tempo. All from the coordinator; nothing timed here.
            var hold = live ? liveQuality?["hold"] as JObject : null;
            var tempo = live ? liveQuality?["tempo"] as JObject : null;
            bool holding = hold?["holding"]?.Value<bool>() == true, holdMet = hold?["met"]?.Value<bool>() == true;
            bool tooFast = (string)tempo?["guidance"] == "slower";
            string phase = (string)tempo?["phase"];
            bool inRep = live && liveQuality != null && calibrated && !(coach && coach.Demonstrating);
            cueStep.tone = holdMet || reached && !tooFast ? KTag.Tone.Good : sessionError || over ? KTag.Tone.Trouble : KTag.Tone.Info;
            cueStep.text = stoppingSession ? "SAVING" : !running ? "GET READY" : !calibrated ? "HOLD STILL" : coach && coach.Demonstrating ? "WATCH"
                : !inRep ? "YOUR TURN" : tooFast ? "SLOWER" : holding ? "HOLD" : phase == "lower" ? "LOWER" : "RAISE";
            cueTitle.text = stoppingSession ? "Saving your set…" : sessionError ? "Let’s reconnect." : startingSession ? "Hold still." : !running
                ? summaryReceived ? "Well done today." : !fresh ? "Secure your AirPod." : ReadyToBegin ? "Ready. Let’s begin." : "Rest your arm."
                : !fresh ? "Let’s find your AirPod." : !calibrated ? "Hold still to calibrate." : coach && coach.Demonstrating ? "Watch Alex."
                : tooFast ? "Slower on the way down." : over ? "Lower gently." : holdMet ? "Held. Now lower slowly." : holding ? "Hold it there."
                : reached ? "Hold. Then lower slowly." : phase == "lower" ? "Lower slowly, all the way." : "Raise, hold, lower.";
            if (holdRing != null)
            {
                holdRing.EnableInClassList("hidden", !holding);
                holdRing.fraction = hold?["fraction"]?.Value<float>() ?? 0;
                holdRing.tone = holdMet ? KArc.Tone.Good : KArc.Tone.Progress;
                holdReadout.value = $"{(hold?["heldMs"]?.Value<float>() ?? 0) / 1000:0.0}";
            }
            if (!running && !startingSession && !sessionError)
                statusLabel.text = Cue = summaryReceived ? "Your session is saved." : !fresh ? $"Connect the AirPod on your {MissingPlacement}." : ReadyToBegin ? "Starting automatically…" : $"Keep your {AllPlacements} still. We’ll begin automatically.";
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
            // The headset, when one is worn: the torso leans and the head turns with the wearer's, before the arm.
            if (HeadPoseFeed.Ensure().TryGet(out var headOffset, out var headRotation)) rig.ApplyHeadPose(headOffset, headRotation);
            // At rest the Mii still takes the movement's posture (arm out for 90/90, standing for a leg raise), so
            // the patient can see how to set up before the first rep.
            bool live = running && Fresh && liveAngle.HasValue;
            float before = shownAngle, dt = Time.unscaledDeltaTime;
            if (live) shownAngle = Mathf.Lerp(shownAngle, liveAngle.Value, 1 - Mathf.Exp(-12 * dt));
            // The speed of the angle being shown, for the mechanics' feel. Presentation only; the tempo verdict is the coordinator's.
            speedDegS = live && dt > 0 ? Mathf.Lerp(speedDegS, (shownAngle - before) / dt, 1 - Mathf.Exp(-10 * dt)) : 0;
            rig.ApplyMovement(Body, side == "left", live ? shownAngle : 0);
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
                    case "exercise.started":
                        exerciseKind = (string)p["exerciseKind"] ?? exerciseKind; shownAngle = 0;
                        // Which qualities this set is judged on, so a mechanic with nothing to answer to can hide.
                        if (p["qualities"] is JArray qualities)
                            qualityIds = new System.Collections.Generic.HashSet<string>(qualities.OfType<JObject>().Select(q => (string)q["id"]).Where(id => id != null));
                        UpdatePlanLabels(); break;
                    case "exercise.sample":
                        phase = (string)p["phase"] ?? phase;
                        liveAngle = p["valid"]?.Value<bool>() == true && p["angleDeg"]?.Type is JTokenType.Float or JTokenType.Integer ? p["angleDeg"].Value<float>() : null;
                        if (liveAngle.HasValue) lastSampleAt = Time.unscaledTime;
                        liveQuality = p["quality"] as JObject;
                        // The moment the hold reaches its target is worth a flash, once per rep.
                        if (liveQuality?["hold"]?["met"]?.Value<bool>() == true && p["rep"]?.Value<int>() is int rep && rep != holdMetRep) { holdMetRep = rep; Flash(Good); }
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
                    liveQuality = null;
                    streak = e["streak"]?.Value<int>() ?? 0;
                    var verdict = e["quality"] as JObject;
                    var longest = Num(verdict?["hold"]?["longestMs"]);
                    if (longest > bestHoldMs) bestHoldMs = longest.Value;
                    if (e["valid"]?.Value<bool>() == true)
                    {
                        valid++;
                        status = valid >= prescribedReps ? "That's the set — great work" : Critique(verdict);
                        Flash(Good);
                        Kinesthetic.Menu.ActivityNavigation.Instance?.PlaySelect();   // a rep earned is heard as well as seen
                    }
                    else
                    {
                        status = (string)e["reason"] switch {
                            "did_not_reach_target" => "Almost — reach a little higher and hold",
                            "trunk_compensation" => "Keep your chest facing forward — that one didn't count",
                            "trunk_lean" => "Keep your chest still — leaning doesn't count",
                            "upper_arm_swing" => "Keep your elbow at your side — that one didn't count",
                            "tracking_lost" => useCameraPose ? "Tracking paused. Try again." : "AirPod disconnected. Try again.",
                            "too_fast" => "Slow it down and control the movement",
                            _ => "That one didn't count" };
                        Flash(Bad);
                    }
                    if (valid >= prescribedReps && running) StartCoroutine(Stop());
                    break;
            }
        }

        /// What a counted rep earns as its status line: the streak when it is building, otherwise the one thing
        /// to change next time, in the order a therapist would call it — the hold, then the lowering, then smoothness.
        string Critique(JObject q)
        {
            bool Ok(string id) => q?[id] == null || q[id]["ok"]?.Value<bool>() != false;
            if (q == null) return $"Rep {valid} counted ✓  Lower slowly, then go again";
            if (!Ok("hold")) return $"Counted ✓ · next time hold the top for {Seconds(HoldTargetMs)}";
            if (!Ok("tempo")) return (string)q["tempo"]["lower"] == "fast" ? "Counted ✓ · lower more slowly next time" : "Counted ✓ · raise more slowly next time";
            if (!Ok("control")) return "Counted ✓ · keep it smooth, no catching";
            return streak >= 2 ? $"Rep {valid} ✓ · {streak} in a row, beautifully done" : $"Rep {valid} counted ✓ · nice and controlled";
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
            var root = boards;
            // A word for how it went, three numbers, and nothing the portal says better: per-rep reasons and the
            // form breakdown are the care team's, not a wall of captions at the end of a set.
            root.Q<Label>("summary-title").text = attempted == 0 ? "Ready for another day" : valid >= prescribedReps ? "Set complete!" : "Good practice";
            root.Q<KReadout>("summary-valid").value = valid.ToString();
            root.Q<KReadout>("summary-attempted").value = attempted.ToString();
            root.Q<KReadout>("summary-peak").value = median;
            root.Q<Label>("summary-progress")?.AddToClassList("hidden");   // filled by the progression verdict that follows
            bool simulated = s["simulated"]?.Value<bool>() == true;
            if (root.Q<KTag>("summary-badge") is KTag badge) { badge.text = simulated ? "DEMO SESSION" : "SESSION SAVED"; badge.tone = simulated ? KTag.Tone.Neutral : KTag.Tone.Good; }
            summaryCard.Present();
            root.Q<Button>("summary-close").Focus();
            Kinesthetic.Menu.ActivityNavigation.Ensure().PlaySelect();
            status = "Session saved";
        }

        // The progression rules' verdict on this session (coordinator/progression.ts), in the patient's words.
        // Arrives just after the summary; a level change inside the clinician's envelope has already applied.
        void ShowProgression(JObject p)
        {
            var label = boards.Q<Label>("summary-progress");
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
            // With the IMU, guides are laid along the measured segment itself (PoseRig.MovementSegment): the arm, the
            // shin, the head, whatever the movement moves, from the joint it moves about.
            var body = useCameraPose ? null : Body;
            if (rig.MovementSegment(body, left, 0, out var origin, out _, out var length)) { shoulder = origin; reach = length; }
            Vector3 At(float deg, float r)
            {
                if (rig.MovementSegment(body, left, deg, out _, out var direction, out _)) return shoulder + direction * r;
                float a = deg * Mathf.Deg2Rad; return shoulder + (down * Mathf.Cos(a) + across * Mathf.Sin(a)) * r;
            }

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

        /// One AirPod pair's stream from the relay, reduced to what readiness needs: is it live, and has it been still
        /// long enough to calibrate against. The coordinator scores; this only decides when a set may begin.
        sealed class MotionWatch
        {
            long ticks, sequence = -1; string session; float stillSince = -1;
            public bool Fresh => LivePoseClient.Fresh(ticks);
            public bool Still => Fresh && stillSince >= 0 && Time.unscaledTime - stillSince >= 1.5f;
            public void Reset() { ticks = 0; sequence = -1; session = null; stillSince = -1; }
            public void Read(string url, string type)
            {
                var motion = SensorHub.Instance?.MotionFor(url);
                while (motion != null && motion.Take(out var text, out var t))
                {
                    ClubMotionPacket packet;
                    try { packet = JsonUtility.FromJson<ClubMotionPacket>(text); } catch (Exception) { continue; }
                    if (packet == null || packet.type != type || packet.playerId != "patient" || !LivePoseClient.Fresh(t)) continue;
                    if (packet.sessionId != session) { session = packet.sessionId; sequence = -1; stillSince = -1; }
                    if (packet.sequence <= sequence || packet.rotationRate?.Length != 3 || packet.quaternion?.Length != 4) continue;
                    sequence = packet.sequence; ticks = t;
                    float speed = new Vector3(packet.rotationRate[0], packet.rotationRate[1], packet.rotationRate[2]).magnitude;
                    if (!float.IsFinite(speed) || speed > .35f) stillSince = -1;
                    else if (stillSince < 0) stillSince = Time.unscaledTime;
                }
                if (!Fresh || Time.timeScale == 0) stillSince = -1;
            }
        }

        void OnDestroy()
        {
            exercise?.Dispose();   // the hub owns the pose channel
            var groups = Kinesthetic.Menu.GroupPanel.Instance;
            if (groups) groups.RoomChanged -= ArrangeCompany;
        }

        /// The mirror when you are on your own, the other person when you are in a group — never both, since they
        /// stand in the same place.
        void ArrangeCompany()
        {
            if (useCameraPose || !this) return;
            bool together = Kinesthetic.Menu.GroupPanel.Instance?.InRoom == true;
            if (together)
            {
                if (GetComponent<MirrorPanel>() is MirrorPanel mirror) Destroy(mirror);
                if (!GetComponent<PeerAvatar>()) gameObject.AddComponent<PeerAvatar>().view = this;
            }
            else
            {
                if (GetComponent<PeerAvatar>() is PeerAvatar partner) Destroy(partner);
                if (!GetComponent<MirrorPanel>()) gameObject.AddComponent<MirrorPanel>().view = this;
            }
        }
    }
}
