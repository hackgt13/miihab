using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// An activity's glyph on its gallery card. Golf, the studio and bowling keep their drawings; a
    /// movement is a figure with the tracker marked where it is worn, so the card itself says where the
    /// AirPod goes.
    ///
    ///     new KActivityIcon { activity = entry.Id, tag = entry.CardTag }
    ///
    /// The screen sizes it (the drawings are laid out for a 62px square) and paints the disc behind it;
    /// the glyph is painted here from Palette.cs. Its content is two strings, so the headset's replica
    /// draws the same glyph without a catalog of its own.
    [UxmlElement]
    public partial class KActivityIcon : VisualElement
    {
        string activityValue = "", tagValue = "";

        /// The catalog id: golf.adaptive, rehab.studio, bowling.adaptive, or a movement.
        [UxmlAttribute]
        public string activity { get => activityValue; set { activityValue = value ?? ""; MarkDirtyRepaint(); } }

        /// The card's tag, which names where each AirPod is worn: "WRIST", "UPPER ARM · WRIST".
        [UxmlAttribute]
        public string tag { get => tagValue; set { tagValue = value ?? ""; MarkDirtyRepaint(); } }

        public KActivityIcon()
        {
            AddToClassList("k-activity-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            switch (activityValue)
            {
                case "golf.adaptive": DrawVenue(ctx, false); return;
                case "rehab.studio": DrawVenue(ctx, true); return;
                case "bowling.adaptive": DrawBowling(ctx); return;
            }
            var p = ctx.painter2D;
            p.strokeColor = Palette.Prussian60; p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.Stroke(); }
            p.BeginPath(); p.Arc(new(31, 14), 5.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            Line(new(31, 20), new(31, 37));
            Line(new(20, 33), new(31, 24), new(42, 33));
            Line(new(24, 52), new(31, 37), new(38, 52));
            // One dot for each AirPod the tag names: two for a two-AirPod movement.
            var mounts = new (string word, Vector2 at)[] {
                ("EARS", new(37, 14)), ("CHEST", new(31, 27)), ("UPPER ARM", new(37, 28)), ("WRIST", new(42, 33)), ("HANDLE", new(42, 33)),
                ("THIGH", new(35, 43)), ("SHIN", new(37, 47)), ("ANKLE", new(38, 51)) };
            p.fillColor = Palette.Cerulean40; p.strokeColor = Palette.Sand00; p.lineWidth = 2;
            foreach (var (word, at) in mounts)
                if (tagValue.Contains(word)) { p.BeginPath(); p.Arc(at, 5, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke(); }
        }

        static void DrawBowling(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.strokeColor = Palette.Prussian60; p.lineWidth = 2.5f; p.lineJoin = LineJoin.Round;
            p.fillColor = Palette.Sand00;
            p.BeginPath(); p.MoveTo(new(35, 12));
            p.BezierCurveTo(new(32, 4), new(47, 4), new(44, 12));
            p.BezierCurveTo(new(39, 21), new(52, 28), new(49, 44));
            p.LineTo(new(31, 44));
            p.BezierCurveTo(new(27, 28), new(39, 21), new(35, 12));
            p.ClosePath(); p.Fill(); p.Stroke();
            p.strokeColor = Palette.Slate50;
            p.BeginPath(); p.MoveTo(new(35, 19)); p.LineTo(new(44, 19)); p.Stroke();
            p.fillColor = Palette.Prussian60;
            p.BeginPath(); p.Arc(new(23, 39), 13, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.fillColor = Palette.Sand00;
            foreach (var point in new[] { new Vector2(21, 33), new Vector2(27, 34), new Vector2(23, 39) })
            { p.BeginPath(); p.Arc(point, 1.7f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
        }

        static void DrawVenue(MeshGenerationContext ctx, bool studio)
        {
            var p = ctx.painter2D;
            p.strokeColor = Palette.Prussian60; p.lineWidth = 3; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points) { p.BeginPath(); p.MoveTo(points[0]); for (int i = 1; i < points.Length; i++) p.LineTo(points[i]); p.Stroke(); }
            if (studio)
            {
                p.BeginPath(); p.Arc(new(30, 17), 5, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                Line(new(30, 26), new(30, 39), new(42, 39), new(44, 49));
                Line(new(30, 28), new(42, 24), new(47, 12));
                Line(new(30, 28), new(19, 36)); Line(new(19, 40), new(19, 49), new(33, 49));
                p.strokeColor = Palette.Cerulean40; p.lineWidth = 2;
                p.BeginPath(); p.Arc(new(31, 31), 21, Angle.Degrees(235), Angle.Degrees(305)); p.Stroke();
            }
            else
            {
                Line(new(30, 47), new(30, 13), new(48, 20), new(30, 26));
                p.strokeColor = Palette.Slate50; Line(new(14, 48), new(48, 48));
                p.fillColor = Palette.Sand00; p.BeginPath(); p.Arc(new(19, 41), 4, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke();
            }
        }
    }
}
