using System;
using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>A bit that has arrived and is waiting to be delivered into its target port.</summary>
    internal readonly struct Arrival
    {
        public readonly InputPort Target;
        public readonly Bit Value;

        public Arrival(InputPort target, Bit value)
        {
            Target = target;
            Value = value;
        }
    }

    /// <summary>
    /// A one-way connection from an output port to an input port, carrying bits with a fixed
    /// travel time. Several bits may be in flight at once; because they all share the edge's
    /// delay, at most one can arrive per tick.
    /// </summary>
    public sealed class Edge
    {
        private struct Transit
        {
            public Bit Value;
            public int TicksRemaining;
        }

        private readonly List<Transit> _inTransit = new List<Transit>();

        /// <summary>
        /// Stable identifier, assigned when the edge is added to a <see cref="Simulation"/> and
        /// never reused or changed. A renderer can key its visuals to this across ticks. -1 until
        /// the edge is registered.
        /// </summary>
        public int Id { get; internal set; } = -1;

        public OutputPort Source { get; }
        public InputPort Target { get; }
        public int Delay { get; }

        /// <summary>How many bits are currently travelling this edge.</summary>
        public int InTransitCount => _inTransit.Count;

        /// <summary>
        /// The bit at <paramref name="index"/>, ordered nearest-to-target first. Returns a value
        /// type, so this allocates nothing and is safe to call every frame.
        /// </summary>
        /// <remarks>
        /// Positions shift as bits are delivered, so an index is not a stable handle on a
        /// particular bit between ticks. See <see cref="BitInTransit"/> for what to key on
        /// instead.
        /// </remarks>
        public BitInTransit GetBitInTransit(int index)
        {
            Transit transit = _inTransit[index];
            return new BitInTransit(transit.Value, transit.TicksRemaining, Delay);
        }

        internal Edge(OutputPort source, InputPort target, int delay)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (delay < 1)
                throw new ArgumentOutOfRangeException(nameof(delay), delay,
                    "Edge delay must be at least 1 tick. Emission happens after delivery within a " +
                    "tick, so a delay-0 bit could never be delivered.");

            Source = source;
            Target = target;
            Delay = delay;
            source.Attach(this);
        }

        internal void Accept(Bit value)
        {
            _inTransit.Add(new Transit { Value = value, TicksRemaining = Delay });
        }

        /// <summary>
        /// Decrements every in-transit bit, appending those that have arrived to
        /// <paramref name="arrivals"/> in flight order and removing them from the edge.
        /// </summary>
        internal void AdvanceAndCollect(List<Arrival> arrivals)
        {
            int write = 0;

            for (int read = 0; read < _inTransit.Count; read++)
            {
                Transit transit = _inTransit[read];
                transit.TicksRemaining--;

                if (transit.TicksRemaining <= 0)
                    arrivals.Add(new Arrival(Target, transit.Value));
                else
                    _inTransit[write++] = transit;
            }

            _inTransit.RemoveRange(write, _inTransit.Count - write);
        }

        public override string ToString() => $"{Source} -> {Target} (delay {Delay})";
    }
}
