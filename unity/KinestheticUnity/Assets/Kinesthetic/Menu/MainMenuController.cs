using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    public sealed class MainMenuController : MonoBehaviour
    {
        VisualElement root;
        VisualElement activityOverlay, helpOverlay, nameOverlay;
        TextField nameField;
        Button[] cards;
        Button music;
        Label caption;
        ActivityNavigation navigation;
        MenuDashboardModel model;
        int selected;

        // Every activation this menu offers, by element name. Both the pointer's clicked handler and the gaze
        // dwell run out of this one table, so the two input paths can never drift apart.
        readonly Dictionary<string, Action> actions = new();

        // What is reachable right now. A pointer cannot reach behind an open sheet because the shade covers
        // it, but the gaze ray is resolved from element rects alone and would happily commit a tile the
        // person cannot even see. So the menu decides what counts as reachable, per open layer.
        static readonly string[] BaseScope = { "start-activity", "choose-activity", "friends", "music", "help", "edit-name" };
        static readonly string[] ActivityScope = { "golf-card", "studio-card", "activity-close" };
        static readonly string[] HelpScope = { "help-close" };
        static readonly string[] NameScope = { "name-save", "name-cancel" };
        string[] scope = BaseScope;

        void Start() { navigation = ActivityNavigation.Ensure(); Bind(); }
        void Update() { if (root == null) Bind(); }

        void Bind()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            if (tree?.Q<Button>("start-activity") == null) return;
            root = tree;
            model = MenuDashboardModel.Placeholder();

            activityOverlay = root.Q("activity-overlay");
            helpOverlay = root.Q("help-overlay");
            nameOverlay = root.Q("name-overlay");
            nameField = root.Q<TextField>("name-field");
            cards = new[] { root.Q<Button>("golf-card"), root.Q<Button>("studio-card") };
            caption = root.Q<Label>("selection-caption");
            music = root.Q<Button>("music");

            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                cards[i].RegisterCallback<PointerEnterEvent>(_ => Select(index, true));
                cards[i].RegisterCallback<FocusInEvent>(_ => Select(index, true));
                Act(cards[i], () => Launch(index == 0 ? ActivityNavigation.GolfScene : ActivityNavigation.StudioScene));
            }

            Act(root.Q<Button>("start-activity"), StartFirst);
            Act(root.Q<Button>("choose-activity"), () => OpenSheet(activityOverlay, ActivityScope, cards[selected]));
            Act(root.Q<Button>("activity-close"), () => CloseSheet(activityOverlay, "choose-activity"));
            Act(music, navigation.ToggleMusic);
            Act(root.Q<Button>("help"), () => OpenSheet(helpOverlay, HelpScope, root.Q<Button>("help-close")));
            Act(root.Q<Button>("help-close"), () => CloseSheet(helpOverlay, "help"));
            Act(root.Q<Button>("edit-name"), OpenNameSheet);
            Act(root.Q<Button>("name-cancel"), () => CloseSheet(nameOverlay, "edit-name"));
            Act(root.Q<Button>("name-save"), SaveName);
            // Enter in the field commits, so the keyboard path does not dead-end on a board whose
            // primary input is a gaze.
            nameField?.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { SaveName(); e.StopPropagation(); }
            });

            navigation.MusicChanged += MusicChanged;
            MusicChanged(navigation.MusicEnabled);

            root.RegisterCallback<NavigationCancelEvent>(e =>
            {
                if (Showing(activityOverlay)) { CloseSheet(activityOverlay, "choose-activity"); e.StopPropagation(); }
                else if (Showing(helpOverlay)) { CloseSheet(helpOverlay, "help"); e.StopPropagation(); }
                else if (Showing(nameOverlay)) { CloseSheet(nameOverlay, "edit-name"); e.StopPropagation(); }
            });
            root.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (!Showing(activityOverlay)) return;
                if (e.direction != NavigationMoveEvent.Direction.Left && e.direction != NavigationMoveEvent.Direction.Right) return;
                Select(e.direction == NavigationMoveEvent.Direction.Left ? 0 : 1, false);
                cards[selected].Focus(); e.StopPropagation();
            });

            var gaze = GetComponent<GazeDwell>();
            if (gaze) { gaze.Entered += Gazed; gaze.Committed += Commit; }

            root.Q("golf-icon").generateVisualContent += c => DrawIcon(c, false);
            root.Q("studio-icon").generateVisualContent += c => DrawIcon(c, true);

            // Added at runtime so the generated menu scene needs no change.
            var friends = GetComponent<FriendsPanel>() ?? gameObject.AddComponent<FriendsPanel>();
            friends.Attach(root, navigation);
            // FriendsPanel owns the button's click; the dwell path needs the same entry in the map.
            actions["friends"] = friends.Open;

            MenuDashboard.Populate(root, model);
            Select(0, false);
            root.schedule.Execute(() => root.Q<Button>("start-activity").Focus());
        }

        // The dwell reports an element name; the pointer reports a click. Both land here.
        void Act(Button button, Action action)
        {
            if (button == null) return;
            actions[button.name] = action;
            button.clicked += action;
        }

        bool InScope(string name) => Array.IndexOf(scope, name) >= 0;
        static bool Showing(VisualElement overlay) => overlay != null && !overlay.ClassListContains("hidden");

        void Gazed(string name)
        {
            if (!InScope(name)) return;
            int card = Array.FindIndex(cards, c => c != null && c.name == name);
            if (card >= 0) { Select(card, true); cards[card].Focus(); return; }
            navigation.PlayHover();
        }

        void Commit(string name)
        {
            if (InScope(name) && actions.TryGetValue(name, out var action)) action();
        }

        /// The CTA launches whatever the plan says is still outstanding, so the board's headline action
        /// and its Today list can never disagree about what comes next.
        void StartFirst()
        {
            var next = model.FirstOutstanding();
            if (next is not { } task) { OpenSheet(activityOverlay, ActivityScope, cards[selected]); return; }
            Launch(task.activityId == "golf.adaptive" ? ActivityNavigation.GolfScene : ActivityNavigation.StudioScene);
        }

        void Launch(string scene) => navigation.LoadActivity(scene);

        void OpenNameSheet()
        {
            if (nameField != null) nameField.value = MenuProfile.Name;
            OpenSheet(nameOverlay, NameScope, nameField);
        }

        void SaveName()
        {
            MenuProfile.Name = nameField?.value ?? MenuProfile.DefaultName;
            model = MenuDashboardModel.Placeholder();
            MenuDashboard.Populate(root, model);
            CloseSheet(nameOverlay, "edit-name");
        }

        void OpenSheet(VisualElement overlay, string[] next, VisualElement focus)
        {
            if (overlay == null) return;
            navigation.PlaySelect();
            overlay.RemoveFromClassList("hidden");
            scope = next;
            focus?.Focus();
        }

        void CloseSheet(VisualElement overlay, string focusName)
        {
            if (overlay == null) return;
            overlay.AddToClassList("hidden");
            scope = BaseScope;
            navigation.PlayBack();
            root.Q<Button>(focusName)?.Focus();
        }

        void Select(int index, bool sound)
        {
            bool changed = index != selected; selected = index;
            for (int i = 0; i < cards.Length; i++) cards[i]?.EnableInClassList("selected", i == selected);
            if (caption != null)
                caption.text = selected == 0 ? "Take a swing with a friend." : "Make a little time for yourself.";
            if (sound && changed) navigation.PlayHover();
        }

        void MusicChanged(bool enabled) { if (music != null) music.text = enabled ? "Music: On" : "Music: Off"; }
        void OnDestroy() { if (navigation) navigation.MusicChanged -= MusicChanged; }

        static void DrawIcon(MeshGenerationContext ctx, bool studio)
        {
            var p = ctx.painter2D;
            p.strokeColor = Palette.Indigo60; p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.Stroke(); }
            if (studio)
            {
                p.BeginPath(); p.Arc(new(30,17),5,Angle.Degrees(0),Angle.Degrees(360));p.Stroke();
                Line(new(30,26),new(30,39),new(42,39),new(44,49));
                Line(new(30,28),new(42,24),new(47,12));
                Line(new(30,28),new(19,36)); Line(new(19,40),new(19,49),new(33,49));
                p.strokeColor = Palette.Ice40;p.lineWidth=2;
                p.BeginPath();p.Arc(new(31,31),21,Angle.Degrees(235),Angle.Degrees(305));p.Stroke();
            }
            else
            {
                Line(new(30,47),new(30,13),new(48,20),new(30,26));
                p.strokeColor = Palette.Glaucous50; Line(new(14,48),new(48,48));
                p.fillColor=Palette.Sand00;p.BeginPath();p.Arc(new(19,41),4,Angle.Degrees(0),Angle.Degrees(360));p.Fill();p.Stroke();
            }
        }
    }
}
