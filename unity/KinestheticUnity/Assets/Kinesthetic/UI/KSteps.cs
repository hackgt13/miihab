using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Activities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A numbered list of steps. Help copy is data: name an activity and its steps come from the catalog
    /// (coordinator/activities.json), so no screen's markup holds a second copy of what the catalog says.
    ///
    ///     <k:KSteps activity="golf.adaptive" />
    ///
    /// A readiness checklist is not help text, so it is written out instead, and numbered Progress so each
    /// badge turns to a check as its step is met:
    ///
    ///     <k:KSteps numbering="Progress">
    ///         <k:KStep name="camera" title="Camera" copy="Looking for you…" />
    ///     </k:KSteps>
    [UxmlElement]
    public partial class KSteps : VisualElement
    {
        public enum Numbering { Number, Progress }

        const string Block = "k-steps";
        string activityId;
        Numbering numberingValue;

        [UxmlAttribute]
        public string activity
        {
            get => activityId;
            set { activityId = value; if (!string.IsNullOrEmpty(value)) FromCatalog(value); }
        }

        [UxmlAttribute]
        public Numbering numbering
        {
            get => numberingValue;
            set { numberingValue = value; KStyles.Variant(this, Block, value); Renumber(); }
        }

        public KSteps()
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Steps");
            numbering = Numbering.Number;
            // Steps written in UXML arrive as children after construction; by attach they are all present.
            RegisterCallback<AttachToPanelEvent>(_ => Renumber());
        }

        public void Show(IEnumerable<(string title, string copy)> steps)
        {
            Clear();
            foreach (var (title, copy) in steps) Add(new KStep(title, copy));
            Renumber();
        }

        public KStep Step(int index) => Children().OfType<KStep>().ElementAtOrDefault(index);

        void FromCatalog(string id)
        {
            var entry = ActivityCatalog.ById(id);
            if (entry == null)
            {
                Debug.LogWarning($"KSteps: no activity '{id}' in the catalog.");
                Clear();
                return;
            }
            Show(entry.HelpSteps.Select(h => (h.Step, h.Copy)));
        }

        void Renumber()
        {
            int n = 0;
            foreach (var step in Children().OfType<KStep>()) step.Place(++n, numberingValue == Numbering.Progress);
        }
    }

    /// One row of a KSteps. Numbered by its parent; Done matters only when the parent numbers Progress.
    [UxmlElement]
    public partial class KStep : VisualElement
    {
        const string Block = "k-step";
        readonly Label badge = new Label(), heading = new Label(), body = new Label();
        int number = 1;
        bool done, progress;

        [UxmlAttribute]
        public string title { get => heading.text; set => heading.text = value; }

        [UxmlAttribute]
        public string copy
        {
            get => body.text;
            set { body.text = value; body.EnableInClassList(Block + "__copy--empty", string.IsNullOrEmpty(value)); }
        }

        public bool Done
        {
            get => done;
            set { done = value; EnableInClassList(Block + "--done", value); Refresh(); }
        }

        public KStep() : this(string.Empty, string.Empty) { }

        public KStep(string title, string copy)
        {
            AddToClassList(Block);
            KStyles.Attach(this, "Steps");

            badge.AddToClassList(Block + "__badge");
            Add(badge);
            var text = new VisualElement();
            text.AddToClassList(Block + "__text");
            heading.AddToClassList(Block + "__title");
            body.AddToClassList(Block + "__copy");
            text.Add(heading);
            text.Add(body);
            Add(text);

            this.title = title;
            this.copy = copy;
            Refresh();
        }

        internal void Place(int n, bool numberedAsProgress)
        {
            number = n;
            progress = numberedAsProgress;
            EnableInClassList(Block + "--first", n == 1);
            Refresh();
        }

        void Refresh() => badge.text = progress && done ? "✓" : number.ToString();
    }
}
