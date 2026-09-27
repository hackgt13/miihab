using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Kinesthetic.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    /// What the menu board shows about the person standing in front of it: what is prescribed today,
    /// how consistent they have been, and whether the reach is actually improving.
    ///
    /// The shapes here deliberately mirror the coordinator's own records — `today` is one entry per
    /// `plan.activities[]` (schema kinesthetic.plan.v2), `history` is one entry per completed session
    /// the way `/api/sessions` reports it. So filling it from a fetch is a swap of this
    /// one object, not a rewrite of the panel.
    ///
    /// FromCoordinator() fills it from `/api/dashboard` (coordinator/dashboard.ts). Until the patient has
    /// a real session it stays Empty(): no history, no calendar, no tasks. It used to be filled with
    /// invented ones -- six weeks of climbing reach and sixteen weeks of adherence generated from a fixed
    /// seed, shaped to look like a real recovery. A board that looks measured when nothing has been
    /// measured is worse than a blank one, and it cost real debugging time to tell the two apart.
    public sealed class MenuDashboardModel
    {
        public string patientName = MenuProfile.DefaultName;
        public string goal = "Play golf again with my best friend";
        /// A plan change not yet seen (coordinator planUpdate): the menu leads with it, and the visit explains it.
        public string planUpdateHeadline, planUpdateDetail;
        public bool PlanUpdated => !string.IsNullOrEmpty(planUpdateHeadline);
        public int streakDays = 12, bestStreakDays = 14;
        public int weekSessionsDone = 4, weekSessionsGoal = 5;
        public int programDay = 21, programTotalDays = 84;
        public float targetDeg = 85;
        public bool measured;                 // true once these come from real sessions
        public TodayTask[] today = Array.Empty<TodayTask>();
        public WeekPoint[] history = Array.Empty<WeekPoint>();
        public DayCell[] calendar = Array.Empty<DayCell>();

        /// One prescribed activity: `plan.activities[i]`, joined against completed sessions.
        public struct TodayTask
        {
            public string activityId, title, detail;
            public bool done;
        }

        /// One completed session, reduced to what a trend line needs.
        public struct WeekPoint
        {
            public string label;
            public int attempted, valid;
            public float medianPeakDeg;
        }

        /// One day in the consistency grid. `level` is 0 (nothing) to 4 (a full day's work).
        public struct DayCell
        {
            public DateTime date;
            public int level;
        }

        public int DaysRemaining => Mathf.Max(0, programTotalDays - programDay);
        public float ProgramFraction => programTotalDays <= 0 ? 0 : Mathf.Clamp01(programDay / (float)programTotalDays);
        public float BestReachDeg => history.Length == 0 ? 0 : history.Max(h => h.medianPeakDeg);

        public TodayTask? FirstOutstanding()
        {
            foreach (var task in today) if (!task.done) return task;
            return null;
        }

        /// Sessions logged, which is what "consistency" actually counts.
        public int ActiveDays => calendar.Count(c => c.level > 0);

        /// The last `count` days ending today, aligned so each row of seven is a Monday-to-Sunday week.
        public MenuDashboardModel.DayCell[] RecentDays(int count)
        {
            var upTo = calendar.Where(c => c.date <= DateTime.Today).ToArray();
            if (upTo.Length == 0) return upTo;
            var last = upTo[^1].date;
            // Pad forward to the end of the current week so today never lands mid-row.
            int trailing = ((int)last.DayOfWeek + 6) % 7;      // Monday = 0
            var tail = Enumerable.Range(1, 6 - trailing)
                .Select(i => new DayCell { date = last.AddDays(i), level = 0 });
            var padded = upTo.Concat(tail).ToArray();
            return padded.Skip(Mathf.Max(0, padded.Length - count)).ToArray();
        }

        public int RecentActiveDays => RecentDays(28).Count(c => c.level > 0);

        /// The coordinator's figures. Today's list and the goal always come from the live plan; the rest
        /// only once `measured` says a real session exists, so a new patient still sees a readable board.
        public static MenuDashboardModel FromCoordinator(JObject j)
        {
            var model = Empty();
            model.goal = (string)j["goal"] ?? model.goal;
            if (j["planUpdate"] is JObject update)
            {
                model.planUpdateHeadline = (string)update["headline"];
                var first = (update["changes"] as JArray)?.FirstOrDefault() as JObject;
                model.planUpdateDetail = first == null ? "" : $"{(string)first["heading"]}: {(string)first["detail"]}";
            }
            if (j["targetDeg"]?.Type is JTokenType.Float or JTokenType.Integer) model.targetDeg = j["targetDeg"].Value<float>();
            if (j["today"] is JArray today)
                model.today = today.Select(t => new TodayTask { activityId = (string)t["activityId"], title = (string)t["title"],
                    detail = (string)t["detail"], done = (bool?)t["done"] ?? false }).ToArray();
            // Where the program stands is the plan's, not the sessions': a new patient is on day 1 of a real program.
            model.programDay = (int?)j["programDay"] ?? model.programDay;
            model.programTotalDays = (int?)j["programTotalDays"] ?? model.programTotalDays;
            if (j["measured"]?.Value<bool>() != true) return model;

            model.measured = true;
            model.streakDays = (int?)j["streakDays"] ?? 0;
            model.bestStreakDays = (int?)j["bestStreakDays"] ?? model.streakDays;
            model.weekSessionsDone = (int?)j["weekSessionsDone"] ?? 0;
            model.weekSessionsGoal = (int?)j["weekSessionsGoal"] ?? model.weekSessionsGoal;
            model.programDay = (int?)j["programDay"] ?? 1;
            model.programTotalDays = (int?)j["programTotalDays"] ?? model.programTotalDays;
            model.history = (j["history"] as JArray ?? new JArray()).Select(h => new WeekPoint { label = (string)h["label"],
                attempted = (int?)h["attempted"] ?? 0, valid = (int?)h["valid"] ?? 0, medianPeakDeg = (float?)h["medianPeakDeg"] ?? 0 }).ToArray();
            model.calendar = (j["calendar"] as JArray ?? new JArray()).Select(c => new DayCell {
                date = DateTime.ParseExact((string)c["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture), level = (int?)c["level"] ?? 0 }).ToArray();
            return model;
        }

        /// Nothing measured yet. The board reads this as a new patient and says so, rather than
        /// drawing a plausible past. FromCoordinator() overwrites today's list and the goal from the
        /// live plan, and the rest only once `measured` confirms a real session exists.
        public static MenuDashboardModel Empty() => new()
        {
            patientName = MenuProfile.Name,
            today = System.Array.Empty<TodayTask>(),
            history = System.Array.Empty<WeekPoint>(),
            calendar = System.Array.Empty<DayCell>(),
        };
    }

    /// Fills the bento tiles from a model and hands the charts their readings. Static because it owns no
    /// state: the panel is the state, and this writes into it. The charts are components (UI/KReach,
    /// KDays, KArc) rather than painters attached here, so the headset's replica draws them too.
    public static class MenuDashboard
    {
        /// The top of the dial. Functional shoulder elevation, not the anatomical 180.
        const float Ceiling = 120;

        // Rotates on the hour of day rather than at random, so the board is not a different greeting
        // every time someone glances at it.
        static string Greeting(int hour) =>
            hour < 12 ? "Good morning" : hour < 18 ? "Welcome back" : "Good evening";

        public static void Populate(VisualElement root, MenuDashboardModel model)
        {
            var now = DateTime.Now;
            Text(root, "splash-greeting", $"{Greeting(now.Hour)}, {model.patientName}!");
            Text(root, "splash-sub", $"{now:dddd d MMMM} · Day {model.programDay} of {model.programTotalDays}");

            Text(root, "streak-number", model.streakDays.ToString());
            Text(root, "streak-caption", model.streakDays >= model.bestStreakDays
                ? "your best run yet" : $"{model.bestStreakDays - model.streakDays} to beat your best");
            Text(root, "week-count", $"{model.weekSessionsDone} of {model.weekSessionsGoal} done this week");
            Text(root, "goal-copy", $"“{model.goal}”");

            Text(root, "ring-number", model.DaysRemaining.ToString());
            Text(root, "ring-caption", "days to go");

            Text(root, "calendar-note", $"{model.RecentActiveDays} of the last 28 days");

            // Today's list is rebuilt rather than patched: the prescription can change between visits.
            var list = root.Q("today-list");
            if (list != null)
            {
                list.Clear();
                for (int i = 0; i < model.today.Length; i++) list.Add(TaskRow(model.today[i], i == 0));
                if (model.today.Length == 0)
                    list.Add(new Label("Nothing prescribed today. Play anyway?") { name = "today-empty" });
            }

            var outstanding = model.FirstOutstanding();
            var cta = root.Q<Button>("start-activity");
            if (cta != null)
            {
                // An unseen plan change leads: the button opens the visit that explains it, which marks it seen.
                if (model.PlanUpdated)
                {
                    cta.Q<Label>("cta-title").text = model.planUpdateHeadline;
                    cta.Q<Label>("cta-sub").text = string.IsNullOrEmpty(model.planUpdateDetail) ? "See what's new" : model.planUpdateDetail + " · see what's new";
                }
                else
                {
                    cta.Q<Label>("cta-title").text = outstanding is { } next ? $"Start {next.title}" : "All done for today";
                    cta.Q<Label>("cta-sub").text = outstanding is { } n2 ? n2.detail : "Play something for the fun of it";
                }
            }

            float reach = model.history.Length > 0 ? model.history[^1].medianPeakDeg : 0;
            float first = model.history.Length > 0 ? model.history[0].medianPeakDeg : 0;
            float delta = reach - first;
            // Three lines. The green one says how far it has moved, the grey one where it stands against the
            // target; "median peak" is the clinician's word for it and belongs in the note, not the readout.
            int target = Mathf.RoundToInt(model.targetDeg), gap = Mathf.RoundToInt(Mathf.Abs(reach - model.targetDeg));
            Text(root, "reach-now", $"{Mathf.RoundToInt(reach)}°");
            Text(root, "reach-delta", model.history.Length > 1
                ? $"{(delta >= 0 ? "+" : "")}{Mathf.RoundToInt(delta)}° since week one"
                : "First session — no trend yet");
            Text(root, "reach-best", reach >= model.targetDeg
                ? $"target {target}° · {gap}° past it"
                : $"target {target}° · {gap}° to go");
            Text(root, "data-note", model.measured ? "Median peak · measured" : "No sessions measured yet");

            // The charts are components that carry their readings as attributes (UI/KReach, KDays, KArc). A
            // painter this class attached to a plain element drew on the Mac and never crossed the /ui wire,
            // so the headset's replica showed the numbers beside an empty box.
            var fan = root.Q<KReach>("range-fan");
            if (fan != null)
            {
                fan.measured = model.history.Length > 0;
                fan.now = reach; fan.start = first; fan.target = model.targetDeg; fan.ceiling = Ceiling;
            }
            var grid = root.Q<KDays>("calendar-grid");
            if (grid != null)
            {
                var (levels, today) = Days(model.RecentDays(28));
                grid.form = KDays.Form.Calendar; grid.levels = levels; grid.today = today;
            }
            var ring = root.Q<KArc>("program-ring");
            if (ring != null) { ring.form = KArc.Form.Continuous; ring.fraction = model.ProgramFraction; }
        }

        /// A run of days as KDays reads them: a digit per day, '-' for a day not here yet, and which one is today.
        public static (string levels, int today) Days(MenuDashboardModel.DayCell[] cells)
        {
            var text = new StringBuilder(cells.Length);
            int today = -1;
            var now = DateTime.Today;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].date > now) { text.Append('-'); continue; }
                text.Append((char)('0' + Mathf.Clamp(cells[i].level, 0, 4)));
                if (cells[i].date == now) today = i;
            }
            return (text.ToString(), today);
        }

        static void Text(VisualElement root, string name, string value)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = value;
        }

        static VisualElement TaskRow(MenuDashboardModel.TodayTask task, bool first)
        {
            var row = new VisualElement();
            row.AddToClassList("task-row");
            if (first) row.AddToClassList("first");
            var dot = new VisualElement();
            dot.AddToClassList("task-dot");
            if (task.done) dot.AddToClassList("done");
            row.Add(dot);
            var copy = new VisualElement();
            copy.AddToClassList("task-copy");
            var title = new Label(task.title); title.AddToClassList("task-title");
            var detail = new Label(task.detail); detail.AddToClassList("task-detail");
            copy.Add(title); copy.Add(detail);
            row.Add(copy);
            var status = new Label(task.done ? "Done" : "To do");
            status.AddToClassList("task-status");
            if (task.done) status.AddToClassList("done");
            row.Add(status);
            return row;
        }
    }
}
