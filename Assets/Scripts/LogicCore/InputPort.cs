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
        public int LastCorruptedTick => _corruptedOnTick;

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
