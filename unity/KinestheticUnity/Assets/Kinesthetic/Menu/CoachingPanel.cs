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
        // Every registered kind (coordinator/exercise/registry.ts), not just the two the LIBRARY names:
        // the demo plan on disk prescribes the camera kind, and a pane that answered "shoulder-raise.v1 ·
        // right" would be showing the patient a database key. The camera and AirPod kinds measure the same
        // movement with different sensors, so they share a name; only one is ever prescribed at a time.
        static readonly Dictionary<string, string> Movements = new()
        {
            ["arm-elevation.v1"] = "Seated shoulder raise",
            ["shoulder-raise.v1"] = "Seated shoulder raise",
            ["elbow-flexion.v1"] = "Seated biceps curl",
            ["trunk-rotation.v1"] = "Seated trunk rotation",
            // The single-AirPod library (coordinator/exercise/imu-library.ts).
            ["neck-flexion.v1"] = "Seated neck flexion",
            ["neck-extension.v1"] = "Seated neck extension",
            ["neck-lateral-flexion.v1"] = "Seated neck side bend",
            ["shoulder-abduction.v1"] = "Seated side arm raise",
            ["scaption.v1"] = "Seated scaption raise",
            ["arm-hold.v1"] = "Seated raise and hold",
            ["forearm-rotation.v1"] = "Seated forearm turn",
            ["shoulder-external-rotation.v1"] = "Shoulder rotation at 90/90",
            ["trunk-flexion.v1"] = "Seated forward bend",
            ["trunk-lateral-flexion.v1"] = "Seated side bend",
            ["trunk-extension.v1"] = "Standing back bend",
            ["knee-extension.v1"] = "Seated knee straightening",
            ["straight-leg-raise.v1"] = "Straight-leg raise",
            ["hip-abduction.v1"] = "Standing side leg raise",
            ["seated-march.v1"] = "Seated march",
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
        static readonly Color Panel = Palette.Panel;
        static readonly Color Pen = Palette.Coral50;           // the hand crossing a day off

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

            Paint(root, "program-grid", (ctx, r) => DrawProgram(ctx, r, start, columns, finish, levels));
            PlaceEndFlag(root, columns, totalDays);
            Paint(root, "envelope-ladder", (ctx, r) => DrawEnvelope(ctx, r, primary));
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
        /// The rect is divided into columns x 7 with no aspect cap, so the day letters and week numbers beside
        /// it — laid out by flex on the same rect — land on the same rows and columns without either side
        /// knowing the other's cell size.
        static void DrawProgram(MeshGenerationContext ctx, Rect r, DateTime start, int columns,
                                DateTime finish, Dictionary<DateTime, int> levels)
        {
            if (r.width < 40 || r.height < 40 || columns < 1) return;
            var p = ctx.painter2D;
            var today = DateTime.Today;

            var (cw, ch, gap) = Grid(r, columns);
            float radius = Mathf.Min(cw, ch) * .28f;

            for (int column = 0; column < columns; column++)
            for (int row = 0; row < 7; row++)
            {
                var date = start.AddDays(column * 7 + row);
                if (date > finish) continue;                      // the tail of the last week, if it has one
                var cell = new Rect(column * (cw + gap), row * (ch + gap), cw, ch);
                levels.TryGetValue(date.Date, out int level);
                bool now = date == today;

                // The paper the day is written on. A crossed-off day keeps the plain square a past day has —
                // the pen is the mark, and tinting the paper under it as well would say the same thing twice.
                // The last day of the program is the one square filled for what it is rather than for what
                // happened on it: it is a coral wash rather than a ring, because a ring would read as another
                // kind of today next to the one four squares away.
                p.fillColor = now ? Progress.At(.20f)
                            : date == finish ? Target.At(.26f)
                            : date < today ? Reference.At(.20f)
                            : Reference.At(.11f);
                Cell(p, cell.x, cell.y, cell.width, cell.height, radius);

                // Today is ringed as well as washed, so "where am I" and "did I train" stay two separate
                // readings: the ring is the date, the pen is the work, and a day can carry both.
                if (now)
                {
                    p.strokeColor = Progress; p.lineWidth = 4;
                    Cell(p, cell.x - 3, cell.y - 3, cell.width + 6, cell.height + 6, radius + 2, stroke: true);
                }

                if (date < today) CrossOff(p, cell, date.DayOfYear * 31 + date.Year, level);
            }
        }

        /// How the grid divides its rect: columns across, seven down, with a gap that comes off the cell size so
        /// a twelve-week block and a thirty-week one are spaced the same way. Shared, so anything that has to
        /// land on a particular day lands where the pen does.
        static (float cw, float ch, float gap) Grid(Rect r, int columns)
        {
            float gap = Mathf.Clamp(Mathf.Min(r.width / columns, r.height / 7f) * .14f, 3, 9);
            return ((r.width - gap * (columns - 1)) / columns, (r.height - gap * 6) / 7f, gap);
        }

        /// "END" written on the last day of the program, because a coral square at the end of the block says
        /// something is there without saying what. Painter2D draws no text, so this is a label placed on the
        /// cell — positioned from the same Grid() the painter uses, and put back whenever the grid is resized.
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
                var (cw, ch, gap) = Grid(r, columns);
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

        /// Two bowed strokes through a day, drawn as a hand would: each end wanders, each stroke overshoots the
        /// square by a little and bows off true, one is heavier than the other, and the second starts slightly
        /// after the first crosses it. `seed` is the date, so the same day is crossed off the same way every
        /// repaint; `level` is how much work landed, and a fuller day presses harder.
        static void CrossOff(Painter2D p, Rect cell, int seed, int level)
        {
            float size = Mathf.Min(cell.width, cell.height);
            if (size < 10) return;

            // The stroke aims at a point inside the square, not at its corner. Aiming at the corner and then
            // overshooting put every X across its neighbours, and fourteen of them made a mesh rather than
            // fourteen crossed-off days — on a grid this tight the hand has to stay inside the box.
            float inset = size * .15f;           // where the stroke starts, in from the corner
            float wander = size * .07f;          // how far its end misses that
            float weight = Mathf.Clamp(size * .062f, 1.8f, 3.6f) + level * .2f;

            float Jitter(int salt) => (Noise(seed, salt) - .5f) * 2f * wander;
            Vector2 Corner(bool right, bool low, int salt) => new(
                (right ? cell.xMax - inset : cell.x + inset) + Jitter(salt),
                (low ? cell.yMax - inset : cell.y + inset) + Jitter(salt + 7));

            p.lineCap = LineCap.Round;
            p.strokeColor = Pen;

            // Top-left to bottom-right, then top-right to bottom-left. The bow is perpendicular to the stroke
            // and its side comes off the seed, so some X's bulge out and some in.
            void Stroke(Vector2 from, Vector2 to, float bow, float width)
            {
                var mid = (from + to) * .5f;
                var away = new Vector2(-(to - from).y, (to - from).x).normalized;
                p.lineWidth = width;
                p.BeginPath();
                p.MoveTo(from);
                p.QuadraticCurveTo(mid + away * bow, to);
                p.Stroke();
            }

            float bowAmount = size * .055f;
            Stroke(Corner(false, false, 1), Corner(true, true, 2),
                   (Noise(seed, 3) - .5f) * 2f * bowAmount, weight);
            Stroke(Corner(true, false, 4), Corner(false, true, 5),
                   (Noise(seed, 6) - .5f) * 2f * bowAmount, weight * .86f);
        }

        /// A stable 0..1 from a seed and a salt. Two integers in, one hash out: no Random, because a painter
        /// runs again on every repaint and a day that redrew itself differently each time would shimmer.
        static float Noise(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 374761393 + salt * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
            }
        }

        /// A rounded cell. Corners rather than a plain rect because a grid of 84 squares with sharp corners
        /// reads as a table, and this is a run of days.
        static void Cell(Painter2D p, float x, float y, float w, float h, float radius, bool stroke = false)
        {
            float rr = Mathf.Max(0, Mathf.Min(radius, Mathf.Min(w, h) * .5f));
            p.BeginPath();
            p.MoveTo(new(x + rr, y));
            p.LineTo(new(x + w - rr, y));
            p.Arc(new(x + w - rr, y + rr), rr, Angle.Degrees(-90), Angle.Degrees(0));
            p.LineTo(new(x + w, y + h - rr));
            p.Arc(new(x + w - rr, y + h - rr), rr, Angle.Degrees(0), Angle.Degrees(90));
            p.LineTo(new(x + rr, y + h));
            p.Arc(new(x + rr, y + h - rr), rr, Angle.Degrees(90), Angle.Degrees(180));
            p.LineTo(new(x, y + rr));
            p.Arc(new(x + rr, y + rr), rr, Angle.Degrees(180), Angle.Degrees(270));
            p.ClosePath();
            if (stroke) p.Stroke(); else p.Fill();
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
