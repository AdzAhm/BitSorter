using System;
using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// Emits a scripted sequence, one entry per tick starting at tick 0, then falls silent.
    /// Having no input ports, it is vacuously ready every tick.
    /// </summary>
    /// <remarks>
    /// **An entry may be nothing.** A null in the sequence is a tick this source stays quiet on,
    /// which is how a level spaces its vectors out into clock cycles: a period of three is a bit
    /// followed by two silent ticks. Levels without a clock hand in a dense sequence and behave
    /// exactly as they always did.
    ///
    /// This is the shape the sparse case was always going to take -- a `Bit?[]` where null means
    /// emit nothing -- rather than a rate the source counts against, because a rate cannot express a
    /// gap that is not regular and would have to be re-explained anywhere a stream is read.
    ///
    /// A sequence should not end on silence. Trailing nulls leave the source unexhausted, so the
    /// run is not settled and the grader waits for a bit that is never coming; a level therefore
    /// puts its gaps *between* vectors and stops after the last one.
    /// </remarks>
    public sealed class SourceNode : Node
    {
        private readonly Bit?[] _sequence;
        private int _next;

        /// <summary>A source that emits on every tick until its sequence runs out.</summary>
        public SourceNode(IEnumerable<Bit> sequence) : base(0, 1)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));

            var dense = new List<Bit?>();

            foreach (Bit bit in sequence)
                dense.Add(bit);

            _sequence = dense.ToArray();
        }

        /// <summary>A source that stays silent on every tick its sequence has no bit for.</summary>
        public SourceNode(IEnumerable<Bit?> sequence) : base(0, 1)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            _sequence = new List<Bit?>(sequence).ToArray();
        }

        /// <summary>The scripted sequence, where a null entry is a tick with no bit on it.</summary>
        public IReadOnlyList<Bit?> Sequence => _sequence;

        /// <summary>
        /// Whether the whole sequence has been played out, silences included. What the grader waits
        /// for before it will call a run settled.
        /// </summary>
        public bool IsExhausted => _next >= _sequence.Length;

        protected override void OnEvaluate(Bit[] inputs, int tick)
        {
            if (_next >= _sequence.Length)
                return;

            Bit? value = _sequence[_next];
            _next++;

            if (value.HasValue)
                Out(0).Emit(value.Value);
        }
    }
}
