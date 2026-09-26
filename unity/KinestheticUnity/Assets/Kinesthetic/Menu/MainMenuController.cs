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
                cards[i].clicked += () => navigation.LoadActivity(index == 0 ? ActivityNavigation.GolfScene : ActivityNavigation.StudioScene);
            }
            music.clicked += navigation.ToggleMusic;
            navigation.MusicChanged += MusicChanged;
            MusicChanged(navigation.MusicEnabled);
            var helpButton = root.Q<Button>("help"); var close = root.Q<Button>("help-close");
            helpButton.clicked += () => { navigation.PlaySelect(); help.RemoveFromClassList("hidden"); close.Focus(); };
            close.clicked += CloseHelp;
            root.RegisterCallback<NavigationCancelEvent>(e => { if (!help.ClassListContains("hidden")) { CloseHelp(); e.StopPropagation(); } });
            root.RegisterCallback<NavigationMoveEvent>(e => {
                if (!help.ClassListContains("hidden")) return;
                if (e.direction != NavigationMoveEvent.Direction.Left && e.direction != NavigationMoveEvent.Direction.Right) return;
                Select(e.direction == NavigationMoveEvent.Direction.Left ? 0 : 1, false);
                cards[selected].Focus(); e.StopPropagation();
            });
            root.Q("golf-icon").generateVisualContent += c => DrawIcon(c, false);
            root.Q("studio-icon").generateVisualContent += c => DrawIcon(c, true);
            var backdrop = root.Q("backdrop");
            backdrop.generateVisualContent += c => DrawBackdrop(c, backdrop.contentRect);
            backdrop.schedule.Execute(backdrop.MarkDirtyRepaint).Every(40);
            root.schedule.Execute(() => cards[selected].Focus());
        }

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

        static void DrawBackdrop(MeshGenerationContext ctx, Rect r)
        {
            if (r.width < 1 || r.height < 1) return;
            var p = ctx.painter2D; float w = r.width, h = r.height;
            p.fillGradient = FillGradient.MakeLinearGradient(Color.white, new Color(.86f,.95f,.99f), Vector2.zero, new Vector2(0,h), AddressMode.Clamp);
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new(w,0)); p.LineTo(new(w,h)); p.LineTo(new(0,h)); p.ClosePath(); p.Fill();
            float drift = Mathf.Sin(Time.unscaledTime * .22f) * 20;
            for (int i = 0; i < 10; i++)
            {
                float y = h * .76f + i * 15;
                p.strokeColor = new Color(.30f,.71f,.88f,.09f + i*.007f); p.lineWidth = 1.2f;
                p.BeginPath(); p.MoveTo(new(-40,y+100));
                p.BezierCurveTo(new(w*.35f,y-45+drift), new(w*.65f,y+135-drift), new(w+40,y-80)); p.Stroke();
            }
            p.strokeColor = new Color(.43f,.77f,.92f,.12f); p.lineWidth = 2;
            for (int i = 0; i < 3; i++)
            { p.BeginPath(); p.Arc(new(w-20, 20), 160+i*28, Angle.Degrees(70), Angle.Degrees(210)); p.Stroke(); }
        }
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
