using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

    /// <summary>
    /// A whole visual direction: its <see cref="Palette"/>, and the choices that are not colours --
    /// how gate bodies are filled, how the grid is marked, how panels are drawn, how hard the board
    /// blooms.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Palette"/> so the palette stays what its name says, every colour
    /// and nothing else. <see cref="Palette.Current"/> reads through <see cref="Current"/>, so
    /// nothing that draws a colour needs to know a look exists.
    ///
    /// <see cref="Classic"/> is the game as it shipped, and every choice here defaults to what it
    /// already did; the reference screenshots hold it to that pixel for pixel. Chosen before the
    /// scene loads, and never changed in place -- derive a new one.
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

        /// <summary>
        /// How strongly the board blooms, and how far the glow spreads. The threshold is not here:
        /// it stays at <see cref="BitVisuals.BloomThreshold"/> in every look, because what crosses
        /// it is what makes a bit the thing that glows.
        /// </summary>
        public float BloomIntensity = 1f;

        /// <inheritdoc cref="BloomIntensity"/>
        public float BloomScatter = 0.62f;

        private static Look _current;

        /// <summary>The look everything is drawn in.</summary>
        /// <remarks>
        /// Read through a fallback for the reason <see cref="Palette.Current"/> used to be: a
        /// static initialiser written above <see cref="Classic"/> would run first and be null.
        /// </remarks>
        public static Look Current => _current ?? Classic;

        /// <summary>Makes a look the current one. Null goes back to <see cref="Classic"/>.</summary>
        public static void Use(Look look) => _current = look;

        /// <summary>A copy of this look under a new name, changed by <paramref name="change"/>.</summary>
        public Look Derive(string name, System.Action<Look> change)
        {
            var copy = (Look)MemberwiseClone();
            copy.Name = name;
            change?.Invoke(copy);
            return copy;
        }

        /// <summary>The game as it shipped.</summary>
        public static Look Classic { get; } = new Look
        {
            Name = "classic",
            Colours = Palette.Classic,
        };

        /// <summary>
        /// Sets the board's bloom from the current look, on this session's copy of the profile.
        /// </summary>
        /// <remarks>
        /// <see cref="Volume.profile"/> hands back an instance for the session rather than the
        /// asset, so the look never writes into the project. Classic's values are the asset's own,
        /// so for the shipped game this changes nothing.
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
