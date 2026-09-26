using System;
using System.Collections;
using System.Linq;
using Kinesthetic.Activities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    public sealed class ActivityNavigation : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";
        const string MusicPreference = "RehabMii.MenuMusic";
        public AudioClip menuMusic, hoverSound, selectSound, backSound;
        public static ActivityNavigation Instance { get; private set; }
        public bool MusicEnabled { get; private set; }
        public bool Busy { get; private set; }
        public bool OverlayOpen => Busy || (dialog != null && !dialog.ClassListContains("hidden")) || (help != null && !help.ClassListContains("hidden"));
        public event Action<bool> MusicChanged;
        AudioSource musicSource, effects;
        VisualElement root, dialog, curtain, help;
        Button returnButton, confirm, cancel, helpButton, helpClose, helpMusic;
        Label detail, title, loading;
        bool menuActive, supported, showMusic;
        float lastHover = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Supports(SceneManager.GetActiveScene().name)) Ensure();
        }
        static bool Supports(string scene) =>
            scene == MenuScene || ActivityCatalog.All.Any(a => a.Scene == scene && a.UsesSharedNavigation);
        /// <summary>The activity whose scene is loaded, or null in the menu.</summary>
        static ActivityEntry Current() => ActivityCatalog.All.FirstOrDefault(a => a.Scene == SceneManager.GetActiveScene().name);
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
            MusicEnabled = PlayerPrefs.GetInt(MusicPreference, 1) != 0;
            musicSource = gameObject.AddComponent<AudioSource>(); musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0; musicSource.loop = true; musicSource.clip = menuMusic; musicSource.volume = 0;
            effects = gameObject.AddComponent<AudioSource>(); effects.playOnAwake = false; effects.spatialBlend = 0; effects.volume = .55f;
            SceneManager.sceneLoaded += SceneLoaded;
            SyncScene();
        }
        bool Bind()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            if (tree?.Q<Button>("return-menu") == null) return false;
            if (returnButton == tree.Q<Button>("return-menu")) return true;
            root = tree; root.pickingMode = PickingMode.Ignore;
            returnButton = root.Q<Button>("return-menu"); dialog = root.Q("return-dialog"); curtain = root.Q("transition");
            confirm = root.Q<Button>("return-confirm"); cancel = root.Q<Button>("return-cancel");
            detail = root.Q<Label>("return-detail"); title = root.Q<Label>("return-title"); loading = root.Q<Label>("loading-label");
            returnButton.clicked += OpenReturn;
            help = root.Q("activity-help-panel"); helpButton = root.Q<Button>("activity-help");
            helpClose = root.Q<Button>("activity-help-close"); helpMusic = root.Q<Button>("activity-music");
            helpButton.clicked += OpenHelp; helpClose.clicked += CloseHelp; helpMusic.clicked += ToggleMusic;
            helpButton.EnableInClassList("hidden", menuActive || !supported);
            cancel.clicked += CloseReturn;
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
            }
            if (showMusic && MusicEnabled && musicSource.clip && !musicSource.isPlaying) musicSource.Play();
        }
        void Update()
        {
            if (!Bind()) return;
            float target = showMusic && MusicEnabled ? (menuActive ? .38f : .22f) : 0;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, Time.unscaledDeltaTime * 1.8f);
            if (target == 0 && musicSource.volume == 0 && musicSource.isPlaying) musicSource.Pause();
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && !menuActive && supported && !Busy)
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
            PlaySelect(); StartCoroutine(Load(entry.Scene));
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
            PlaySelect(); yield return Load(MenuScene);
        }
        IEnumerator Load(string scene)
        {
            Busy = true; showMusic = false;
            curtain.RemoveFromClassList("hidden");
            loading.text = ActivityCatalog.All.FirstOrDefault(a => a.Scene == scene)?.LoadingMessage ?? "Back to your activities…";
            yield return new WaitForSecondsRealtime(.22f);
            var load = SceneManager.LoadSceneAsync(scene);
            while (!load.isDone) yield return null;
            Busy = false; curtain.AddToClassList("hidden"); confirm.SetEnabled(true); cancel.SetEnabled(true);
        }
        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= SceneLoaded; Instance = null;
        }
    }
}
