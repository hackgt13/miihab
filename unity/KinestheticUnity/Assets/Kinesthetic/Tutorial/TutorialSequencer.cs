using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Tutorial
{
    /// First-run onboarding: Alex introduces herself and demonstrates two arm lifts (right then left).
    /// After each demo the tutorial goes silent and waits for the patient to mimic the motion
    /// (detected via IMU rotation rate from either AirPod on the motion relay, with head-movement fallback).
    /// If no motion is detected within the timeout the coach repeats the demo once, then carries on, so the
    /// tutorial always finishes. The last step asks the patient to look to the right — something every
    /// patient here can do; many use a wheelchair.
    public sealed class TutorialSequencer : MonoBehaviour
    {
        [Header("Coach")]
        public Coach.CoachDemonstrator coach;

        [Header("Navigate step")]
        [Tooltip("A GazeDwell target. Wire in the scene; the sequencer activates it at the right moment.")]
        public GameObject navigateTarget;

        [Header("Timing")]
        [Tooltip("Seconds into the intro audio when the subtitle swaps to 'Watch me' and the demo starts.")]
        public float demoStartDelay = 10f;
        public float demoDuration = 16f;
        public float tryTimeout = 15f;
        public float lookTimeout = 8f;
        public float navigateTimeout = 8f;
        [Tooltip("Demos repeated in a Try step before the tutorial carries on without a detected attempt.")]
        public int maxRepeats = 1;
        public float completePause = 3f;
        public float autoStartSeconds = 2f;

        [Header("Motion detection")]
        [Tooltip("IMU rotation rate magnitude (rad/s) that counts as intentional movement.")]
        public float imuThreshold = 1.0f;
        [Tooltip("How long (seconds) IMU motion must persist to count as a real attempt.")]
        public float imuSustain = 0.4f;
        [Tooltip("Camera position shift (m) fallback when IMU is unavailable.")]
        public float moveThreshold = 0.08f;
        [Tooltip("Head turn (degrees of yaw) that counts as looking to the side.")]
        public float lookThreshold = 25f;

        [Header("IMU")]
        // Either AirPod: the wrist strap (the arm lift's own mount) or the club AirPod in a handle.
        public string wristMotionUrl = "ws://127.0.0.1:8767/bowling-motion?role=viewer";
        public string clubMotionUrl = Activities.SensorHub.DefaultMotionUrl;

        //                                  0             1      2          3         4         5        6          7         8
        enum Step { WaitToStart, Intro, DemoRight, TryRight, DemoLeft, TryLeft, Navigate, Look, Complete }
        Step current = Step.WaitToStart;
        float stepStart;
        string subtitle = "";
        Vector3 tryOrigin;
        Quaternion tryRotOrigin;
        Quaternion lookOrigin;
        int repeats;
        GUIStyle captionStyle, captionBg;

        // Repeat sub-state: coach re-demos inside a Try step, then resumes waiting.
        bool repeating;
        float repeatEnd;

        // IMU motion tracking
        Golf.GolfMotionClient[] motions;
        float imuActiveStart = -1;
        bool imuAvailable;

        static readonly string[] Subtitles =
        {
            "",
            "Hi, I'm Alex, a virtual clinician.\nIn a moment you're about to enter physical therapy at home.\nI'll be there the whole way, so don't worry.",
            "Before we begin, I want you to get used to the controls.\nWatch me, and copy.",
            "Your turn. Go ahead and try it.",
            "Good! Now the other side.",
            "Your turn.",
            "Perfect! Now head over to this button over here.",
            "Now turn your head and look to your right.",
            "Great job! You're all set. Let's get started.",
        };

        const string CompletePref = "RehabMii.TutorialComplete";
        public static bool IsComplete => PlayerPrefs.GetInt(CompletePref, 0) != 0;

        /// The intro plays once, the first time the app opens on the menu; after that it is the Getting Started
        /// tile. Not on the headset, which follows whatever the Mac is running.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void PlayOnFirstLaunch()
        {
            if (IsComplete || Application.platform == RuntimePlatform.Android) return;
            if (SceneManager.GetActiveScene().name != "MainMenu") return;
            if (Application.CanStreamedLevelBeLoaded("Tutorial")) SceneManager.LoadScene("Tutorial");
        }

        void Start()
        {
            SetupScene();
            SetupIMU();
            if (navigateTarget) navigateTarget.SetActive(false);
            current = Step.WaitToStart;
            stepStart = Time.time;
            subtitle = Subtitles[0];
        }

        void SetupIMU()
        {
            Activities.SensorHub.Ensure();
            motions = new[] { Activities.SensorHub.Instance.MotionFor(wristMotionUrl), Activities.SensorHub.Instance.MotionFor(clubMotionUrl) };
        }

        void SetupScene()
        {
            bool studio = GameObject.Find("Movement studio");
            foreach (var existing in FindObjectsByType<Coach.CoachDemonstrator>(FindObjectsSortMode.None))
                Destroy(existing.gameObject);
            coach = null;

            var prefab = Resources.Load<GameObject>("Coach/TrainerCoach");
            if (prefab)
            {
                var go = Instantiate(prefab);
                go.name = "Coach";
                // The patient sits at the origin facing +Z — in the studio, towards the windows and the garden —
                // and Alex sits across from them, facing back.
                go.transform.position = new Vector3(0, 0, studio ? 2.2f : 2.5f);
                go.transform.rotation = Quaternion.Euler(0, 180, 0);
                coach = go.GetComponent<Coach.CoachDemonstrator>();
                coach.demonstrating = false;

                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.enabled = true;

                if (!go.GetComponent<Coach.CoachVoice>())
                    go.AddComponent<Coach.CoachVoice>();

                Debug.Log($"[Tutorial] Coach spawned. Renderers: {go.GetComponentsInChildren<Renderer>(true).Length}, " +
                          $"Bones: {go.GetComponentsInChildren<Transform>(true).Length}");
            }
            else
            {
                Debug.LogError("[Tutorial] Coach prefab not found at Resources/Coach/TrainerCoach. " +
                               "Check Assets/Kinesthetic/Coach/Resources/Coach/TrainerCoach.prefab exists.");
            }

            var cam = Camera.main;
            if (cam)
            {
                // The patient's seated eyes, looking at Alex.
                cam.transform.position = new Vector3(0, 1.03f, 0);
                cam.transform.rotation = Quaternion.Euler(5, 0, 0);
                cam.fieldOfView = 50;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 200;
                cam.backgroundColor = studio ? Palette.Cerulean10 : Palette.Prussian80;
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            if (studio) return;   // the studio brings its own floor and light
            if (!FindAnyObjectByType<Light>())
            {
                var lightGo = new GameObject("Tutorial Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Palette.Sand00;
                light.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(45, 30, 0);
            }

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(3, 1, 3);
            var floorShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (floorShader)
            {
                var mat = new Material(floorShader) { color = Palette.Prussian70 };
                floor.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        void EnterStep(Step step)
        {
            current = step;
            stepStart = Time.time;
            subtitle = Subtitles[(int)step];
            repeating = false;
            repeats = 0;
            imuActiveStart = -1;

            switch (step)
            {
                case Step.Intro:
                    // One continuous TTS call speaks the full intro. Coach stays still during the greeting;
                    // the subtitle and demo swap happen on a timer in Update (demoStartDelay).
                    if (coach) coach.demonstrating = false;
                    var voice = Coach.CoachVoice.Instance;
                    if (voice && !voice.Connected)
                    {
                        var id = PlayerPrefs.GetString("RehabMii.TutorialPatientId", "");
                        if (string.IsNullOrEmpty(id)) { id = System.Guid.NewGuid().ToString(); PlayerPrefs.SetString("RehabMii.TutorialPatientId", id); PlayerPrefs.Save(); }
                        voice.Begin(id, "tutorial");
                    }
                    break;

                case Step.DemoRight:
                    // Subtitle already swapped by Update. Start the right arm demo — no cue needed,
                    // the audio is still playing from the single intro TTS call.
                    if (coach) { coach.SetMotion("arm_lift"); coach.SetSide("right"); coach.targetDeg = 80; coach.tempo = 2.0f; coach.holdSeconds = 2.0f; coach.restSeconds = 1.5f; coach.demonstrating = true; }
                    break;

                case Step.TryRight:
                    if (coach) coach.demonstrating = false;
                    SnapshotOrigin();
                    break;

                case Step.DemoLeft:
                    Coach.CoachVoice.Instance?.Cue("demo_left");
                    if (coach) { coach.SetSide("left"); coach.targetDeg = 80; coach.demonstrating = true; }
                    break;

                case Step.TryLeft:
                    if (coach) coach.demonstrating = false;
                    SnapshotOrigin();
                    break;

                case Step.Navigate:
                    Coach.CoachVoice.Instance?.Cue("navigate");
                    if (coach) coach.demonstrating = false;
                    if (navigateTarget) navigateTarget.SetActive(true);
                    break;

                case Step.Look:
                    Coach.CoachVoice.Instance?.Cue("look");
                    lookOrigin = Camera.main ? Camera.main.transform.rotation : Quaternion.identity;
                    break;

                case Step.Complete:
                    Coach.CoachVoice.Instance?.Cue("complete");
                    if (coach) coach.demonstrating = false;
                    PlayerPrefs.SetInt(CompletePref, 1);
                    PlayerPrefs.Save();
                    StartCoroutine(FinishTutorial());
                    break;
            }
        }

        void Update()
        {
            DrainIMU();
            float elapsed = Time.time - stepStart;

            switch (current)
            {
                case Step.WaitToStart:
                    // A cutscene: it begins on its own after a beat; Space begins it at once.
                    if (elapsed >= autoStartSeconds || (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame))
                        EnterStep(Step.Intro);
                    break;

                case Step.Intro:
                    // Swap subtitle and start demo mid-audio when "Before we begin" is spoken.
                    if (elapsed >= demoStartDelay) EnterStep(Step.DemoRight);
                    break;

                case Step.DemoRight:
                    if (elapsed >= demoDuration) EnterStep(Step.TryRight);
                    break;

                case Step.TryRight:
                    UpdateTryStep(Step.DemoLeft, "repeat_right", "right", 80);
                    break;

                case Step.DemoLeft:
                    if (elapsed >= demoDuration) EnterStep(Step.TryLeft);
                    break;

                case Step.TryLeft:
                    UpdateTryStep(Step.Navigate, "repeat_left", "left", 80);
                    break;

                case Step.Navigate:
                    // Without a gaze target (or a headset to gaze with) it moves on by itself.
                    if (elapsed >= (navigateTarget ? navigateTimeout : 4f)) EnterStep(Step.Look);
                    break;

                case Step.Look:
                    if (DetectLook() || elapsed >= lookTimeout) EnterStep(Step.Complete);
                    break;
            }
        }

        /// Shared logic for TryRight / TryLeft: wait for motion, repeat on timeout.
        void UpdateTryStep(Step nextStep, string repeatCue, string side, float targetDeg)
        {
            if (repeating)
            {
                if (Time.time >= repeatEnd)
                {
                    repeating = false;
                    if (coach) coach.demonstrating = false;
                    SnapshotOrigin();
                    imuActiveStart = -1;
                    stepStart = Time.time;
                }
                return;
            }

            if (DetectMotion())
            {
                EnterStep(nextStep);
            }
            else if (Time.time - stepStart >= tryTimeout && repeats >= maxRepeats)
            {
                // No attempt seen after a repeat (no AirPod, or a movement too gentle to register): carry on
                // rather than loop — the studio measures the real thing.
                Coach.CoachVoice.Instance?.Cue("keep_going");
                EnterStep(nextStep);
            }
            else if (Time.time - stepStart >= tryTimeout)
            {
                repeats++;
                Coach.CoachVoice.Instance?.Cue(repeatCue);
                if (coach) { coach.SetSide(side); coach.targetDeg = targetDeg; coach.demonstrating = true; }
                repeating = true;
                repeatEnd = Time.time + demoDuration;
            }
        }

        // ── IMU motion detection ──────────────────────────────────────────────

        /// Drain all pending IMU packets and track whether the patient is actively moving.
        void DrainIMU()
        {
            if (motions == null) return;
            foreach (var motion in motions)
            while (motion != null && motion.Take(out var text, out _))
            {
                imuAvailable = true;
                if (current is not (Step.TryRight or Step.TryLeft)) continue;
                if (repeating) continue;

                try
                {
                    var p = JObject.Parse(text);
                    var rate = p["rotationRate"];
                    if (rate == null) continue;
                    float rx = (float)rate[0], ry = (float)rate[1], rz = (float)rate[2];
                    float mag = Mathf.Sqrt(rx * rx + ry * ry + rz * rz);

                    if (mag >= imuThreshold)
                    {
                        if (imuActiveStart < 0) imuActiveStart = Time.time;
                    }
                    else
                    {
                        imuActiveStart = -1;
                    }
                }
                catch (System.Exception) { }
            }
        }

        bool DetectIMUMotion()
        {
            return imuActiveStart > 0 && Time.time - imuActiveStart >= imuSustain;
        }

        // ── Camera fallback ───────────────────────────────────────────────────

        void SnapshotOrigin()
        {
            var cam = Camera.main;
            if (!cam) return;
            tryOrigin = cam.transform.position;
            tryRotOrigin = cam.transform.rotation;
        }

        bool DetectCameraMotion()
        {
            if (!Camera.main) return false;
            var cam = Camera.main.transform;
            float posDelta = (cam.position - tryOrigin).magnitude;
            float rotDelta = Quaternion.Angle(cam.rotation, tryRotOrigin);
            return posDelta >= moveThreshold || rotDelta >= 10f;
        }

        // ── Combined detection ────────────────────────────────────────────────

        /// IMU if available, camera fallback otherwise.
        bool DetectMotion()
        {
            if (imuAvailable) return DetectIMUMotion();
            return DetectCameraMotion();
        }

        bool DetectLook()
        {
            if (!Camera.main) return false;
            float yaw = Mathf.DeltaAngle(lookOrigin.eulerAngles.y, Camera.main.transform.rotation.eulerAngles.y);
            return yaw >= lookThreshold;
        }

        public void OnNavigateComplete()
        {
            if (current == Step.Navigate) EnterStep(Step.Look);
        }

        IEnumerator FinishTutorial()
        {
            yield return new WaitForSeconds(completePause);
            if (Application.CanStreamedLevelBeLoaded("MainMenu"))
                SceneManager.LoadScene("MainMenu");
        }

        // ── Subtitles ─────────────────────────────────────────────────────────

        void OnGUI()
        {
            if (string.IsNullOrEmpty(subtitle)) return;

            if (captionStyle == null)
            {
                captionStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.026f),
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    padding = new RectOffset(24, 24, 14, 14),
                    normal = { textColor = Palette.Sand00 },
                };
                captionBg = new GUIStyle(GUI.skin.box)
                {
                    normal = { background = MakeTex(Translucent(Palette.Prussian80, .72f)) },
                };
            }

            float w = Screen.width * 0.75f;
            float h = captionStyle.CalcHeight(new GUIContent(subtitle), w) + 28;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height - h - Screen.height * 0.05f, w, h);
            GUI.Box(rect, GUIContent.none, captionBg);
            GUI.Label(rect, subtitle, captionStyle);
        }

        static Color Translucent(Color c, float a) { c.a = a; return c; }

        static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
