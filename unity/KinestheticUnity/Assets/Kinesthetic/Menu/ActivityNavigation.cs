using System;
using System.Collections;
using System.Linq;
using Kinesthetic.Activities;
using Kinesthetic.Shell;
using Kinesthetic.UI.Remote;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    public sealed class ActivityNavigation : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";
        /// The intro is not a catalog activity (the coordinator neither prescribes nor records it), but it
        /// gets the same Menu button and Esc as one, so no one is stuck inside it.
        public const string TutorialScene = "Tutorial";
        const string MusicPreference = "MiiHab.MenuMusic", OldMusicPreference = "RehabMii.MenuMusic";   // the old key is read once, as a fallback
        public AudioClip menuMusic, hoverSound, selectSound, backSound;
        public static ActivityNavigation Instance { get; private set; }
        public bool MusicEnabled { get; private set; }
        public bool Busy { get; private set; }
        public bool OverlayOpen => Busy || (group != null && group.Showing) || (dialog != null && !dialog.ClassListContains("hidden")) || (help != null && !help.ClassListContains("hidden"));
        public event Action<bool> MusicChanged;
        AudioSource musicSource, effects;
        GroupPanel group;
        VisualElement root, dialog, help;
        Button returnButton, confirm, cancel, helpButton, helpClose, helpMusic;
        Label detail, title;
        bool menuActive, supported, showMusic;
        float lastHover = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; LaunchedActivityId = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Supports(SceneManager.GetActiveScene().name)) Ensure();
        }
        static bool Supports(string scene) =>
            scene == MenuScene || scene == TutorialScene || ActivityCatalog.All.Any(a => a.Scene == scene && a.UsesSharedNavigation);
        /// <summary>The activity last launched from the menu. Several activities share a scene (every movement opens the
        /// studio), so the scene alone cannot say which one is running; the scene asks this instead.</summary>
        public static string LaunchedActivityId { get; private set; }
        /// <summary>The activity whose scene is loaded, or null in the menu. The launched one when it owns this scene.</summary>
        public static ActivityEntry Current()
        {
            string scene = SceneManager.GetActiveScene().name;
            var launched = ActivityCatalog.ById(LaunchedActivityId);
            return launched != null && launched.Scene == scene ? launched : ActivityCatalog.All.FirstOrDefault(a => a.Scene == scene);
        }
        /// <summary>Whatever the shell is driving right now, without knowing what kind of thing it is.</summary>
        static IActivity CurrentActivity() => FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .OfType<IActivity>().FirstOrDefault();
        public static ActivityNavigation Ensure()
        {
            if (Instance) return Instance;
            var prefab = Resources.Load<GameObject>("Menu/ActivityNavigation");
            if (!prefab) throw new InvalidOperationException("The activity navigation prefab is missing.");
            return Instantiate(prefab).GetComponent<ActivityNavigation>();
        }
        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            MusicEnabled = PlayerPrefs.GetInt(MusicPreference, PlayerPrefs.GetInt(OldMusicPreference, 1)) != 0;
            musicSource = gameObject.AddComponent<AudioSource>(); musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0; musicSource.loop = true; musicSource.clip = menuMusic; musicSource.volume = 0;
            effects = gameObject.AddComponent<AudioSource>(); effects.playOnAwake = false; effects.spatialBlend = 0; effects.volume = .55f;
            // Group therapy rides on this object for the same reason the music does: it outlives the scene.
            group = GetComponent<GroupPanel>() ?? gameObject.AddComponent<GroupPanel>();
            SceneManager.sceneLoaded += SceneLoaded;
            SyncScene();
        }
        bool Bind()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            if (tree?.Q<Button>("return-menu") == null) return false;
            if (returnButton == tree.Q<Button>("return-menu")) return true;
            root = tree; root.pickingMode = PickingMode.Ignore;
            returnButton = root.Q<Button>("return-menu"); dialog = root.Q("return-dialog");
            confirm = root.Q<Button>("return-confirm"); cancel = root.Q<Button>("return-cancel");
            detail = root.Q<Label>("return-detail"); title = root.Q<Label>("return-title");
            returnButton.clicked += OpenReturn;
            help = root.Q("activity-help-panel"); helpButton = root.Q<Button>("activity-help");
            helpClose = root.Q<Button>("activity-help-close"); helpMusic = root.Q<Button>("activity-music");
            helpButton.clicked += OpenHelp; helpClose.clicked += CloseHelp; helpMusic.clicked += ToggleMusic;
            helpButton.EnableInClassList("hidden", menuActive || !supported);
            cancel.clicked += CloseReturn;
            group.Mount(root, this);
            confirm.clicked += () => { if (!Busy) StartCoroutine(ReturnToMenu()); };
            returnButton.EnableInClassList("hidden", menuActive || !supported);
            return true;
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode) { SyncScene(); }
        void SyncScene()
        {
            string scene = SceneManager.GetActiveScene().name;
            var entry = Current();
            menuActive = scene == MenuScene; supported = Supports(scene);
            // An activity has music when the catalog gives it a track; nothing here knows which one.
            var activityMusic = entry?.Music == null ? null : Resources.Load<AudioClip>(entry.Music);
            showMusic = menuActive || activityMusic;
            var nextMusic = menuActive ? menuMusic : activityMusic;
            if (musicSource.clip != nextMusic) { musicSource.Stop(); musicSource.clip = nextMusic; musicSource.volume = 0; }
            if (Bind())
            {
                returnButton.EnableInClassList("hidden", menuActive || !supported);
                helpButton.EnableInClassList("hidden", menuActive || !supported);
                help.AddToClassList("hidden");
                dialog.AddToClassList("hidden");
                group.SceneChanged(menuActive);
            }
            if (showMusic && MusicEnabled && musicSource.clip && !musicSource.isPlaying) musicSource.Play();
        }
        void Update()
        {
            if (!Bind()) return;
            float target = showMusic && MusicEnabled ? (menuActive ? .38f : .22f) : 0;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, Time.unscaledDeltaTime * 1.8f);
            if (target == 0 && musicSource.volume == 0 && musicSource.isPlaying) musicSource.Pause();
            bool escape = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
            if (escape && group.Showing) { group.Cancel(); return; }
            if (escape && !menuActive && supported && !Busy)
            { if (!help.ClassListContains("hidden")) CloseHelp(); else if (dialog.ClassListContains("hidden")) OpenReturn(); else CloseReturn(); }
        }
        public void ToggleMusic()
        {
            MusicEnabled = !MusicEnabled; PlayerPrefs.SetInt(MusicPreference, MusicEnabled ? 1 : 0); PlayerPrefs.Save();
            if (MusicEnabled && showMusic && musicSource.clip) { if (musicSource.time > 0) musicSource.UnPause(); else musicSource.Play(); }
            MusicChanged?.Invoke(MusicEnabled); PlaySelect();
            if (helpMusic != null) helpMusic.text = MusicEnabled ? "Music: On" : "Music: Off";
        }
        void OpenHelp()
        {
            if (Busy || !dialog.ClassListContains("hidden")) return;
            // Help copy is the activity's own, read from the catalog, so a new activity brings its guide
            // with it instead of adding another branch here.
            var entry = Current();
            if (entry == null) return;
            root.Q<Label>("activity-help-title").text = entry.HelpTitle;
            var names = new[] { "one", "two", "three" };
            for (int i = 0; i < names.Length && i < entry.HelpSteps.Length; i++)
            {
                root.Q<Label>("help-step-" + names[i]).text = entry.HelpSteps[i].Step;
                root.Q<Label>("help-copy-" + names[i]).text = entry.HelpSteps[i].Copy;
            }
            helpMusic.text = MusicEnabled ? "Music: On" : "Music: Off";
            help.RemoveFromClassList("hidden"); helpClose.Focus(); PlaySelect();
        }
        void CloseHelp() { help.AddToClassList("hidden"); helpButton.Focus(); PlayBack(); }
        public void PlayHover() { if (Time.unscaledTime - lastHover < .09f) return; lastHover = Time.unscaledTime; if (hoverSound) effects.PlayOneShot(hoverSound); }
        public void PlaySelect() { if (selectSound) effects.PlayOneShot(selectSound); }
        public void PlayBack() { if (backSound) effects.PlayOneShot(backSound); }
        /// <summary>Takes a catalog activity id. The scene it lives in is the catalog's business.</summary>
        public void LoadActivity(string activityId)
        {
            var entry = ActivityCatalog.ById(activityId);
            if (Busy || entry == null) return;
            if (!Application.CanStreamedLevelBeLoaded(entry.Scene)) { ShowUnavailable(); return; }
            // Every launch, from any card, tile or button, first asks: alone, or with other people?
            if (Bind() && group.Intercept(entry, () => Go(entry))) return;
            Go(entry);
        }
        public void LoadTutorial()
        {
            if (Busy) return;
            if (!Application.CanStreamedLevelBeLoaded(TutorialScene)) { ShowUnavailable(); return; }
            LaunchedActivityId = null;
            PlaySelect(); StartCoroutine(Load(TutorialScene, null));
        }
        void Go(ActivityEntry entry)
        {
            if (Busy) return;
            LaunchedActivityId = entry.Id;
            PlaySelect(); StartCoroutine(Load(entry.Scene, entry.Venue));
        }
        /// Straight back to the menu, no confirmation: for an activity whose own screen already asked (the
        /// therapist visit's "Got it!"). A running activity still finishes its session first, as through
        /// the dialog.
        public void ReturnToMenuNow()
        {
            if (Busy || menuActive || !supported || !Bind()) return;
            StartCoroutine(ReturnToMenu());
        }
        public void OpenReturn()
        {
            if (Busy || menuActive || !supported || !Bind()) return;
            help.AddToClassList("hidden");
            var activity = CurrentActivity();
            title.text = "Back to the menu?";
            detail.text = activity != null && activity.IsRunning ? "We'll finish your exercise session before returning to the activity menu." : "Your current activity will close. You can choose another activity from the menu.";
            cancel.text = "Keep playing"; confirm.text = "Return to menu";
            confirm.RemoveFromClassList("hidden"); confirm.SetEnabled(true); cancel.SetEnabled(true);
            dialog.RemoveFromClassList("hidden"); cancel.Focus(); PlayBack();
        }
        void CloseReturn() { if (Busy) return; dialog.AddToClassList("hidden"); returnButton.Focus(); PlayBack(); }
        void ShowUnavailable()
        {
            if (!Bind()) return;
            title.text = "Activity unavailable"; detail.text = "This activity isn't available in this version.";
            cancel.text = "Back"; confirm.AddToClassList("hidden"); dialog.RemoveFromClassList("hidden"); cancel.Focus();
        }
        IEnumerator ReturnToMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(MenuScene)) { ShowUnavailable(); yield break; }
            Busy = true; confirm.SetEnabled(false); cancel.SetEnabled(false);
            var activity = CurrentActivity();
            if (activity != null && activity.IsBusy)
            {
                detail.text = "Waiting for your session to finish connecting…";
                while (activity != null && activity.IsBusy) yield return null;
            }
            if (activity != null && activity.IsRunning)
            {
                bool ended = false;
                detail.text = "Finishing your exercise session…";
                yield return activity.RequestExit(success => ended = success);
                if (!ended)
                {
                    Busy = false; confirm.SetEnabled(true); cancel.SetEnabled(true);
                    title.text = "Couldn't finish the session";
                    detail.text = "The measurement service didn't respond. Please retry before leaving your session.";
                    confirm.text = "Try again"; yield break;
                }
            }
            yield return group.LeaveOnReturn();
            PlaySelect(); yield return Load(MenuScene, null);
        }
        /// Into `scene`, through the doorway of `venue` when this scene has one (the plaza does, for every
        /// catalog venue), behind a fade when it does not (an activity, on the way back). The next scene loads
        /// during the walk and is switched in on the covered frame. The headset is told at both ends
        /// (UiCue.Scene) so it walks through its own copy of the door at the same moment.
        IEnumerator Load(string scene, string venue)
        {
            Busy = true; showMusic = false;
            dialog.AddToClassList("hidden");
            UiCue.SendScene(scene, venue, UiCue.Leave);
            var fade = HeadFade.Ensure();
            // Load hard now, while the door swings and nothing moves, so the walk runs on quiet frames.
            var priority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;
            var load = SceneManager.LoadSceneAsync(scene);
            load.allowSceneActivation = false;
            var portal = Portal.Find(venue);
            var cam = Camera.main;
            if (portal != null && cam != null)
            {
                var carousel = FindAnyObjectByType<PaneCarousel>();
                yield return PlazaApproach.Enter(portal, cam.transform, fade, turnToward: true, () => load.progress >= .9f, carousel ? carousel.gameObject : null);
            }
            else yield return fade.CoverTo(1, PlazaApproach.ClearSeconds);
            fade.Detach();   // off the camera that is about to go with the scene
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            Application.backgroundLoadingPriority = priority;
            UiCue.SendScene(scene, venue, UiCue.Arrive);
            yield return PlazaApproach.Arrive(fade);
            Busy = false; confirm.SetEnabled(true); cancel.SetEnabled(true);
        }
        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= SceneLoaded; Instance = null;
        }
    }
}
