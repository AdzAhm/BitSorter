using System;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// A one-slot mailbox on a node. Holds at most one pending bit, or nothing.
    /// Written only during a tick's delivery phase; cleared when its owner evaluates, or when a
    /// collision between differing values destroys its contents.
    /// </summary>
    public sealed class InputPort
    {
        private int _corruptedOnTick = -1;

        public Node Owner { get; }
        public int Index { get; }

        public Bit? Pending { get; private set; }
        public bool IsOccupied => Pending.HasValue;

        /// <summary>
        /// The tick on which a mixed-value collision last destroyed this port's contents, or -1.
        /// While it equals the tick being delivered, the port refuses further arrivals.
        /// </summary>
        /// <remarks>
        /// This is the **poison flag**, and it is simulation state rather than a report: the tick
        /// loop reads it to refuse later arrivals. It is therefore set only where the port is
        /// actually emptied, which is a mixed-value collision and nothing else. For "a bit was
        /// destroyed here", which is the wider fact, use <see cref="LastCollisionTick"/>.
        /// </remarks>
        public int LastCorruptedTick => _corruptedOnTick;

        /// <summary>
        /// The tick on which a collision last destroyed a bit at this port, or -1 for never.
        /// </summary>
        /// <remarks>
        /// The companion of <see cref="Simulation.CorruptedCount"/>, which counts destroyed bits:
        /// this says when one was last destroyed here, whatever kind of collision did it.
        ///
        /// Distinct from <see cref="LastCorruptedTick"/>, and the pair is easy to conflate. A
        /// matching-value collision destroys the arrival and leaves the port holding its value, so
        /// it loses a bit without emptying the port and without poisoning it -- which means it sets
        /// this and not that. A mixed-value collision sets both.
        ///
        /// Purely observational: nothing in the tick loop reads it, so recording it cannot change
        /// what the simulation does. That separation is the point. Folding this into the poison flag
        /// would make a matching collision refuse the rest of the tick's arrivals, which changes
        /// both CorruptedCount and the port's contents.
        /// </remarks>
        public int LastCollisionTick => _corruptedOnTick;

        internal InputPort(Node owner, int index)
        {
            Owner = owner;
            Index = index;
        }

        /// <summary>
        /// Offers <paramref name="value"/> to the port and reports how many bits were destroyed
        /// (0, 1 or 2) so <see cref="Simulation"/> can tally them. Never throws.
        /// </summary>
        /// <remarks>
        /// An empty port simply accepts the bit. An occupied port holding the same value keeps it
        /// and destroys the arrival -- the value is unambiguous, so something survives. An occupied
        /// port holding a different value is ambiguous, so neither bit survives: the port is
        /// cleared and stays poisoned for the remainder of this tick's delivery phase. Without the
        /// poison, a third arrival in the same tick could refill the port that a mixed collision
        /// just emptied, and the outcome would depend on edge insertion order again.
        /// </remarks>
        internal int Deliver(Bit value, int tick)
        {
            if (_corruptedOnTick == tick)
                return 1;

            if (!Pending.HasValue)
            {
                Pending = value;
                return 0;
            }

            if (Pending.Value == value)
                return 1;

            Pending = null;
            _corruptedOnTick = tick;
            return 2;
        }

        internal Bit Consume()
        {
            if (!Pending.HasValue)
                throw new InvalidOperationException(
                    $"Input port {Index} on {Owner} is empty and cannot be consumed.");

            Bit value = Pending.Value;
            Pending = null;
            return value;
        }

        public override string ToString() =>
            $"{Owner}.In({Index})=" + (Pending.HasValue ? Pending.Value.ToString() : "empty");
    }
}
