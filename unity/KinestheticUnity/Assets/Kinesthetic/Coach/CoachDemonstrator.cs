using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Coach
{
    // A seated trainer who demonstrates the prescribed exercise for the patient to follow.
    // Movement shape comes from real rehab motion capture (UI-PRMD, median of 90 repetitions); the peak is scaled
    // to the physician's active plan target, with the plan's hold at the top. The coach always shows ideal form:
    // the recorded trunk sway is deliberately not reproduced. Facing the patient, the coach mirrors the side.
    public sealed class CoachDemonstrator : MonoBehaviour
    {
        public Transform model;
        public string motionName = "shoulder_scaption";
        public string planUrl = "http://127.0.0.1:8766/api/plans/active";
        public bool mirrorPatient = true;          // patient raises right → coach (facing them) raises left
        public string patientSide = "right";
        public float targetDeg = 80, holdSeconds = .4f, tempo = 1.4f, restSeconds = 1.2f;
        public bool demonstrating = true;
        public string exerciseUrl = "ws://127.0.0.1:8766/exercise?role=viewer";
        public float maxWaitAtTopSeconds = 2.5f, maxWaitForPatientSeconds = 5f;
        [Tooltip("Reps the coach shows at the start of a session before handing over to the patient.")]
        public int demoReps = 2;
        public float handOffSeconds = 1.8f;
        [Header("Life")]
        public Texture2D[] eyeTextures;            // open, half, closed
        public Renderer eyeRenderer;
        public float leanForwardDeg = 6;
        Transform spine3, spine4; Material eyeMaterial; Transform patientHead;
        float nextBlink, blinkStart = -10, nodStart = -10; Quaternion headLook = Quaternion.identity;

        JObject motion; float[] elev01, plane, elbow, shrug; float restDeg = 10;
        Transform clavicle, upper, fore, wrist, hip, neck, head;
        Transform[] legs;
        Vector3 up, right, forward;                // body frame captured at rest (torso stays still)
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> rest = new();
        float cycleStart;
        public float CurrentElevationDeg { get; private set; }
        public string Phase { get; private set; } = "rest";
        public string Mode => mode.ToString();

        // Loop: showing the movement before a session. Demo: at the start of a session, the prescribed rep shown
        // demoReps times while the patient sits still (their calibration happens meanwhile). HandOff: the coach
        // turns and points at the mirror — "your turn, watch yourself". Calibrating: sitting still with the patient.
        // Lead: one rep at a time — raise, hold until the patient reaches the target, lower, wait for their rep.
        // Celebrate: both arms up when the prescribed set is done.
        enum CoachMode { Loop, Demo, HandOff, Calibrating, Lead, Celebrate }
        enum Stage { Raise, Hold, Lower, Wait }
        CoachMode mode = CoachMode.Loop; Stage stage = Stage.Wait;
        float stageStart, patientReachedAt = -1, patientRepDoneAt = -1, modeStart; bool trackingPaused;
        Kinesthetic.Rehab.ExerciseClient exercise;
        Transform otherClavicle, otherUpper, otherFore, otherWrist;
        bool calibratedDuringDemo;
        Kinesthetic.Rehab.MirrorPanel mirror;
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> relaxedOther = new();

        void Start()
        {
            Application.runInBackground = true;   // keep leading while the camera page has focus
            var text = Resources.Load<TextAsset>("CoachMotions/" + motionName);
            motion = JObject.Parse(text.text);
            elev01 = motion["elevation01"].Select(v => (float)v).ToArray();
            plane = motion["planeDeg"].Select(v => (float)v).ToArray();
            elbow = motion["elbowFlexDeg"].Select(v => (float)v).ToArray();
            shrug = motion["shrugMm"].Select(v => (float)v).ToArray();
            BindBones();
            StartCoroutine(LoadPlan());
            cycleStart = Time.time;
            exercise = new Kinesthetic.Rehab.ExerciseClient(exerciseUrl);
            spine3 = Bone("spine_3"); spine4 = Bone("spine_4");
            foreach (var t in new[] { spine3, spine4, neck, head }) if (t) rest[t] = t.localRotation;
            if (eyeRenderer) eyeMaterial = eyeRenderer.material;
            var patient = FindObjectsByType<PoseRig>(FindObjectsSortMode.None).FirstOrDefault(r => r.seated);
            if (patient) patientHead = patient.avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
            nextBlink = Time.time + 2;
        }

        Transform Bone(string n) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
        void BindBones()
        {
            string s = mirrorPatient ? (patientSide == "right" ? "l" : "r") : (patientSide == "right" ? "r" : "l");
            clavicle = Bone("clavicle_" + s); upper = Bone("arm_" + s + "1"); fore = Bone("arm_" + s + "2"); wrist = Bone("wrist_" + s);
            hip = Bone("hip"); neck = Bone("neck"); head = Bone("head");
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) rest[t] = t.localRotation;
            SeatLegs();
            var o = s == "l" ? "r" : "l";
            RelaxOtherArm(o);
            otherClavicle = Bone("clavicle_" + o); otherUpper = Bone("arm_" + o + "1"); otherFore = Bone("arm_" + o + "2"); otherWrist = Bone("wrist_" + o);
            foreach (var t in new[] { otherClavicle, otherUpper, otherFore, otherWrist }) if (t) relaxedOther[t] = t.localRotation;
            foreach (var t in new[] { clavicle, upper, fore, wrist }) rest[t] = t.localRotation;
            up = (neck.position - hip.position).normalized;
            var outward = (upper.position - Bone(s == "l" ? "arm_r1" : "arm_l1").position).normalized;
            right = Vector3.ProjectOnPlane(outward, up).normalized;                     // toward the demonstrating arm
            var toe = Bone("toe_l").position - Bone("ankle_l").position;
            forward = Vector3.ProjectOnPlane(toe, up).normalized;                      // feet point where the body faces
        }

        // Seated: thighs forward, shins down. Legs are posed once and never animated.
        void SeatLegs()
        {
            var down = -(Bone("neck").position - Bone("hip").position).normalized;
            var fwd = Vector3.ProjectOnPlane(Bone("toe_l").position - Bone("ankle_l").position, down).normalized;
            foreach (var s in new[] { "l", "r" })
            {
                Aim(Bone("leg_" + s + "1"), Bone("leg_" + s + "2"), fwd);
                Aim(Bone("leg_" + s + "2"), Bone("ankle_" + s), down);
            }
        }

        // The arm that is not demonstrating rests at the side with a soft elbow instead of the model's T-pose.
        void RelaxOtherArm(string s)
        {
            var up = (Bone("neck").position - Bone("hip").position).normalized;
            var fwd = Vector3.ProjectOnPlane(Bone("toe_l").position - Bone("ankle_l").position, up).normalized;
            var arm1 = Bone("arm_" + s + "1"); var arm2 = Bone("arm_" + s + "2"); var hand = Bone("wrist_" + s);
            var outward = Vector3.ProjectOnPlane(arm1.position - Bone(s == "l" ? "arm_r1" : "arm_l1").position, up).normalized;
            Aim(arm1, arm2, (-up + outward * .16f + fwd * .12f).normalized);
            Aim(arm2, hand, (-up * .8f + fwd * .6f).normalized);
        }

        static void Aim(Transform joint, Transform child, Vector3 dir)
        {
            if (!joint || !child) return;
            joint.rotation = Quaternion.FromToRotation(child.position - joint.position, dir) * joint.rotation;
        }

        IEnumerator LoadPlan()
        {
            using var request = UnityWebRequest.Get(planUrl);
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;   // keep the defaults offline
            var plan = JObject.Parse(request.downloadHandler.text); var e = plan["exercise"];
            targetDeg = (float?)e?["targetDeg"] ?? targetDeg;
            holdSeconds = ((float?)e?["holdMs"] ?? holdSeconds * 1000) / 1000f;
            var side = (string)e?["side"] ?? patientSide;
            if (side != patientSide) { patientSide = side; foreach (var t in new[] { clavicle, upper, fore, wrist }) t.localRotation = rest[t]; BindBones(); }
        }

        static float Sample(float[] curve, float u)
        {
            float x = Mathf.Clamp01(u) * (curve.Length - 1); int i = Mathf.Min((int)x, curve.Length - 2);
            return Mathf.Lerp(curve[i], curve[i + 1], x - i);
        }

        void ReadSession()
        {
            while (exercise != null && exercise.Take(out var text))
            {
                JObject m; try { m = JObject.Parse(text); } catch (Exception) { continue; }
                var type = (string)m["type"]; var e = m["payload"] as JObject;
                if (type == "exercise.started") { SetMode(demoReps > 0 ? CoachMode.Demo : CoachMode.Calibrating); calibratedDuringDemo = false; StartCoroutine(LoadPlan()); }
                else if (type == "exercise.summary")
                {   // Celebrate real work only; an empty or abandoned session just returns to demonstrating.
                    if (((int?)m["payload"]?["valid"] ?? 0) > 0) SetMode(CoachMode.Celebrate); else { SetMode(CoachMode.Loop); cycleStart = Time.time + 1f; }
                }
                else if (type == "exercise.event" && e != null)
                    switch ((string)e["type"])
                    {
                        case "calibration.complete":
                            // During the demonstration the patient is only getting ready; the lead starts after the handoff.
                            if (mode is CoachMode.Demo or CoachMode.HandOff) calibratedDuringDemo = true;
                            else { SetMode(CoachMode.Lead); BeginStage(Stage.Raise); }
                            break;
                        case "target.reached": patientReachedAt = Time.time; break;
                        case "rep.completed": patientRepDoneAt = Time.time; if (e["valid"]?.Value<bool>() == true) nodStart = Time.time; break;
                        case "tracking.lost": trackingPaused = true; break;
                        case "tracking.recovered": trackingPaused = false; break;
                    }
            }
        }
        void SetMode(CoachMode m) { mode = m; modeStart = Time.time; stage = Stage.Wait; stageStart = Time.time; }
        void BeginStage(Stage st) { stage = st; stageStart = Time.time; if (st == Stage.Raise) { patientReachedAt = -1; patientRepDoneAt = -1; } }

        void LateUpdate()
        {
            if (upper == null) return;
            ReadSession();
            // A script reload clears the captured rest pose; skip the frame rather than throw.
            foreach (var t in new[] { clavicle, upper, fore, wrist }) { if (!t || !rest.TryGetValue(t, out var r)) return; t.localRotation = r; }
            foreach (var kv in relaxedOther) kv.Key.localRotation = kv.Value;
            ApplyBodyLife();
            float rep = (float)motion["repSeconds"] * tempo;
            int peakIndex = Array.IndexOf(elev01, elev01.Max());
            float uPeak = peakIndex / (float)(elev01.Length - 1);
            float rise = rep * uPeak, lower = rep - rise, u = 1, now = Time.time, inStage = now - stageStart;

            switch (mode)
            {
                case CoachMode.Loop:
                {
                    float cycle = rise + holdSeconds + lower + restSeconds, t0 = demonstrating ? (now - cycleStart) % cycle : cycle;
                    if (t0 < rise) { u = t0 / rep; Phase = "raise"; }
                    else if (t0 < rise + holdSeconds) { u = uPeak; Phase = "hold"; }
                    else if (t0 < rise + holdSeconds + lower) { u = (t0 - holdSeconds) / rep; Phase = "lower"; }
                    else Phase = "rest";
                    break;
                }
                case CoachMode.Demo:
                {
                    float cycle = rise + holdSeconds + lower + restSeconds, t0 = now - modeStart;
                    if (t0 >= cycle * demoReps) { SetMode(CoachMode.HandOff); break; }
                    t0 %= cycle;
                    if (t0 < rise) { u = t0 / rep; Phase = "demo-raise"; }
                    else if (t0 < rise + holdSeconds) { u = uPeak; Phase = "demo-hold"; }
                    else if (t0 < rise + holdSeconds + lower) { u = (t0 - holdSeconds) / rep; Phase = "demo-lower"; }
                    else Phase = "demo-rest";
                    break;
                }
                case CoachMode.HandOff:
                    Phase = "handoff";
                    if (now - modeStart >= handOffSeconds)
                    {
                        if (calibratedDuringDemo) { SetMode(CoachMode.Lead); BeginStage(Stage.Raise); } else SetMode(CoachMode.Calibrating);
                    }
                    break;
                case CoachMode.Calibrating: Phase = "still"; break;
                case CoachMode.Celebrate:
                    Phase = "celebrate";
                    if (now - modeStart > 3.5f) { SetMode(CoachMode.Loop); cycleStart = now + 2f; }
                    break;
                case CoachMode.Lead:
                    switch (stage)
                    {
                        case Stage.Raise: u = Mathf.Min(inStage / rep, uPeak); Phase = "raise"; if (inStage >= rise) BeginStage(Stage.Hold); break;
                        case Stage.Hold:
                            u = uPeak; Phase = "hold";
                            // Hold with the patient: until they reach the target (plus the plan's hold), never frozen forever.
                            bool reached = patientReachedAt >= stageStart - rise && now - patientReachedAt >= holdSeconds;
                            if ((reached && inStage >= holdSeconds) || inStage >= maxWaitAtTopSeconds + holdSeconds) BeginStage(Stage.Lower);
                            break;
                        case Stage.Lower: u = uPeak + Mathf.Min(inStage / rep, 1 - uPeak); Phase = "lower"; if (inStage >= lower) BeginStage(Stage.Wait); break;
                        case Stage.Wait:
                            Phase = trackingPaused ? "paused" : "wait";
                            // Next rep once the patient has finished theirs (or after a patient pause), never while tracking is lost.
                            bool done = patientRepDoneAt >= stageStart - rep;
                            if (!trackingPaused && inStage >= .8f && (done || inStage >= maxWaitForPatientSeconds)) BeginStage(Stage.Raise);
                            break;
                    }
                    break;
            }

            // Peak lands just inside the prescribed band so the demonstration always meets the plan.
            float peakDeg = targetDeg + 5;
            CurrentElevationDeg = restDeg + Sample(elev01, u) * (peakDeg - restDeg);
            float planeDeg = Sample(plane, u), elbowDeg = Sample(elbow, u), shrugMm = Mathf.Max(0, Sample(shrug, u));
            if (mode == CoachMode.Celebrate)
            {
                // Both arms overhead in a V, Wii Fit style, rising over the first half second.
                float k = Mathf.SmoothStep(0, 1, Mathf.Min(1, (now - modeStart) / .5f));
                CurrentElevationDeg = Mathf.Lerp(restDeg, 160, k); planeDeg = 15; elbowDeg = 12; shrugMm = 0;
                var outwardOther = Vector3.ProjectOnPlane(otherUpper.position - upper.position, up).normalized;
                PoseArm(otherUpper, otherFore, otherWrist, outwardOther, CurrentElevationDeg, planeDeg, elbowDeg);
            }
            if (clavicle) clavicle.rotation = Quaternion.AngleAxis(-Mathf.Sign(Vector3.Dot(Vector3.Cross(forward, right), up)) * Mathf.Min(shrugMm * .12f, 6f), forward) * clavicle.rotation;
            PoseArm(upper, fore, wrist, right, CurrentElevationDeg, planeDeg, elbowDeg);
            if (mode == CoachMode.HandOff) PointAtMirror();
            ApplyHeadAndEyes();
        }

        /// Where the patient should look now: the mirror window, if the scene has one.
        public Vector3? MirrorPosition => (mirror ??= FindAnyObjectByType<Kinesthetic.Rehab.MirrorPanel>()) ? mirror.WindowPosition : null;
        public bool HandingOff => mode == CoachMode.HandOff;
        public bool Demonstrating => mode == CoachMode.Demo;

        // The resting arm rises to point across at the mirror, easing in over the first half second.
        void PointAtMirror()
        {
            if (MirrorPosition is not Vector3 target || !otherUpper || !otherFore || !otherWrist) return;
            float k = Mathf.SmoothStep(0, 1, Mathf.Min(1, (Time.time - modeStart) / .5f));
            var toward = (target - otherUpper.position).normalized;
            var relaxed = (otherFore.position - otherUpper.position).normalized;
            var arm = Vector3.Slerp(relaxed, toward, k);
            Aim(otherUpper, otherFore, arm); Aim(otherFore, otherWrist, arm);
        }

        // Breathing and an engaged forward lean: small, slow spine motion so the coach never looks frozen.
        void ApplyBodyLife()
        {
            foreach (var t in new[] { spine3, spine4, neck, head }) if (t && rest.TryGetValue(t, out var r)) t.localRotation = r;
            float breath = Mathf.Sin(Time.time * 1.45f);
            var side = Vector3.Cross(up, forward).normalized;
            if (spine3) spine3.rotation = Quaternion.AngleAxis(leanForwardDeg * .6f + breath * .7f, side) * spine3.rotation;
            if (spine4) spine4.rotation = Quaternion.AngleAxis(leanForwardDeg * .4f - breath * .9f, side) * spine4.rotation;
        }

        // Head follows the patient (clamped, smoothed), nods when a rep counts; eyes blink with the trainer's own textures.
        void ApplyHeadAndEyes()
        {
            if (head && neck && patientHead)
            {
                // Handing off, the coach looks at the mirror with the patient; otherwise at the patient.
                var lookAt = mode == CoachMode.HandOff && MirrorPosition is Vector3 m ? m : patientHead.position;
                var to = lookAt - head.position; var flat = Vector3.ProjectOnPlane(to, up);
                float yaw = Mathf.Clamp(Vector3.SignedAngle(forward, flat, up), -55, 55);
                float pitch = Mathf.Clamp(-Vector3.SignedAngle(flat, to, Vector3.Cross(up, flat)), -20, 20);
                float nod = Time.time - nodStart < .6f ? Mathf.Sin((Time.time - nodStart) / .6f * Mathf.PI) * 12 : 0;
                var side = Vector3.Cross(up, forward).normalized;
                var target = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch + nod, side);
                headLook = Quaternion.Slerp(headLook, target, 1 - Mathf.Exp(-Time.deltaTime * 5));
                headLook.ToAngleAxis(out var angle, out var axis);
                neck.rotation = Quaternion.AngleAxis(angle * .4f, axis) * neck.rotation;
                head.rotation = Quaternion.AngleAxis(angle * .6f, axis) * head.rotation;
            }
            if (eyeMaterial && eyeTextures != null && eyeTextures.Length == 3)
            {
                if (Time.time >= nextBlink) { blinkStart = Time.time; nextBlink = Time.time + UnityEngine.Random.Range(2.8f, 5.2f); }
                float b = Time.time - blinkStart;
                eyeMaterial.SetTexture("_BaseMap", b < .05f ? eyeTextures[1] : b < .11f ? eyeTextures[2] : b < .16f ? eyeTextures[1] : eyeTextures[0]);
            }
        }

        void PoseArm(Transform arm1, Transform arm2, Transform hand, Vector3 outward, float elevDeg, float planeDeg, float elbowDeg)
        {
            float e = elevDeg * Mathf.Deg2Rad, p = Mathf.Clamp(planeDeg, 0, 60) * Mathf.Deg2Rad;
            var armDir = (-up * Mathf.Cos(e) + (outward * Mathf.Cos(p) + forward * Mathf.Sin(p)) * Mathf.Sin(e)).normalized;
            Aim(arm1, arm2, armDir);
            // Soft elbow: bend the forearm toward the front of the body by the recorded amount.
            var axis = Vector3.Cross(armDir, forward); if (axis.sqrMagnitude < 1e-4f) axis = Vector3.Cross(armDir, up);
            var a = Quaternion.AngleAxis(elbowDeg, axis.normalized) * armDir; var b = Quaternion.AngleAxis(-elbowDeg, axis.normalized) * armDir;
            Aim(arm2, hand, Vector3.Dot(a, forward) >= Vector3.Dot(b, forward) ? a : b);
        }

        void OnDestroy() => exercise?.Dispose();

        // The coach joins the rehab scene automatically, beside the patient, without editing that scene.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void JoinRehabScene()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The headset's QuestRehab gets the coach too; RehabStateClient then drives it from the Mac's pose.
            if (scene.name is not ("Rehab" or "QuestRehab") || FindAnyObjectByType<CoachDemonstrator>()) return;
            var prefab = Resources.Load<GameObject>("Coach/TrainerCoach"); if (!prefab) return;
            var patient = FindAnyObjectByType<PoseRig>();
            var coach = Instantiate(prefab);
            // Placed from the patient, not a camera: ahead and to their right, facing them, the same spot in third
            // person, first person and the headset. (The Mii faces local -Z; its anatomical right is local -X.)
            var basePos = patient ? (patient.transform.parent ? patient.transform.parent.position : patient.transform.position) : Vector3.zero;
            var forward = patient ? Vector3.ProjectOnPlane(patient.transform.TransformDirection(Vector3.back), Vector3.up).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            coach.transform.position = basePos + right * 1.0f + forward * 1.35f;
            coach.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(basePos - coach.transform.position, Vector3.up), Vector3.up);
            // On the Mac the coach talks: Alex, the ElevenLabs voice PT. The headset renders the Mac's coach instead.
            if (scene.name == "Rehab") coach.AddComponent<CoachVoice>();
        }
    }
}
