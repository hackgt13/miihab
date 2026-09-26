using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
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
        static readonly Dictionary<string, string> Movements = new()
        {
            ["arm-elevation.v1"] = "Seated shoulder raise",
            ["elbow-flexion.v1"] = "Seated biceps curl",
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
                    string movement = kind != null && Movements.TryGetValue(kind, out var named) ? named : kind;
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
        static readonly Color Progress = Palette.Progress;     // a measured value
        static readonly Color Reference = Palette.Reference;    // the plan it is drawn against
        static readonly Color Target = Palette.Target;         // what is being reached for
        static readonly Color Good = Palette.Good;             // a rep that counted
        static readonly Color Panel = Palette.Panel;
        static readonly Color Line = Palette.Line;

        public static void Populate(VisualElement root, CoachingPlanModel plan, MenuDashboardModel dashboard)
        {
            if (root == null || plan == null || dashboard == null) return;
            var primary = plan.Primary;
            var history = dashboard.history;
            var last = history.Length > 0 ? history[^1] : default;

            Text(root, "plan-provenance",
                $"Version {plan.version} · approved by {plan.approvedBy} · {plan.approvedAt:d MMM yyyy}");
            Text(root, "plan-source", plan.live ? "From your clinic" : "Demo plan · not from your clinic");
            Text(root, "coaching-note", string.IsNullOrWhiteSpace(plan.coachingNote)
                ? "Nothing added this visit." : $"“{plan.coachingNote}”");

            // ---- the reach, session by session
            float now = history.Length > 0 ? last.medianPeakDeg : 0;
            float first = history.Length > 0 ? history[0].medianPeakDeg : 0;
            Text(root, "trend-now", history.Length > 0 ? $"{Mathf.RoundToInt(now)}°" : "—");
            Text(root, "trend-delta", history.Length > 1
                ? $"{(now - first >= 0 ? "+" : "")}{Mathf.RoundToInt(now - first)}° since your first session"
                : "No trend yet — one session to go");
            Text(root, "trend-target", primary is { } p1
                ? $"median peak · prescribed to {Mathf.RoundToInt(p1.targetDeg)}°" +
                  (p1.targetMaxDeg > p1.targetDeg ? $", ceiling {Mathf.RoundToInt(p1.targetMaxDeg)}°" : "")
                : "median peak of each session");
            Text(root, "trend-note", history.Length switch
            {
                0 => "no sessions yet",
                1 => "1 session",
                _ => $"last {history.Length} sessions",
            });

            // ---- the three figures
            if (primary != null && primary.Value.envelope != null)
            {
                var measured = primary.Value;
                var envelope = measured.envelope.Value;
                int level = envelope.LevelOf(measured.targetDeg);
                Text(root, "level-number", level.ToString());
                Text(root, "level-caption", $"of {envelope.Levels} · now {Mathf.RoundToInt(measured.targetDeg)}°");
                Text(root, "envelope-note", envelope.autoApply
                    ? $"moves itself after {envelope.sessionsToProgress} good sessions"
                    : "your clinician moves this");
                Text(root, "envelope-caption",
                    $"{Mathf.RoundToInt(envelope.minTargetDeg)}° to {Mathf.RoundToInt(envelope.maxTargetDeg)}° · " +
                    $"{Mathf.RoundToInt(envelope.stepDeg)}° a step");
            }
            else
            {
                Text(root, "level-number", "—");
                Text(root, "level-caption", "no envelope set");
                Text(root, "envelope-note", "set by your clinician");
                Text(root, "envelope-caption", primary is { } only
                    ? $"prescribed to {Mathf.RoundToInt(only.targetDeg)}°" : "nothing measured in this plan");
            }

            int inBand = last.attempted > 0 ? Mathf.RoundToInt(100f * last.valid / last.attempted) : 0;
            Text(root, "band-number", last.attempted > 0 ? $"{inBand}%" : "—");
            Text(root, "band-caption", last.attempted > 0
                ? $"last session · {last.valid} of {last.attempted}"
                : "nothing measured yet");

            int week = Mathf.Max(1, Mathf.CeilToInt(dashboard.programDay / 7f));
            int weeks = Mathf.Max(1, Mathf.CeilToInt(dashboard.programTotalDays / 7f));
            Text(root, "week-number", week.ToString());
            Text(root, "week-caption", $"of {weeks} · {dashboard.DaysRemaining} days to go");

            Text(root, "dose-note", primary is { } dose ? $"{dose.targetCount} prescribed" : "");
            Text(root, "visit-sub", $"Talk through these numbers · “{plan.goal}”");

            // ---- the prescription, rebuilt rather than patched: approving a change writes a new version
            var list = root.Q("prescription-list");
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

            Paint(root, "plan-trend", (ctx, r) => DrawTrend(ctx, r, history, primary));
            Paint(root, "envelope-ladder", (ctx, r) => DrawEnvelope(ctx, r, primary));
            Paint(root, "dose-bars", (ctx, r) => DrawDose(ctx, r, history, primary?.targetCount ?? 0));
        }

        static VisualElement PrescriptionRow(CoachingPlanModel.Prescription p, bool first)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("prescription-row");
            if (first) row.AddToClassList("first");

            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("prescription-mark");
            if (p.measured) marker.AddToClassList("measured");
            row.Add(marker);

            var copy = new VisualElement { pickingMode = PickingMode.Ignore };
            copy.AddToClassList("prescription-copy");
            var title = new Label(p.movement) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("prescription-title");
            var dose = new Label(p.Dose) { pickingMode = PickingMode.Ignore };
            dose.AddToClassList("prescription-dose");
            copy.Add(title); copy.Add(dose);
            if (!string.IsNullOrWhiteSpace(p.note))
            {
                var note = new Label(p.note) { pickingMode = PickingMode.Ignore };
                note.AddToClassList("prescription-note-copy");
                copy.Add(note);
            }
            row.Add(copy);

            var where = new Label(p.where) { pickingMode = PickingMode.Ignore };
            where.AddToClassList("prescription-where");
            row.Add(where);
            return row;
        }

        // ------------------------------------------------------------------ the reach, session by session

        /// A line, because this is the one place a trend belongs. The board draws the same measurement as an
        /// arc pivoting at the shoulder — a reach, not a chart — and that is right for a headline number, but
        /// it can only ever show today. Six sessions side by side is a different reading, and the shape a
        /// person already knows how to read is a line going up.
        ///
        /// Drawn against the plan rather than against itself: the band from the target to the ceiling is the
        /// zone the prescription asks for, so a line that has climbed into it has arrived somewhere, and a
        /// line above it has overshot a limit rather than done especially well. Without the band the same
        /// polyline is just "a number that went up", which is exactly what a rehab chart must not be.
        static void DrawTrend(MeshGenerationContext ctx, Rect r,
                              MenuDashboardModel.WeekPoint[] history, CoachingPlanModel.Prescription? primary)
        {
            if (r.width < 40 || r.height < 30 || history.Length == 0) return;
            var p = ctx.painter2D;

            float pad = 10;
            float left = pad, right = r.width - pad, top = pad, bottom = r.height - pad;
            float target = primary?.targetDeg ?? 0, ceiling = primary?.targetMaxDeg ?? 0;

            // The scale has to hold the plan as well as the data, or a band the line never reaches falls off
            // the top and the chart quietly stops being drawn against anything.
            float lo = history.Min(h => h.medianPeakDeg), hi = history.Max(h => h.medianPeakDeg);
            if (target > 0) { lo = Mathf.Min(lo, target); hi = Mathf.Max(hi, target); }
            if (ceiling > 0) hi = Mathf.Max(hi, ceiling);
            float span = Mathf.Max(12, hi - lo);
            lo -= span * .18f; hi += span * .18f;

            float Y(float deg) => bottom - (deg - lo) / (hi - lo) * (bottom - top);
            float X(int i) => history.Length == 1 ? (left + right) * .5f
                : left + i * (right - left) / (history.Length - 1);

            // The band the prescription asks for.
            if (ceiling > target && target > 0)
            {
                float yTop = Y(ceiling), yBottom = Y(target);
                p.fillColor = Good.At(.16f);
                p.BeginPath();
                p.MoveTo(new(left, yTop)); p.LineTo(new(right, yTop));
                p.LineTo(new(right, yBottom)); p.LineTo(new(left, yBottom));
                p.ClosePath(); p.Fill();
            }

            // The target, as the line the band starts at; the ceiling as the quieter line it ends at.
            void Rule(float deg, Color colour, float width)
            {
                if (deg <= 0) return;
                p.strokeColor = colour; p.lineWidth = width; p.lineCap = LineCap.Butt;
                p.BeginPath(); p.MoveTo(new(left, Y(deg))); p.LineTo(new(right, Y(deg))); p.Stroke();
            }
            Rule(target, Target, 3);
            Rule(ceiling, Line.At(.85f), 2);

            // The baseline, so the line has a floor to sit on rather than floating in the tile.
            p.strokeColor = Line.At(.55f); p.lineWidth = 2;
            p.BeginPath(); p.MoveTo(new(left, bottom)); p.LineTo(new(right, bottom)); p.Stroke();

            // The measurement.
            p.strokeColor = Progress; p.lineWidth = 5; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new(X(0), Y(history[0].medianPeakDeg)));
            for (int i = 1; i < history.Length; i++) p.LineTo(new(X(i), Y(history[i].medianPeakDeg)));
            if (history.Length > 1) p.Stroke();

            for (int i = 0; i < history.Length; i++)
            {
                bool latest = i == history.Length - 1;
                var at = new Vector2(X(i), Y(history[i].medianPeakDeg));
                p.fillColor = Panel; p.strokeColor = Progress; p.lineWidth = latest ? 5 : 4;
                p.BeginPath(); p.Arc(at, latest ? 11 : 7, Angle.Degrees(0), Angle.Degrees(360));
                p.Fill(); p.Stroke();
            }
        }

        // ------------------------------------------------------------------ the envelope

        /// Where the target stands inside the limits the clinician approved, as a ladder with one rung per
        /// level. A bar would say "62% of the way", which is a number nobody was given; the rungs say "level
        /// 2 of 11", which is the number on the plan, and the gap to the ceiling says how much room is left
        /// before the clinician has to decide again.
        static void DrawEnvelope(MeshGenerationContext ctx, Rect r, CoachingPlanModel.Prescription? primary)
        {
            if (r.width < 40 || r.height < 12 || primary == null) return;
            var p = primary.Value;
            if (p.envelope == null) return;
            var envelope = p.envelope.Value;
            var painter = ctx.painter2D;

            float pad = 8, left = pad, right = r.width - pad;
            float mid = r.height * .5f, thickness = Mathf.Min(18, r.height * .5f);
            float lo = envelope.minTargetDeg, hi = Mathf.Max(envelope.maxTargetDeg, lo + 1);
            float At(float deg) => left + Mathf.Clamp01((deg - lo) / (hi - lo)) * (right - left);
            float here = At(p.targetDeg);

            void Track(float from, float to, Color colour)
            {
                if (to <= from) return;
                painter.strokeColor = colour; painter.lineWidth = thickness; painter.lineCap = LineCap.Round;
                painter.BeginPath(); painter.MoveTo(new(from, mid)); painter.LineTo(new(to, mid)); painter.Stroke();
            }

            Track(left, right, Reference.At(.24f));    // everything the envelope allows
            Track(left, here, Progress);               // everything already asked for

            // One rung per level, so the steps are countable rather than implied.
            int levels = envelope.Levels;
            for (int i = 0; i < levels; i++)
            {
                float x = At(lo + i * envelope.stepDeg);
                bool reached = lo + i * envelope.stepDeg <= p.targetDeg + .01f;
                painter.strokeColor = reached ? Panel.At(.75f) : Reference.At(.45f);
                painter.lineWidth = 2; painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.MoveTo(new(x, mid - thickness * .32f));
                painter.LineTo(new(x, mid + thickness * .32f));
                painter.Stroke();
            }

            // Today's target, as a marker sitting on the track, and the ceiling as a stop past the end of it.
            painter.strokeColor = Target; painter.lineWidth = 4; painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(new(here, mid - thickness * .95f));
            painter.LineTo(new(here, mid + thickness * .95f));
            painter.Stroke();

            painter.fillColor = Panel;
            painter.strokeColor = Target; painter.lineWidth = 4;
            painter.BeginPath(); painter.Arc(new(here, mid), thickness * .5f, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill(); painter.Stroke();
        }

        // ------------------------------------------------------------------ the dose

        /// Reps that counted against reps attempted, one column per session, with the prescribed count as
        /// the line across. Attempted-but-not-counted is drawn as the pale remainder of the same column
        /// rather than as a second bar: the reps happened, they are one effort, and splitting them into two
        /// bars invites reading the pale one as a mistake.
        static void DrawDose(MeshGenerationContext ctx, Rect r,
                             MenuDashboardModel.WeekPoint[] history, int prescribed)
        {
            if (r.width < 24 || r.height < 16 || history.Length == 0) return;
            var p = ctx.painter2D;

            float pad = 6, bottom = r.height - pad, top = pad;
            int ceiling = Mathf.Max(prescribed, history.Max(h => Mathf.Max(h.attempted, h.valid)));
            if (ceiling <= 0) return;
            float Y(float reps) => bottom - Mathf.Clamp01(reps / ceiling) * (bottom - top);

            float gap = Mathf.Min(10, r.width * .04f);
            float width = (r.width - gap * (history.Length - 1)) / history.Length;
            float radius = Mathf.Min(width * .3f, 7);

            for (int i = 0; i < history.Length; i++)
            {
                float x = i * (width + gap);
                p.fillColor = Reference.At(.22f);
                Column(p, x, Y(history[i].attempted), width, bottom - Y(history[i].attempted), radius);
                p.fillColor = Good;
                Column(p, x, Y(history[i].valid), width, bottom - Y(history[i].valid), radius);
            }

            if (prescribed <= 0) return;
            p.strokeColor = Target; p.lineWidth = 2.5f; p.lineCap = LineCap.Butt;
            p.BeginPath(); p.MoveTo(new(0, Y(prescribed))); p.LineTo(new(r.width, Y(prescribed))); p.Stroke();
        }

        /// A column with its top corners rounded and its base square, so a row of them reads as standing on
        /// the same floor.
        static void Column(Painter2D p, float x, float y, float width, float height, float radius)
        {
            if (height <= .5f) return;
            float rr = Mathf.Min(radius, Mathf.Min(width, height) * .5f);
            p.BeginPath();
            p.MoveTo(new(x, y + height));
            p.LineTo(new(x, y + rr));
            p.Arc(new(x + rr, y + rr), rr, Angle.Degrees(180), Angle.Degrees(270));
            p.LineTo(new(x + width - rr, y));
            p.Arc(new(x + width - rr, y + rr), rr, Angle.Degrees(270), Angle.Degrees(360));
            p.LineTo(new(x + width, y + height));
            p.ClosePath();
            p.Fill();
        }

        // ------------------------------------------------------------------ plumbing

        // One painter per element, for the same reason MenuDashboard keeps one: Populate runs again when the
        // plan arrives from the bridge, and a second handler would draw the old plan underneath the new one.
        static readonly ConditionalWeakTable<VisualElement, Action<MeshGenerationContext>> painters = new();

        static void Paint(VisualElement root, string name, Action<MeshGenerationContext, Rect> draw)
        {
            var element = root.Q(name);
            if (element == null) return;
            if (painters.TryGetValue(element, out var previous))
            { element.generateVisualContent -= previous; painters.Remove(element); }
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
    }
}
