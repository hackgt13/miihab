using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Kinesthetic.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    /// The plan itself, as the patient's own copy of it.
    ///
    /// The board next door answers "what do I do today". This answers the question people actually take to
    /// an appointment: *what am I on, and is it moving?* So it shows the prescription as prescribed — dose,
    /// target, the ceiling not to pass — and then three readings against it: the reach session by session,
    /// where the target sits inside the envelope the clinician approved, and how many reps are landing in
    /// the band. Nothing here is a verdict. A plan that is not moving is information for the therapist, not
    /// a telling-off, which is why no number on this pane is ever coloured coral for being low.
    ///
    /// The shape mirrors `coordinator/plans.ts` (schema kinesthetic.plan.v2) so `FromCoordinator` is a read
    /// of `/api/plans/active` rather than a translation layer, exactly as MenuDashboardModel mirrors
    /// `/api/dashboard`. Offline it falls back to `Placeholder()` — the coordinator's own SEED_PLAN — and
    /// says so on the panel, because a demo plan presented as a real one is the one lie a rehab app cannot
    /// afford to tell.
    public sealed class CoachingPlanModel
    {
        public bool live;                    // true once this came from the coordinator
        public int version = 1;
        public string goal = "Play golf again with their best friend";
        public string coachingNote = "Slow and controlled. Stop at the line; higher is not better.";
        public string approvedBy = "PM&R physician (demo)";
        public string rationale = "Initial home program after outpatient assessment (synthetic case).";
        public DateTime approvedAt = DateTime.Today;
        public Prescription[] prescriptions = Array.Empty<Prescription>();

        /// One line of `plan.activities[]`. `measured` is the `exerciseKind != null` distinction the schema
        /// draws: the activity is where the patient goes, the kind is what gets measured there, and golf has
        /// the first without the second.
        public struct Prescription
        {
            public string id, activityId, where, movement, note;
            public bool measured;
            public int targetCount;
            public float targetDeg, targetMaxDeg;
            public Envelope? envelope;

            /// The dose, in the words the plan uses. A measured prescription is a count against an angle;
            /// play is just a count.
            public string Dose => measured
                ? $"{targetCount} reps · to {Mathf.RoundToInt(targetDeg)}°" +
                  (targetMaxDeg > targetDeg ? $" · stop by {Mathf.RoundToInt(targetMaxDeg)}°" : "")
                : $"{targetCount} to do";
        }

        /// The progression envelope: the limits inside which the data may move the target without the
        /// clinician (coordinator/progression.ts). Levels are steps across it, which is what the ladder draws.
        public struct Envelope
        {
            public float stepDeg, minTargetDeg, maxTargetDeg, inBandRatio;
            public int sessionsToProgress;
            public bool autoApply;

            public int Levels => stepDeg <= 0 ? 1 : Mathf.FloorToInt((maxTargetDeg - minTargetDeg) / stepDeg) + 1;
            public int LevelOf(float target) => stepDeg <= 0 ? 1
                : Mathf.Clamp(Mathf.FloorToInt((target - minTargetDeg) / stepDeg) + 1, 1, Levels);
        }

        /// The prescription the readings are drawn against — the first measured one with an envelope, which
        /// is the same choice `dashboard.ts` makes for the reach trend, so the two surfaces cannot disagree
        /// about which exercise "the reach" means.
        public Prescription? Primary
        {
            get
            {
                foreach (var p in prescriptions) if (p.measured && p.envelope.HasValue) return p;
                foreach (var p in prescriptions) if (p.measured) return p;
                return null;
            }
        }

        // The coordinator's own vocabulary, duplicated here rather than fetched: two extra round trips to
        // put a human name on a kind id is not worth a pane that has to draw before the bridge answers.
        // Keep in step with coordinator/exercises.ts (LIBRARY) and dashboard.ts (TITLES).
        static readonly Dictionary<string, string> Places = new()
        {
            ["rehab.studio"] = "Movement Studio",
            ["golf.adaptive"] = "Golf",
            ["bowling.adaptive"] = "Bowling",
        };
        // A movement's name is the catalog's (its gallery card), so it matches the card the patient picks. These are the
        // kinds with no card of their own: the camera kinds, and the one-AirPod raise and curl that the two-AirPod
        // versions replaced in the gallery. The demo plan on disk still prescribes them, and a pane that answered
        // "shoulder-raise.v1 · right" would be showing the patient a database key.
        static readonly Dictionary<string, string> Movements = new()
        {
            ["arm-elevation.v1"] = "Seated shoulder raise",
            ["shoulder-raise.v1"] = "Seated shoulder raise",
            ["elbow-flexion.v1"] = "Seated biceps curl",
            ["trunk-rotation.v1"] = "Seated trunk rotation",
        };

        public static CoachingPlanModel FromCoordinator(JObject j)
        {
            var model = new CoachingPlanModel { live = true };
            model.version = (int?)j["version"] ?? 1;
            model.goal = (string)j["goal"]?["text"] ?? model.goal;
            model.coachingNote = (string)j["coachingNote"] ?? model.coachingNote;
            model.approvedBy = (string)j["approvedBy"] ?? model.approvedBy;
            model.rationale = (string)j["rationale"] ?? model.rationale;
            if (DateTime.TryParse((string)j["approvedAt"], CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal, out var at)) model.approvedAt = at.ToLocalTime();

            model.prescriptions = (j["activities"] as JArray ?? new JArray())
                .OrderBy(a => (int?)a["order"] ?? 0)
                .Select(a =>
                {
                    string activityId = (string)a["activityId"] ?? "";
                    string kind = (string)a["exerciseKind"];
                    var side = (string)a["params"]?["side"];
                    var prescription = new Prescription
                    {
                        id = (string)a["id"] ?? activityId,
                        activityId = activityId,
                        where = Places.TryGetValue(activityId, out var place) ? place : activityId,
                        measured = !string.IsNullOrEmpty(kind),
                        note = (string)a["note"] ?? "",
                        targetCount = (int?)a["targetCount"] ?? 0,
                        targetDeg = (float?)a["params"]?["targetDeg"] ?? 0,
                        targetMaxDeg = (float?)a["params"]?["targetMaxDeg"] ?? 0,
                    };
                    string movement = kind == null ? null : Kinesthetic.Activities.ActivityCatalog.MovementFor(kind)?.DisplayName ?? (Movements.TryGetValue(kind, out var named) ? named : kind);
                    prescription.movement = prescription.measured
                        ? movement + (string.IsNullOrEmpty(side) ? "" : $" · {side}")
                        : prescription.where;
                    if (a["progression"] is JObject env)
                        prescription.envelope = new Envelope
                        {
                            stepDeg = (float?)env["stepDeg"] ?? 5,
                            minTargetDeg = (float?)env["minTargetDeg"] ?? prescription.targetDeg,
                            maxTargetDeg = (float?)env["maxTargetDeg"] ?? prescription.targetDeg,
                            sessionsToProgress = (int?)env["sessionsToProgress"] ?? 2,
                            inBandRatio = (float?)env["inBandRatio"] ?? .8f,
                            autoApply = (bool?)env["autoApply"] ?? false,
                        };
                    return prescription;
                }).ToArray();
            return model;
        }

        /// The coordinator's SEED_PLAN, so an offline pane shows the same plan a fresh demo patient gets.
        public static CoachingPlanModel Placeholder() => new()
        {
            version = 1,
            approvedAt = DateTime.Today,
            prescriptions = new[]
            {
                new Prescription
                {
                    id = "arm-elevation-right", activityId = "rehab.studio", where = "Movement Studio",
                    movement = "Seated shoulder raise · right", measured = true, targetCount = 8,
                    targetDeg = 45, targetMaxDeg = 60, note = "Stop at the line; higher is not better.",
                    envelope = new Envelope { stepDeg = 5, minTargetDeg = 40, maxTargetDeg = 90,
                                              sessionsToProgress = 2, inBandRatio = .8f, autoApply = true },
                },
                new Prescription
                {
                    id = "elbow-flexion-right", activityId = "rehab.studio", where = "Movement Studio",
                    movement = "Seated biceps curl · right", measured = true, targetCount = 10,
                    targetDeg = 90, targetMaxDeg = 110, note = "Elbow by your side.",
                },
                new Prescription
                {
                    id = "golf.adaptive", activityId = "golf.adaptive", where = "Golf", movement = "Golf",
                    targetCount = 9, note = "Nine holes with a friend.",
                },
            },
        };
    }

    /// Fills the coaching pane from a plan and the board's own dashboard figures, and paints its three
    /// charts. Static for the same reason MenuDashboard is: it owns no state — the panel is the state.
    public static class CoachingPanel
    {
        /// Fills the three answers. Every label on this pane is a handful of words: it is read from two metres,
        /// and the figures it used to carry — a reach delta, a reps-in-band percentage, a level, a week count —
        /// were five measurements of the same two sessions where a patient wanted a date, a movement and a list.
        public static void Populate(VisualElement root, CoachingPlanModel plan, MenuDashboardModel dashboard)
        {
            if (root == null || plan == null || dashboard == null) return;
            var primary = plan.Primary;

            Text(root, "plan-provenance", $"Version {plan.version} · {plan.approvedBy} · {plan.approvedAt:d MMM}");
            Text(root, "plan-source", plan.live ? "From your clinic" : "Demo plan · not from your clinic");

            // ---- how long is this, and when am I done
            int totalDays = Mathf.Max(7, dashboard.programTotalDays);
            int day = Mathf.Clamp(dashboard.programDay, 1, totalDays);
            var start = DateTime.Today.AddDays(1 - day);
            var finish = start.AddDays(totalDays - 1);
            int week = Mathf.Max(1, Mathf.CeilToInt(day / 7f));
            int weeks = Mathf.Max(1, Mathf.CeilToInt(totalDays / 7f));
            Text(root, "program-note", $"Week {week} of {weeks} · ends {finish:d MMM}");

            // A column is a week of the program, counted from its own first day rather than from a Monday.
            // Monday-aligned columns put an 84-day program across 13 of them whenever it starts mid-week,
            // which is a grid that disagrees with the "of 12" written above it.
            int columns = Mathf.CeilToInt(totalDays / 7f);
            Ticks(root, "program-weeks", columns, i => (i + 1).ToString());
            // So row 0 is whatever weekday the program began on, and the letters down the side rotate to match.
            Ticks(root, "program-days", 7, i => "MTWTFSS"[(((int)start.DayOfWeek + 6) % 7 + i) % 7].ToString());

            // ---- what am I working on right now
            if (primary is { } focus)
            {
                Text(root, "focus-movement", focus.movement);
                Text(root, "focus-where", focus.where);
                Text(root, "focus-target", $"{Mathf.RoundToInt(focus.targetDeg)}°");
                Text(root, "focus-caption", focus.targetMaxDeg > focus.targetDeg
                    ? $"today's target · stop by {Mathf.RoundToInt(focus.targetMaxDeg)}°" : "today's target");
                Text(root, "focus-step", focus.envelope is { } envelope
                    ? $"Level {envelope.LevelOf(focus.targetDeg)} of {envelope.Levels}" +
                      (envelope.autoApply
                          ? $" · next {Mathf.RoundToInt(Mathf.Min(focus.targetDeg + envelope.stepDeg, envelope.maxTargetDeg))}°" +
                            $" after {envelope.sessionsToProgress} good sessions"
                          : " · your clinician sets the next step")
                    : "Your clinician sets this target");
            }
            else
            {
                Text(root, "focus-movement", "Nothing measured in this plan");
                Text(root, "focus-where", "");
                Text(root, "focus-target", "—");
                Text(root, "focus-caption", "");
                Text(root, "focus-step", "Ask your therapist to add an exercise to measure.");
            }

            // ---- what am I supposed to do
            // Rebuilt rather than patched: approving a change writes a new version. The ScrollView's own
            // children are its scrollers, so rows go into its content container.
            var scroller = root.Q<ScrollView>("prescription-list");
            var list = scroller != null ? scroller.contentContainer : root.Q("prescription-list");
            if (list != null)
            {
                list.Clear();
                for (int i = 0; i < plan.prescriptions.Length; i++)
                    list.Add(PrescriptionRow(plan.prescriptions[i], i == 0));
                if (plan.prescriptions.Length == 0)
                    list.Add(new Label("Nothing prescribed yet.") { name = "prescription-empty" });
            }
            Text(root, "prescription-note", plan.prescriptions.Length == 1
                ? "1 thing" : $"{plan.prescriptions.Length} things");

            // The clinician's own words, under what they asked for — and only if the rows are not already
            // carrying them, because the one sentence on this pane must not be the same sentence twice.
            bool onARow = plan.prescriptions.Any(
                p => string.Equals(p.note?.Trim(), plan.coachingNote?.Trim(), StringComparison.OrdinalIgnoreCase));
            Text(root, "coaching-note", string.IsNullOrWhiteSpace(plan.coachingNote) || onARow
                ? "" : $"“{plan.coachingNote}”");

            Text(root, "visit-sub", "Talk these numbers through together");

            // Last write wins rather than ToDictionary, which throws if the coordinator ever sends a date twice.
            var levels = new Dictionary<DateTime, int>();
            foreach (var cell in dashboard.calendar) levels[cell.date.Date] = cell.level;

            // Both charts are components that carry their readings as attributes (UI/KDays, KLadder), so the
            // headset's replica draws them too; a painter attached here never crossed the /ui wire.
            var grid = root.Q<KDays>("program-grid");
            if (grid != null)
            {
                grid.form = KDays.Form.Program;
                grid.levels = ProgramDays(start, totalDays, levels);
                grid.today = day - 1;
                grid.seed = start.DayOfYear * 31 + start.Year;
            }
            PlaceEndFlag(root, columns, totalDays);

            var ladder = root.Q<KLadder>("envelope-ladder");
            if (ladder != null)
            {
                if (primary is { } p && p.envelope is { } e)
                {
                    float lo = e.minTargetDeg, hi = Mathf.Max(e.maxTargetDeg, lo + 1);
                    ladder.rungs = e.Levels;
                    ladder.pitch = e.stepDeg / (hi - lo);
                    ladder.fraction = (p.targetDeg - lo) / (hi - lo);
                }
                else ladder.rungs = 0;   // no envelope, no ladder
            }
        }

        /// One prescribed thing, built as the board builds a thing to do: the movement with where it is done
        /// as a pill beside it, then its dose and any form note under both. Same .task-row the Today tile
        /// uses, so a prescription and today's work read as the same kind of object.
        ///
        /// The pill shares the title's line rather than standing at the row's right edge because this column
        /// is a third of the pane: a title, a pill and a dose across one line ran the dose under the pill and
        /// clipped it. With the pill up beside the title, the dose gets the row's full width and wraps.
        static VisualElement PrescriptionRow(CoachingPlanModel.Prescription p, bool first)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("task-row");
            if (first) row.AddToClassList("first");

            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("prescription-mark");
            if (p.measured) marker.AddToClassList("measured");
            row.Add(marker);

            var copy = new VisualElement { pickingMode = PickingMode.Ignore };
            copy.AddToClassList("task-copy");

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("prescription-head");
            head.Add(Styled("task-title", p.movement));
            head.Add(Styled("task-status", p.where));
            copy.Add(head);

            // The note joins the dose rather than taking a line of its own, and the line wraps: a cue such as
            // "stop at the line" is what the row is for, and a cue cut off at the tile's edge is no cue.
            copy.Add(Styled("task-detail", string.IsNullOrWhiteSpace(p.note) ? p.Dose : $"{p.Dose} · {p.note}"));
            row.Add(copy);
            return row;
        }

        /// A row (or column) of small labels that has to keep step with a painted grid: same count, same flex
        /// rule, so the two divide the same rect the same way.
        static void Ticks(VisualElement root, string name, int count, Func<int, string> text)
        {
            var holder = root.Q(name);
            if (holder == null) return;
            holder.Clear();
            for (int i = 0; i < count; i++)
            {
                var tick = new Label(text(i)) { pickingMode = PickingMode.Ignore };
                tick.AddToClassList("day-letter");
                holder.Add(tick);
            }
        }

        static Label Styled(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        // ------------------------------------------------------------------ the whole program

        /// Every day of the program at once: weeks across, days down, one square each, and a day you have done
        /// crossed off by hand.
        ///
        /// The question this answers is the one nobody could answer from this pane before — *how long is this,
        /// and when am I done* — so the shape is the program's own length and the block stops where the program
        /// stops. Read left to right it is what I have crossed off, where I am, what is left, and the date it
        /// ends.
        ///
        /// The crossing-off is drawn rather than filled because that is what a person does to a paper plan on a
        /// fridge, and because a wall of flat coloured squares says "data" where two pen strokes say "that one
        /// is behind me". Every stroke is bowed and its ends are jittered, so no two X's are the same and none
        /// of them is straight — but the jitter comes from the date, so a day's X is the same X on every repaint.
        ///
        /// Every day that has gone by is crossed, whether it had rehab in it or not. A calendar on a wall gets
        /// crossed off because the day is over, and crossing only the days with a session would quietly turn
        /// this into an attendance record — which is the board's consistency grid, not this. A day with work in
        /// it is pressed a little harder, and that is the whole of the difference. Today is never crossed: it is
        /// where the person is, not something they have finished.
        ///
        /// "END" written on the last day of the program, because a coral square at the end of the block says
        /// something is there without saying what. Painter2D draws no text, so this is a label placed on the
        /// cell — positioned from the same KDays.ProgramGrid() the component paints with, and put back whenever the grid is resized.
        static void PlaceEndFlag(VisualElement root, int columns, int totalDays)
        {
            var grid = root.Q("program-grid");
            if (grid == null) return;

            var flag = grid.Q<Label>("end-flag");
            if (flag == null)
            {
                flag = new Label("END") { name = "end-flag", pickingMode = PickingMode.Ignore };
                flag.AddToClassList("end-flag");
                grid.Add(flag);
            }

            int column = (totalDays - 1) / 7, row = (totalDays - 1) % 7;
            void Put()
            {
                var r = grid.contentRect;
                if (r.width < 40 || r.height < 40) return;
                var (cw, ch, gap) = KDays.ProgramGrid(r, columns);
                flag.style.left = column * (cw + gap);
                flag.style.top = row * (ch + gap);
                flag.style.width = cw;
                flag.style.height = ch;
            }

            // One callback per element, for the same reason there is one painter per element: Populate runs
            // again when the plan arrives from the bridge.
            if (placers.TryGetValue(grid, out var previous))
            { grid.UnregisterCallback(previous); placers.Remove(grid); }
            EventCallback<GeometryChangedEvent> place = _ => Put();
            grid.RegisterCallback(place);
            placers.Add(grid, place);
            Put();
        }

        static readonly ConditionalWeakTable<VisualElement, EventCallback<GeometryChangedEvent>> placers = new();

        /// Every day of the program as KDays reads it: a digit per day for how much work landed on it.
        static string ProgramDays(DateTime start, int totalDays, Dictionary<DateTime, int> levels)
        {
            var text = new System.Text.StringBuilder(totalDays);
            for (int i = 0; i < totalDays; i++)
            {
                levels.TryGetValue(start.AddDays(i).Date, out int level);
                text.Append((char)('0' + Mathf.Clamp(level, 0, 4)));
            }
            return text.ToString();
        }

        static void Text(VisualElement root, string name, string value)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = value;
        }
    }
}
