using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// Values written to variables at given times: what a testbench's <c>initial</c> block does with
    /// <c>a = 1; #10 a = 0;</c>, before the engine runs procedural code (Part 5).
    /// </summary>
    /// <remarks>
    /// A write takes effect at its time, in the first delta of that time step, together with any gate
    /// output due then; the gates that read it see it in the same step. Only a variable can be written
    /// this way: a net's value is what its drivers resolve to.
    /// </remarks>
    public sealed class Stimulus
    {
        private readonly List<(long Time, SignalId Signal, LogicVec Value)> _writes = new List<(long, SignalId, LogicVec)>();

        /// <summary>Writes <paramref name="value"/> to <paramref name="signal"/> at <paramref name="time"/>.</summary>
        public Stimulus Set(long time, SignalId signal, LogicVec value)
        {
            if (time < 0)
                throw new ArgumentOutOfRangeException(nameof(time), time, "Time starts at 0.");
            if (value.IsEmpty)
                throw new ArgumentException("A write needs a value.", nameof(value));

            _writes.Add((time, signal, value));
            return this;
        }

        /// <summary>Writes a one-bit 0, 1, x or z.</summary>
        public Stimulus Set(long time, SignalId signal, Logic4 bit) => Set(time, signal, LogicVec.Fill(bit, 1));

        /// <summary>Every write, in the order they were added.</summary>
        public IReadOnlyList<(long Time, SignalId Signal, LogicVec Value)> Writes => _writes;
    }
}
