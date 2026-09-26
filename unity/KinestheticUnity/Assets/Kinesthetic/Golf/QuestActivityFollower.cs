using System;
using System.Collections;
using Kinesthetic.Activities;
using Kinesthetic.Menu;
using Kinesthetic.Shell;
using Kinesthetic.UI.Remote;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Golf
{
    /// Headset only: one Quest app holds the plaza and every activity, and follows the Mac. The Mac says
    /// where it is going (UiCue.Scene) and the headset goes the same way — through the venue's doorway in
    /// its own copy of the plaza when it is leaving the menu, behind a plain fade otherwise. The next scene
    /// is loading the whole time and is switched in on the covered frame, so the cut is never seen.
    ///
    /// The cue is the primary signal. The host streams (golf.state, bowling.state, rehab.state) remain the
    /// fallback for a headset that missed it — it was reconnecting, or the Mac was already mid-activity when
    /// it booted: whichever host is publishing is where the Mac is, so that is the scene it shows.
    ///
    /// Created on load on Android; `Ensure()` starts one anywhere else, for a Mac standing in as a headset.
    public sealed class QuestActivityFollower : MonoBehaviour
    {
        public const string MenuScene = "QuestMenu", MacMenuScene = "MainMenu";
        public const string GolfScene = "QuestGolf", BowlingScene = "QuestBowling", RehabScene = "QuestRehab";
        // Channel, the state type its host publishes, and the headset scene that renders it.
        static readonly (string path, string type, string scene)[] Activities =
            { ("/state", "golf.state", GolfScene), ("/bowling-state", "bowling.state", BowlingScene), ("/rehab-state", "rehab.state", RehabScene) };

        public static QuestActivityFollower Instance { get; private set; }

        LatestSocket[] sockets;
        float[] liveAt;
        AsyncOperation loading;
        bool transitioning;

        /// A walk or a fade is in progress; the verification watches this.
        public bool Transitioning => transitioning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            Ensure();
        }

        public static QuestActivityFollower Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("Quest activity follower");
            DontDestroyOnLoad(go);
            return go.AddComponent<QuestActivityFollower>();
        }

        /// The headset scene that stands for a Mac scene, or null when the headset has no copy of it.
        public static string HeadsetSceneFor(string macScene) =>
            macScene == MacMenuScene ? MenuScene : ActivityCatalog.QuestSceneOf(macScene);

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable() { UiCue.Received += Cue; }
        void OnDisable() { UiCue.Received -= Cue; }

        void Start()
        {
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            if (!config || string.IsNullOrEmpty(config.token)) return;   // cues still arrive; only the fallback needs the hosts
            string Url(string path) => $"ws://{config.host}:{config.port}{path}?role=client&token={Uri.EscapeDataString(config.token)}";
            sockets = Array.ConvertAll(Activities, a => new LatestSocket(Url(a.path)));
            liveAt = new float[Activities.Length];
            for (int i = 0; i < liveAt.Length; i++) liveAt[i] = -99;
        }

        void Cue(JObject message)
        {
            if ((string)message["kind"] != UiCue.Scene) return;
            string scene = HeadsetSceneFor((string)message["scene"]);
            string phase = (string)message["phase"], venue = (string)message["venue"];
            if (scene == null || transitioning || !Application.CanStreamedLevelBeLoaded(scene)) return;
            // `leave` is the walk itself. `arrive` alone means the walk was missed; just get there.
            if (phase == UiCue.Leave) StartCoroutine(Transition(scene, venue));
            else if (phase == UiCue.Arrive && SceneManager.GetActiveScene().name != scene) StartCoroutine(Transition(scene, null));
        }

        /// Into `scene`: through the doorway of `venue` if this scene has one, else behind a fade.
        public IEnumerator Transition(string scene, string venue)
        {
            transitioning = true;
            var fade = HeadFade.Ensure();
            var load = SceneManager.LoadSceneAsync(scene);
            load.allowSceneActivation = false;   // loads now, switches in on the covered frame
            loading = load;

            var portal = Portal.Find(venue);
            var cam = Camera.main;
            if (portal && cam)
            {
                // A tracked camera is moved by its rig's root — the seat anchor — never by its own transform.
                var mover = cam.GetComponent<TrackedPoseDriver>() ? cam.transform.root : cam.transform;
                var carousel = FindAnyObjectByType<PaneCarousel>();
                yield return PlazaApproach.Enter(portal, mover, fade, turnToward: false, carousel ? carousel.gameObject : null);
            }
            else yield return fade.CoverTo(1, PlazaApproach.ClearSeconds);

            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            yield return null;   // the new scene's camera exists; HeadFade has moved onto it
            yield return PlazaApproach.Arrive(fade);
            transitioning = false;
        }

        void Update()
        {
            if (sockets == null) return;
            float now = Time.unscaledTime;
            // Whichever host published most recently is what the Mac is running now.
            int newest = -1;
            for (int i = 0; i < Activities.Length; i++)
            {
                if (sockets[i].Take(out var text) && text.Contains($"\"{Activities[i].type}\"")) liveAt[i] = now;
                if (now - liveAt[i] < 2 && (newest < 0 || liveAt[i] > liveAt[newest])) newest = i;
            }
            if (newest < 0 || transitioning || loading is { isDone: false } || SceneManager.GetActiveScene().name == Activities[newest].scene) return;
            if (!Application.CanStreamedLevelBeLoaded(Activities[newest].scene)) return;
            StartCoroutine(Transition(Activities[newest].scene, null));
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (sockets != null) foreach (var s in sockets) s?.Dispose();
        }
    }
}
