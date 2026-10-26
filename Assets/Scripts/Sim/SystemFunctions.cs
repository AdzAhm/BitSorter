using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// <c>$random</c>'s generator, exactly as the standard gives it -- the C of IEEE 1364's annex,
    /// <c>rtl_dist_uniform</c>, which IEEE 1800 keeps (20.15.2) so that a seed gives the same numbers in
    /// every simulator (Part L3). Icarus's numbers for seeds 0, 7 and -5, and with no seed at all, are
    /// held by <c>RandomTests</c>.
    /// </summary>
    internal static class RtlRandom
    {
        /// <summary><c>$random(seed)</c>: the next number, and the seed moved on.</summary>
        public static int Random(ref int seed) => DistUniform(ref seed, int.MinValue, int.MaxValue);

        /// <summary>A whole number from <paramref name="start"/> to <paramref name="end"/>, both included.</summary>
        public static int DistUniform(ref int seed, int start, int end)
        {
            if (start >= end)
                return start;

            long i;
            if (end != int.MaxValue)
            {
                long top = (long)end + 1;
                double r = Uniform(ref seed, start, top);
                i = r >= 0 ? (long)r : (long)(r - 1);
                if (i < start) i = start;
                if (i >= top) i = top - 1;
            }
            else if (start != int.MinValue)
            {
                long bottom = (long)start - 1;
                double r = Uniform(ref seed, bottom, end) + 1.0;
                i = r >= 0 ? (long)r : (long)(r - 1);
                if (i <= bottom) i = bottom + 1;
                if (i > end) i = end;
            }
            else
            {
                double r = (Uniform(ref seed, start, end) + 2147483648.0) / 4294967295.0;
                r = r * 4294967296.0 - 2147483648.0;
                i = r >= 0 ? (long)r : (long)(r - 1);
            }

            return unchecked((int)i);
        }

        private static double Uniform(ref int seed, long start, long end)
        {
            const double d = 0.00000011920928955078125;
            if (seed == 0)
                seed = 259341593;

            double a, b;
            if (start >= end)
            {
                a = 0.0;
                b = 2147483647.0;
            }
            else
            {
                a = start;
                b = end;
            }

            seed = unchecked(69069 * seed + 1);
            uint bits = (unchecked((uint)seed) >> 9) | 0x3f800000u;
            double c = BitConverter.Int32BitsToSingle(unchecked((int)bits));
            c += c * d;
            return ((b - a) * (c - 1.0)) + a;
        }
    }

    /// <summary>Which random system function a <see cref="Expr.Random"/> is.</summary>
    internal enum RandomKind : byte
    {
        /// <summary><c>$random</c>: a signed <c>int</c> from the standard's generator.</summary>
        Random,

        /// <summary><c>$urandom</c>: an unsigned 32-bit number. The standard leaves its algorithm to each simulator; BitSorter's is <c>$random</c>'s bits read unsigned.</summary>
        URandom,

        /// <summary><c>$urandom_range(max, min)</c>: from the smaller to the larger, both included.</summary>
        URandomRange,
    }

    public abstract partial class Expr
    {
        /// <summary>
        /// A random system function's number from the seed's value now (<paramref name="nextSeed"/> false),
        /// or the seed it moves on to (true). The elaborator writes both before the statement that calls
        /// it, each worked out from the same old seed, so the call has no side effect of its own.
        /// </summary>
        internal static Expr Random(Expr seed, RandomKind kind, Expr low, Expr high, bool nextSeed) => new RandomExpr(seed, kind, low, high, nextSeed);

        private sealed class RandomExpr : Expr
        {
            private readonly Expr _seed, _low, _high;
            private readonly RandomKind _kind;
            private readonly bool _nextSeed;

            public RandomExpr(Expr seed, RandomKind kind, Expr low, Expr high, bool nextSeed)
            {
                _seed = seed ?? throw new ArgumentNullException(nameof(seed));
                _kind = kind;
                _low = low;
                _high = high;
                _nextSeed = nextSeed;
            }

            public override int Width => 32;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                int seed = Whole(_seed.Evaluate(values));
                int value;

                switch (_kind)
                {
                    case RandomKind.URandomRange:
                    {
                        int a = _high != null ? Whole(_high.Evaluate(values)) : 0;
                        int b = _low != null ? Whole(_low.Evaluate(values)) : 0;
                        value = RtlRandom.DistUniform(ref seed, Math.Min(a, b), Math.Max(a, b));
                        break;
                    }

                    default:
                        value = RtlRandom.Random(ref seed);
                        break;
                }

                if (_nextSeed)
                    return LogicVec.FromInt64(seed, 32, signed: true);

                return _kind == RandomKind.Random
                    ? LogicVec.FromInt64(value, 32, signed: true)
                    : LogicVec.FromUInt64(unchecked((uint)value), 32);
            }

            /// <summary>A seed or a bound as a 32-bit number: its low 32 bits, x and z as 0.</summary>
            private static int Whole(LogicVec value)
            {
                LogicVec low = value.Width > 32 ? value.Slice(0, 32) : value.Extend(32, value.IsSigned);
                low.ToTwoState().AsUnsigned().TryGetUInt64(out ulong bits);
                return unchecked((int)(uint)bits);
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _seed.CollectReads(reads);
                _low?.CollectReads(reads);
                _high?.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _seed.CollectCalls(calls);
                _low?.CollectCalls(calls);
                _high?.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_seed), Math.Max(HeightOf(_low), HeightOf(_high)));
        }

        // -----------------------------------------------------------------
        // An enum's methods (IEEE 1800, 6.19.5)
        // -----------------------------------------------------------------

        /// <summary><c>s.name()</c>: the name of the member <paramref name="value"/> holds, as a string; empty when it holds none.</summary>
        internal static Expr EnumName(Expr value, EnumInfo @enum) => new EnumNameExpr(value, @enum);

        /// <summary>
        /// <c>s.next(n)</c> (<paramref name="direction"/> 1) or <c>s.prev(n)</c> (-1): the member <paramref name="steps"/>
        /// on in declaration order, round from the end to the start; <paramref name="fallback"/> -- the type's
        /// default value -- when the value is no member.
        /// </summary>
        internal static Expr EnumStep(Expr value, EnumInfo @enum, Expr steps, int direction, LogicVec fallback) =>
            new EnumStepExpr(value, @enum, steps, direction, fallback);

        private static int MemberIndex(EnumInfo @enum, LogicVec value)
        {
            for (int i = 0; i < @enum.Members.Count; i++)
                if (LogicVec.Truth(LogicVec.CaseEqual(value, @enum.Members[i].Value)) == Logic4.One)
                    return i;
            return -1;
        }

        private sealed class EnumNameExpr : Expr
        {
            private readonly Expr _value;
            private readonly EnumInfo _enum;
            private readonly LogicVec[] _names;
            private readonly LogicVec _none;

            public EnumNameExpr(Expr value, EnumInfo @enum)
            {
                _value = value ?? throw new ArgumentNullException(nameof(value));
                _enum = @enum ?? throw new ArgumentNullException(nameof(@enum));

                int longest = 1;
                foreach (EnumMemberInfo member in @enum.Members)
                    longest = Math.Max(longest, member.Name.Length);

                Width = 8 * longest;
                _none = LogicVec.FromUInt64(0, Width);
                _names = new LogicVec[@enum.Members.Count];
                for (int i = 0; i < _names.Length; i++)
                    _names[i] = Text(@enum.Members[i].Name, Width);
            }

            public override int Width { get; }

            public override LogicVec Evaluate(LogicVec[] values)
            {
                int i = MemberIndex(_enum, _value.Evaluate(values));
                return i >= 0 ? _names[i] : _none;
            }

            /// <summary>A name's characters, the last in the lowest byte, zeros in front: how a string sits in bits.</summary>
            private static LogicVec Text(string name, int width)
            {
                LogicVec bits = LogicVec.FromUInt64(0, width);
                for (int i = 0; i < name.Length; i++)
                {
                    LogicVec c = LogicVec.FromUInt64((ulong)(name[name.Length - 1 - i] & 0xFF), width);
                    bits = LogicVec.Or(bits, LogicVec.ShiftLeft(c, 8 * i));
                }

                return bits;
            }

            public override void CollectReads(ICollection<int> reads) => _value.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _value.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_value);
        }

        private sealed class EnumStepExpr : Expr
        {
            private readonly Expr _value;
            private readonly EnumInfo _enum;
            private readonly Expr _steps;
            private readonly int _direction;
            private readonly LogicVec _fallback;

            public EnumStepExpr(Expr value, EnumInfo @enum, Expr steps, int direction, LogicVec fallback)
            {
                _value = value ?? throw new ArgumentNullException(nameof(value));
                _enum = @enum ?? throw new ArgumentNullException(nameof(@enum));
                _steps = steps;
                _direction = direction;
                _fallback = fallback;
            }

            public override int Width => _enum.Width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                int i = MemberIndex(_enum, _value.Evaluate(values));
                int count = _enum.Members.Count;
                if (i < 0 || count == 0)
                    return _fallback;

                long steps = 1;
                if (_steps != null && !_steps.Evaluate(values).TryGetInt64(out steps))
                    return _fallback;

                long at = ((i + _direction * (steps % count)) % count + count) % count;
                return _enum.Members[(int)at].Value;
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _value.CollectReads(reads);
                _steps?.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _value.CollectCalls(calls);
                _steps?.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_value), HeightOf(_steps));
        }
    }
}
