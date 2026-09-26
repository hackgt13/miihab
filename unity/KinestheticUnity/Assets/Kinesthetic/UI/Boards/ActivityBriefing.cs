using System;
using Kinesthetic.Shell;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Boards
{
    /// One measured line of a prescription: what it is, and what it says. "Repetitions / 8".
    public readonly struct BriefingLine
    {
        public readonly string label, value;
        public BriefingLine(string label, string value) { this.label = label; this.value = value; }
    }

    /// What an activity hands you before it begins: the exercise, the dose, and whatever a person wrote
    /// about it. A value, so an activity describes itself and does not lay anything out — the studio's
    /// prescription and a round of golf's card are the same document with different lines in it.
    ///
    /// Every field is content the activity already has. Nothing here is decorative: a briefing with an
    /// invented clinician, date or signature on it would be a fabricated record, which is exactly what
    /// this codebase took out once already. An empty field is left off the sheet.
    public struct Briefing
    {
        public string eyebrow;       // "PRESCRIBED PLAN · V3"
        public string title;         // "Shoulder raises"
        public string subtitle;      // "Right arm, seated"
        public BriefingLine[] lines; // the dose, one line each
        public string note;          // words a person wrote, verbatim; omitted when there are none
        public string noteFrom;      // who wrote them, e.g. "FROM YOUR CARE TEAM"
        public string action;        // what the button says: "Begin my set"
    }

    /// The briefing sheet, for any activity. It stands at the Focus station — the one the stations were
    /// given "a summary, a setup" for — reads as a page of paper, and is dismissed by the person before
    /// anything starts.
    ///
    /// Dismissing it is the point. Before this, the studio began a set because the AirPod had been still
    /// for a second and a half, which is a fact about a sensor standing in for a decision by a patient.
    /// Now the activity waits on `Dismissed`, and starting is something someone did.
    ///
    /// A venue adopts it by putting one element on its Focus board:
    ///
    ///     <ui:VisualElement name="briefing" class="briefing-host" />
    ///
    /// and calling Bind every time it binds its boards — a board that rebuilt its tree hands back a new
    /// host, and Bind mounts into it again, in whatever state the sheet was already in.
    public sealed class ActivityBriefing
    {
        /// The element a venue's Focus board provides for the sheet to stand in.
        public const string HostName = "briefing";

        /// The button's name, so a gaze dwell or a pointer can find it the way BoardSet finds any button.
        public const string ActionName = "briefing-begin";

        const string Block = "k-briefing";

        /// How long after the room appears before the sheet is offered.
        const long BeatMilliseconds = 260;

        VisualElement host, lineBox, noteBox;
        KSheet sheet;
        KEyebrow eyebrow, noteFrom;
        KText title, subtitle, note;
        KButton action;
        Action began;
        Briefing content;
        bool described;
        IVisualElementScheduledItem waiting;

        /// Whether the person has put the sheet down. An activity gates its own start on this.
        public bool Dismissed { get; private set; }

        /// Mount into `host`, building the sheet the first time and after any rebuild of the board's tree.
        /// Idempotent: calling it every frame from a venue's bind is the intended use.
        public bool Bind(VisualElement host, Action onBegin)
        {
            began = onBegin;
            if (host == null) return false;
            if (ReferenceEquals(this.host, host) && sheet != null && sheet.panel != null) return true;
            this.host = host;
            Build();
            if (described) Fill();
            if (Dismissed) sheet.Hide(); else HandOverWhenVisible();
            return true;
        }

        /// Say what the activity is. Safe to call whenever the numbers change — a plan arriving from the
        /// service after the sheet is already standing rewrites it in place rather than handing it over
        /// a second time.
        public void Describe(in Briefing briefing)
        {
            content = briefing;
            described = true;
            if (sheet != null) Fill();
        }

        /// Put it down, with the exit. What the button does, and what a venue calls to skip the sheet.
        public void Dismiss()
        {
            Dismissed = true;
            waiting?.Pause(); waiting = null;
            sheet?.Dismiss();
        }

        /// Hand it over again — a new activity, or a plan that changed enough to be worth re-reading.
        public void Present()
        {
            Dismissed = false;
            if (sheet != null) HandOverWhenVisible();
        }

        /// An activity is entered behind HeadFade's curtain, which takes PlazaApproach.ClearSeconds to come
        /// down. A sheet handed over during it is handed to nobody: the entrance plays against a covered
        /// view and by the time the room appears the sheet is already standing there. So it waits for the
        /// curtain, and then for a beat, so the hand-over reads as something that happened to you rather
        /// than as the state the room was found in.
        void HandOverWhenVisible()
        {
            waiting?.Pause(); waiting = null;
            if (ViewIsClear) { HandOverAfterABeat(); return; }
            waiting = sheet.schedule.Execute(() =>
            {
                if (!ViewIsClear) return;
                waiting?.Pause(); waiting = null;
                HandOverAfterABeat();
            });
            waiting.Every(32);
        }

        void HandOverAfterABeat()
        {
            waiting = sheet.schedule.Execute(() => sheet.Present());
            waiting.ExecuteLater(BeatMilliseconds);
        }

        /// Nothing has raised a curtain (the scene was entered straight from the editor), or it is down.
        static bool ViewIsClear => HeadFade.Current == null || HeadFade.Current.Cover <= .01f;

        void Build()
        {
            host.Clear();
            sheet = new KSheet { name = "briefing-sheet", entrance = KSheet.Entrance.Right };
            sheet.AddToClassList(Block);
            KStyles.Attach(sheet, "Briefing");

            eyebrow = new KEyebrow();
            title = new KText { size = KText.Size.Title };
            subtitle = new KText { tone = KText.Tone.Soft };
            subtitle.AddToClassList(Block + "__subtitle");
            lineBox = new VisualElement { pickingMode = PickingMode.Ignore };
            lineBox.AddToClassList(Block + "__lines");

            noteBox = new VisualElement { pickingMode = PickingMode.Ignore };
            noteBox.AddToClassList(Block + "__note");
            note = new KText { size = KText.Size.Caption };
            noteFrom = new KEyebrow();
            noteFrom.AddToClassList(Block + "__note-from");
            noteBox.Add(noteFrom); noteBox.Add(note);

            action = new KButton(() => began?.Invoke()) { name = ActionName, text = "Begin", tone = KButton.Tone.Primary, size = KButton.Size.Large };
            action.AddToClassList(Block + "__action");

            sheet.Add(eyebrow); sheet.Add(title); sheet.Add(subtitle);
            sheet.Add(lineBox); sheet.Add(noteBox); sheet.Add(action);
            host.Add(sheet);
        }

        void Fill()
        {
            Say(eyebrow, content.eyebrow);
            Say(title, content.title);
            Say(subtitle, content.subtitle);
            action.text = string.IsNullOrWhiteSpace(content.action) ? "Begin" : content.action;

            lineBox.Clear();
            foreach (var line in content.lines ?? Array.Empty<BriefingLine>())
            {
                if (string.IsNullOrWhiteSpace(line.value)) continue;
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList(Block + "__line");
                var label = new KText { text = line.label, size = KText.Size.Caption, tone = KText.Tone.Soft };
                label.AddToClassList(Block + "__line-label");
                var value = new KText { text = line.value };
                value.AddToClassList(Block + "__line-value");
                row.Add(label); row.Add(value);
                lineBox.Add(row);
            }
            lineBox.EnableInClassList("hidden", lineBox.childCount == 0);

            bool hasNote = !string.IsNullOrWhiteSpace(content.note);
            noteBox.EnableInClassList("hidden", !hasNote);
            if (hasNote) { note.text = content.note; Say(noteFrom, content.noteFrom); }
        }

        /// Copy that is not there leaves no gap: the element goes out of layout rather than standing empty.
        static void Say(TextElement element, string text)
        {
            element.text = text ?? "";
            element.EnableInClassList("hidden", string.IsNullOrWhiteSpace(text));
        }
    }
}
