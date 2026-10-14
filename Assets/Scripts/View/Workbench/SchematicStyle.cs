namespace BitSorter.View
{
    /// <summary>
    /// The Schematic look's measurements: how thick a wire is for each value, how strong the glow on
    /// a change in flight is, how long a gate's flash lasts. Its colours are <see cref="SchematicPalette"/>.
    /// </summary>
    /// <remarks>
    /// Ahmad chose the look on 2026-10-12 from three variants drawn side by side in an HTML mock
    /// (<c>bitsorter-5-look-v2</c>, then <c>-v3</c>): Quiet as the base, with Lively's trail and
    /// glow on a change in flight and its tint on a gate as it flips, both kept faint, and the
    /// waveform's lines in their values' colours. "Faint" is <see cref="GlowStrength"/>, which he
    /// set himself on the mock's slider. Every length here is in the stage's own units, the mock's
    /// SVG units, so the two can be compared number for number.
    /// </remarks>
    public static class SchematicStyle
    {
        /// <summary>
        /// How much of Lively's glow the Schematic look keeps: 0 is Quiet, 1 is Lively. Ahmad's
        /// choice, 35%. It scales the trail, the halo and the gate's tint together. Every look has
        /// its own (<see cref="Look.CanvasGlow"/>); this is the Schematic look's.
        /// </summary>
        public const float GlowStrength = 0.35f;

        /// <summary>The glow the canvas is drawn with now: the current look's, or none at all when motion is reduced.</summary>
        public static float Glow => ReducedMotion.IsOn ? 0f : Look.Current.CanvasGlow;

        /// <summary>How far into its flash a gate is drawn, given how far it is: not at all when motion is reduced.</summary>
        public static float Flash(float flash) => ReducedMotion.IsOn ? 0f : flash;

        /// <summary>A wire holding 0, and holding 1 or x: a 1 is a little heavier, so 0 and 1 differ by more than colour.</summary>
        public const float ZeroWidth = 3f;

        /// <inheritdoc cref="ZeroWidth"/>
        public const float OneWidth = 4f;

        /// <summary>A wire nothing drives: thin, and dashed.</summary>
        public const float HighZWidth = 2f;

        /// <summary>A bus, whatever it holds.</summary>
        public const float BusWidth = 6.5f;

        /// <summary>The dashes of an undriven wire.</summary>
        public const float DashOn = 6f;

        /// <inheritdoc cref="DashOn"/>
        public const float DashOff = 5f;

        /// <summary>A gate's and a port box's outline at rest.</summary>
        public const float OutlineWidth = 1f;

        /// <summary>How much thicker an outline gets at the peak of its flash.</summary>
        public const float FlashExtraWidth = 1.4f;

        /// <summary>
        /// How long a gate flashes after its output flips, and how long a swallowed change takes to
        /// fade from its wire -- in simulated nanoseconds, so both keep pace with the playback.
        /// </summary>
        public const double FlashTime = 0.7;

        /// <inheritdoc cref="FlashTime"/>
        public const double FadeTime = 1.2;

        /// <summary>A change's ring: for one digit, and for more.</summary>
        public const float RingRadius = 9f;

        /// <inheritdoc cref="RingRadius"/>
        public const float WideRingRadius = 11f;

        /// <summary>The ring's stroke.</summary>
        public const float RingWidth = 2f;

        /// <summary>A junction dot where a wire branches.</summary>
        public const float JunctionRadius = 4.2f;

        /// <summary>A NOT's bubble.</summary>
        public const float BubbleRadius = 5f;

        /// <summary>The corner radius of a port box.</summary>
        public const float PortCorner = 8f;

        /// <summary>A port box's height.</summary>
        public const float PortHeight = 30f;

        /// <summary>The gate's tint at the peak of its flash, at full glow: Lively's 28%, scaled by <see cref="GlowStrength"/>.</summary>
        public const float FlashTint = 0.28f;

        /// <summary>The trail behind a change: its length, width and opacity at full glow, and its blur.</summary>
        public const float TrailBaseLength = 18f;

        /// <inheritdoc cref="TrailBaseLength"/>
        public const float TrailExtraLength = 28f;

        /// <inheritdoc cref="TrailBaseLength"/>
        public const float TrailBaseWidth = 5f;

        /// <inheritdoc cref="TrailBaseLength"/>
        public const float TrailExtraWidth = 5f;

        /// <inheritdoc cref="TrailBaseLength"/>
        public const float TrailOpacity = 0.7f;

        /// <inheritdoc cref="TrailBaseLength"/>
        public const float Blur = 3.2f;

        /// <summary>The halo round a change: its radius and opacity at full glow.</summary>
        public const float HaloBaseRadius = 10f;

        /// <inheritdoc cref="HaloBaseRadius"/>
        public const float HaloExtraRadius = 7f;

        /// <inheritdoc cref="HaloBaseRadius"/>
        public const float HaloOpacity = 0.75f;

        /// <summary>How many nanoseconds of the run play in a second.</summary>
        public const double NanosecondsPerSecond = 1.6;
    }
}
