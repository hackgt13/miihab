using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    /// What the menu board shows about the person standing in front of it: what is prescribed today,
    /// how consistent they have been, and whether the reach is actually improving.
    ///
    /// The shapes here deliberately mirror the coordinator's own records — `today` is one entry per
    /// `plan.activities[]` (schema kinesthetic.plan.v2), `history` is one entry per completed session
    /// the way `/api/sessions` reports it. So replacing Placeholder() with a fetch is a swap of this
    /// one object, not a rewrite of the panel.
    ///
    /// Until that fetch exists every number is invented by Placeholder(), from a fixed seed so the
    /// board looks identical between runs and can actually be judged as a design. The reach trend is
    /// the one exception: those figures are the authored demo history in
    /// `coordinator/fixtures/history.json`. Nothing here is measured, and the board says so.
    public sealed class MenuDashboardModel
    {
        public string patientName = MenuProfile.DefaultName;
        public string goal = "Play golf again with my best friend";
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

        public static MenuDashboardModel Placeholder()
        {
            var model = new MenuDashboardModel
            {
                patientName = MenuProfile.Name,
                today = new[]
                {
                    new TodayTask { activityId = "rehab.studio", title = "Movement Studio", detail = "8 shoulder raises · right", done = false },
                    new TodayTask { activityId = "golf.adaptive", title = "Golf", detail = "9 holes with a friend", done = false },
                },
                // The first three weeks are the authored history in coordinator/fixtures/history.json;
                // the rest continue that trend so the hero chart has something to show.
                history = new[]
                {
                    new WeekPoint { label = "Wk 1", attempted = 8, valid = 3, medianPeakDeg = 68 },
                    new WeekPoint { label = "Wk 2", attempted = 8, valid = 5, medianPeakDeg = 76 },
                    new WeekPoint { label = "Wk 3", attempted = 9, valid = 6, medianPeakDeg = 84 },
                    new WeekPoint { label = "Wk 4", attempted = 9, valid = 6, medianPeakDeg = 81 },
                    new WeekPoint { label = "Wk 5", attempted = 10, valid = 8, medianPeakDeg = 88 },
                    new WeekPoint { label = "Wk 6", attempted = 10, valid = 9, medianPeakDeg = 93 },
                },
            };
            model.calendar = SyntheticCalendar(model.streakDays);
            return model;
        }

        /// 16 weeks of invented activity, seeded so it is the same picture every run. Shaped like a real
        /// recovery rather than noise: patchy at the start, denser as the habit forms, and the last
        /// `streak` days unbroken so the grid and the streak tile agree with each other.
        static DayCell[] SyntheticCalendar(int streak)
        {
            const int weeks = 16, days = weeks * 7;
            var rng = new System.Random(20260926);
            var cells = new DayCell[days];
            // Start on a Sunday so each column is a clean week, like the graph this is modelled on.
            var end = DateTime.Today;
            var start = end.AddDays(-(days - 1));
            start = start.AddDays(-(int)start.DayOfWeek);

            for (int i = 0; i < days; i++)
            {
                var date = start.AddDays(i);
                float progress = i / (float)(days - 1);
                if (date > end) { cells[i] = new DayCell { date = date, level = 0 }; continue; }

                // Adherence climbs from about a third of days to most of them.
                float chance = Mathf.Lerp(.3f, .82f, progress);
                bool rest = date.DayOfWeek == DayOfWeek.Sunday && rng.NextDouble() < .55;
                int level = 0;
                if (!rest && rng.NextDouble() < chance)
                {
                    double roll = rng.NextDouble() + progress * .35;
                    level = roll > 1.05 ? 4 : roll > .78 ? 3 : roll > .45 ? 2 : 1;
                }
                cells[i] = new DayCell { date = date, level = level };
            }

            // The tail is the current streak, so the brightest run sits where the eye ends up.
            for (int i = 0; i < days; i++)
            {
                var date = cells[i].date;
                if (date > end) continue;
                int back = (end - date).Days;
                if (back < streak && cells[i].level == 0) cells[i].level = 1 + (back % 3);
            }
            return cells;
        }
    }

    /// Fills the bento tiles from a model and paints the charts. Static because it owns no state:
    /// the panel is the state, and this writes into it.
    public static class MenuDashboard
    {
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
            Text(root, "goal-copy", $"Working toward “{model.goal}”");

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
                cta.Q<Label>("cta-title").text = outstanding is { } next ? $"Start {next.title}" : "All done for today";
                cta.Q<Label>("cta-sub").text = outstanding is { } n2 ? n2.detail : "Play something for the fun of it";
            }

            float reach = model.history.Length > 0 ? model.history[^1].medianPeakDeg : 0;
            float first = model.history.Length > 0 ? model.history[0].medianPeakDeg : 0;
            float delta = reach - first;
            Text(root, "reach-now", $"{Mathf.RoundToInt(reach)}°");
            Text(root, "reach-delta", model.history.Length > 1
                ? $"{(delta >= 0 ? "+" : "")}{Mathf.RoundToInt(delta)}° further than week one"
                : "First session — no trend yet");
            Text(root, "reach-best", $"median peak · target {Mathf.RoundToInt(model.targetDeg)}°");
            Text(root, "reach-cheer", reach >= model.targetDeg
                ? $"Past your target by {Mathf.RoundToInt(reach - model.targetDeg)}° \u2014 nice."
                : $"{Mathf.RoundToInt(model.targetDeg - reach)}° to reach your target");
            Text(root, "data-note", model.measured ? "From your measured sessions" : "Demo data · not measured");

            Paint(root, "range-fan", (ctx, r) => DrawRangeFan(ctx, r, model));
            Paint(root, "calendar-grid", (ctx, r) => DrawCalendar(ctx, r, model.RecentDays(28)));
            Paint(root, "browse-glyph", DrawBrowseGlyph);
            Paint(root, "program-ring", (ctx, r) => DrawRing(ctx, r, model.ProgramFraction));
        }

        static void Paint(VisualElement root, string name, Action<MeshGenerationContext, Rect> draw)
        {
            var element = root.Q(name);
            if (element == null) return;
            element.generateVisualContent += ctx => draw(ctx, element.contentRect);
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
        /// A line graph of shoulder degrees is generic — it could be plotting anything, and "93°" does
        /// not feel like a distance until you see the arm sweep it. So this is a fan pivoting at the
        /// shoulder: a pale wedge for where week one reached, a bright band for everything gained
        /// since, and a tick at the clinician's target. The gain is the band, which is the one thing
        /// worth looking at.
        static void DrawRangeFan(MeshGenerationContext ctx, Rect r, MenuDashboardModel model)
        {
            if (r.width < 24 || r.height < 24 || model.history.Length == 0) return;
            var p = ctx.painter2D;
            float now = model.history[^1].medianPeakDeg;
            float start = model.history[0].medianPeakDeg;
            float target = model.targetDeg;

            // Arm at the side points down; raising it sweeps towards horizontal. Painter2D measures
            // from +X clockwise, so straight down is 90 and an elevation of E sits at 90 - E.
            float radius = Mathf.Min(r.width, r.height) * .94f;
            var pivot = new Vector2((r.width - radius) * .5f, (r.height - radius) * .5f);
            float Ang(float elevation) => 90 - elevation;

            void Wedge(float from, float to, Color fill)
            {
                p.fillColor = fill;
                p.BeginPath();
                p.MoveTo(pivot);
                p.Arc(pivot, radius, Angle.Degrees(Ang(to)), Angle.Degrees(Ang(from)));
                p.ClosePath();
                p.Fill();
            }
            void Spoke(float elevation, float inner, float outer, Color colour, float width)
            {
                float a = Ang(elevation) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                p.strokeColor = colour; p.lineWidth = width; p.lineCap = LineCap.Round;
                p.BeginPath();
                p.MoveTo(pivot + dir * (radius * inner));
                p.LineTo(pivot + dir * (radius * outer));
                p.Stroke();
            }

            // Faint guides every 30°, so the fan reads as a measurement and not just a shape.
            for (int deg = 30; deg <= 150; deg += 30)
                Spoke(deg, .22f, 1.02f, Reference.At(.20f), 1.5f);

            Wedge(0, start, Reference.At(.24f));
            Wedge(start, now, Progress.At(.62f));

            // The target tick sits outside the fan when it has been passed, which is the point.
            Spoke(target, .84f, 1.14f, Target, 5);

            // The arm itself, ending in the hand.
            float armA = Ang(now) * Mathf.Deg2Rad;
            var armDir = new Vector2(Mathf.Cos(armA), Mathf.Sin(armA));
            p.strokeColor = Ink;
            p.lineWidth = 9; p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(pivot); p.LineTo(pivot + armDir * radius); p.Stroke();

            p.fillColor = Ink;
            p.BeginPath(); p.Arc(pivot, 11, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.fillColor = Panel;
            p.strokeColor = Ink; p.lineWidth = 5;
            p.BeginPath(); p.Arc(pivot + armDir * radius, 13, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill(); p.Stroke();

            // Where week one stopped, so the band has a visible near edge.
            float oldA = Ang(start) * Mathf.Deg2Rad;
            var oldDir = new Vector2(Mathf.Cos(oldA), Mathf.Sin(oldA));
            p.strokeColor = Reference;
            p.lineWidth = 3;
            p.BeginPath(); p.MoveTo(pivot + oldDir * (radius * .12f)); p.LineTo(pivot + oldDir * radius); p.Stroke();
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

                // Today gets a ring rather than a different fill, so "where am I" and "did I train"
                // stay two separate readings instead of one ambiguous colour.
                if (cells[i].date != today) continue;
                p.strokeColor = Palette.Attention;
                p.lineWidth = 3;
                p.BeginPath();
                p.Arc(new Vector2(x + cell * .5f, y + cell * .5f), cell * .62f, Angle.Degrees(0), Angle.Degrees(360));
                p.Stroke();
            }
        }

        /// Four squares for "browse everything", so the two tiles on this board do not both end in the
        /// same chevron and read as the same control twice.
        static void DrawBrowseGlyph(MeshGenerationContext ctx, Rect r)
        {
            if (r.width < 8 || r.height < 8) return;
            var p = ctx.painter2D;
            float size = Mathf.Min(r.width, r.height), gap = size * .16f;
            float cell = (size - gap) * .5f;
            float ox = (r.width - size) * .5f, oy = (r.height - size) * .5f;
            p.fillColor = Palette.Live;
            for (int i = 0; i < 4; i++)
                RoundedSquare(p, ox + (i % 2) * (cell + gap), oy + (i / 2) * (cell + gap), cell, cell * .3f);
        }

        static void RoundedSquare(Painter2D p, float x, float y, float size, float radius, float height = -1)
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
            p.Fill();
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
