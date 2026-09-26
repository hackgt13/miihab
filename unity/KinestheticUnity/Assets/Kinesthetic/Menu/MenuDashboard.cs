using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    /// What the menu board shows about the person standing in front of it: what is prescribed today,
    /// how the streak is going, and whether the reach is actually improving.
    ///
    /// The shapes here deliberately mirror the coordinator's own records — `today` is one entry per
    /// `plan.activities[]` (schema kinesthetic.plan.v2), `history` is one entry per completed session
    /// the way `/api/sessions` reports it. So replacing Placeholder() with a fetch is a swap of this
    /// one object, not a rewrite of the panel.
    ///
    /// Until that fetch exists the numbers come from `coordinator/fixtures/history.json`, which is
    /// authored demo data and labelled as such in the fixture itself. Nothing here is measured, and
    /// the board says so.
    public sealed class MenuDashboardModel
    {
        public string patientName = "Alex";
        public string goal = "Play golf again with their best friend";
        public int streakDays = 12;
        public int weekSessionsDone = 4, weekSessionsGoal = 5;
        public int programDay = 21;
        public bool measured;                 // true once these come from real sessions
        public TodayTask[] today = Array.Empty<TodayTask>();
        public WeekPoint[] history = Array.Empty<WeekPoint>();

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

        public static MenuDashboardModel Placeholder() => new()
        {
            // Values transcribed from coordinator/fixtures/history.json so the shape and the trend are
            // the real ones; only the join to a live plan is missing.
            today = new[]
            {
                new TodayTask { activityId = "rehab.studio", title = "Movement Studio", detail = "8 shoulder raises · right", done = false },
                new TodayTask { activityId = "golf.adaptive", title = "Golf", detail = "9 holes with a friend", done = false },
            },
            history = new[]
            {
                new WeekPoint { label = "Wk 1", attempted = 8, valid = 3, medianPeakDeg = 68 },
                new WeekPoint { label = "Wk 2", attempted = 8, valid = 5, medianPeakDeg = 76 },
                new WeekPoint { label = "Wk 3", attempted = 9, valid = 6, medianPeakDeg = 84 },
            },
        };

        public TodayTask? FirstOutstanding()
        {
            foreach (var task in today) if (!task.done) return task;
            return null;
        }
    }

    /// Fills the bento tiles from a model and paints the two charts. Static because it owns no state:
    /// the panel is the state, and this writes into it.
    public static class MenuDashboard
    {
        // Rotates on the hour of day rather than at random, so the board is not a different greeting
        // every time someone glances at it.
        static string Greeting(int hour) =>
            hour < 12 ? "Good morning" : hour < 18 ? "Welcome back" : "Good evening";

        public static void Populate(VisualElement root, MenuDashboardModel model)
        {
            var now = DateTime.Now;
            Text(root, "splash-greeting", $"{Greeting(now.Hour)}, {model.patientName}!");
            Text(root, "splash-sub", $"{now:dddd d MMMM} · Day {model.programDay} of your program");
            Text(root, "goal-copy", $"“{model.goal}”");

            Text(root, "streak-number", model.streakDays.ToString());
            Text(root, "streak-caption", model.streakDays == 1 ? "day in a row" : "days in a row");
            Text(root, "week-count", $"{model.weekSessionsDone} of {model.weekSessionsGoal}");

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
                cta.SetEnabled(true);
            }

            var reach = model.history.Length > 0 ? model.history[^1].medianPeakDeg : 0;
            var first = model.history.Length > 0 ? model.history[0].medianPeakDeg : 0;
            Text(root, "reach-now", $"{Mathf.RoundToInt(reach)}°");
            float delta = reach - first;
            Text(root, "reach-delta", model.history.Length > 1
                ? $"{(delta >= 0 ? "+" : "")}{Mathf.RoundToInt(delta)}° since {model.history[0].label}"
                : "First session — no trend yet");
            Text(root, "data-note", model.measured ? "From your measured sessions" : "Demo data · not measured");

            var chart = root.Q("progress-chart");
            if (chart != null)
            {
                chart.generateVisualContent += ctx => DrawTrend(ctx, chart.contentRect, model.history);
                chart.MarkDirtyRepaint();
            }
            var meter = root.Q("week-meter");
            if (meter != null)
            {
                meter.generateVisualContent += ctx => DrawMeter(ctx, meter.contentRect, model.weekSessionsDone, model.weekSessionsGoal);
                meter.MarkDirtyRepaint();
            }
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

        // Reach over time. A line, its soft fill, and a dot per session — the point is the slope,
        // so the axis starts below the lowest reading rather than at zero.
        static void DrawTrend(MeshGenerationContext ctx, Rect r, MenuDashboardModel.WeekPoint[] points)
        {
            if (r.width < 4 || r.height < 4 || points.Length == 0) return;
            var p = ctx.painter2D;
            float w = r.width, h = r.height;
            float lo = points.Min(x => x.medianPeakDeg), hi = points.Max(x => x.medianPeakDeg);
            float pad = Mathf.Max(6, (hi - lo) * .35f);
            lo -= pad; hi += pad;
            float X(int i) => points.Length == 1 ? w * .5f : w * i / (points.Length - 1f);
            float Y(float v) => h - (v - lo) / Mathf.Max(.001f, hi - lo) * h;

            // Faint baselines so the rise reads as a measurement, not a decoration.
            p.strokeColor = new Color(.44f, .68f, .79f, .16f);
            p.lineWidth = 1;
            for (int i = 0; i <= 3; i++)
            {
                float y = h * i / 3f;
                p.BeginPath(); p.MoveTo(new(0, y)); p.LineTo(new(w, y)); p.Stroke();
            }

            if (points.Length > 1)
            {
                p.fillColor = new Color(.24f, .66f, .85f, .16f);
                p.BeginPath();
                p.MoveTo(new(X(0), h));
                for (int i = 0; i < points.Length; i++) p.LineTo(new(X(i), Y(points[i].medianPeakDeg)));
                p.LineTo(new(X(points.Length - 1), h));
                p.ClosePath(); p.Fill();

                p.strokeColor = new Color(.13f, .61f, .83f);
                p.lineWidth = 4; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                p.MoveTo(new(X(0), Y(points[0].medianPeakDeg)));
                for (int i = 1; i < points.Length; i++) p.LineTo(new(X(i), Y(points[i].medianPeakDeg)));
                p.Stroke();
            }

            for (int i = 0; i < points.Length; i++)
            {
                var at = new Vector2(X(i), Y(points[i].medianPeakDeg));
                p.fillColor = Color.white;
                p.strokeColor = new Color(.13f, .61f, .83f);
                p.lineWidth = 3;
                p.BeginPath(); p.Arc(at, i == points.Length - 1 ? 8 : 6, Angle.Degrees(0), Angle.Degrees(360));
                p.Fill(); p.Stroke();
            }
        }

        // Sessions this week as pips. Discrete on purpose — "4 of 5" is a count a person can check,
        // where a continuous bar invites reading a precision that is not there.
        static void DrawMeter(MeshGenerationContext ctx, Rect r, int done, int goal)
        {
            if (r.width < 4 || r.height < 2 || goal <= 0) return;
            var p = ctx.painter2D;
            float gap = 8, cell = (r.width - gap * (goal - 1)) / goal, h = Mathf.Min(r.height, 16);
            for (int i = 0; i < goal; i++)
            {
                p.fillColor = i < done ? new Color(.16f, .67f, .87f) : new Color(.62f, .78f, .86f, .35f);
                float x = i * (cell + gap);
                p.BeginPath();
                p.MoveTo(new(x, 0)); p.LineTo(new(x + cell, 0));
                p.LineTo(new(x + cell, h)); p.LineTo(new(x, h));
                p.ClosePath(); p.Fill();
            }
        }
    }
}
