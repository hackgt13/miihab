using System;
using System.Collections.Generic;

namespace Kinesthetic.Bowling
{
    // Ten-pin rules, including delayed strike/spare bonuses and the tenth-frame rack.
    public sealed class BowlingScore
    {
        readonly List<int>[] frames = new List<int>[10];
        public int Frame { get; private set; }
        public bool Complete { get; private set; }
        public int Standing { get; private set; } = 10;
        public int BallNumber => Complete ? frames[9].Count : frames[Frame].Count + 1;
        public int Total { get { int total = 0; for (int f = 0; f < 10; f++) { var s = Cumulative(f); if (s.HasValue) total = s.Value; } return total; } }
        public BowlingScore() { for (int f = 0; f < 10; f++) frames[f] = new List<int>(3); }
        public IReadOnlyList<int> Rolls(int frame) => frames[frame];

        // Returns true when a fresh rack is needed for the next delivery.
        public bool Add(int pins)
        {
            if (Complete || pins < 0 || pins > Standing) throw new ArgumentOutOfRangeException(nameof(pins));
            var rolls = frames[Frame]; rolls.Add(pins); Standing -= pins;
            if (Frame < 9)
            {
                if (pins == 10 || rolls.Count == 2) { Frame++; Standing = 10; return true; }
                return false;
            }
            if (rolls.Count == 2 && rolls[0] + rolls[1] < 10 || rolls.Count == 3) { Complete = true; return false; }
            if (Standing == 0) { Standing = 10; return true; }
            return false;
        }

        public int? Cumulative(int through)
        {
            int total = 0;
            for (int f = 0; f <= through; f++)
            {
                var r = frames[f];
                if (f == 9) { if (!Complete) return null; foreach (int p in r) total += p; continue; }
                if (r.Count == 0) return null;
                bool strike = r[0] == 10, spare = r.Count == 2 && r[0] + r[1] == 10;
                if (!strike && r.Count < 2) return null;
                int bonus = strike ? 2 : spare ? 1 : 0;
                total += strike || spare ? 10 : r[0] + r[1];
                for (int next = f + 1; next < 10 && bonus > 0; next++)
                    foreach (int p in frames[next]) { total += p; if (--bonus == 0) break; }
                if (bonus > 0) return null;
            }
            return total;
        }

        public string Marks(int frame)
        {
            var r = frames[frame]; var marks = new List<string>();
            for (int i = 0; i < r.Count; i++)
            {
                bool spare = i > 0 && r[i - 1] != 10 && r[i - 1] + r[i] == 10 &&
                    (i == 1 || frame == 9 && r[0] == 10);
                marks.Add(spare ? "/" : r[i] == 10 ? "X" : r[i] == 0 ? "–" : r[i].ToString());
            }
            return string.Join("  ", marks);
        }
    }
}
