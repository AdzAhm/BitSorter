using System.Collections.Generic;
using BitSorter.ViewModel;

namespace BitSorter.View
{
    /// <summary>How a panel's backdrop is drawn.</summary>
    public enum PanelStyle
    {
        /// <summary>A filled slab, as the game shipped.</summary>
        Filled,

        /// <summary>A dark body with a thin edge drawn round it.</summary>
        Bordered,
    }

    /// <summary>
    /// A whole visual direction: its <see cref="Palette"/>, how its panels are drawn, and how the 5.0 workbench's canvas is
    /// drawn (<see cref="Canvas"/>).
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Palette"/> so the palette stays what its name says, every colour
    /// and nothing else. <see cref="Palette.Current"/> reads through <see cref="Current"/>, so
    /// nothing that draws a colour needs to know a look exists.
    ///
    /// <see cref="Default"/> is what the game is drawn in unless the player chooses another in
    /// Settings: Schematic, which is the workbench as Ahmad chose it on Neon Board's interface -- Neon
    /// Board was chosen out of three directions rendered side by side. <see cref="Classic"/> is the game as 2.0 shipped it, and
    /// every choice here defaults to what it did, so Classic is still one line to draw and the
    /// reference screenshots can hold a change that claims to touch no look to that, pixel for
    /// pixel. Chosen before the scene loads, and never changed in place -- derive a new one.
    ///
    /// A look also decided how the 4.x board's gates, bits and trails were drawn, how hard it bloomed and what marked its
    /// grid. Those went with the board in Parts 16a and 16b.
    /// </remarks>
    public sealed class Look
    {
        /// <summary>What this look is called: what a reference shot names it by, and what <see cref="Named"/> finds it with.</summary>
        public string Name { get; private set; }

        /// <summary>Its colours.</summary>
        public Palette Colours;

        /// <summary>How panels are drawn.</summary>
        public PanelStyle Panels = PanelStyle.Filled;

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
        /// review (2026-10-14).
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
    }
}
