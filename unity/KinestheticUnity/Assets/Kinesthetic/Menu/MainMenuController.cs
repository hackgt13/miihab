using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    public sealed class MainMenuController : MonoBehaviour
    {
        VisualElement root, help;
        Button[] cards;
        Button music;
        Label caption;
        ActivityNavigation navigation;
        int selected;

        // Every activation this menu offers, by element name. Both the pointer's clicked handler and the gaze
        // dwell run out of this one table, so the two input paths can never drift apart.
        readonly Dictionary<string, Action> actions = new();

        void Start() { navigation = ActivityNavigation.Ensure(); Bind(); }
        void Update() { if (root == null) Bind(); }

        void Bind()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            if (tree?.Q<Button>("golf-card") == null) return;
            root = tree;
            cards = new[] { root.Q<Button>("golf-card"), root.Q<Button>("studio-card") };
            caption = root.Q<Label>("selection-caption");
            music = root.Q<Button>("music"); help = root.Q("help-overlay");
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                cards[i].RegisterCallback<PointerEnterEvent>(_ => Select(index, true));
                cards[i].RegisterCallback<FocusInEvent>(_ => Select(index, true));
                Act(cards[i], () => navigation.LoadActivity(index == 0 ? ActivityNavigation.GolfScene : ActivityNavigation.StudioScene));
            }
            Act(music, navigation.ToggleMusic);
            navigation.MusicChanged += MusicChanged;
            MusicChanged(navigation.MusicEnabled);
            var helpButton = root.Q<Button>("help"); var close = root.Q<Button>("help-close");
            Act(helpButton, () => { navigation.PlaySelect(); help.RemoveFromClassList("hidden"); close.Focus(); });
            Act(close, CloseHelp);
            root.RegisterCallback<NavigationCancelEvent>(e => { if (!help.ClassListContains("hidden")) { CloseHelp(); e.StopPropagation(); } });
            root.RegisterCallback<NavigationMoveEvent>(e => {
                if (!help.ClassListContains("hidden")) return;
                if (e.direction != NavigationMoveEvent.Direction.Left && e.direction != NavigationMoveEvent.Direction.Right) return;
                Select(e.direction == NavigationMoveEvent.Direction.Left ? 0 : 1, false);
                cards[selected].Focus(); e.StopPropagation();
            });
            var gaze = GetComponent<GazeDwell>();
            if (gaze) { gaze.Entered += Gazed; gaze.Committed += Commit; }
            root.Q("golf-icon").generateVisualContent += c => DrawIcon(c, false);
            root.Q("studio-icon").generateVisualContent += c => DrawIcon(c, true);
            root.schedule.Execute(() => cards[selected].Focus());
        }

        // The dwell reports an element name; the pointer reports a click. Both land here.
        void Act(Button button, Action action) { actions[button.name] = action; button.clicked += action; }

        void Gazed(string name)
        {
            int card = System.Array.FindIndex(cards, c => c.name == name);
            if (card >= 0) { Select(card, true); cards[card].Focus(); return; }
            if (actions.ContainsKey(name)) navigation.PlayHover();
        }

        void Commit(string name) { if (actions.TryGetValue(name, out var action)) action(); }

        void Select(int index, bool sound)
        {
            bool changed = index != selected; selected = index;
            for (int i = 0; i < cards.Length; i++) cards[i].EnableInClassList("selected", i == selected);
            caption.text = selected == 0 ? "Take a swing with a friend." : "Make a little time for yourself.";
            if (sound && changed) navigation.PlayHover();
        }
        void MusicChanged(bool enabled) { if (music != null) music.text = enabled ? "Music: On" : "Music: Off"; }
        void CloseHelp() { help.AddToClassList("hidden"); navigation.PlayBack(); root.Q<Button>("help").Focus(); }
        void OnDestroy() { if (navigation) navigation.MusicChanged -= MusicChanged; }

        static void DrawIcon(MeshGenerationContext ctx, bool studio)
        {
            var p = ctx.painter2D;
            p.strokeColor = new Color(.17f,.59f,.78f); p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.Stroke(); }
            if (studio)
            {
                p.BeginPath(); p.Arc(new(27,15),5,Angle.Degrees(0),Angle.Degrees(360));p.Stroke();
                Line(new(27,24),new(27,36),new(38,36),new(40,45));
                Line(new(27,26),new(38,22),new(43,11));
                Line(new(27,26),new(17,33)); Line(new(17,37),new(17,45),new(30,45));
                p.strokeColor = new Color(.38f,.77f,.87f);p.lineWidth=2;
                p.BeginPath();p.Arc(new(28,28),19,Angle.Degrees(235),Angle.Degrees(305));p.Stroke();
            }
            else
            {
                Line(new(27,43),new(27,12),new(44,18),new(27,24));
                p.strokeColor = new Color(.47f,.73f,.48f); Line(new(13,44),new(44,44));
                p.fillColor=Color.white;p.BeginPath();p.Arc(new(17,37),4,Angle.Degrees(0),Angle.Degrees(360));p.Fill();p.Stroke();
            }
        }
    }
}
