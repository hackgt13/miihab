using UnityEngine;

namespace Kinesthetic
{
    /// The one place the product's colours are written down. Every in-game surface — the menu board,
    /// the studio, golf, bowling, the replay panel — draws from these five hues and nothing else, so a
    /// patient moving between activities stays inside one identity instead of three accidental ones.
    ///
    ///   sand dune       #EAE0CC   warm neutral: panels, page fills, lettering on dark
    ///   rich cerulean   #2274A5   primary: the button you press, and anything being measured
    ///   medium jungle   #4AAD52   good: a rep that counted, a day done, a target reached
    ///   prussian blue   #0E1C36   ink, and the glass of panels that sit over the world
    ///   vibrant coral   #FE5F55   what you are reaching for, and what has gone wrong
    ///
    /// Three of those carry meaning a patient has to read at two metres, so they never swap jobs:
    /// **cerulean is primary and measured**, **jungle is good**, **coral is target or trouble**. A
    /// measured line is never green just because the number is high — it turns green when it passes
    /// the plan, and that difference is the whole point of the board.
    ///
    /// The numbered steps are tints and shades of those five, mixed toward white or black only — no new
    /// hues enter here. Steps rise with darkness: 00 is nearly white, 80 nearly black, 40–50 is the
    /// published colour. Slate is prussian blue lightened, which is where secondary copy comes from.
    /// USS files carry the same values as literals (UI Toolkit resolves stylesheets before any of this
    /// runs); the hex in a comment beside each one names the step it came from.
    ///
    /// Contrast is checked against the surface a colour actually lands on: Slate70 and Jungle60 on
    /// Sand00 clear 4.5:1 for body copy, and sand on Cerulean50 clears 4.9:1 — which is why a primary
    /// button is lettered in sand rather than in jungle, a pairing that would fail.
    public static class Palette
    {
        // ------------------------------------------------------------------ sand dune
        public static readonly Color Sand00 = Hex("FDFBF6");
        public static readonly Color Sand10 = Hex("F8F3E8");
        public static readonly Color Sand20 = Hex("F2EBDA");
        public static readonly Color Sand30 = Hex("EAE0CC");   // published
        public static readonly Color Sand40 = Hex("DCCFB4");

        // ------------------------------------------------------------------ rich cerulean
        public static readonly Color Cerulean10 = Hex("EAF3F9");
        public static readonly Color Cerulean20 = Hex("CFE4F0");
        public static readonly Color Cerulean30 = Hex("9FC8E0");
        public static readonly Color Cerulean40 = Hex("5C9EC6");
        public static readonly Color Cerulean50 = Hex("2274A5");   // published
        public static readonly Color Cerulean60 = Hex("185A83");
        public static readonly Color Cerulean70 = Hex("124563");

        // ------------------------------------------------------------------ medium jungle
        public static readonly Color Jungle10 = Hex("EDF8EE");
        public static readonly Color Jungle20 = Hex("D5EFD7");
        public static readonly Color Jungle30 = Hex("A5DBA9");
        public static readonly Color Jungle40 = Hex("4AAD52");   // published
        public static readonly Color Jungle50 = Hex("3A8B41");
        public static readonly Color Jungle60 = Hex("2B6B31");

        // ------------------------------------------------------------------ vibrant coral
        public static readonly Color Coral10 = Hex("FFEDEB");
        public static readonly Color Coral20 = Hex("FFD3CF");
        public static readonly Color Coral30 = Hex("FF9D96");
        public static readonly Color Coral40 = Hex("FE5F55");   // published
        public static readonly Color Coral50 = Hex("DC4439");
        public static readonly Color Coral60 = Hex("A93228");

        // ------------------------------------------------------------------ prussian blue, and its tints
        public static readonly Color Slate30 = Hex("B4BFD3");
        public static readonly Color Slate40 = Hex("97A4BE");
        public static readonly Color Slate50 = Hex("6B7EA4");
        public static readonly Color Slate60 = Hex("4D6187");
        public static readonly Color Slate70 = Hex("33456A");
        public static readonly Color Prussian50 = Hex("2F3A4E");
        public static readonly Color Prussian60 = Hex("1B2C4A");
        public static readonly Color Prussian70 = Hex("0E1C36");   // published
        public static readonly Color Prussian80 = Hex("08111F");

        /// What each hue is *for*, so a call site reads as intent rather than as a swatch. Reach for
        /// these first; drop to a numbered step only when a surface needs a specific tint.
        public static readonly Color Ink = Prussian50;        // headings and figures on a light panel
        public static readonly Color Body = Slate70;          // paragraph copy on a light panel
        public static readonly Color Muted = Slate50;         // captions, units, things read second
        public static readonly Color Panel = Sand00;          // the light panel itself
        public static readonly Color PanelDark = Prussian70;  // a panel that sits over the world
        public static readonly Color InkOnDark = Sand30;      // copy on that dark panel
        public static readonly Color Line = Slate30;          // dividers and hairlines
        public static readonly Color Call = Cerulean50;       // the primary action
        public static readonly Color CallInk = Sand00;        // lettering on that action
        public static readonly Color Live = Cerulean40;       // selected, focused, happening now
        public static readonly Color Progress = Cerulean50;   // a measured value drawn against a plan
        public static readonly Color Reference = Slate50;     // the plan it is drawn against
        public static readonly Color Good = Jungle40;         // counted, done, reached
        public static readonly Color GoodInk = Jungle60;      // the same verdict as lettering
        public static readonly Color Target = Coral40;        // what is being reached for
        public static readonly Color Attention = Coral40;     // and what has gone wrong

        /// `Hex("0E1C36")` or `Hex("0E1C3680")` — RGB or RGBA, no leading '#'. Falls back to magenta
        /// rather than throwing, so a typo shows up on screen instead of taking the panel down.
        static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out var color) ? color : Color.magenta;
        }

        /// The same colour at a different opacity, for translucent fills over the world.
        public static Color At(this Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
