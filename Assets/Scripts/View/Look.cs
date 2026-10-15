using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BitSorter.ViewModel;

namespace BitSorter.View
{
    /// <summary>How a gate's body is filled.</summary>
    public enum BodyStyle
    {
        /// <summary>Solid, as the game shipped.</summary>
        Filled,

        /// <summary>A drawn outline over a faint fill: a symbol rather than an object.</summary>
        Outline,

        /// <summary>A bright rim fading into a darker, see-through middle.</summary>
        Glass,

        /// <summary>Lit from the top left, so the body reads as a raised part with edges.</summary>
        Raised,

        /// <summary>
        /// Glass lit from the top left: its rim bright on the side facing the light and falling
        /// away on the far side, so a glowing part still reads as an object with a shape.
        /// </summary>
        LitGlass,
    }

    /// <summary>What marks the placement grid's cells.</summary>
    public enum GridStyle
    {
        /// <summary>Small squares, as the game shipped.</summary>
        Squares,

        /// <summary>Round dots.</summary>
        Dots,

        /// <summary>Nothing but the board tile's own lines.</summary>
        None,
    }

    /// <summary>How a panel's backdrop is drawn.</summary>
    public enum PanelStyle
    {
        /// <summary>A filled slab, as the game shipped.</summary>
        Filled,

        /// <summary>A dark body with a thin edge drawn round it.</summary>
        Bordered,
    }

    /// <summary>What a bit in flight is drawn as.</summary>
    public enum BitStyle
    {
        /// <summary>A glowing dot, told apart from the other value by its colour alone.</summary>
        Dot,

        /// <summary>
        /// Its own digit, 0 or 1, upright whichever way the wire runs: told apart by its shape,
        /// which bloom and colour blindness leave alone.
        /// </summary>
        Digit,
    }

    /// <summary>
    /// A whole visual direction: its <see cref="Palette"/>, the choices that are not colours -- how
    /// gate bodies are filled, how the grid is marked, how panels are drawn, how hard the board
    /// blooms -- and how the 5.0 workbench's canvas is drawn (<see cref="Canvas"/>).
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Palette"/> so the palette stays what its name says, every colour
    /// and nothing else. <see cref="Palette.Current"/> reads through <see cref="Current"/>, so
    /// nothing that draws a colour needs to know a look exists.
    ///
    /// <see cref="Default"/> is what the game is drawn in unless the player chooses another in
    /// Settings: Schematic, which is the workbench as Ahmad chose it on Neon Board's board -- Neon
    /// Board was chosen out of three directions rendered side by side. <see cref="Classic"/> is the game as 2.0 shipped it, and
    /// every choice here defaults to what it did, so Classic is still one line to draw and the
    /// reference screenshots can hold a change that claims to touch no look to that, pixel for
    /// pixel. Chosen before the scene loads, and never changed in place -- derive a new one.
    /// </remarks>
    public sealed class Look
    {
        /// <summary>What this look is called, and what its styled sprites are cached under.</summary>
        public string Name { get; private set; }

        /// <summary>Its colours.</summary>
        public Palette Colours;

        /// <summary>How gate bodies are filled.</summary>
        public BodyStyle Bodies = BodyStyle.Filled;

        /// <summary>What marks the placement grid.</summary>
        public GridStyle Grid = GridStyle.Squares;

        /// <summary>How panels are drawn.</summary>
        public PanelStyle Panels = PanelStyle.Filled;

        /// <summary>What a bit in flight is drawn as.</summary>
        public BitStyle Bits = BitStyle.Dot;

        /// <summary>How large a bit in flight is, as a share of the scene's size.</summary>
        public float BitScale = 1f;

        /// <summary>
        /// How strongly the halo behind a bit glows, as a share of the scene's glow. A digit needs
        /// less than a dot: behind a stroke, a halo as bright as the stroke hides the shape it
        /// surrounds, and the bloom already gives it all the glow it needs.
        /// </summary>
        public float BitGlow = 1f;

        /// <summary>How wide a bit's trail is, as a share of the scene's width.</summary>
        public float TrailWidth = 1f;

        /// <summary>
        /// How strongly the board blooms, and how far the glow spreads. The threshold is not here:
        /// it stays at <see cref="BitVisuals.BloomThreshold"/> in every look, because what crosses
        /// it is what makes a bit the thing that glows.
        /// </summary>
        public float BloomIntensity = 1f;

        /// <inheritdoc cref="BloomIntensity"/>
        public float BloomScatter = 0.62f;

        /// <summary>
        /// How long a bit's trail is, as a share of the length the scene gives it. A trail says
        /// which way a bit is going; one as long as the bit's own crossing outshouts the bit.
        /// </summary>
        public float TrailLength = 1f;

        /// <summary>How strongly a gate's halo glows, as a share of the scene's glow.</summary>
        public float GateGlow = 1f;

