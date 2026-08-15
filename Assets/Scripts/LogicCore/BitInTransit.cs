namespace BitSorter.LogicCore
{
    /// <summary>
    /// A snapshot of one bit travelling along an edge, as seen by an observer.
    /// </summary>
    /// <remarks>
    /// A value type on purpose. Reading one allocates nothing, and the copy the caller receives
    /// is disconnected from the simulation -- writing to it cannot affect anything.
    ///
    /// Within a single edge, <see cref="TicksRemaining"/> uniquely identifies a bit: every bit on
    /// an edge shares that edge's delay, and no node emits more than once per output port per
    /// tick, so no two bits on one edge can ever hold the same remaining count. A renderer that
    /// needs to follow an individual bit between frames should key on
    /// (<see cref="Edge.Id"/>, <see cref="TicksRemaining"/>) rather than on list position, which
    /// shifts as bits are delivered.
    /// </remarks>
    public readonly struct BitInTransit
    {
        public readonly Bit Value;

        /// <summary>Ticks left before delivery. Counts down; the bit is removed when it hits 0.</summary>
        public readonly int TicksRemaining;

        /// <summary>The delay of the edge this bit is travelling, so the struct reads standalone.</summary>
        public readonly int TotalDelay;

        public BitInTransit(Bit value, int ticksRemaining, int totalDelay)
        {
            Value = value;
            TicksRemaining = ticksRemaining;
            TotalDelay = totalDelay;
        }

        /// <summary>
        /// How far along the edge the bit has travelled: 0 on the tick it was emitted, rising
        /// towards 1 at the target. An observable bit never reaches exactly 1, because it is
        /// delivered and removed in the same phase that would take it there.
        /// </summary>
        public float Progress =>
            TotalDelay <= 0 ? 0f : (TotalDelay - TicksRemaining) / (float)TotalDelay;

        public override string ToString() => $"{Value} ({TicksRemaining}/{TotalDelay} ticks left)";
    }
}
