using System;
using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits a scripted sequence, one bit per tick starting at tick 0, then falls silent.
    /// Having no input ports, it is vacuously ready every tick.
    /// </summary>
    public sealed class SourceNode : Node
    {
        private readonly Bit[] _sequence;
        private int _next;

        public SourceNode(IEnumerable<Bit> sequence) : base(0, 1)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            _sequence = new List<Bit>(sequence).ToArray();
        }

        public IReadOnlyList<Bit> Sequence => _sequence;
        public bool IsExhausted => _next >= _sequence.Length;

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            if (_next >= _sequence.Length)
                return;

            Out(0).Emit(_sequence[_next]);
            _next++;
        }
    }
}
