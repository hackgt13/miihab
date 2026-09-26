using System;
using System.Collections;
using System.IO;
using Kinesthetic.Activities;
using Kinesthetic.Golf;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Bowling
{
    public sealed class BowlingGame : MonoBehaviour, IActivity
    {
        public Rigidbody ball;
        public Rigidbody[] pins;
        public Camera spectator;
        public LineRenderer aimLine;
        public BowlingAvatar avatar;
        public string motionUrl = "ws://127.0.0.1:8767/bowling-motion?role=viewer";
        public string bridge = "http://127.0.0.1:8766";
        public bool startServices = true;
        public bool renderOnly;
        public const float BallRadius = .12655f, HeadPinZ = 18.825f;
        public readonly BowlingSwing Swing = new();
        public BowlingScore Score { get; private set; } = new();
        public string Phase { get; private set; } = "Setup";
        public string Result { get; private set; } = "";
        public string Connection { get; private set; } = "Connecting AirPods…";
        public float LastAim { get; private set; }
        public float LastPower { get; private set; }
        public int LastPins { get; private set; }
        public bool MotionReady => client != null && client.connected && Age(lastArrival) < .3;
        public bool Paused { get; private set; }
        public bool CanRecalibrate => Phase == "Ready" || Phase == "Setup";
        // IActivity. The shell drives this the same way it drives golf and a therapy session.
        public string ActivityId => "bowling.adaptive";
        public bool IsRunning => !renderOnly && !gameReported;
        public bool IsBusy => postingGame;
        /// <summary>Raised once when the tenth frame closes, carrying the recorded session id.</summary>
        public event Action<string> Completed;
        /// <summary>Nothing server-side to close, but do not leave mid-POST or the game is lost.</summary>
        public IEnumerator RequestExit(Action<bool> succeeded)
        {
            // A game left partway is still the patient's practice: record it, marked not completed.
            if (!gameReported && rolls > 0) ReportGame(false);
            while (postingGame) yield return null;
            succeeded?.Invoke(true);
        }
        DateTime gameStartedUtc = DateTime.UtcNow;
        bool gameReported, postingGame;
        int rolls, reconnectsAtStart;
        public string Cue => Paused ? "Paused" : Phase == "Rolling" ? "Nice and easy." : Phase == "Result" || Phase == "Complete" ? Result : !MotionReady ? Connection : Swing.Cue;
        GolfMotionClient client => SensorHub.Instance?.MotionFor(motionUrl);
        Vector3[] pinPositions;
        Quaternion[] pinRotations;
        bool[] standing;
        string session, source;
        long sequence = -1, lastArrival;
        double sensorTime = -1;
        float rollStarted, stillStarted = -1, resultUntil, readySince = -1;
        bool newRack;
        Vector3 cameraHome;
        Quaternion cameraRotation;
        bool startingServices;
        static double Age(long ticks) => ticks == 0 ? double.MaxValue : (System.Diagnostics.Stopwatch.GetTimestamp() - ticks) / (double)System.Diagnostics.Stopwatch.Frequency;

        void Start()
        {
            if (renderOnly) return;
            Application.runInBackground = true;
            pinPositions = new Vector3[pins.Length]; pinRotations = new Quaternion[pins.Length]; standing = new bool[pins.Length];
            for (int i = 0; i < pins.Length; i++) { pinPositions[i] = pins[i].position; pinRotations[i] = pins[i].rotation; }
            if (spectator) { cameraHome = spectator.transform.position; cameraRotation = spectator.transform.rotation; }
            SensorHub.Ensure().MotionFor(motionUrl);
            RestartRound();
            if (startServices) Connect();
        }
        public void Connect()
        {
            if (!renderOnly && !startingServices) { startingServices = true; StartCoroutine(StartServices()); }
        }
        IEnumerator StartServices()
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string script = Path.Combine(root, "scripts/start_bowling_session.sh");
            if (!File.Exists(script)) { Connection = "Open Bowling Motion on your Mac."; startingServices = false; yield break; }
            System.Diagnostics.Process process = null;
            try { process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = "/bin/zsh", Arguments = "\"" + script + "\"", UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception) { Connection = "Open Bowling Motion on your Mac."; }
            if (process != null) { while (!process.HasExited) yield return null; process.Dispose(); }
#endif
            startingServices = false;
            yield break;
        }
        void Update()
        {
            if (renderOnly) return;
            while (client != null && client.Take(out string text, out long ticks)) ReceiveMotion(text, ticks);
            if (!MotionReady)
            {
                if (lastArrival != 0) Connection = "Reconnecting Bowling Motion…";
                if (Swing.Calibrated) Swing.Reset();
                readySince = -1;
                if (Phase == "Ready") Phase = "Setup";
            }
            if (Paused) return;
            if (Phase == "Setup" && MotionReady && Swing.Calibrated)
            {
                if (readySince < 0) readySince = Time.unscaledTime;
                if (Time.unscaledTime - readySince >= .6f) { Phase = "Ready"; Swing.Rearm(); }
            }
            if (Phase == "Rolling")
            {
                bool moving = ball.linearVelocity.sqrMagnitude > .01f;
                for (int i = 0; i < pins.Length; i++) if (standing[i] && (pins[i].linearVelocity.sqrMagnitude > .015f || pins[i].angularVelocity.sqrMagnitude > .04f)) moving = true;
                if (moving) stillStarted = -1; else if (stillStarted < 0) stillStarted = Time.time;
                if (Time.time - rollStarted > 12 || ball.position.y < -2 ||
                    ball.position.z > HeadPinZ + 1.7f && Time.time - rollStarted > 5 ||
                    stillStarted >= 0 && Time.time - stillStarted > 1.2f && Time.time - rollStarted > 2) FinishRoll();
            }
            if (Phase == "Result" && Time.time >= resultUntil) PrepareBall(newRack);
            if (aimLine)
            {
                aimLine.enabled = Phase == "Ready";
                Vector3 start = new(0, .012f, .3f);
                aimLine.SetPosition(0, start);
                aimLine.SetPosition(1, start + Quaternion.Euler(0, Swing.Aim, 0) * Vector3.forward * 4.2f);
            }
        }

        // Same entry point used by the websocket and deterministic editor fixtures.
        public void ReceiveMotion(string text, long arrival)
        {
            if (renderOnly || Age(arrival) > .25) return;
            try
            {
                var p = JObject.Parse(text);
                if ((string)p["playerId"] != "patient") return;
                if ((string)p["type"] == "bowling.disconnected") { LoseMotion(); return; }
                if ((string)p["type"] != "bowling.motion") return;
                string nextSession = (string)p["sessionId"], nextSource = (string)p["sourceId"];
                if (string.IsNullOrEmpty(nextSession) || nextSource != "Left" && nextSource != "Right") return;
                long seq = (long)p["sequence"]; double time = (double)p["sensorTime"];
                var q = p["quaternion"] as JArray; var r = p["rotationRate"] as JArray;
                if (q?.Count != 4 || r?.Count != 3 || !double.IsFinite(time) || seq < 0) return;
                var attitude = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
                var rate = new Vector3((float)r[0], (float)r[1], (float)r[2]);
                float norm = Quaternion.Dot(attitude, attitude);
                if (!float.IsFinite(norm) || norm < .5f || norm > 1.5f || !float.IsFinite(rate.sqrMagnitude) || rate.magnitude > 100) return;
                if (session != nextSession || source != nextSource)
                { session = nextSession; source = nextSource; sequence = -1; sensorTime = -1; Swing.Reset(); readySince = -1; if (Phase == "Ready") Phase = "Setup"; }
                if (seq <= sequence || time <= sensorTime) return;
                sequence = seq; sensorTime = time; lastArrival = arrival; Connection = "AirPod connected";
                if (Paused) return;
                if (Swing.Sample(attitude, rate, time, Phase == "Ready", out float aim, out float power)) Launch(aim, power);
                else if (Phase == "Ready" && !Swing.Calibrated) { Phase = "Setup"; readySince = -1; }
            }
            catch (Exception) { /* Ignore malformed packets without advancing freshness. */ }
        }
        void LoseMotion() { lastArrival = 0; Swing.Reset(); readySince = -1; Connection = "Reconnecting AirPods…"; if (Phase == "Ready") Phase = "Setup"; }
        public void Recalibrate() { if (!CanRecalibrate) return; Swing.Reset(); readySince = -1; Phase = "Setup"; }
        public void SetPaused(bool value)
        {
            if (renderOnly) return;
            Paused = value; Time.timeScale = value ? 0 : 1;
            Swing.Rearm();
            if (!value && CanRecalibrate) Recalibrate();
        }
        public bool Launch(float aim, float power)
        {
            if (renderOnly || Phase != "Ready" || Paused || !float.IsFinite(aim) || !float.IsFinite(power)) return false;
            LastAim = Mathf.Clamp(aim, -8, 8); LastPower = Mathf.Clamp01(power);
            Phase = "Rolling"; rollStarted = Time.time; stillStarted = -1;
            if (avatar) avatar.Release(LastAim);
            Vector3 velocity = Quaternion.Euler(0, LastAim, 0) * Vector3.forward * BowlingSwing.BallSpeed(LastPower);
            ball.isKinematic = false; ball.linearVelocity = velocity;
            ball.angularVelocity = Vector3.Cross(Vector3.up, velocity) / BallRadius;
            for (int i = 0; i < pins.Length; i++) if (standing[i]) { pins[i].isKinematic = false; pins[i].WakeUp(); }
            return true;
        }
        void FinishRoll()
        {
            int knocked = 0;
            for (int i = 0; i < pins.Length; i++)
            {
                if (standing[i] && (Vector3.Dot(pins[i].transform.up, Vector3.up) < .75f || pins[i].position.y < -.08f ||
                    Vector3.ProjectOnPlane(pins[i].position - pinPositions[i], Vector3.up).magnitude > .18f))
                { standing[i] = false; knocked++; }
                Freeze(pins[i]);
            }
            Freeze(ball); LastPins = knocked;
            rolls++;
            int before = Score.Standing;
            newRack = Score.Add(knocked);
            Result = knocked == 10 ? "Strike!" : knocked == before ? "Spare!" : knocked == 0 ? "Try again. You've got this." : $"{knocked} pins. Nice roll!";
            Phase = Score.Complete ? "Complete" : "Result"; resultUntil = Time.time + 2.7f;
            if (Score.Complete) CompleteGame();
        }
        void CompleteGame() => ReportGame(true);
        void ReportGame(bool completed)
        {
            if (gameReported) return;
            gameReported = true;
            string id = Guid.NewGuid().ToString();
            var marks = new string[10];
            for (int f = 0; f < 10; f++) marks[f] = Score.Marks(f);
            // Every roll counts in bowling, so attempted equals valid. The figure that travels across
            // activities is how much the person moved; the score stays in the payload.
            var subjects = new[] { new ActivitySubject {
                SubjectId = "patient", Attempted = rolls, Valid = rolls,
                MetricName = "score", MetricValue = Score.Total, MetricUnit = "pins",
            } };
            int lost = (SensorHub.Instance?.MotionReconnectsFor(motionUrl) ?? 0) - reconnectsAtStart;
            Completed?.Invoke(id);
            postingGame = true;
            StartCoroutine(ActivityRecorder.Send(bridge, ActivityId, id, gameStartedUtc, subjects,
                lost, "bowling.game", new { total = Score.Total, rolls, frames = marks },
                _ => postingGame = false, completed));
        }
        public void RestartRound()
        {
            if (renderOnly || pinPositions == null) return;
            Time.timeScale = 1; Paused = false; Score = new BowlingScore(); LastAim = LastPower = 0; Result = "";
            gameStartedUtc = DateTime.UtcNow; gameReported = false; rolls = 0;
            reconnectsAtStart = SensorHub.Instance?.MotionReconnectsFor(motionUrl) ?? 0;
            Swing.Reset(); readySince = -1; PrepareBall(true);
        }
        void PrepareBall(bool fullRack)
        {
            Freeze(ball); ball.position = new Vector3(0, BallRadius + .005f, .1f); ball.rotation = Quaternion.identity;
            for (int i = 0; i < pins.Length; i++)
            {
                Freeze(pins[i]);
                if (fullRack)
                {
                    standing[i] = true; pins[i].gameObject.SetActive(true);
                    pins[i].position = pinPositions[i]; pins[i].rotation = pinRotations[i];
                    pins[i].transform.SetPositionAndRotation(pinPositions[i], pinRotations[i]);
                }
                pins[i].gameObject.SetActive(standing[i]);
            }
            Swing.Rearm(); Phase = Swing.Calibrated && MotionReady ? "Ready" : "Setup";
            if (avatar) avatar.Prepare();
        }
        static void Freeze(Rigidbody body) { if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; } body.isKinematic = true; }
        void LateUpdate()
        {
            if (renderOnly || !spectator) return;
            // Let the follow-through read before following the ball down the lane.
            float follow = (Phase == "Rolling" || Phase == "Result") && Time.time - rollStarted > 1.1f ? Mathf.Clamp(ball.position.z - 6, 0, HeadPinZ - 4) : 0;
            Vector3 target = cameraHome + Vector3.forward * follow;
            spectator.transform.position = Vector3.Lerp(spectator.transform.position, target, 1 - Mathf.Exp(-3 * Time.unscaledDeltaTime));
            spectator.transform.rotation = cameraRotation;
        }
        void OnDestroy() { if (!renderOnly) Time.timeScale = 1; }   // the hub owns the motion channel
    }
}
