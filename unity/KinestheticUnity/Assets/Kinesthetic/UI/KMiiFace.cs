using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// A person's face: a flat cartoon in the Mii idiom rather than a copy of one — a big rounded head on
    /// a plain disc, no shading anywhere, and the tall oval eyes that style is really made of. Everything is
    /// drawn from the variant index, so a person's face is the same in the friends roster, a thread, a group
    /// session's member list and the landing page.
    ///
    ///     <k:KMiiFace variant="3" class="row-face" />
    ///
    /// The screen gives it a size; the face fills the smaller side and clips itself round.
    ///
    /// Pseudonymous by design: no photographs of patients.
    ///
    /// Skin and hair stay their own colours. They are representational, not chrome, and flattening real
    /// skin tones into the five brand hues would erase the only variety these faces carry. The disc behind
    /// the head is chrome, so that one comes from the palette.
    [UxmlElement]
    public partial class KMiiFace : VisualElement
    {
        int variantValue;

        [UxmlAttribute]
        public int variant { get => variantValue; set { variantValue = value; MarkDirtyRepaint(); } }

        public KMiiFace()
        {
            AddToClassList("k-face");
            pickingMode = PickingMode.Ignore;
            style.overflow = Overflow.Hidden;
            style.flexShrink = 0;
            generateVisualContent += ctx => Draw(ctx, variantValue, Side(contentRect));
            // Round to whatever size the screen gave it: the shoulders are cut off by this edge.
            RegisterCallback<GeometryChangedEvent>(_ => Round(this, Side(contentRect)));
        }

        static float Side(Rect r) => Mathf.Min(r.width, r.height);

        static void Round(VisualElement e, float size)
        {
            var radius = new StyleLength(size / 2);
            e.style.borderTopLeftRadius = radius; e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius; e.style.borderBottomRightRadius = radius;
        }

        /// For a plain element a screen already has (a thread's header, a profile card): draw this face
        /// into it at `size` pixels, replacing whatever face it held.
        public static void Paint(VisualElement target, int variant, float size)
        {
            target.generateVisualContent = null;
            target.generateVisualContent += ctx => Draw(ctx, variant, size);
            target.MarkDirtyRepaint();
        }

        // Warm skin tones and hair colours, paired by index so a person's face is stable everywhere.
        static readonly Color[] Skin =
        {
            new(.98f,.84f,.72f), new(.95f,.78f,.62f), new(.85f,.65f,.49f), new(.68f,.48f,.35f),
            new(.99f,.87f,.78f), new(.78f,.57f,.42f), new(.92f,.74f,.58f), new(.56f,.38f,.27f),
        };
        static readonly Color[] Hair =
        {
            new(.28f,.20f,.16f), new(.52f,.33f,.18f), new(.15f,.13f,.12f), new(.72f,.58f,.34f),
            new(.35f,.24f,.20f), new(.20f,.16f,.15f), new(.62f,.42f,.24f), new(.30f,.28f,.30f),
        };
        static readonly Color[] Shirt =
        {
            new(.28f,.62f,.80f), new(.93f,.66f,.36f), new(.45f,.72f,.52f), new(.85f,.51f,.55f),
            new(.55f,.55f,.82f), new(.35f,.74f,.74f), new(.88f,.74f,.41f), new(.62f,.52f,.75f),
        };
        // The field behind the head. Chrome, so these come from Palette.cs.
        static readonly Color[] Disc =
        {
            Palette.Cerulean20, Palette.Sand30, Palette.Jungle20, Palette.Coral20,
            Palette.Cerulean10, Palette.Sand20, Palette.Jungle10, Palette.Coral10,
        };
        static readonly Color Ink = Palette.Prussian70;

        /// The same person's colours for their 3D Mii, so the body in the room matches the face in the list.
        public static Color SkinOf(int variant) => Skin[Mathf.Abs(variant) % Skin.Length];
        public static Color HairOf(int variant) => Hair[Mathf.Abs(variant) % Hair.Length];
        public static Color ShirtOf(int variant) => Shirt[Mathf.Abs(variant) % Shirt.Length];

        public static void Draw(MeshGenerationContext ctx, int variant, float size)
        {
            if (size <= 1) return;
            var p = ctx.painter2D;
            int v = Mathf.Abs(variant) % Skin.Length;
            float c = size * .5f;

            // The disc. A face on a plain field is what makes these read as portraits rather than stickers
            // dropped on whatever is behind them.
            p.fillColor = Disc[v % Disc.Length];
            p.BeginPath();
            p.Arc(new Vector2(c, c), size * .5f, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill();

            // Shoulders, cut off by the disc, so the head sits on a body.
            p.fillColor = Shirt[v];
            p.BeginPath();
            p.Arc(new Vector2(c, size * 1.08f), size * .42f, Angle.Degrees(180), Angle.Degrees(360));
            p.Fill();

            float headY = size * .47f, headW = size * .30f, headH = size * .35f;

            // Hair behind the head, but only over the scalp: an ellipse tall enough to wrap the jaw turns
            // every face into a hood. This one ends around the temples and leaves the cheeks and chin clear.
            p.fillColor = Hair[v];
            Ellipse(p, c, headY - headH * .28f, headW * 1.09f, headH * .78f);
            p.Fill();

            p.fillColor = Skin[v];
            Ellipse(p, c, headY, headW, headH);
            p.Fill();

            // The fringe: straight across on even variants, swept to one side on odd ones. Two faces in a
            // row should not look like the same person.
            p.fillColor = Hair[v];
            p.BeginPath();
            p.MoveTo(new Vector2(c - headW, headY - headH * .35f));
            p.BezierCurveTo(
                new Vector2(c - headW, headY - headH * 1.2f),
                new Vector2(c + headW, headY - headH * 1.2f),
                new Vector2(c + headW, headY - headH * .35f));
            p.BezierCurveTo(
                new Vector2(c + headW * .45f, headY - headH * (v % 2 == 0 ? .62f : .40f)),
                new Vector2(c - headW * .45f, headY - headH * (v % 2 == 0 ? .62f : .78f)),
                new Vector2(c - headW, headY - headH * .35f));
            p.ClosePath();
            p.Fill();

            // The eyes carry the style: tall ovals, not dots.
            float eyeDx = headW * .42f, eyeY = headY + headH * .08f;
            float eyeW = Mathf.Max(.9f, size * .052f), eyeH = Mathf.Max(1.4f, size * .082f);
            p.fillColor = Ink;
            Ellipse(p, c - eyeDx, eyeY, eyeW, eyeH); p.Fill();
            Ellipse(p, c + eyeDx, eyeY, eyeW, eyeH); p.Fill();

            // Brows, set high — a Mii's face is mostly forehead.
            p.strokeColor = Hair[v];
            p.lineWidth = Mathf.Max(1f, size * .038f);
            p.lineCap = LineCap.Round;
            float browY = eyeY - eyeH * 1.7f, browW = eyeW * 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(c - eyeDx - browW, browY));
            p.LineTo(new Vector2(c - eyeDx + browW, browY));
            p.MoveTo(new Vector2(c + eyeDx - browW, browY));
            p.LineTo(new Vector2(c + eyeDx + browW, browY));
            p.Stroke();

            // A small mouth, low on the face, closed and easy. This has to look welcoming on the days
            // someone has not shown up.
            p.strokeColor = Ink;
            p.lineWidth = Mathf.Max(1.2f, size * .046f);
            p.BeginPath();
            p.Arc(new Vector2(c, headY + headH * .30f), headW * .34f, Angle.Degrees(20), Angle.Degrees(160));
            p.Stroke();
        }

        /// Painter2D draws circles but not ellipses, and every rounded shape in this face is taller than it
        /// is wide. Four beziers, the usual circle constant.
        static void Ellipse(Painter2D p, float cx, float cy, float rx, float ry)
        {
            const float K = .5523f;
            p.BeginPath();
            p.MoveTo(new Vector2(cx, cy - ry));
            p.BezierCurveTo(new Vector2(cx + rx * K, cy - ry), new Vector2(cx + rx, cy - ry * K), new Vector2(cx + rx, cy));
            p.BezierCurveTo(new Vector2(cx + rx, cy + ry * K), new Vector2(cx + rx * K, cy + ry), new Vector2(cx, cy + ry));
            p.BezierCurveTo(new Vector2(cx - rx * K, cy + ry), new Vector2(cx - rx, cy + ry * K), new Vector2(cx - rx, cy));
            p.BezierCurveTo(new Vector2(cx - rx, cy - ry * K), new Vector2(cx - rx * K, cy - ry), new Vector2(cx, cy - ry));
            p.ClosePath();
        }
    }
}
