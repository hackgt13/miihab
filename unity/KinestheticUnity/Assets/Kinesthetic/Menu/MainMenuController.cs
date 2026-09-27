using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Kinesthetic.Shell;
using Kinesthetic.UI;
using Kinesthetic.Activities;

namespace Kinesthetic.Menu
{
    public sealed class MainMenuController : MonoBehaviour
    {
        VisualElement root;                                     // the board
        VisualElement coachingRoot, galleryRoot, friendsRoot;   // the panes standing round it
        PaneCarousel carousel;
        VisualElement helpOverlay, nameOverlay;
        TextField nameField;
        Button[] cards;             // every gallery card, in catalog order; only one page of them is showing
        ActivityEntry[] galleryEntries;
        int page;
        Label pageLabel;
        Button music;
        Label caption;
        ActivityNavigation navigation;
        MenuDashboardModel model;
        CoachingPlanModel plan;
        int selected;

        // Every activation this menu offers, by element name. Both the pointer's clicked handler and the gaze
        // dwell run out of this one table, so the two input paths can never drift apart.
        readonly Dictionary<string, Action> actions = new();

        // One press is one action, whichever of the input paths delivered it.
        readonly PressGate press = new();

        // What is reachable right now. A pointer cannot reach behind an open sheet because the shade covers
        // it, but the gaze ray is resolved from element rects alone and would happily commit a tile the
        // person cannot even see. So the menu decides what counts as reachable, per open layer.
        // The board's own scope is finished in Bind, once the ring says which panes it holds: every pane
        // gets a button here (see BuildPaneLinks), and a button the gaze cannot commit is a broken one.
        static readonly string[] BoardScope = { "start-activity", "friends", "music", "help", "intro", "edit-name" };
        static readonly string[] FriendsScope = { "friends-invite", "friends-accept" };
        static readonly string[] CoachingScope = { "visit-therapist" };
        static readonly string[] HelpScope = { "help-close" };
        static readonly string[] NameScope = { "name-save", "name-cancel" };
        // The gallery's scope is the page of cards showing plus the pager, rebuilt whenever the page turns.
        const int CardsPerPage = 6, CardsPerRow = 3;
        static readonly string[] PagerScope = { "gallery-prev", "gallery-next" };
        string[] activityScope = PagerScope;
        string[] baseScope = BoardScope;
        string[] scope = BoardScope;

        // What each pane's button says under its title. Keyed by slot id; a pane with no line here still
        // gets a button, captioned from its title. To add a pane: adopt it in MainMenuSetup, and if the
        // title alone does not say why someone would go there, add one line here.
        static readonly Dictionary<string, string> PaneBlurbs = new()
        {
            ["coaching"] = "Your plan, and how it is going",
            ["gallery"] = "Everything you can play",
            ["friends"] = "Who is cheering you on",
        };

        /// The button that turns the ring to a pane, by slot id: "pane-gallery", "pane-friends".
        static string PaneLink(string slotId) => $"pane-{slotId}";

        // The board's figures come from the coordinator on this Mac (coordinator/dashboard.ts). The menu is a
        // Mac scene; on a headset 127.0.0.1 would be the headset itself, so the board would keep its demo data.
        const string DashboardUrl = "http://127.0.0.1:8766/api/dashboard";
        // The coaching pane reads the plan itself rather than the board's summary of it, because the dose,
        // the ceiling and the progression envelope are the whole point of that pane and /api/dashboard
        // reduces them to one target angle.
        const string PlanUrl = "http://127.0.0.1:8766/api/plans/active";
        JObject dashboard;   // the last reply; null until one arrives

        MenuDashboardModel BuildModel() => dashboard != null ? MenuDashboardModel.FromCoordinator(dashboard) : MenuDashboardModel.Empty();

