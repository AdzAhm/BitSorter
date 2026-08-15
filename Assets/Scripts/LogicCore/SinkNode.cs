using System;
using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// Terminal node that records every bit it consumes together with the tick it was consumed on.
    /// </summary>
    public sealed class SinkNode : Node
    {
        /// <summary>
        /// A recorded bit. <see cref="Tick"/> is the tick the sink <em>evaluated</em> on. For a
        /// single-input sink that is also the tick the bit arrived; for a multi-input sink the two
        /// diverge, because a bit waits in its port until every sibling port is filled.
        /// </summary>
        public readonly struct Reception : IEquatable<Reception>
        {
            public readonly Bit Value;
            public readonly int Tick;

            public Reception(Bit value, int tick)
            {
                Value = value;
                Tick = tick;
            }

            public bool Equals(Reception other) => Value == other.Value && Tick == other.Tick;
            public override bool Equals(object obj) => obj is Reception other && Equals(other);
            public override int GetHashCode() => ((int)Value * 397) ^ Tick;
            public override string ToString() => $"({Value} @ tick {Tick})";
        }

        private readonly List<Reception> _received = new List<Reception>();

        public SinkNode(int inputCount = 1) : base(RequireAtLeastOne(inputCount), 0)
        {
        }

        public IReadOnlyList<Reception> Received => _received;

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            for (int i = 0; i < inputs.Length; i++)
                _received.Add(new Reception(inputs[i], tick));
        }

        private static int RequireAtLeastOne(int inputCount)
        {
            if (inputCount < 1)
                throw new ArgumentOutOfRangeException(nameof(inputCount), inputCount,
                    "A sink needs at least one input port.");

            return inputCount;
        }
    }
}
