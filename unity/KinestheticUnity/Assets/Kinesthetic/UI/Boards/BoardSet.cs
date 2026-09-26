using System.Collections.Generic;
using Kinesthetic.Shell;
using Kinesthetic.UI.Remote;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Boards
{
    /// A venue's boards as one query root. A screen used to be one document, so its code asked one root
    /// for everything: `root.Q<Label>("status")`. Now the same screen is several boards standing at
    /// several stations, and the code asks this instead — `boards.Q<Label>("status")` — with the names
    /// unchanged. Names stay unique across a venue, so the first board that answers is the only one.
    ///
    /// Two things a single document gave for free are kept here. `Generation` moves whenever any board
    /// rebuilds its tree (a UIDocument disabled and enabled again makes a new root), so a screen that
    /// cached its elements knows to bind again. And a press lands on the button's own `clicked`: a
    /// world-space panel gets no pointer events at all, so GazeDwell and PanePointerInput report a name,
    /// and this presses the named button the way the remote host does, through one gate — the venue keeps
    /// writing `button.clicked += …` and never learns which road the press came by. On a Replica board
    /// the inputs never raise Committed (RemoteBoard.Intercepts sends the press to the Mac), so nothing
    /// here runs on a headset.
    ///
    /// The dwell only counts when a head drives the view (GazeDwell.HeadDriven): on the Mac the studio's
    /// camera is scripted, and a dwell on whatever its centre rests on — the summary's buttons, after a
    /// set — would be a press nobody made. The mouse covers the Mac; the dwell is the headset's.
    public sealed class BoardSet : MonoBehaviour
    {
        [SerializeField] List<UIDocument> boards = new();

        readonly List<VisualElement> lastRoots = new();
        readonly PressGate gate = new();
        readonly List<(GazeDwell dwell, PanePointerInput pointer, System.Action<string> entered, System.Action<string> committed)> subscriptions = new();
        KButton hot;

        public IReadOnlyList<UIDocument> Boards => boards;

        /// Increments when any board's root is a different element from last time it was asked.
        public int Generation { get; private set; }

        /// Every board has a tree in a panel.
        public bool Live
        {
            get
            {
                Refresh();
                foreach (var board in boards) if (!board || !board.enabled || board.rootVisualElement?.panel == null) return false;
                return boards.Count > 0;
            }
        }

        public void Adopt(UIDocument board)
        {
            if (board && !boards.Contains(board)) boards.Add(board);
        }

        public T Q<T>(string name = null, string className = null) where T : VisualElement
        {
            Refresh();
            foreach (var board in boards)
            {
                var found = board && board.enabled ? board.rootVisualElement?.Q<T>(name, className) : null;
                if (found != null) return found;
            }
            return null;
        }

        public VisualElement Q(string name = null, string className = null) => Q<VisualElement>(name, className);

        void Refresh()
        {
            if (lastRoots.Count != boards.Count) { lastRoots.Clear(); foreach (var board in boards) lastRoots.Add(board ? board.rootVisualElement : null); Generation++; Unhot(); return; }
            for (int i = 0; i < boards.Count; i++)
            {
                var root = boards[i] ? boards[i].rootVisualElement : null;
                if (ReferenceEquals(root, lastRoots[i])) continue;
                lastRoots[i] = root;
                Generation++;
                Unhot();
            }
        }

        void Update()
        {
            bool headDriven = GazeDwell.HeadDriven(Camera.main);
            foreach (var (dwell, _, _, _) in subscriptions) if (dwell && dwell.enabled != headDriven) dwell.enabled = headDriven;
            // A replica's `replace` op, or a rebuilt tree, can take the highlighted button out of its panel.
            if (hot != null && hot.panel == null) hot = null;
        }

        void Unhot()
        {
            if (hot != null && hot.panel != null) hot.Hot = false;
            hot = null;
        }

        void OnEnable()
        {
            foreach (var board in boards)
            {
                if (!board) continue;
                var dwell = board.GetComponent<GazeDwell>();
                var pointer = board.GetComponent<PanePointerInput>();
                var captured = board;
                System.Action<string> entered = name => Enter(captured, name);
                System.Action<string> committed = name => Press(captured, name);
                if (dwell) { dwell.Entered += entered; dwell.Committed += committed; }
                if (pointer) { pointer.Entered += entered; pointer.Committed += committed; }
                subscriptions.Add((dwell, pointer, entered, committed));
            }
        }

        void OnDisable()
        {
            foreach (var (dwell, pointer, entered, committed) in subscriptions)
            {
                if (dwell) { dwell.Entered -= entered; dwell.Committed -= committed; }
                if (pointer) { pointer.Entered -= entered; pointer.Committed -= committed; }
            }
            subscriptions.Clear();
            Unhot();
        }

        /// Hover comes from the ray, not from :hover, which never fires on a world-space panel. The inputs
        /// report the element under the ray and a null when it leaves, so the highlight follows the ray.
        void Enter(UIDocument board, string name)
        {
            Unhot();
            hot = name == null ? null : board.rootVisualElement?.Q<KButton>(name);
            if (hot != null) hot.Hot = true;
        }

        void Press(UIDocument board, string name)
        {
            var button = board.rootVisualElement?.Q<Button>(name);
            if (button == null || !button.enabledInHierarchy || !gate.Accept(board.name + "/" + name)) return;
            Unhot();
            PanelWire.Click(button);
        }
    }
}
