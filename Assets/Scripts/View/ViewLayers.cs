namespace BitSorter.View
{
    /// <summary>
    /// The board's layer stack, back to front. Every sprite on the board draws at one of these.
    /// </summary>
    /// <remarks>
    /// Named constants rather than literals at sixteen call sites, because the stack was also
    /// written out in prose in three of those files and all three had drifted. The board claimed
    /// the grid sat at -2 when it sits at -6; the grid claimed bits ran 1 to 3 when a bit body is
    /// at 5; and the scorch mark claimed bits were at 3. None of them mentioned the scorch mark,
    /// the wiring preview, the tutorial's rings or the win celebration at all.
    ///
    /// A stack written down beside the thing it describes is a second copy of a fact, and it goes
    /// stale silently -- nothing recompiles when a comment stops being true. Here the names are
    /// the documentation, and there is one of them.
    ///
    /// Two rules hold the order together. **A thing drawn behind what it describes is invisible by
    /// construction**: a scorch mark sits on an input port, so at -4 it was under the node body and
    /// showed as a few red pixels at the gate's left edge. And **a bit is always on top**, because
    /// a bit crossing a scorched, stalled, wired gate is the thing the player is following.
    /// </remarks>
    internal static class ViewLayers
    {
        /// <summary>The flat field the whole board sits on.</summary>
        public const int Board = -10;

        /// <summary>The placement grid's cells.</summary>
        public const int Grid = -6;

        /// <summary>A node's glow, behind its body so the body reads as lit rather than outlined.</summary>
        public const int NodeGlow = -3;

        /// <summary>A wire's dark casing, and its brighter core over it.</summary>
        public const int WireCasing = -2;

        /// <inheritdoc cref="WireCasing"/>
        public const int WireCore = -1;

        /// <summary>The bins lighting up on a win, behind everything the player reads.</summary>
        public const int Celebration = -2;

        /// <summary>A node's body.</summary>
        public const int NodeBody = 0;

        /// <summary>A port's socket glow, a node's label, and the trail behind a moving bit.</summary>
        public const int NodeDetail = 1;

        /// <summary>A burn mark, over the gate it marks and under the bits crossing it.</summary>
        public const int Scorch = 2;

        /// <summary>
        /// A port stub, over the scorch mark.
        /// </summary>
        /// <remarks>
        /// A mark is four times the width of a stub, and a port that has collided before is
        /// exactly the one whose state is worth reading -- so the burn must not bury it.
        /// </remarks>
        public const int Port = 3;

        /// <summary>The wire being dragged. Above everything, because it is a cursor.</summary>
        public const int WiringPreview = 3;

        /// <summary>
        /// The cross on a waiting bit that an imminent collision will take with it, over its port.
        /// </summary>
        /// <remarks>
        /// Shares its order with a bit's glow, and never its depth: a port sits at depth zero and
        /// every bit at a small positive one (<see cref="BitRenderer.DepthOf"/>), so which is in
        /// front is decided the same way every time rather than left to chance.
        /// </remarks>
        public const int PortMark = 4;

        /// <summary>Sparks, a bit's own glow, and the tutorial's rings.</summary>
        public const int Spark = 4;

        /// <inheritdoc cref="Spark"/>
        public const int BitGlow = 4;

        /// <inheritdoc cref="Spark"/>
        public const int TutorialRing = 4;

        /// <summary>A bit, in front of nodes, wires, scorch marks and port stubs.</summary>
        public const int Bit = 5;

        /// <summary>
        /// A wire's delay, and the dark pill behind it: above everything else on the board.
        /// </summary>
        /// <remarks>
        /// A number under a glowing bit is unreadable. Still on the board, though, and so under
        /// every panel on the canvas -- these were drawn with IMGUI once, which paints over the
        /// canvas, and they printed across the level list and on top of the solved card.
        /// </remarks>
        public const int WireLabelBacking = 6;

        /// <inheritdoc cref="WireLabelBacking"/>
        public const int WireLabel = 7;
    }
}