        /// <summary>
        /// How strongly a source or a sink glows. Apart from <see cref="GateGlow"/> because the
        /// fixtures are the level's, and the gates are the player's.
        /// </summary>
        public float FixtureGlow = 1f;

        /// <summary>
        /// How many world units the board tile covers before it repeats. Its lines run along the
        /// tile's edges and through its middle, so at 1 they are half a unit apart -- a quarter of
        /// a cell -- and at 4 they are a cell apart and cross on every cell's centre.
        /// </summary>
        public float BoardUnits = 1f;

        /// <summary>How wide the board tile's lines are, as a share of the shipped width.</summary>
        /// <remarks>Held in world units, so a tile that covers more of the board does not thicken them.</remarks>
        public float BoardLineWidth = 1f;

        /// <summary>How large a placement grid mark is, as a share of the scene's size.</summary>
        public float GridMarkSize = 1f;

        /// <summary>What Settings calls it.</summary>
        public string Title = "CLASSIC";

        /// <summary>
        /// The number the player's choice is kept as. Never its place in <see cref="All"/>, so a look
        /// added or moved cannot change anyone's choice -- the frame cap's rule, kept as a rate.
        /// </summary>
        public int Code = 1;

        /// <summary>The 5.0 workbench's canvas in this look: its colours.</summary>
        public SchematicPalette Canvas;

        /// <summary>
        /// How much of the mock's Lively glow the canvas keeps on a change in flight and a gate as it
        /// flips: 0 none, 1 all of it. Schematic's is Ahmad's <see cref="SchematicStyle.GlowStrength"/>.
        /// </summary>
        public float CanvasGlow = 0.2f;

        private static Look _current;

        /// <summary>The look everything is drawn in.</summary>
        /// <remarks>
        /// Read through a fallback for the reason <see cref="Palette.Current"/> used to be: a
        /// static initialiser would run before the look it names had been built, and be null.
        /// </remarks>
        public static Look Current => _current ?? Default;

        /// <summary>The look the game is drawn in unless the player chooses another.</summary>
        /// <remarks>
        /// Schematic, as the plan proposed and Ahmad approved (2026-10-12), and confirmed at Part 8's
        /// review (2026-10-14). Its board is Neon Board's, so making it the default moved no pixel of the game.
        /// </remarks>
        public static Look Default => Looks.Schematic;

        /// <summary>Makes a look the current one. Null goes back to <see cref="Default"/>.</summary>
        public static void Use(Look look) => _current = look;

        /// <summary>A copy of this look under a new name, changed by <paramref name="change"/>.</summary>
        public Look Derive(string name, System.Action<Look> change)
        {
            var copy = (Look)MemberwiseClone();
            copy.Name = name;
            change?.Invoke(copy);
            return copy;
        }

        /// <summary>The game as 2.0 shipped it, and its colours on the 5.0 canvas.</summary>
        public static Look Classic { get; } = new Look
        {
            Name = "classic",
            Colours = Palette.Classic,
            Canvas = SchematicPalette.Classic,
        };

        private static Look[] _all;

        /// <summary>Every look there is, in the order Settings offers them.</summary>
        /// <remarks>
        /// Built on first use rather than in a static initialiser: <see cref="Looks"/> derives its
        /// looks from <see cref="Classic"/>, so whichever class was touched first would find the
        /// other half built and read a null out of it.
        /// </remarks>
        public static IReadOnlyList<Look> All => _all ??= new[] { Looks.Schematic, Looks.SchematicLight, Looks.NeonBoard, Classic };

        /// <summary>Where the player's choice of look is kept, beside the other settings.</summary>
        public const string PreferenceKey = "bitsorter.look";

        /// <summary>The look the player chose, or <see cref="Default"/> if they never chose one or chose one this build does not have.</summary>
        public static Look Chosen
        {
            get
            {
                int code = Preferences.GetInt(PreferenceKey, 0);
                foreach (Look look in All)
                {
                    if (look.Code == code)
                        return look;
                }

                return Default;
            }
        }

        /// <summary>The look with this name, or null if there is none.</summary>
        public static Look Named(string name)
        {
            foreach (Look look in All)
            {
                if (look.Name == name)
                    return look;
            }

            return null;
        }

        /// <summary>
        /// Sets the board's bloom from the current look, on this session's copy of the profile.
        /// </summary>
        /// <remarks>
        /// <see cref="Volume.profile"/> hands back an instance for the session rather than the
        /// asset, so the look never writes into the project. The asset holds Classic's values;
        /// every scene the game loads has the current look's put on it by <see cref="LookBoot"/>.
        /// </remarks>
        public static void ApplyBloom()
        {
            foreach (Volume volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (volume.sharedProfile == null || !volume.profile.TryGet(out Bloom bloom))
                    continue;

                bloom.intensity.value = Current.BloomIntensity;
                bloom.scatter.value = Current.BloomScatter;
            }
        }
    }
}
