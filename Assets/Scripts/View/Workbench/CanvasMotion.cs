using BitSorter.ViewModel;

namespace BitSorter.View
{
    /// <summary>
    /// How much the canvas moves as it is drawn now: the current look's glow, and a gate's flash --
    /// both none at all when motion is reduced.
    /// </summary>
    /// <remarks>
    /// The part of <see cref="SchematicStyle"/> that reads the player's settings, so it stays in the
    /// view when the rest of the look's measurements moved to the view-model (Part 9): the look is
    /// chosen in Settings and reduced motion is a preference, and neither is the view-model's to read.
    /// </remarks>
    public static class CanvasMotion
    {
        /// <summary>The glow the canvas is drawn with now: the current look's, or none at all when motion is reduced.</summary>
        public static float Glow => ReducedMotion.IsOn ? 0f : Look.Current.CanvasGlow;

        /// <summary>How far into its flash a gate is drawn, given how far it is: not at all when motion is reduced.</summary>
        public static float Flash(float flash) => ReducedMotion.IsOn ? 0f : flash;
    }
}
