using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
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

    /// Fills the bento tiles from a model and paints the charts. Static because it owns no state:
    /// the panel is the state, and this writes into it.
    public static class MenuDashboard
    {
        /// The top of the dial. Functional shoulder elevation, not the anatomical 180.
        const float Ceiling = 120;

        // Taken from Palette.cs by role, not by hex, so retuning the system moves these with it.
        // The calendar ramp is the jungle scale because its role is exactly what a done day is —
        // Good, "counted, done, reached" — and depth of green then reads as how much was done.
        static readonly Color Ink = Palette.Ink;
        static readonly Color Progress = Palette.Progress;     // a measured value
        static readonly Color Reference = Palette.Reference;   // the plan it is drawn against
        static readonly Color Target = Palette.Target;         // what is being reached for
        static readonly Color Panel = Palette.Panel;
        static readonly Color Empty = Palette.Line.At(.45f);
        static readonly Color[] Levels = { Palette.Jungle10, Palette.Jungle20, Palette.Jungle30, Palette.Jungle40 };
        static readonly Color Accent = Palette.Progress;

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

            Paint(root, "range-fan", (ctx, r) => DrawRangeFan(ctx, r, model));
            Paint(root, "calendar-grid", (ctx, r) => DrawCalendar(ctx, r, model.RecentDays(28)));
            Paint(root, "program-ring", (ctx, r) => DrawRing(ctx, r, model.ProgramFraction));
        }

        // One painter per element: Populate runs again when fresh data arrives, and a second handler
        // would draw the old model underneath the new one.
        static readonly ConditionalWeakTable<VisualElement, Action<MeshGenerationContext>> painters = new();

        static void Paint(VisualElement root, string name, Action<MeshGenerationContext, Rect> draw)
        {
            var element = root.Q(name);
            if (element == null) return;
            if (painters.TryGetValue(element, out var previous)) { element.generateVisualContent -= previous; painters.Remove(element); }
            Action<MeshGenerationContext> paint = ctx => draw(ctx, element.contentRect);
            element.generateVisualContent += paint;
            painters.Add(element, paint);
            element.MarkDirtyRepaint();
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

        // ------------------------------------------------------------------ hero

        /// The reach, drawn as the movement instead of as a chart.
        ///
        /// A line graph of shoulder degrees is generic — it could be plotting anything, and "93°"
        /// does not feel like a distance until you see the arm sweep it. So this is an arc pivoting
        /// at the shoulder, read like a dial: a faint track for the range a shoulder has, a quiet
        /// band for where week one stopped, and a solid band for everything gained since.
        ///
        /// Three bands and two marks, and nothing else. The earlier version drew guide spokes, a
        /// filled pie, an arm, a hand and a separate edge line on top of each other, which is why it
        /// read as clutter rather than as a diagram.
        static void DrawRangeFan(MeshGenerationContext ctx, Rect r, MenuDashboardModel model)
        {
            if (r.width < 24 || r.height < 24 || model.history.Length == 0) return;
            var p = ctx.painter2D;
            float now = Mathf.Clamp(model.history[^1].medianPeakDeg, 0, Ceiling);
            float start = Mathf.Clamp(model.history[0].medianPeakDeg, 0, Ceiling);
            float target = Mathf.Clamp(model.targetDeg, 0, Ceiling);

            // Arm at the side points down; raising it sweeps towards horizontal and past it. Painter2D
            // measures from +X clockwise, so straight down is 90 and an elevation of E sits at 90 - E.
            // Drawing 0..Ceiling covers a box `radius` wide and `radius * (1 + sin(Ceiling - 90))` tall.
            float Ang(float e) => 90 - e;
            float tall = 1 + Mathf.Sin((Ceiling - 90) * Mathf.Deg2Rad);
            float radius = Mathf.Min(r.width, r.height / tall) * .96f;
            var pivot = new Vector2((r.width - radius) * .5f,
                                    (r.height - radius * tall) * .5f + radius * (tall - 1));

            float thickness = radius * .17f;
            float mid = radius - thickness * .5f;

            void Band(float from, float to, Color colour)
            {
                if (to <= from) return;
                p.strokeColor = colour;
                p.lineWidth = thickness;
                p.lineCap = LineCap.Butt;
                p.BeginPath();
                p.Arc(pivot, mid, Angle.Degrees(Ang(to)), Angle.Degrees(Ang(from)));
                p.Stroke();
            }

            Band(0, Ceiling, Palette.Line.At(.20f));    // the range a shoulder has
            Band(0, start, Reference.At(.55f));         // where week one stopped
            Band(start, now, Progress);                 // everything gained since

            // The target, as a notch cut across the band rather than a line laid over it.
            float ta = Ang(target) * Mathf.Deg2Rad;
            var tdir = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
            p.strokeColor = Target; p.lineWidth = 4; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(pivot + tdir * (mid - thickness * .62f));
            p.LineTo(pivot + tdir * (mid + thickness * .62f));
            p.Stroke();

            // The arm. Without it the arc floats and the shoulder dot reads as a stray speck —
            // this is the line that makes the whole figure a reach rather than a gauge.
            float na = Ang(now) * Mathf.Deg2Rad;
            var ndir = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
            p.strokeColor = Ink; p.lineWidth = 6; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(pivot);
            p.LineTo(pivot + ndir * (mid - thickness * .5f));
            p.Stroke();

            p.fillColor = Ink;
            p.BeginPath(); p.Arc(pivot, 9, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();

            // The hand, sitting on the band at today's reach.
            p.fillColor = Panel;
            p.strokeColor = Progress; p.lineWidth = 5;
            p.BeginPath(); p.Arc(pivot + ndir * mid, thickness * .58f, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill(); p.Stroke();
        }

        // ------------------------------------------------------------------ consistency

        /// Four weeks of days, as a calendar rather than a contribution wall. The wall version was both
        /// a straight lift of a graph everyone recognises and illegible here: 112 cells across a 1.85 m
        /// board is about three millimetres each. Twenty-eight cells are large enough to actually read,
        /// and a month is the span a person can hold in their head anyway.
        static void DrawCalendar(MeshGenerationContext ctx, Rect r, MenuDashboardModel.DayCell[] cells)
        {
            if (r.width < 8 || r.height < 8 || cells.Length == 0) return;
            var p = ctx.painter2D;
            int rows = Mathf.CeilToInt(cells.Length / 7f);
            float gap = 8;
            float cell = Mathf.Min((r.width - gap * 6) / 7f, (r.height - gap * (rows - 1)) / rows);
            float gridW = cell * 7 + gap * 6, gridH = cell * rows + gap * (rows - 1);
            float ox = (r.width - gridW) * .5f, oy = (r.height - gridH) * .5f;
            float radius = cell * .3f;
            var today = DateTime.Today;

            for (int i = 0; i < cells.Length; i++)
            {
                int row = i / 7, col = i % 7;
                float x = ox + col * (cell + gap), y = oy + row * (cell + gap);
                if (cells[i].date > today) continue;
                p.fillColor = cells[i].level <= 0 ? Empty : Levels[Mathf.Clamp(cells[i].level - 1, 0, Levels.Length - 1)];
                RoundedSquare(p, x, y, cell, radius);

                // Today is outlined rather than filled differently, so "where am I" and "did I
                // train" stay two separate readings. An outline on the square itself sits in the
                // grid; a circle over it looked like a separate mark that had landed there.
                if (cells[i].date != today) continue;
                p.strokeColor = Target;
                p.lineWidth = 3;
                RoundedRect(p, x - 2.5f, y - 2.5f, cell + 5, radius + 2, cell + 5, stroke: true);
            }
        }

        static void RoundedSquare(Painter2D p, float x, float y, float size, float radius, float height = -1)
            => RoundedRect(p, x, y, size, radius, height);

        static void RoundedRect(Painter2D p, float x, float y, float size, float radius, float height = -1, bool stroke = false)
        {
            float w = size, hgt = height > 0 ? height : size;
            float rr = Mathf.Min(radius, Mathf.Min(w, hgt) * .5f);
            p.BeginPath();
            p.MoveTo(new(x + rr, y));
            p.LineTo(new(x + w - rr, y));
            p.Arc(new(x + w - rr, y + rr), rr, Angle.Degrees(-90), Angle.Degrees(0));
            p.LineTo(new(x + w, y + hgt - rr));
            p.Arc(new(x + w - rr, y + hgt - rr), rr, Angle.Degrees(0), Angle.Degrees(90));
            p.LineTo(new(x + rr, y + hgt));
            p.Arc(new(x + rr, y + hgt - rr), rr, Angle.Degrees(90), Angle.Degrees(180));
            p.LineTo(new(x, y + rr));
            p.Arc(new(x + rr, y + rr), rr, Angle.Degrees(180), Angle.Degrees(270));
            p.ClosePath();
            if (stroke) p.Stroke(); else p.Fill();
        }

        // ------------------------------------------------------------------ ring

        /// How much of the prescribed program is behind them. An arc rather than a bar because the
        /// number in the middle is the point — the ring is the context, not the reading.
        static void DrawRing(MeshGenerationContext ctx, Rect r, float fraction)
        {
            float size = Mathf.Min(r.width, r.height);
            if (size < 16) return;
            var p = ctx.painter2D;
            var centre = new Vector2(r.width * .5f, r.height * .5f);
            float radius = size * .5f - 10;
            float thickness = Mathf.Max(10, size * .11f);

            p.lineWidth = thickness;
            p.lineCap = LineCap.Butt;
            p.strokeColor = Reference.At(.28f);
            p.BeginPath(); p.Arc(centre, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();

            if (fraction <= 0) return;
            p.lineCap = LineCap.Round;
            p.strokeColor = Accent;
            p.BeginPath();
            p.Arc(centre, radius, Angle.Degrees(-90), Angle.Degrees(-90 + 360 * Mathf.Clamp01(fraction)));
            p.Stroke();
        }

        // Sessions this week as pips. Discrete on purpose — "4 of 5" is a count a person can check,
        // where a continuous bar invites reading a precision that is not there.
        static void DrawMeter(MeshGenerationContext ctx, Rect r, int done, int goal)
        {
            if (r.width < 4 || r.height < 2 || goal <= 0) return;
            var p = ctx.painter2D;
            float gap = 7, cell = (r.width - gap * (goal - 1)) / goal, h = Mathf.Min(r.height, 15);
            for (int i = 0; i < goal; i++)
            {
                p.fillColor = i < done ? Accent : Empty;
                RoundedSquare(p, i * (cell + gap), 0, cell, h * .5f, h);
            }
        }
    }
}
