using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Kinesthetic.Activities;
using Kinesthetic.Bowling;
using Kinesthetic.Menu;
using Kinesthetic.Panes;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class MenuIntegrationVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator routine;
    static double deadline;
    public static string Result { get; private set; } = "Not run";

    [MenuItem("Kinesthetic/Menu/Verify navigation and panes (Play mode)")]
    public static string Run()
    {
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != ActivityNavigation.MenuScene)
            throw new InvalidOperationException("Run from MainMenu in Play mode.");
        if (routine != null) throw new InvalidOperationException("Verification is already running.");
        Result = "Running"; deadline = EditorApplication.timeSinceStartup + 45;
        routine = Verify(); EditorApplication.update += Step;
        return Result;
    }

    static void Step()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                throw new Exception("Verification interrupted or timed out.");
            if (routine.MoveNext()) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            Result = "PASS: scaled/rotated pane picking, modal occlusion, content lifecycle, IMU catalog, Bowling Escape and menu round trip.";
        }
        catch (Exception e) { Result = "FAIL: " + e; }
        (routine as IDisposable)?.Dispose(); routine = null;
        EditorApplication.update -= Step;
        Debug.Log(Result);
    }

    static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Menu integration: " + message); }
    static void Commit(MainMenuController menu, string name)
        => typeof(MainMenuController).GetMethod("Commit", Private).Invoke(menu, new object[] { name });

    static IEnumerator Verify()
    {
        var menu = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
        var gaze = menu.GetComponent<Kinesthetic.GazeDwell>();
        bool wasEnabled = gaze.enabled; gaze.enabled = false;
        var template = menu.GetComponent<UIDocument>().panelSettings;
        var fixture = new GameObject("Pane verification fixture");
        FixtureContent first = null, second = null;
        SceneManager.sceneLoaded += PrepareBowling;
        try
        {
            var bowling = ActivityCatalog.ById("bowling.adaptive");
            Check(bowling != null && bowling.NeedsImu && !bowling.NeedsPose && bowling.Subjects == 1,
                "Bowling must be a solo IMU activity");
            Check(!bowling.UsesSharedNavigation, "Bowling must own its controls");
            Check(ActivityCatalog.ById("rehab.studio").NeedsImu && !ActivityCatalog.ById("rehab.studio").NeedsPose,
                "Studio must stay IMU-only");
            Check(Application.CanStreamedLevelBeLoaded(bowling.Scene), "Bowling missing from the build list");
            // An activity is entered by walking through its venue's door, so a venue with no door is a dead card.
            foreach (var entry in ActivityCatalog.All)
                Check(Portal.Find(entry.Venue) != null, $"the plaza has no doorway for venue '{entry.Venue}'");

            fixture.transform.SetPositionAndRotation(new Vector3(100, 100, 100), Quaternion.Euler(12, 38, 7));
            fixture.transform.localScale = new Vector3(1.7f, .8f, 1.2f);
            var surface = new GameObject("Content"); surface.transform.SetParent(fixture.transform, false);
            var doc = surface.AddComponent<UIDocument>(); doc.panelSettings = template;
            var pane = surface.AddComponent<Pane>();
            first = new FixtureContent(); pane.SetContent(first);
            for (int i = 0; i < 6; i++) yield return null;
            Check(first.BindCount == 1 && first.Ticks > 0, "content did not bind and tick");
            Check(doc.panelSettings != template, "pane changed shared panel settings");
            Check(Vector2.Distance(pane.WorldSize, new Vector2(1.2f * 1.7f, .8f * .8f)) < .01f,
                "pane size does not match its rendered surface");
            foreach (var point in new[] { new Vector2(.1f, .2f), new Vector2(.5f, .5f), new Vector2(.8f, .9f) })
            {
                var root = doc.rootVisualElement;
                var pixels = new Vector2(point.x * root.contentRect.width, (1 - point.y) * root.contentRect.height);
                var panel = root.LocalToWorld(pixels);
                var world = doc.transform.TransformPoint(new Vector3(panel.x, panel.y, 0));
                Check(pane.TryProject(world, out _, out var actual) && Vector2.Distance(point, actual) < .001f,
                    "scaled/rotated ray coordinates drifted");
                Check(pane.SendPointer(world, true) && first.Pressed && Vector2.Distance(first.Pointer, point) < .001f,
                    "content pointer coordinates differ from picking");
            }

            Physics.SyncTransforms();
            var button = doc.rootVisualElement.Q<Button>("pane-test");
            Check(Pick(doc, button) == button, "ray missed the managed content button");
            doc.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Kinesthetic/Menu/MainMenu.uss"));
            var shade = new VisualElement(); shade.AddToClassList("modal-shade"); doc.rootVisualElement.Add(shade);
            for (int i = 0; i < 3; i++) yield return null;
            Check(Pick(doc, button) == null, "ray clicked through the modal shade");
            shade.RemoveFromHierarchy();
            button.SetEnabled(false);
            Check(Pick(doc, button) == null, "disabled button was selectable");

            second = new FixtureContent(); pane.SetContent(second);
            Check(first.Disposals == 1, "replaced content was not disposed exactly once");
            for (int i = 0; i < 3; i++) yield return null;
            int ticks = second.Ticks;
            pane.enabled = false;
            for (int i = 0; i < 3; i++) yield return null;
            Check(second.Ticks == ticks && second.Disposals == 0, "hidden pane lost content or kept ticking");
            doc.enabled = false; yield return null;
            doc.enabled = true; pane.enabled = true;
            for (int i = 0; i < 5; i++) yield return null;
            Check(second.BindCount == 2 && second.Ticks > ticks, "reopened document did not rebind its content");
            UnityEngine.Object.Destroy(fixture);
            yield return null;
            Check(second.Disposals == 1, "destroyed pane did not dispose content once");

            Commit(menu, "pane-gallery");
            // The cards stand on the gallery pane, not the board (MainMenuSetup adopts it as slot "gallery"),
            // and the menu only lets a card through once the ring has settled on that pane.
            var ring = UnityEngine.Object.FindAnyObjectByType<Kinesthetic.Shell.PaneCarousel>();
            for (int i = 0; i < 3; i++) yield return null;
            while (ring.IsTurning) yield return null;
            for (int i = 0; i < 3; i++) yield return null;
            var gallerySlot = ring.Slots.First(s => s.id == "gallery");
            var galleryDoc = gallerySlot.pane.GetComponent<UIDocument>();
            // Cards are generated from the catalog (MainMenuController.BuildGallery), named card-<activity id>.
            const string bowlingCard = "card-bowling.adaptive";
            Check(Pick(galleryDoc, galleryDoc.rootVisualElement.Q<Button>(bowlingCard))?.name == bowlingCard,
                "Bowling card cannot be selected by a ray");
            Commit(menu, bowlingCard);
            while (SceneManager.GetActiveScene().name != bowling.Scene || ActivityNavigation.Instance.Busy) yield return null;
            yield return null;
            var navigation = ActivityNavigation.Instance;
            var navRoot = navigation.GetComponent<UIDocument>().rootVisualElement;
            Check(navRoot.Q<Button>("return-menu").ClassListContains("hidden") &&
                  navRoot.Q<Button>("activity-help").ClassListContains("hidden"), "duplicate navigation controls appeared");
            var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
            var hud = UnityEngine.Object.FindAnyObjectByType<BowlingHud>();
            Check(Keyboard.current != null, "keyboard unavailable for Escape regression check");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Escape)); InputSystem.Update();
            typeof(BowlingHud).GetMethod("Update", Private).Invoke(hud, null);
            typeof(ActivityNavigation).GetMethod("Update", Private).Invoke(navigation, null);
            Check(game.Paused && navRoot.Q("return-dialog").ClassListContains("hidden"), "Escape opened two overlays");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); InputSystem.Update();
            typeof(BowlingHud).GetMethod("Back", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            while (SceneManager.GetActiveScene().name != ActivityNavigation.MenuScene) yield return null;
            Check(Time.timeScale == 1, "return left the menu paused");
        }
        finally
        {
            SceneManager.sceneLoaded -= PrepareBowling;
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            if (fixture) UnityEngine.Object.Destroy(fixture);
            if (gaze) gaze.enabled = wasEnabled;
        }
    }

    static Button Pick(UIDocument doc, Button button)
    {
        var point = button.worldBound.center;
        var world = doc.transform.TransformPoint(new Vector3(point.x, point.y, 0));
        var ray = new Ray(world - doc.transform.forward * .05f, doc.transform.forward);
        return WorldPanelPick.Under<Button>(ray, .1f, Physics.DefaultRaycastLayers, out _, out _, out _);
    }

    static void PrepareBowling(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != ActivityCatalog.SceneOf("bowling.adaptive")) return;
        var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
        game.startServices = false; game.motionUrl = "ws://127.0.0.1:18790/bowling-motion?role=viewer";
        game.GetComponent<BowlingStatePublisher>().url = "ws://127.0.0.1:18790/bowling-state?role=host";
    }

    sealed class FixtureContent : IPaneContent, IPanePointerTarget
    {
        public string Title => "Pane verification";
        public Vector2 PreferredSize => new(1.2f, .8f);
        public int BindCount, Ticks, Disposals;
        public Vector2 Pointer;
        public bool Pressed;
        public void Bind(VisualElement root) { BindCount++; root.Add(new Button { name = "pane-test", text = "Test" }); }
        public void Tick() => Ticks++;
        public void Dispose() => Disposals++;
        public void OnPointer(Vector2 point, bool pressed) { Pointer = point; Pressed = pressed; }
    }
}
