using UnityEngine;

namespace Kinesthetic
{
    /// The one place the product's colours are written down. Every in-game surface — the menu board,
    /// the studio, golf, bowling, the replay panel — draws from these five hues and nothing else, so a
    /// patient moving between activities stays inside one identity instead of three accidental ones.
    ///
    ///   sand dune       #EAE0CC   warm neutral: panels, page fills, ink on dark
    ///   vibrant coral   #FF715B   the one call to action, and attention that has to be seen
    ///   twilight indigo #29335C   headings, body ink on light, dark panel fills
    ///   baby blue ice   #89BBFE   selection, focus, "this is live" highlights
    ///   glaucous        #6F8AB7   secondary copy, dividers, reference lines
    ///
    /// The numbered steps are tints and shades of those five, mixed toward white or black only — no new
    /// hues enter here. Steps rise with darkness: 00 is nearly white, 80 nearly black, 30–40 is the
    /// published colour. USS files carry the same values as literals (UI Toolkit resolves stylesheets
    /// before any of this runs); the hex in a comment beside each one names the step it came from.
    ///
    /// Contrast is checked against the surface a colour actually lands on: Indigo60 and Glaucous70 on
    /// Sand00 clear 4.5:1 for body copy, and Indigo70 on Coral40 clears 6:1 — which is why the coral
    /// call to action is lettered in indigo rather than white, a pairing white would fail at 2.1:1.
    public static class Palette
    {
        // ------------------------------------------------------------------ sand dune
        public static readonly Color Sand00 = Hex("FDFBF6");
        public static readonly Color Sand10 = Hex("F8F3E8");
        public static readonly Color Sand20 = Hex("F2EBDA");
        public static readonly Color Sand30 = Hex("EAE0CC");   // published
        public static readonly Color Sand40 = Hex("DCCFB4");

        // ------------------------------------------------------------------ baby blue ice
        public static readonly Color Ice10 = Hex("EDF4FF");
        public static readonly Color Ice20 = Hex("D8E8FE");
        public static readonly Color Ice30 = Hex("B7D4FE");
        public static readonly Color Ice40 = Hex("89BBFE");    // published
        public static readonly Color Ice50 = Hex("5C9CF2");
        public static readonly Color Ice60 = Hex("3D7CD4");

        // ------------------------------------------------------------------ glaucous
        public static readonly Color Glaucous30 = Hex("AABDD9");
        public static readonly Color Glaucous40 = Hex("8CA3C7");
        public static readonly Color Glaucous50 = Hex("6F8AB7");   // published
        public static readonly Color Glaucous60 = Hex("5A749F");
        public static readonly Color Glaucous70 = Hex("485F88");

        // ------------------------------------------------------------------ twilight indigo
        public static readonly Color Indigo40 = Hex("4E5A8C");
        public static readonly Color Indigo50 = Hex("3B4673");
        public static readonly Color Indigo60 = Hex("29335C");   // published
        public static readonly Color Indigo70 = Hex("1F2747");
        public static readonly Color Indigo80 = Hex("151A31");

        // ------------------------------------------------------------------ vibrant coral
        public static readonly Color Coral10 = Hex("FFEAE5");
        public static readonly Color Coral20 = Hex("FFD3C9");
        public static readonly Color Coral30 = Hex("FFA695");
        public static readonly Color Coral40 = Hex("FF715B");   // published
        public static readonly Color Coral50 = Hex("E4523C");
        public static readonly Color Coral60 = Hex("B33F2C");

        /// What each hue is *for*, so a call site reads as intent rather than as a swatch. Reach for
        /// these first; drop to a numbered step only when a surface needs a specific tint.
        public static readonly Color Ink = Indigo60;          // headings and figures on a light panel
        public static readonly Color Body = Glaucous70;       // paragraph copy on a light panel
        public static readonly Color Muted = Glaucous50;      // captions, units, things read second
        public static readonly Color Panel = Sand00;          // the light panel itself
        public static readonly Color PanelDark = Indigo70;    // a panel that sits over the world
        public static readonly Color InkOnDark = Sand30;      // copy on that dark panel
        public static readonly Color Line = Glaucous30;       // dividers and hairlines
        public static readonly Color Call = Coral40;          // the single primary action
        public static readonly Color CallInk = Indigo70;      // lettering on that action
        public static readonly Color Attention = Coral40;     // a number or cue that must be noticed
        public static readonly Color Live = Ice40;            // connected, selected, happening now
        public static readonly Color Progress = Ice50;        // a measured value drawn against a plan
        public static readonly Color Reference = Glaucous50;  // the plan it is drawn against

        /// `Hex("29335C")` or `Hex("29335C80")` — RGB or RGBA, no leading '#'. Falls back to magenta
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
