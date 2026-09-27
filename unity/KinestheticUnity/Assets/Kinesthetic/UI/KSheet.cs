using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A sheet of paper that is handed to you and taken away again. The summary card was already this
    /// shape — a document that stands at the Focus station, is read, and is dismissed — and an activity's
    /// briefing is the same object at the other end of the session, so both are this one component.
    ///
    ///     <k:KSheet name="summary-card" entrance="Below" />
    ///
    /// It enters with ease-out-back, which overshoots a few millimetres and settles, and it travels at a
    /// slight angle that straightens as it lands: a page put down by a hand rather than a panel faded in.
    /// It leaves faster, straight and downward — set down, not deleted.
    ///
    /// The motion lives in Sheet.uss rather than in the screen that uses it, because `transition` is paint
    /// and a sealed screen sets layout only (scripts/check_ui.py, UI.md). So every sheet in every venue
    /// enters the same way, and a screen chooses only which side it comes from.
    [UxmlElement]
    public partial class KSheet : KSurface
    {
        /// Which side it comes from. Motivated by the room rather than by the layout: in the studio the
        /// coach stands ahead-right, so the briefing he hands you arrives from the Right.
        public enum Entrance { Right, Left, Below }

        /// Which side it leaves by. `Back` retraces the entrance, which is right for something that was
        /// only being shown — a summary rises and sinks again. A page you have finished with is not put
        /// back where it came from: it is swept aside, and that is a different side from the one that
        /// handed it to you.
        public enum Exit { Back, Left, Right, Below }

        /// What Sheet.uss spends leaving. The sheet is taken out of layout once it has gone, and this is
        /// how long to wait before doing it.
        const long ExitMilliseconds = 260;

        const string Block = "k-sheet";
        const string Offstage = Block + "--offstage", Gone = Block + "--gone";

        Entrance entranceValue;
        Exit exitValue;
        IVisualElementScheduledItem pending;

        [UxmlAttribute]
        public Entrance entrance { get => entranceValue; set { entranceValue = value; if (Presented) return; KStyles.Variant(this, Block, value); } }

        [UxmlAttribute]
        public Exit exit { get => exitValue; set => exitValue = value; }

        /// The side the sheet is offstage on while it is leaving.
        Entrance Leaving => exitValue switch
        {
            Exit.Left => Entrance.Left,
            Exit.Right => Entrance.Right,
            Exit.Below => Entrance.Below,
            _ => entranceValue,
        };

        /// Whether the sheet is standing, or on its way to standing. A caller asking "is the summary up"
        /// means this, not whether the entrance has finished playing.
        public bool Presented { get; private set; }

        public KSheet()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Sheet");
            entrance = Entrance.Right;
            AddToClassList(Offstage); AddToClassList(Gone);   // nobody has been handed it yet
        }

        /// Hand it over. Always from offstage, so presenting a sheet that is already up replays the
        /// entrance rather than doing nothing visible.
        public void Present()
        {
            Presented = true;
            Cancel();
            RemoveFromClassList(Gone);
            KStyles.Variant(this, Block, entranceValue);   // back to the side it is handed from
            AddToClassList(Offstage);
            // The offstage style has to be laid out once before the landed style is somewhere to travel
            // from. A sheet built and shown inside a single frame has no resolved geometry yet, so dropping
            // the class straight away computes no "from" value and the sheet just appears where it landed.
            // Geometry is the signal; the timer covers a rebuild at a size the sheet already had.
            RegisterCallback<GeometryChangedEvent>(Land);
            pending = schedule.Execute(() => Land(null)); pending.ExecuteLater(96);
        }

        void Land(GeometryChangedEvent _)
        {
            Cancel();
            if (Presented) RemoveFromClassList(Offstage);
        }

        void Cancel()
        {
            UnregisterCallback<GeometryChangedEvent>(Land);
            pending?.Pause(); pending = null;
        }

        /// Take it away, and drop it out of layout once it has left.
        public void Dismiss()
        {
            if (!Presented) { Hide(); return; }
            Presented = false;
            Cancel();
            // Swapped while the sheet still stands, where the side classes draw nothing: only the move to
            // offstage reads them, so this decides where it goes without moving it first.
            KStyles.Variant(this, Block, Leaving);
            AddToClassList(Offstage);
            pending = schedule.Execute(() => AddToClassList(Gone)); pending.ExecuteLater(ExitMilliseconds);
        }

        /// Gone now, with no exit: a reset rather than a dismissal — starting a set again, or rebuilding a
        /// board's tree under a sheet that was already down.
        public void Hide()
        {
            Presented = false;
            Cancel();
            AddToClassList(Offstage); AddToClassList(Gone);
        }
    }
}