        IEnumerator LoadDashboard()
        {
            using var request = UnityWebRequest.Get(DashboardUrl);
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || root == null) yield break;   // keep the demo board offline
            try { dashboard = JObject.Parse(request.downloadHandler.text); } catch { yield break; }
            model = BuildModel();
            bound = true;
            MenuDashboard.Populate(root, model);
            // Coaching draws the trend and the dose from these same figures, so it is redrawn with them
            // rather than left holding the placeholder the board has just replaced.
            if (coachingRoot != null) CoachingPanel.Populate(coachingRoot, plan, model);
        }

        IEnumerator LoadPlan()
        {
            using var request = UnityWebRequest.Get(PlanUrl);
            request.timeout = 3;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || coachingRoot == null) yield break;
            try { plan = CoachingPlanModel.FromCoordinator(JObject.Parse(request.downloadHandler.text)); }
            catch { yield break; }   // a plan that will not parse is not a reason to blank the pane
            CoachingPanel.Populate(coachingRoot, plan, model);
        }

        bool bound;   // the dashboard has answered at least once
        bool wired;   // and the subscriptions below happened, however often Bind is retried
        float nextTry;

        void Start() { navigation = ActivityNavigation.Ensure(); Bind(); }
        // Retried until the dashboard answers, but on a timer rather than every frame:
        // each retry starts another request, and a frame's worth of those is a burst of
        // identical calls at the coordinator for no gain.
        void Update() { if (!bound && Time.unscaledTime >= nextTry) Bind(); }

        void Bind()
        {
            var tree = GetComponent<UIDocument>().rootVisualElement;
            if (tree?.Q<Button>("start-activity") == null) return;
            nextTry = Time.unscaledTime + 1f;
            // `bound` is only set once the dashboard replies, so Bind ran again on every
            // frame until then and everything below subscribed again with it: two handlers
            // on Send meant one press posted two messages, and FriendsPanel built a second
            // introductions card. Subscribe once; a retry re-asks for the data only.
            if (wired) { StartCoroutine(LoadDashboard()); return; }
            wired = true;
            root = tree;
            model = BuildModel();

            // The gallery and friends are panes of their own now, a quarter-turn either side. Reach them
            // through the ring rather than through this document, which no longer contains them.
            carousel = FindAnyObjectByType<PaneCarousel>();
            galleryRoot = PaneRoot("gallery");
            friendsRoot = PaneRoot("friends");
            if (carousel == null || galleryRoot == null || friendsRoot == null) { root = null; return; }
            // Optional, unlike the two above: a scene generated before the coaching pane existed still has a
            // working board, and the ring's own slot list is what decides whether a button appears for it.
            coachingRoot = PaneRoot("coaching");

            helpOverlay = root.Q("help-overlay");
            nameOverlay = root.Q("name-overlay");
            nameField = root.Q<TextField>("name-field");
            caption = galleryRoot.Q<Label>("selection-caption");
            BuildGallery();
            music = root.Q<Button>("music");

            Act(root.Q<Button>("start-activity"), StartFirst);
            // The bottom-right of the board is one button per pane in the ring. None of them opens
            // anything — each turns the ring to a pane that was standing there the whole time. The panes
            // carry no Back button of their own: the ring's arrows and dots are the way home, and a pane
            // that also closed itself would be a second way of doing the one thing.
            BuildPaneLinks();
            if (coachingRoot != null) Act(coachingRoot.Q<Button>("visit-therapist"), VisitTherapist);
            Act(music, navigation.ToggleMusic);
            Act(root.Q<Button>("help"), () => OpenSheet(helpOverlay, HelpScope, root.Q<Button>("help-close")));
            Act(root.Q<Button>("help-close"), () => CloseSheet(helpOverlay, "help"));
            Act(root.Q<Button>("intro"), navigation.LoadTutorial);
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
                if (helpOverlay == null) { }
                if (Showing(helpOverlay)) { CloseSheet(helpOverlay, "help"); e.StopPropagation(); }
                else if (Showing(nameOverlay)) { CloseSheet(nameOverlay, "edit-name"); e.StopPropagation(); }
            });
            root.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (carousel.Current.id != "gallery") return;
                if (e.direction != NavigationMoveEvent.Direction.Left && e.direction != NavigationMoveEvent.Direction.Right) return;
                if (cards.Length == 0) return;
                int step = e.direction == NavigationMoveEvent.Direction.Left ? -1 : 1;
                int next = (selected + step + cards.Length) % cards.Length;
                if (next / CardsPerPage != page) ShowPage(next / CardsPerPage);
                Select(next, false);
                cards[selected].Focus(); e.StopPropagation();
            });

            // Added at runtime so the generated menu scene needs no change.
            var friends = GetComponent<FriendsPanel>() ?? gameObject.AddComponent<FriendsPanel>();
            friends.Attach(friendsRoot, navigation);
            // FriendsPanel still owns its own buttons; the friends button here only turns the ring.
            actions["friends"] = () => carousel.Show("friends");

            // Each pane has its own dwell, because GazeDwell resolves elements from the document it sits on.
            // They all report into the one action table, so head and pointer cannot drift apart.
            foreach (var slot in carousel.Slots)
            {
                var dwell = slot.pane.GetComponent<GazeDwell>();
                if (dwell != null) { dwell.Entered += Gazed; dwell.Committed += Commit; }

                // The mouse speaks the same two events, so a click and a dwell reach the same action and
                // the hover a world-space panel cannot raise on its own arrives through Gazed like any other.
                var pointer = slot.pane.GetComponent<PanePointerInput>();
                if (pointer != null) { pointer.Entered += Gazed; pointer.Committed += Commit; }
            }

            // What the gaze may commit follows whichever pane is facing, which is what the scope list used
            // to do for a stack of sheets. One mechanism, now driven by where the person is looking.
            // The headset's ring is its own geometry and follows this one by name (QuestMenuMirror).
            carousel.Changed += slot => Kinesthetic.UI.Remote.UiCue.SendFace(slot.id);
            carousel.Settled += slot => scope = slot.id switch
            {
                "gallery" => activityScope,
                "friends" => FriendsScope,
                "coaching" => CoachingScope,
                _ => baseScope,
            };

            MenuDashboard.Populate(root, model);
            if (coachingRoot != null)
            {
                plan = CoachingPlanModel.Placeholder();
                CoachingPanel.Populate(coachingRoot, plan, model);
                StartCoroutine(LoadPlan());
            }
            StartCoroutine(LoadDashboard());
            Select(0, false);
            root.schedule.Execute(() => root.Q<Button>("start-activity").Focus());
        }

        // The dwell reports an element name; the pointer reports a click. Both land here.
        // UI Toolkit's own click is kept — it is what a keyboard submit on a focused button rides — but it
        // reports the element rather than running the action, so it goes through the same gate as the
        // pointer and the dwell instead of straight past them.
        void Act(Button button, Action action)
        {
            if (button == null) return;
            string element = button.name;
            actions[element] = action;
            button.clicked += () => Fire(element);
        }

        /// The one place a press becomes an action. Every input path ends here: the dwell, the pointer, the
        /// button's own click, and anything added later.
        void Fire(string element)
        {
            if (!InScope(element) || !press.Accept(element)) return;
            if (actions.TryGetValue(element, out var action)) action();
        }

        bool InScope(string name) => Array.IndexOf(scope, name) >= 0;

        /// One button for every pane in the ring except the board itself, in a row where "Choose activity"
        /// used to be. Built from the carousel's own slots rather than authored in the UXML, so a pane
        /// adopted in MainMenuSetup shows up here without anyone remembering to add it — and in the order
        /// they physically stand, left to right, so the row is a map of the ring. A pane to the left gets
        /// its chevron on the left, a pane to the right on the right: the way the ring will turn.
        void BuildPaneLinks()
        {
            var links = root.Q("pane-links");
            if (links == null) return;
            links.Clear();

            // Where the board itself stands in the row: panes before it are reached by turning left, panes
            // after it by turning right. The board is this document's own pane.
            var order = carousel.LeftToRight;
            bool IsBoard(PaneCarousel.Slot s) => s.pane == transform || s.id == "home";
            int home = order.FindIndex(i => IsBoard(carousel.Slots[i]));
            var names = new List<string>();
            for (int at = 0; at < order.Count; at++)
            {
                var slot = carousel.Slots[order[at]];
                if (IsBoard(slot)) continue;
                string id = slot.id, element = PaneLink(id);
                var button = new Button { name = element };
                button.AddToClassList("pane-link");
                var copy = new VisualElement { pickingMode = PickingMode.Ignore };
                copy.AddToClassList("pane-link-copy");
                var title = new Label(slot.title) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("pane-link-title");
                var sub = new Label(PaneBlurbs.TryGetValue(id, out var blurb) ? blurb : $"Turn to {slot.title}") { pickingMode = PickingMode.Ignore };
                sub.AddToClassList("pane-link-sub");
                copy.Add(title); copy.Add(sub);
                bool left = at < home;
                var arrow = new Label(left ? "‹" : "›") { pickingMode = PickingMode.Ignore };
                arrow.AddToClassList("pane-link-arrow");
                arrow.AddToClassList(left ? "leading" : "trailing");
                if (left) { button.Add(arrow); button.Add(copy); } else { button.Add(copy); button.Add(arrow); }
                button.tooltip = slot.title;
                links.Add(button);
                Act(button, () => carousel.Show(id));
                names.Add(element);
            }
            if (links.childCount > 0) links[links.childCount - 1].AddToClassList("last");

            baseScope = BoardScope.Concat(names).ToArray();
            if (ReferenceEquals(scope, BoardScope)) scope = baseScope;
        }
        /// The root of a pane standing in the ring, by name.
        VisualElement PaneRoot(string id)
        {
            foreach (var slot in carousel == null ? System.Array.Empty<PaneCarousel.Slot>() : (System.Collections.Generic.IEnumerable<PaneCarousel.Slot>)carousel.Slots)
                if (slot.id == id)
                    return slot.pane.GetComponent<UIDocument>()?.rootVisualElement;
            return null;
        }

        static bool Showing(VisualElement overlay) => overlay != null && !overlay.ClassListContains("hidden");

        void Gazed(string name)
        {
            if (!InScope(name)) return;
            int card = Array.FindIndex(cards, c => c != null && c.name == name);
            if (card >= 0) { Select(card, true); cards[card].Focus(); return; }
            navigation.PlayHover();
        }

        void Commit(string name) => Fire(name);

        /// The CTA launches whatever the plan says is still outstanding, so the board's headline action
        /// and its Today list can never disagree about what comes next.
        void StartFirst()
        {
            if (model != null && model.PlanUpdated) { VisitTherapist(); return; }   // the plan changed: hear why first
            var next = model.FirstOutstanding();
            if (next is not { } task) { carousel.Show("gallery"); cards[selected]?.Focus(); return; }
            Launch(task.activityId);
        }

        void Launch(string activityId) => navigation.LoadActivity(activityId);

        /// The visit is a catalog activity like any other (therapist.visit), so it goes through the clinic's
        /// door the way the studio goes through the pavilion's. If the build has no visit scene, navigation
        /// says so in its own dialog rather than the press going nowhere.
        void VisitTherapist() => Launch("therapist.visit");

        void OpenNameSheet()
        {
            if (nameField != null) nameField.value = MenuProfile.Name;
            OpenSheet(nameOverlay, NameScope, nameField);
        }

        void SaveName()
        {
            MenuProfile.Name = nameField?.value ?? MenuProfile.DefaultName;
            model = BuildModel();
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
            scope = baseScope;
            navigation.PlayBack();
            root.Q<Button>(focusName)?.Focus();
        }

        /// The gallery is the catalog: one card for every activity with a card (activities.json), golf, the studio and
        /// bowling beside every movement in the exercise library, each built the same way and the same size. Nothing is
        /// authored in Gallery.uxml, so `npm run catalog` is all a new movement needs to be here as an equal.
        void BuildGallery()
        {
            var grid = galleryRoot.Q("cards");
            galleryEntries = ActivityCatalog.Gallery;
            grid?.Clear();
            cards = new Button[galleryEntries.Length];
            for (int i = 0; i < galleryEntries.Length; i++)
            {
                var entry = galleryEntries[i];
                var card = cards[i] = Card(entry, i % CardsPerRow == 1);
                int index = i; string id = entry.Id;
                card.RegisterCallback<PointerEnterEvent>(_ => Select(index, true));
                card.RegisterCallback<FocusInEvent>(_ => Select(index, true));
                Act(card, () => Launch(id));
                grid?.Add(card);
            }
            var pager = galleryRoot.Q("pager");
            if (pager != null)
            {
                pager.Clear();
                var prev = new KButton { name = "gallery-prev", text = "‹", shape = KButton.Shape.Round, size = KButton.Size.Small, tooltip = "Previous page" };
                var next = new KButton { name = "gallery-next", text = "›", shape = KButton.Shape.Round, size = KButton.Size.Small, tooltip = "Next page" };
                pageLabel = new Label { pickingMode = PickingMode.Ignore };
                pageLabel.AddToClassList("page-label");
                pager.Add(prev); pager.Add(pageLabel); pager.Add(next);
                Act(prev, () => TurnPage(-1));
                Act(next, () => TurnPage(1));
            }
            ShowPage(0);
        }

        /// One card: the same tag, icon, title, description and arrow the authored cards had, for every activity.
        static Button Card(ActivityEntry entry, bool middle)
        {
            var card = new Button { name = "card-" + entry.Id, tooltip = entry.DisplayName };
            card.AddToClassList("activity-card"); card.AddToClassList("gallery-card");
            if (middle) card.AddToClassList("row-middle");
            VisualElement Part(VisualElement parent, VisualElement child, string cls)
            { child.pickingMode = PickingMode.Ignore; child.AddToClassList(cls); parent.Add(child); return child; }
            Part(card, new Label(entry.CardTag), "preview-tag");
            var info = Part(card, new VisualElement(), "card-info");
            Part(info, new VisualElement(), "mode-icon").generateVisualContent += c => DrawCardIcon(c, entry);
            var copy = Part(info, new VisualElement(), "card-copy");
            Part(copy, new Label(entry.DisplayName), "card-title");
            Part(copy, new Label(entry.Tagline), "card-description");
            Part(info, new Label("›"), "card-arrow");
            return card;
        }

        int Pages => Mathf.Max(1, (cards.Length + CardsPerPage - 1) / CardsPerPage);
        void TurnPage(int step) { navigation.PlaySelect(); ShowPage((page + step + Pages) % Pages); Select(page * CardsPerPage, false); }

        void ShowPage(int index)
        {
            page = Mathf.Clamp(index, 0, Pages - 1);
            var showing = new List<string>();
            for (int i = 0; i < cards.Length; i++)
            {
                bool on = i / CardsPerPage == page;
                cards[i].EnableInClassList("hidden", !on);
                if (on) showing.Add(cards[i].name);
            }
            if (pageLabel != null) pageLabel.text = $"{page + 1} of {Pages}";
            galleryRoot.Q("pager")?.EnableInClassList("hidden", Pages < 2);
            activityScope = showing.Concat(PagerScope).ToArray();
            if (carousel != null && carousel.Current.id == "gallery") scope = activityScope;
        }

        void Select(int index, bool sound)
        {
            if (cards.Length == 0) return;
            bool changed = index != selected; selected = Mathf.Clamp(index, 0, cards.Length - 1);
            for (int i = 0; i < cards.Length; i++) cards[i]?.EnableInClassList("selected", i == selected);
            if (caption != null) caption.text = galleryEntries[selected].CardCaption;
            if (sound && changed) navigation.PlayHover();
        }

        void MusicChanged(bool enabled) { if (music != null) music.text = enabled ? "Music: On" : "Music: Off"; }
        void OnDestroy() { if (navigation) navigation.MusicChanged -= MusicChanged; }

        /// Golf, the studio and bowling keep their drawings. A movement is a figure with the tracker marked where it is
        /// worn, so the card itself says where the AirPod goes.
        static void DrawCardIcon(MeshGenerationContext ctx, ActivityEntry entry)
        {
            switch (entry.Id)
            {
                case "golf.adaptive": DrawIcon(ctx, false); return;
                case "rehab.studio": DrawIcon(ctx, true); return;
                case "bowling.adaptive": DrawBowlingIcon(ctx); return;
            }
            var p = ctx.painter2D;
            p.strokeColor = Palette.Prussian60; p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.Stroke(); }
            p.BeginPath(); p.Arc(new(31, 14), 5.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            Line(new(31, 20), new(31, 37));
            Line(new(20, 33), new(31, 24), new(42, 33));
            Line(new(24, 52), new(31, 37), new(38, 52));
            // One dot for each AirPod the tag names: two for a two-AirPod movement.
            string tag = entry.CardTag ?? "";
            var mounts = new (string word, Vector2 at)[] {
                ("EARS", new(37, 14)), ("CHEST", new(31, 27)), ("UPPER ARM", new(37, 28)), ("WRIST", new(42, 33)), ("HANDLE", new(42, 33)),
                ("THIGH", new(35, 43)), ("SHIN", new(37, 47)), ("ANKLE", new(38, 51)) };
            p.fillColor = Palette.Cerulean40; p.strokeColor = Palette.Sand00; p.lineWidth = 2;
            foreach (var (word, at) in mounts)
                if (tag.Contains(word)) { p.BeginPath(); p.Arc(at, 5, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke(); }
        }

        static void DrawBowlingIcon(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.strokeColor = Palette.Prussian60; p.lineWidth = 2.5f; p.lineJoin = LineJoin.Round;
            p.fillColor = Palette.Sand00;
            p.BeginPath(); p.MoveTo(new(35, 12));
            p.BezierCurveTo(new(32, 4), new(47, 4), new(44, 12));
            p.BezierCurveTo(new(39, 21), new(52, 28), new(49, 44));
            p.LineTo(new(31, 44));
            p.BezierCurveTo(new(27, 28), new(39, 21), new(35, 12));
            p.ClosePath(); p.Fill(); p.Stroke();
            p.strokeColor = Palette.Slate50;
            p.BeginPath(); p.MoveTo(new(35, 19)); p.LineTo(new(44, 19)); p.Stroke();
            p.fillColor = Palette.Prussian60;
            p.BeginPath(); p.Arc(new(23, 39), 13, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.fillColor = Palette.Sand00;
            foreach (var point in new[] { new Vector2(21, 33), new Vector2(27, 34), new Vector2(23, 39) })
            { p.BeginPath(); p.Arc(point, 1.7f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
        }

        static void DrawIcon(MeshGenerationContext ctx, bool studio)
        {
            var p = ctx.painter2D;
            p.strokeColor = Palette.Prussian60; p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for(int i=1;i<points.Length;i++)p.LineTo(points[i]);p.Stroke(); }
            if (studio)
            {
                p.BeginPath(); p.Arc(new(30,17),5,Angle.Degrees(0),Angle.Degrees(360));p.Stroke();
                Line(new(30,26),new(30,39),new(42,39),new(44,49));
                Line(new(30,28),new(42,24),new(47,12));
                Line(new(30,28),new(19,36)); Line(new(19,40),new(19,49),new(33,49));
                p.strokeColor = Palette.Cerulean40;p.lineWidth=2;
                p.BeginPath();p.Arc(new(31,31),21,Angle.Degrees(235),Angle.Degrees(305));p.Stroke();
            }
            else
            {
                Line(new(30,47),new(30,13),new(48,20),new(30,26));
                p.strokeColor = Palette.Slate50; Line(new(14,48),new(48,48));
                p.fillColor=Palette.Sand00;p.BeginPath();p.Arc(new(19,41),4,Angle.Degrees(0),Angle.Degrees(360));p.Fill();p.Stroke();
            }
        }
    }
}
