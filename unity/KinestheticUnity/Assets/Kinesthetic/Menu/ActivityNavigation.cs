using System;
using System.Collections;
using Kinesthetic.Rehab;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    public sealed class ActivityNavigation : MonoBehaviour
    {
        public const string MenuScene = "MainMenu", GolfScene = "AdaptiveGolf", StudioScene = "Rehab";
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
        static bool Supports(string scene) => scene == MenuScene || scene == GolfScene || scene == StudioScene;
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
            menuActive = scene == MenuScene; supported = Supports(scene); showMusic = menuActive;
            if (Bind())
            {
                returnButton.EnableInClassList("hidden", menuActive || !supported);
                helpButton.EnableInClassList("hidden", menuActive || !supported);
                help.AddToClassList("hidden");
                dialog.AddToClassList("hidden");
            }
            if (menuActive && MusicEnabled && !musicSource.isPlaying) musicSource.Play();
        }
        void Update()
        {
            if (!Bind()) return;
            float target = showMusic && MusicEnabled ? .38f : 0;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, target, Time.unscaledDeltaTime * 1.8f);
            if (target == 0 && musicSource.volume == 0 && musicSource.isPlaying) musicSource.Pause();
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && !menuActive && supported && !Busy)
            { if (!help.ClassListContains("hidden")) CloseHelp(); else if (dialog.ClassListContains("hidden")) OpenReturn(); else CloseReturn(); }
        }
        public void ToggleMusic()
        {
            MusicEnabled = !MusicEnabled; PlayerPrefs.SetInt(MusicPreference, MusicEnabled ? 1 : 0); PlayerPrefs.Save();
            if (MusicEnabled && menuActive) { if (musicSource.time > 0) musicSource.UnPause(); else musicSource.Play(); }
            MusicChanged?.Invoke(MusicEnabled); PlaySelect();
            if (helpMusic != null) helpMusic.text = MusicEnabled ? "Menu music: On" : "Menu music: Off";
        }
        void OpenHelp()
        {
            if (Busy || !dialog.ClassListContains("hidden")) return;
            bool golf = SceneManager.GetActiveScene().name == GolfScene;
            root.Q<Label>("activity-help-title").text = golf ? "Your round, at a glance" : "Find your studio rhythm";
            root.Q<Label>("help-step-one").text = golf ? "Get connected" : "Make yourself comfortable";
            root.Q<Label>("help-copy-one").text = golf ? "Open Setup to connect the camera and AirPods. Keep both hands, elbows and shoulders in view." : "Sit with your shoulders, elbows and hips in view. Start session opens the camera and loads your prescribed set.";
            root.Q<Label>("help-step-two").text = golf ? "Settle, aim, swing" : "Reach, hold, return";
            root.Q<Label>("help-copy-two").text = golf ? "Use the aim arrows. Rest the club at the mat until calibration finishes, then make a controlled swing." : "Rest your arm while calibration finishes. Follow the glowing target, hold gently, and lower slowly.";
            root.Q<Label>("help-step-three").text = golf ? "Take turns together" : "Follow your own pace";
            root.Q<Label>("help-copy-three").text = golf ? "Turns change after each shot. Recalibrate at each new lie. Both players finish the hole to see the scorecard." : "The ring fills with counted repetitions. Finish set ends early and shows your session summary. Your session continues while this guide is open.";
            helpMusic.text = MusicEnabled ? "Menu music: On" : "Menu music: Off";
            help.RemoveFromClassList("hidden"); helpClose.Focus(); PlaySelect();
        }
        void CloseHelp() { help.AddToClassList("hidden"); helpButton.Focus(); PlayBack(); }
        public void PlayHover() { if (Time.unscaledTime - lastHover < .09f) return; lastHover = Time.unscaledTime; if (hoverSound) effects.PlayOneShot(hoverSound); }
        public void PlaySelect() { if (selectSound) effects.PlayOneShot(selectSound); }
        public void PlayBack() { if (backSound) effects.PlayOneShot(backSound); }
        public void LoadActivity(string scene)
        {
            if (Busy || (scene != GolfScene && scene != StudioScene)) return;
            if (!Application.CanStreamedLevelBeLoaded(scene)) { ShowUnavailable(); return; }
            PlaySelect(); StartCoroutine(Load(scene));
        }
        public void OpenReturn()
        {
            if (Busy || menuActive || !supported || !Bind()) return;
            help.AddToClassList("hidden");
            var rehab = FindAnyObjectByType<RehabSession>();
            title.text = "Back to the menu?";
            detail.text = rehab && rehab.IsRunning ? "We'll finish your exercise session before returning to the activity menu." : "Your current activity will close. You can choose another activity from the menu.";
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
            var rehab = FindAnyObjectByType<RehabSession>();
            if (rehab && rehab.IsBusy)
            {
                detail.text = "Waiting for your session to finish connecting…";
                while (rehab && rehab.IsBusy) yield return null;
            }
            if (rehab && rehab.IsRunning)
            {
                bool ended = false;
                detail.text = "Finishing your exercise session…";
                yield return rehab.FinishSession(success => ended = success);
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
            loading.text = scene == GolfScene ? "Heading to the course…" : scene == StudioScene ? "Opening the studio…" : "Back to your activities…";
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
