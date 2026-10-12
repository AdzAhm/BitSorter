using System;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// A gate's or a continuous assignment's delay: <c>#d</c>, <c>#(rise, fall)</c> or
    /// <c>#(rise, fall, turn-off)</c>, in time units.
    /// </summary>
    /// <remarks>
    /// Which of them a change takes depends on where the output is going (IEEE 1800, 28.16):
    /// - a single bit going to 1 takes the rise delay, to 0 the fall delay, to z the turn-off delay,
    ///   and to x the smallest of them;
    /// - a vector going from nonzero to all zeros takes the fall delay, to all z the turn-off delay,
    ///   and anything else the rise delay.
    /// Without a turn-off delay, going to z takes the smaller of rise and fall. Icarus chooses a
    /// vector's delay differently, by something like its lowest bit (Docs/oracle-notes.md); the
    /// standard wins, and the course never delays a vector.
    /// </remarks>
    public readonly struct Delay : IEquatable<Delay>
    {
        public readonly long Rise;
        public readonly long Fall;

        /// <summary>The delay to z: given, or the smaller of rise and fall.</summary>
        public readonly long TurnOff;

        private Delay(long rise, long fall, long turnOff)
        {
            if (rise < 0 || fall < 0 || turnOff < 0)
                throw new ArgumentOutOfRangeException(nameof(rise), "A delay cannot be negative.");

            Rise = rise;
            Fall = fall;
            TurnOff = turnOff;
        }

        /// <summary>No delay: the change happens in the same time step, a delta cycle later.</summary>
        public static Delay Zero => default;

        /// <summary><c>#d</c>: every change takes <paramref name="d"/>.</summary>
        public static Delay Of(long d) => new Delay(d, d, d);

        /// <summary><c>#(rise, fall)</c>; going to z takes the smaller.</summary>
        public static Delay Of(long rise, long fall) => new Delay(rise, fall, Math.Min(rise, fall));

        /// <summary><c>#(rise, fall, turnOff)</c>.</summary>
        public static Delay Of(long rise, long fall, long turnOff) => new Delay(rise, fall, turnOff);

        /// <summary>Whether every change is immediate.</summary>
        public bool IsZero => Rise == 0 && Fall == 0 && TurnOff == 0;

        /// <summary>The delay a change from <paramref name="from"/> to <paramref name="to"/> takes.</summary>
        public long For(LogicVec from, LogicVec to)
        {
            if (to.Width == 1)
            {
                switch (to[0])
                {
                    case Logic4.One: return Rise;
                    case Logic4.Zero: return Fall;
                    case Logic4.Z: return TurnOff;
                    default: return Math.Min(Math.Min(Rise, Fall), TurnOff);
                }
            }

            if (to.IsAllZ)
                return TurnOff;

            bool toZero = to.IsKnown && LogicVec.Truth(to) == Logic4.Zero;
            bool fromZero = from.IsKnown && LogicVec.Truth(from) == Logic4.Zero;
            return toZero && !fromZero ? Fall : Rise;
        }

        public bool Equals(Delay other) => Rise == other.Rise && Fall == other.Fall && TurnOff == other.TurnOff;
        public override bool Equals(object obj) => obj is Delay other && Equals(other);
        public override int GetHashCode() => unchecked((int)(Rise * 397 ^ Fall * 31 ^ TurnOff));

        public override string ToString() =>
            IsZero ? "#0" : Rise == Fall && Fall == TurnOff ? $"#{Rise}" : $"#({Rise}, {Fall}, {TurnOff})";
    }
}
