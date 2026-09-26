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
        // The app's palette is teal, but a contribution grid only reads as one if it is green, so the
        // scale is green tuned warm enough to sit beside the teal rather than fight it.
        static readonly Color Empty = new(.60f, .72f, .78f, .38f);
        static readonly Color[] Levels =
        {
            new(.78f, .90f, .81f), new(.56f, .82f, .63f), new(.33f, .72f, .47f), new(.16f, .58f, .34f),
        };
        static readonly Color Accent = new(.11f, .56f, .77f);

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
            Text(root, "streak-caption", $"best so far {model.bestStreakDays}");
            Text(root, "week-count", $"{model.weekSessionsDone} of {model.weekSessionsGoal} this week");
            Text(root, "goal-copy", $"Working toward “{model.goal}”");

            Text(root, "ring-number", model.DaysRemaining.ToString());
            Text(root, "ring-caption", "days left");
            Text(root, "ring-note", $"{Mathf.RoundToInt(model.ProgramFraction * 100)}% of your program");

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
                ? $"{(delta >= 0 ? "+" : "")}{Mathf.RoundToInt(delta)}° since {model.history[0].label}"
                : "First session — no trend yet");
            Text(root, "reach-best", $"Best {Mathf.RoundToInt(model.BestReachDeg)}° · target {Mathf.RoundToInt(model.targetDeg)}°");
            Text(root, "data-note", model.measured ? "From your measured sessions" : "Demo data · not measured");

            Paint(root, "progress-chart", (ctx, r) => DrawHero(ctx, r, model));
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

        /// Reach over time, as the board's headline. The axis starts below the lowest reading rather
        /// than at zero because the point is the slope, and the prescribed target is drawn as a line
        /// so a rising curve can be read against what it is rising towards.
        static void DrawHero(MeshGenerationContext ctx, Rect r, MenuDashboardModel model)
        {
            var points = model.history;
            if (r.width < 8 || r.height < 8 || points.Length == 0) return;
            var p = ctx.painter2D;
            float w = r.width, h = r.height;

            float lo = points.Min(x => x.medianPeakDeg), hi = points.Max(x => x.medianPeakDeg);
            hi = Mathf.Max(hi, model.targetDeg);
            float pad = Mathf.Max(5, (hi - lo) * .3f);
            lo -= pad; hi += pad;
            float X(int i) => points.Length == 1 ? w * .5f : w * i / (points.Length - 1f);
            float Y(float v) => h - (v - lo) / Mathf.Max(.001f, hi - lo) * h;

            p.strokeColor = new Color(.44f, .68f, .79f, .14f);
            p.lineWidth = 1;
            for (int i = 1; i < 4; i++)
            {
                float y = h * i / 4f;
                p.BeginPath(); p.MoveTo(new(0, y)); p.LineTo(new(w, y)); p.Stroke();
            }

            // Target line, dashed by hand because Painter2D has no dash pattern.
            float ty = Y(model.targetDeg);
            p.strokeColor = new Color(.96f, .58f, .24f, .75f);
            p.lineWidth = 2;
            for (float x = 0; x < w; x += 16)
            {
                p.BeginPath(); p.MoveTo(new(x, ty)); p.LineTo(new(Mathf.Min(x + 9, w), ty)); p.Stroke();
            }

            if (points.Length > 1)
            {
                p.fillColor = new Color(.16f, .62f, .84f, .17f);
                p.BeginPath();
                p.MoveTo(new(X(0), h));
                for (int i = 0; i < points.Length; i++) p.LineTo(new(X(i), Y(points[i].medianPeakDeg)));
                p.LineTo(new(X(points.Length - 1), h));
                p.ClosePath(); p.Fill();

                p.strokeColor = Accent;
                p.lineWidth = 5; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(new(X(0), Y(points[0].medianPeakDeg)));
                for (int i = 1; i < points.Length; i++) p.LineTo(new(X(i), Y(points[i].medianPeakDeg)));
                p.Stroke();
            }

            float best = model.BestReachDeg;
            for (int i = 0; i < points.Length; i++)
            {
                var at = new Vector2(X(i), Y(points[i].medianPeakDeg));
                bool last = i == points.Length - 1;
                bool isBest = Mathf.Approximately(points[i].medianPeakDeg, best);
                if (last)
                {
                    p.fillColor = new Color(.16f, .62f, .84f, .18f);
                    p.BeginPath(); p.Arc(at, 15, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                }
                p.fillColor = Color.white;
                p.strokeColor = isBest ? new Color(.17f, .68f, .40f) : Accent;
                p.lineWidth = last ? 5 : 3;
                p.BeginPath(); p.Arc(at, last ? 9 : 6, Angle.Degrees(0), Angle.Degrees(360));
                p.Fill(); p.Stroke();
            }
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
                p.strokeColor = new Color(.96f, .55f, .20f);
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
            p.fillColor = new Color(.55f, .78f, .88f);
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
            p.strokeColor = new Color(.47f, .66f, .74f, .22f);
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
                p.fillColor = i < done ? Accent : new Color(.62f, .78f, .86f, .32f);
                RoundedSquare(p, i * (cell + gap), 0, cell, h * .5f, h);
            }
        }
    }
}
