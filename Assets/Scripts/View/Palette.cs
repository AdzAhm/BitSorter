using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Every colour the game draws with, as one value -- so a look is data rather than a search
    /// through fifteen files.
    /// </summary>
    /// <remarks>
    /// Before this, the colours lived wherever they were first needed: static fields on UiTheme,
    /// BitVisuals and PortState, two parallel switch tables in NodeShapes, serialized fields on six
    /// renderers, and literals inside the board tile. Nothing could compare them.
    ///
    /// The interface took its accents from the node colours on purpose, so it would not look
    /// bolted on. The side effect nobody weighed was that the state colours became fixture
    /// colours: every source was <see cref="Good"/> to the last digit, every sink
    /// <see cref="Bad"/>, and the NAND gate <see cref="Accent"/> -- so every output on every board
    /// looked like an error. Gathered here, a look can be compared field by field, and a new one
    /// is a new instance rather than an edit to fifteen files.
    ///
    /// <see cref="Classic"/> is exactly the colours the game shipped with, and the reference
    /// screenshots are held to it pixel for pixel -- moving the colours here was not allowed to
    /// change a single one.
    ///
    /// **Never change a palette in place.** Every instance is shared, and <see cref="Current"/> is
    /// read by everything that draws. A new look is <see cref="Derive"/>d from an existing one.
    ///
    /// Chosen before the scene loads, as part of a <see cref="Look"/>. Most renderers take their
    /// colours when they build, so a look swapped mid-session would leave anything already on
    /// screen in the old one.
    /// </remarks>
    public sealed class Palette
    {
        /// <summary>What this look is called, and what the board tile's cache is keyed on.</summary>
        public string Name { get; private set; }

        // -----------------------------------------------------------------
        // The board
        // -----------------------------------------------------------------

        /// <summary>The circuit-board tile under everything: its ground, its traces, its pads.</summary>
        public Color Ground, GroundTrace, GroundPad;

        /// <summary>A placement grid cell.</summary>
        public Color Grid;

        /// <summary>A wire: its dark casing, its core, and the core under the cursor.</summary>
        public Color WireCasing, WireCore, WireHover;

        /// <summary>A wire whose delay just changed, and the sparks that say so.</summary>
        public Color WireFlash;

        /// <summary>The hatches dividing a longer wire into its ticks.</summary>
        public Color WireMark;

        /// <summary>A wire's delay, and the pill behind it.</summary>
        public Color DelayLabel, DelayLabelBacking;

        /// <summary>The fixtures a level provides.</summary>
        public Color Source, Sink;

        /// <summary>One colour per gate. Shape is what tells gates apart; these only help.</summary>
        public Color Xor, And, Or, Nand, Nor, Not;

        /// <summary>
        /// A register's body: the palest, least saturated thing on the board, so the bit held
        /// inside it is what reads.
        /// </summary>
        public Color Register;

        /// <summary>Anything without a colour of its own.</summary>
        public Color OtherNode;

        /// <summary>A bit's own colour, wherever it is drawn -- travelling, landed, or held.</summary>
        public Color BitZero, BitOne;

        /// <summary>A port stub at rest.</summary>
        public Color PortInput, PortOutput;

        /// <summary>
        /// A collision one tick away: only the arriving bit will die, or the waiting one will too.
        /// </summary>
        public Color Waiting, Doomed;

        /// <summary>The burn a collision leaves on the port it happened at.</summary>
        public Color Scorch;

        /// <summary>The wire being dragged: going nowhere yet, onto a port it can join, or not.</summary>
        public Color WirePreview, WirePreviewValid, WirePreviewInvalid;

        /// <summary>The tutorial's rings around what it is pointing at.</summary>
        public Color Highlight;

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
        /// State, and only state: a pass, a failure. Not for anything on the board -- a sink that
        /// wears <see cref="Bad"/> reads as a sink that has gone wrong.
        /// </summary>
        public Color Good, Bad;

        /// <summary>
        /// What a full-screen panel lays over the board: the main menu, the level list, and the
        /// cards (chapter, tutorial, ending).
        /// </summary>
        public Color MenuScrim, ListScrim, CardScrim;

        /// <summary>
        /// A button or row that is the chosen one: the part in hand, the level being played, the
        /// speed in use.
        /// </summary>
        /// <remarks>
        /// This and the colours below were each a colour multiplied down where it was used --
        /// <c>Accent * 0.55f</c> -- which darkens it and makes it see-through at once. On a filled
        /// panel that reads as a dim highlight; on a panel with a drawn edge it made the chosen row
        /// darker than the rows beside it. Named, a look can say what chosen looks like.
        /// </remarks>
        public Color Selected;

        /// <summary>The backdrop of a first-time hint, and of the help badge.</summary>
        public Color HintBackdrop, BadgeBackdrop;

        /// <summary>The backdrop of a refusal, and of the bits-lost meter.</summary>
        public Color ToastBackdrop, MeterBackdrop;

        /// <summary>The rule under the menu's title.</summary>
        public Color Rule;

        /// <summary>
        /// The music credit under the main menu -- the one line a licence requires be shown, so not
        /// something to fade into the background.
        /// </summary>
        public Color Credit;

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
            GroundPad = new Color(0.13f, 0.20f, 0.22f),
            Grid = new Color(0.26f, 0.28f, 0.34f),

            WireCasing = new Color(0.10f, 0.13f, 0.17f),
            WireCore = new Color(0.30f, 0.62f, 0.70f),
            WireHover = new Color(0.62f, 0.92f, 1.00f),
            WireFlash = new Color(1.00f, 0.95f, 0.70f),
            WireMark = new Color(0.58f, 0.80f, 0.88f),
            DelayLabel = new Color(0.94f, 0.96f, 1.00f),
            DelayLabelBacking = new Color(0.03f, 0.04f, 0.06f, 0.85f),

            Source = new Color(0.36f, 0.92f, 0.55f),
            Sink = new Color(0.98f, 0.44f, 0.44f),
            Xor = new Color(0.42f, 0.68f, 1.00f),
            And = new Color(1.00f, 0.78f, 0.32f),
            Or = new Color(0.76f, 0.54f, 1.00f),
            Nand = new Color(0.46f, 0.94f, 0.90f),
            Nor = new Color(0.90f, 0.88f, 0.48f),
            Not = new Color(1.00f, 0.58f, 0.82f),
            Register = new Color(0.64f, 0.70f, 0.84f),
            OtherNode = new Color(0.62f, 0.64f, 0.70f),

            BitZero = new Color(0.42f, 0.48f, 0.58f),
            BitOne = new Color(1.00f, 0.88f, 0.32f),

            PortInput = new Color(0.62f, 0.66f, 0.76f),
            PortOutput = new Color(0.80f, 0.78f, 0.58f),
            Waiting = new Color(1.00f, 0.74f, 0.22f),
            Doomed = new Color(1.00f, 0.28f, 0.24f),
            Scorch = new Color(0.95f, 0.30f, 0.28f),

            WirePreview = new Color(0.70f, 0.72f, 0.80f, 0.85f),
            WirePreviewValid = new Color(0.40f, 0.90f, 0.50f, 0.95f),
            WirePreviewInvalid = new Color(0.95f, 0.40f, 0.36f, 0.95f),
            Highlight = new Color(0.46f, 0.94f, 0.90f),

            Panel = new Color(0.075f, 0.085f, 0.11f, 0.92f),
            PanelEdge = new Color(0.16f, 0.22f, 0.26f, 1f),
            Text = new Color(0.86f, 0.89f, 0.94f),
            TextDim = new Color(0.55f, 0.60f, 0.68f),
            Accent = new Color(0.46f, 0.94f, 0.90f),
            Good = new Color(0.36f, 0.92f, 0.55f),
            Bad = new Color(0.98f, 0.44f, 0.44f),

            MenuScrim = new Color(0f, 0f, 0f, 0.88f),
            ListScrim = new Color(0f, 0f, 0f, 0.78f),
            CardScrim = new Color(0f, 0f, 0f, 0.9f),
        });

        /// <summary>
        /// Classic's named states, worked out from its colours exactly as they used to be where they
        /// were drawn, so naming them changed nothing on screen.
        /// </summary>
        private static Palette WithDerivedStates(Palette p)
        {
            p.Selected = p.Accent * 0.55f;
            p.HintBackdrop = p.Accent * 0.5f;
            p.BadgeBackdrop = p.Accent * 0.5f;
            p.ToastBackdrop = p.Bad * 0.5f;
            p.MeterBackdrop = p.Bad * 0.75f;
            p.Rule = p.Accent * 0.4f;
            p.Credit = p.TextDim * 0.8f;
            return p;
        }
    }
}
