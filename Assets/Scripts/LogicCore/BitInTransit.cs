namespace BitSorter.LogicCore
{
    /// <summary>
    /// A snapshot of one bit travelling along an edge, as seen by an observer.
    /// </summary>
    /// <remarks>
    /// A value type on purpose. Reading one allocates nothing, and the copy the caller receives
    /// is disconnected from the simulation -- writing to it cannot affect anything.
    ///
    /// **To follow one bit between frames, key on (<see cref="Edge.Id"/>, <see cref="Serial"/>).**
    /// Not on list position, which shifts as bits are delivered, and **not** on
    /// <see cref="TicksRemaining"/>.
    ///
    /// This used to recommend TicksRemaining, on the grounds that every bit on an edge shares that
    /// edge's delay so no two can hold the same remaining count. That is true, and it is only true
    /// of one instant. Across a tick boundary the count a departing bit vacates is taken by the bit
    /// behind it, so on a delay-1 edge carrying a dense stream every bit reports the same handle
    /// for the whole run -- four bits, one handle. A renderer diffing frames then reads a new bit
    /// as the old one still travelling, and neither its emission nor its arrival is ever noticed.
    /// </remarks>
    public readonly struct BitInTransit
    {
        public readonly Bit Value;

        /// <summary>Ticks left before delivery. Counts down; the bit is removed when it hits 0.</summary>
        public readonly int TicksRemaining;

        /// <summary>The delay of the edge this bit is travelling, so the struct reads standalone.</summary>
        public readonly int TotalDelay;

        /// <summary>
        /// Which bit this is, among all the bits this edge has ever carried. Never reused.
        /// </summary>
        /// <remarks>
        /// Unique per edge for the life of the graph, so (<see cref="Edge.Id"/>, Serial) is a handle
        /// on one particular bit rather than on a position in a queue. Counts from zero on each
        /// edge; a graph is rebuilt rather than reset, so it starts from zero again with it.
        ///
        /// Deterministic despite being assigned as bits are emitted: an edge has one source port
        /// and a node evaluates at most once per tick, so an edge accepts at most one bit per tick
        /// and the sequence is tick order regardless of the order nodes were visited in.
        /// </remarks>
        public readonly int Serial;

        public BitInTransit(Bit value, int ticksRemaining, int totalDelay, int serial = 0)
        {
            Value = value;
            TicksRemaining = ticksRemaining;
            TotalDelay = totalDelay;
            Serial = serial;
        }

        /// <summary>
        /// How far along the edge the bit has travelled: 0 on the tick it was emitted, rising
        /// towards 1 at the target. An observable bit never reaches exactly 1, because it is
        /// delivered and removed in the same phase that would take it there.
        /// </summary>
        public float Progress =>
            TotalDelay <= 0 ? 0f : (TotalDelay - TicksRemaining) / (float)TotalDelay;

        public override string ToString() =>
            $"#{Serial} {Value} ({TicksRemaining}/{TotalDelay} ticks left)";
    }
}
