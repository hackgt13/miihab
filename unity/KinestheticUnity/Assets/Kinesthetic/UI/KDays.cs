using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// Days, one square each, oldest first. Three forms for the three places a run of days is read:
    ///
    ///   * `Calendar` — the board's consistency tile: rows of seven, a green that deepens with how much was
    ///     done, today outlined. Twenty-eight cells are large enough to read on a 1.85 m board; a wall of
    ///     112 was three millimetres each.
    ///   * `Strip` — a friend's profile: one row, a filled square for a day they moved. No numbers on it:
    ///     it is a rhythm, not a score.
    ///   * `Program` — the coaching pane: the whole block as a calendar, a column per week, every past day
    ///     crossed off by hand, today ringed, the last day washed coral. Anything a screen places inside
    ///     (the "END" label, which Painter2D cannot write) is a child and is laid out by the screen against
    ///     ProgramGrid().
    ///
    ///     <k:KDays name="calendar-grid" form="Calendar" class="calendar-grid" />
    ///     days.levels = "0120400-"; days.today = 6;
    ///
    /// `levels` is one character per day: '0' to '4' for how much was done, and any other character ('-')
    /// for a day not drawn — the rest of this week, which is not here yet. The screen gives it a size.
    /// Every reading is an attribute, so the headset's replica draws the same days from the same string.
    /// Painted in code; colours come from Palette.cs.
    [UxmlElement]
    public partial class KDays : VisualElement
    {
        public enum Form { Calendar, Strip, Program }

        const string Block = "k-days";
        Form formValue;
        string levelsValue = "";
        int todayIndex = -1, seedValue;

        [UxmlAttribute]
        public Form form { get => formValue; set { formValue = value; KStyles.Variant(this, Block, value); MarkDirtyRepaint(); } }

        /// One character per day, oldest first: '0'..'4', or anything else for a day not drawn.
        [UxmlAttribute]
        public string levels { get => levelsValue; set { levelsValue = value ?? ""; MarkDirtyRepaint(); } }

        /// Which character is today, or -1 for none. Calendar outlines it; Program rings it and crosses
        /// off everything before it.
        [UxmlAttribute]
        public int today { get => todayIndex; set { todayIndex = value; MarkDirtyRepaint(); } }

        /// Program only: what the hand's wobble is keyed to (the program's first day), so a day is crossed
        /// off the same way on every repaint and on every screen.
        [UxmlAttribute]
        public int seed { get => seedValue; set { seedValue = value; MarkDirtyRepaint(); } }

        public KDays()
        {
            AddToClassList(Block);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            form = Form.Calendar;
        }

        // The calendar ramp is the jungle scale because its role is exactly what a done day is — Good,
        // "counted, done, reached" — and depth of green then reads as how much was done.
        static readonly Color[] Ramp = { Palette.Jungle10, Palette.Jungle20, Palette.Jungle30, Palette.Jungle40 };

        static int Level(char c) => c >= '0' && c <= '4' ? c - '0' : -1;

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (levelsValue.Length == 0) return;
            switch (formValue)
            {
                case Form.Calendar: DrawCalendar(ctx.painter2D, r); break;
                case Form.Strip: DrawStrip(ctx.painter2D, r); break;
                case Form.Program: DrawProgram(ctx.painter2D, r); break;
            }
        }

        // ------------------------------------------------------------------ calendar

        void DrawCalendar(Painter2D p, Rect r)
        {
            if (r.width < 8 || r.height < 8) return;
            int n = levelsValue.Length, rows = Mathf.CeilToInt(n / 7f);
            float gap = 8;
            float cell = Mathf.Min((r.width - gap * 6) / 7f, (r.height - gap * (rows - 1)) / rows);
            float gridW = cell * 7 + gap * 6, gridH = cell * rows + gap * (rows - 1);
            float ox = r.x + (r.width - gridW) * .5f, oy = r.y + (r.height - gridH) * .5f;
            float radius = cell * .3f;

            for (int i = 0; i < n; i++)
            {
                int level = Level(levelsValue[i]);
                if (level < 0) continue;
                int row = i / 7, col = i % 7;
                float x = ox + col * (cell + gap), y = oy + row * (cell + gap);
                p.fillColor = level == 0 ? Palette.Line.At(.45f) : Ramp[Mathf.Clamp(level - 1, 0, Ramp.Length - 1)];
                Cell(p, x, y, cell, cell, radius);

                // Today is outlined rather than filled differently, so "where am I" and "did I train" stay
                // two separate readings. An outline on the square itself sits in the grid; a circle over it
                // looked like a separate mark that had landed there.
                if (i != todayIndex) continue;
                p.strokeColor = Palette.Target;
                p.lineWidth = 3;
                Cell(p, x - 2.5f, y - 2.5f, cell + 5, cell + 5, radius + 2, stroke: true);
            }
        }

        // ------------------------------------------------------------------ strip

        void DrawStrip(Painter2D p, Rect r)
        {
            if (r.width <= 1) return;
            int n = levelsValue.Length;
            float gap = 3f, cell = Mathf.Max(4f, (r.width - gap * (n - 1)) / n);
            for (int i = 0; i < n; i++)
            {
                int level = Level(levelsValue[i]);
                if (level < 0) continue;
                p.fillColor = level > 0 ? Palette.Good : Palette.Slate30;
                float x = r.x + i * (cell + gap), y = r.y;
                p.BeginPath();
                p.MoveTo(new Vector2(x, y));
                p.LineTo(new Vector2(x + cell, y));
                p.LineTo(new Vector2(x + cell, y + cell));
                p.LineTo(new Vector2(x, y + cell));
                p.ClosePath();
                p.Fill();
            }
        }

        // ------------------------------------------------------------------ program

        /// How a program grid divides its rect: columns across, seven down, with a gap that comes off the
        /// cell size so a twelve-week block and a thirty-week one are spaced the same way. Public so a
        /// screen that has to land something on a particular day lands it where the pen does.
        public static (float cw, float ch, float gap) ProgramGrid(Rect r, int columns)
        {
            columns = Mathf.Max(1, columns);
            float gap = Mathf.Clamp(Mathf.Min(r.width / columns, r.height / 7f) * .14f, 3, 9);
            return ((r.width - gap * (columns - 1)) / columns, (r.height - gap * 6) / 7f, gap);
        }

        /// The rect is divided into columns x 7 with no aspect cap, so day letters and week numbers beside
        /// it — laid out by flex on the same rect — land on the same rows and columns.
        void DrawProgram(Painter2D p, Rect r)
        {
            if (r.width < 40 || r.height < 40) return;
            int n = levelsValue.Length, columns = Mathf.CeilToInt(n / 7f), last = n - 1;
            var (cw, ch, gap) = ProgramGrid(r, columns);
            float radius = Mathf.Min(cw, ch) * .28f;

            for (int i = 0; i < n; i++)
            {
                int column = i / 7, row = i % 7;
                var cell = new Rect(r.x + column * (cw + gap), r.y + row * (ch + gap), cw, ch);
                int level = Mathf.Max(0, Level(levelsValue[i]));
                bool now = i == todayIndex;

                // The paper the day is written on. A crossed-off day keeps the plain square a past day has —
                // the pen is the mark, and tinting the paper under it as well would say the same thing twice.
                // The last day of the program is the one square filled for what it is rather than for what
                // happened on it: a coral wash rather than a ring, because a ring would read as another kind
                // of today next to the one four squares away.
                p.fillColor = now ? Palette.Progress.At(.20f)
                            : i == last ? Palette.Target.At(.26f)
                            : i < todayIndex ? Palette.Reference.At(.20f)
                            : Palette.Reference.At(.11f);
                Cell(p, cell.x, cell.y, cell.width, cell.height, radius);

                // Today is ringed as well as washed, so "where am I" and "did I train" stay two separate
                // readings: the ring is the date, the pen is the work, and a day can carry both.
                if (now)
                {
                    p.strokeColor = Palette.Progress; p.lineWidth = 4;
                    Cell(p, cell.x - 3, cell.y - 3, cell.width + 6, cell.height + 6, radius + 2, stroke: true);
                }

                if (i < todayIndex) CrossOff(p, cell, seedValue + i * 31, level);
            }
        }

        /// Two bowed strokes through a day, drawn as a hand would: each end wanders, each stroke overshoots
        /// the square by a little and bows off true, one is heavier than the other. `seed` is the day, so the
        /// same day is crossed off the same way every repaint; `level` is how much work landed, and a fuller
        /// day presses harder.
        static void CrossOff(Painter2D p, Rect cell, int seed, int level)
        {
            float size = Mathf.Min(cell.width, cell.height);
            if (size < 10) return;

            // The stroke aims at a point inside the square, not at its corner. Aiming at the corner and then
            // overshooting put every X across its neighbours, and fourteen of them made a mesh rather than
            // fourteen crossed-off days — on a grid this tight the hand has to stay inside the box.
            float inset = size * .15f;
            float wander = size * .07f;
            float weight = Mathf.Clamp(size * .062f, 1.8f, 3.6f) + level * .2f;

            float Jitter(int salt) => (Noise(seed, salt) - .5f) * 2f * wander;
            Vector2 Corner(bool right, bool low, int salt) => new(
                (right ? cell.xMax - inset : cell.x + inset) + Jitter(salt),
                (low ? cell.yMax - inset : cell.y + inset) + Jitter(salt + 7));

            p.lineCap = LineCap.Round;
            p.strokeColor = Palette.Coral50;   // the hand crossing a day off

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
            Stroke(Corner(false, false, 1), Corner(true, true, 2), (Noise(seed, 3) - .5f) * 2f * bowAmount, weight);
            Stroke(Corner(true, false, 4), Corner(false, true, 5), (Noise(seed, 6) - .5f) * 2f * bowAmount, weight * .86f);
        }

        /// A stable 0..1 from a seed and a salt. No Random, because a painter runs again on every repaint
        /// and a day that redrew itself differently each time would shimmer.
        static float Noise(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 374761393 + salt * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
            }
        }

        /// A rounded cell. Corners rather than a plain rect because a grid of squares with sharp corners
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
    }
}
