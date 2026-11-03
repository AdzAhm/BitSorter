using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The colours the interface and the canvas's looks are made from, as one value -- so a look is data rather than a search
    /// through fifteen files.
    /// </summary>
    /// <remarks>
    /// Before this, the colours lived wherever they were first needed: static fields on UiTheme,
    /// BitVisuals and PortState, two parallel switch tables in NodeShapes, serialized fields on six
    /// renderers, and literals inside the board tile. Nothing could compare them.
    ///
    /// <see cref="Classic"/> is exactly the colours the game shipped with, and the reference
    /// screenshots are held to it pixel for pixel -- moving the colours here was not allowed to
    /// change a single one. It is also what the canvas's own Classic palette is made from
    /// (<see cref="SchematicPalette.Classic"/>), and Neon Board's canvas takes its ground, its traces, its
    /// text and its 1 from Neon Board's.
    ///
    /// **Never change a palette in place.** Every instance is shared, and <see cref="Current"/> is
    /// read by everything that draws. A new look is <see cref="Derive"/>d from an existing one.
    ///
    /// Chosen before the scene loads, as part of a <see cref="Look"/>. Most renderers take their
    /// colours when they build, so a look swapped mid-session would leave anything already on
    /// screen in the old one.
    ///
    /// It held a colour for every gate, fixture, bit, wire and card of the 4.x board as well, until Part 16b: nothing
    /// read them once the game was gone.
    /// </remarks>
    public sealed class Palette
    {
        /// <summary>What this look is called.</summary>
        public string Name { get; private set; }

        // -----------------------------------------------------------------
        // The canvas's sources
        // -----------------------------------------------------------------

        /// <summary>The ground: what the camera clears to, and what the canvas's ground is made from.</summary>
        public Color Ground, GroundTrace;

        /// <summary>A wire's core: the canvas's outline.</summary>
        public Color WireCore;

        /// <summary>A bit's own colour: the canvas's 0 and 1.</summary>
        public Color BitZero, BitOne;

        /// <summary>The burn a collision left: the canvas's x.</summary>
        public Color Scorch;

        // -----------------------------------------------------------------
        // The interface
        // -----------------------------------------------------------------

        /// <summary>A panel's body and its edge.</summary>
        public Color Panel, PanelEdge;

        /// <summary>Text, and the quieter text beside it.</summary>
        public Color Text, TextDim;

        /// <summary>The one colour that says "look here".</summary>
        public Color Accent;

        /// <summary>
        /// A button's colour by what it is for. See <see cref="ButtonRole"/>; an ordinary button is
        /// <see cref="PanelEdge"/>.
        /// </summary>
        public Color ButtonPrimary, ButtonQuiet, ButtonDestructive;

        // -----------------------------------------------------------------
        // Which one
        // -----------------------------------------------------------------

        /// <summary>The colours everything is drawn in: the current <see cref="Look"/>'s.</summary>
        /// <remarks>
        /// Chosen by choosing a look, never on its own, so a palette cannot be switched without the
        /// styles that were designed with it. Falls back to <see cref="Classic"/> for the same reason
        /// Look.Current does: a static initialiser above Classic would have run first and been null.
        /// </remarks>
        public static Palette Current => Look.Current.Colours ?? Classic;

        /// <summary>
        /// The alpha that makes something look <paramref name="opacity"/> opaque.
        /// </summary>
        /// <remarks>
        /// The project renders in linear colour space, so blending happens there, and the eye does
        /// not see in linear. A black scrim written at 0.88 lets through 12% of the light, which
        /// the eye reads as about 38% of the brightness -- so it looks 62% opaque, and the board
        /// shows through a menu meant to cover it. Every alpha in <see cref="Classic"/> is written
        /// that way and is kept, because Classic is the game as it shipped. A new look states the
        /// opacity it wants and converts it here, once.
        /// </remarks>
        public static float Seen(float opacity) => 1f - Mathf.Pow(1f - Mathf.Clamp01(opacity), 2.2f);

        /// <summary>A copy of this palette under a new name, changed by <paramref name="change"/>.</summary>
        public Palette Derive(string name, System.Action<Palette> change)
        {
            var copy = (Palette)MemberwiseClone();
            copy.Name = name;
            change?.Invoke(copy);
            return copy;
        }

        /// <summary>
        /// The colours the game shipped with through 2.0, exactly -- the values the scene and the
        /// code held, gathered without changing one.
        /// </summary>
        public static Palette Classic { get; } = WithDerivedStates(new Palette
        {
            Name = "classic",

            Ground = new Color(0.055f, 0.065f, 0.085f),
            GroundTrace = new Color(0.10f, 0.15f, 0.17f),

            WireCore = new Color(0.30f, 0.62f, 0.70f),

            BitZero = new Color(0.42f, 0.48f, 0.58f),
            BitOne = new Color(1.00f, 0.88f, 0.32f),

            Scorch = new Color(0.95f, 0.30f, 0.28f),

            Panel = new Color(0.075f, 0.085f, 0.11f, 0.92f),
            PanelEdge = new Color(0.16f, 0.22f, 0.26f, 1f),
            Text = new Color(0.86f, 0.89f, 0.94f),
            TextDim = new Color(0.55f, 0.60f, 0.68f),
            Accent = new Color(0.46f, 0.94f, 0.90f),
        });

        /// <summary>
        /// Classic's named states, worked out from its colours exactly as they used to be where they
        /// were drawn, so naming them changed nothing on screen.
        /// </summary>
        private static Palette WithDerivedStates(Palette p)
        {
            // Classic drew every button alike, and still does.
            p.ButtonPrimary = p.PanelEdge;
            p.ButtonQuiet = p.PanelEdge;
            p.ButtonDestructive = p.PanelEdge;
            return p;
        }
    }
}
