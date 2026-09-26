using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    /// <summary>
    /// The one face. Drawn rather than authored, so a person looks the same in the
    /// roster, a thread, the landing page and under an activity card, at whatever
    /// size that surface asks for.
    ///
    /// It lived inside FriendsPanel until the gallery needed it too. Nothing about
    /// drawing a face belongs to the friends screen.
    /// </summary>
    static class MiiFace
    {
        // Warm skin tones and hair colours, paired by index so a person's face is
        // stable across the roster, the thread and the landing page.
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
        // The field behind the head. Chrome, so these come from Palette.cs.
        static readonly Color[] Disc =
        {
            Palette.Cerulean20, Palette.Sand30, Palette.Jungle20, Palette.Coral20,
            Palette.Cerulean10, Palette.Sand20, Palette.Jungle10, Palette.Coral10,
        };
        static readonly Color Ink = Palette.Prussian70;

        static readonly Color[] Shirt =
        {
            new(.28f,.62f,.80f), new(.93f,.66f,.36f), new(.45f,.72f,.52f), new(.85f,.51f,.55f),
            new(.55f,.55f,.82f), new(.35f,.74f,.74f), new(.88f,.74f,.41f), new(.62f,.52f,.75f),
        };




        /// A flat cartoon face in the Mii idiom rather than a copy of one: a big
        /// rounded head on a plain disc, no shading anywhere, and the tall oval
        /// eyes that style is really made of. Everything is drawn from the variant
        /// index, so a person's face is the same in the roster, the thread and the
        /// landing page.
        ///
        /// Pseudonymous by design: no photographs of patients.
        ///
        /// Skin and hair stay their own colours. They are representational, not
        /// chrome, and flattening real skin tones into the five brand hues would
        /// erase the only variety these faces carry. The disc behind the head is
        /// chrome, so that one comes from the palette.
        public static void Draw(MeshGenerationContext ctx, int variant, float size)
        {
            var p = ctx.painter2D;
            int v = Mathf.Abs(variant) % Skin.Length;
            float c = size * .5f;

            // The disc. A face on a plain field is what makes these read as
            // portraits rather than stickers dropped on whatever is behind them.
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

            // Hair behind the head, but only over the scalp: an ellipse tall enough
            // to wrap the jaw turns every face into a hood. This one ends around the
            // temples and leaves the cheeks and chin clear, which is what makes the
            // head read as a head.
            p.fillColor = Hair[v];
            Ellipse(p, c, headY - headH * .28f, headW * 1.09f, headH * .78f);
            p.Fill();

            p.fillColor = Skin[v];
            Ellipse(p, c, headY, headW, headH);
            p.Fill();

            // The fringe: a lid over the top of the face, straight across on even
            // variants and swept to one side on odd ones. Two faces in a row should
            // not look like the same person.
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

            // A small mouth, low on the face, closed and easy. This surface has to
            // look welcoming on the days someone has not shown up.
            p.strokeColor = Ink;
            p.lineWidth = Mathf.Max(1.2f, size * .046f);
            p.BeginPath();
            p.Arc(new Vector2(c, headY + headH * .30f), headW * .34f,
                Angle.Degrees(20), Angle.Degrees(160));
            p.Stroke();
        }

        /// Painter2D draws circles but not ellipses, and every rounded shape in this
        /// face is taller than it is wide. Four beziers, the usual circle constant.
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

        public static VisualElement Portrait(int variant, float size, string cssClass)
        {
            var element = new VisualElement();
            element.AddToClassList(cssClass);
            element.generateVisualContent += ctx => Draw(ctx, variant, size);
            return element;
        }
    }
}
