using System;
using System.Linq;
using System.Numerics;
using System.Text;

namespace BitSorter.SystemVerilog.Tests
{
    /// <summary>
    /// A second implementation of every operator, slow on purpose, that <see cref="LogicVec"/> is
    /// held to on random values.
    /// </summary>
    /// <remarks>
    /// It shares no code and no method with the real one. Bits are an array, one <see cref="Logic4"/>
    /// each; the bitwise operators read the standard's truth tables, typed in as text from IEEE 1800
    /// tables 11-7 to 11-10; arithmetic is done on <see cref="BigInteger"/>. A mistake would have to
    /// be made twice, in two different ways, to pass.
    /// </remarks>
    internal sealed class ReferenceVec
    {
        /// <summary>The bits, least significant first.</summary>
        public readonly Logic4[] Bits;

        public readonly bool Signed;

        public ReferenceVec(Logic4[] bits, bool signed)
        {
            Bits = bits;
            Signed = signed;
        }

        public int Width => Bits.Length;

        public static ReferenceVec Of(LogicVec v) =>
            new ReferenceVec(Enumerable.Range(0, v.Width).Select(i => v[i]).ToArray(), v.IsSigned);

        /// <summary>The bits as <c>$display("%b")</c> writes them, most significant first.</summary>
        public string Binary
        {
            get
            {
                var text = new StringBuilder(Width);
                for (int i = Width - 1; i >= 0; i--)
                    text.Append(Bits[i].ToChar());
                return text.ToString();
            }
        }

        public override string ToString() => $"{Width}'{(Signed ? "s" : "")}b{Binary}";

        private bool Known => Bits.All(b => b == Logic4.Zero || b == Logic4.One);

        private static ReferenceVec AllX(int width, bool signed) =>
            new ReferenceVec(Enumerable.Repeat(Logic4.X, width).ToArray(), signed);

        private static ReferenceVec Bit(Logic4 bit) => new ReferenceVec(new[] { bit }, false);

        // The standard's tables, rows and columns in the order 0 1 x z.
        private const string Order = "01xz";
        private static readonly string[] AndTable = { "0000", "01xx", "0xxx", "0xxx" };
        private static readonly string[] OrTable = { "01xx", "1111", "x1xx", "x1xx" };
        private static readonly string[] XorTable = { "01xx", "10xx", "xxxx", "xxxx" };
        private const string NotTable = "10xx";

        private static Logic4 Look(string[] table, Logic4 p, Logic4 q) =>
            FromChar(table[Order.IndexOf(p.ToChar())][Order.IndexOf(q.ToChar())]);

        private static Logic4 FromChar(char c)
        {
            Logic4Text.TryParse(c, out Logic4 bit);
            return bit;
        }

        private static Logic4 Invert(Logic4 bit) => FromChar(NotTable[Order.IndexOf(bit.ToChar())]);

        // -----------------------------------------------------------------

        public ReferenceVec Extend(int width, bool signExtend)
        {
            var bits = new Logic4[width];
            for (int i = 0; i < width; i++)
                bits[i] = i < Width ? Bits[i] : signExtend ? Bits[Width - 1] : Logic4.Zero;
            return new ReferenceVec(bits, Signed);
        }

        private static bool Align(ref ReferenceVec a, ref ReferenceVec b)
        {
            bool signed = a.Signed && b.Signed;
            int width = Math.Max(a.Width, b.Width);
            a = a.Extend(width, signed);
            b = b.Extend(width, signed);
            return signed;
        }

        private BigInteger Number(bool signed)
        {
            BigInteger value = BigInteger.Zero;
            for (int i = Width - 1; i >= 0; i--)
                value = value * 2 + (Bits[i] == Logic4.One ? 1 : 0);

            if (signed && Bits[Width - 1] == Logic4.One)
                value -= BigInteger.One << Width;

            return value;
        }

        private static ReferenceVec FromNumber(BigInteger value, int width, bool signed)
        {
            BigInteger modulus = BigInteger.One << width;
            value %= modulus;
            if (value < 0) value += modulus;

            var bits = new Logic4[width];
            for (int i = 0; i < width; i++)
            {
                bits[i] = value % 2 == 1 ? Logic4.One : Logic4.Zero;
                value /= 2;
            }

            return new ReferenceVec(bits, signed);
        }

        // -----------------------------------------------------------------
        // Bitwise
        // -----------------------------------------------------------------

        private static ReferenceVec Table(string[] table, ReferenceVec a, ReferenceVec b, bool invert = false)
        {
            bool signed = Align(ref a, ref b);
            var bits = new Logic4[a.Width];
            for (int i = 0; i < a.Width; i++)
            {
                Logic4 bit = Look(table, a.Bits[i], b.Bits[i]);
                bits[i] = invert ? Invert(bit) : bit;
            }

            return new ReferenceVec(bits, signed);
        }

        public static ReferenceVec And(ReferenceVec a, ReferenceVec b) => Table(AndTable, a, b);
        public static ReferenceVec Or(ReferenceVec a, ReferenceVec b) => Table(OrTable, a, b);
        public static ReferenceVec Xor(ReferenceVec a, ReferenceVec b) => Table(XorTable, a, b);
        public static ReferenceVec Xnor(ReferenceVec a, ReferenceVec b) => Table(XorTable, a, b, invert: true);

        public static ReferenceVec Not(ReferenceVec a) => new ReferenceVec(a.Bits.Select(Invert).ToArray(), a.Signed);

        // -----------------------------------------------------------------
        // Reduction: fold the table over the bits, as the standard defines it
        // -----------------------------------------------------------------

        private static Logic4 Fold(string[] table, ReferenceVec a)
        {
            Logic4 acc = a.Bits[0];
            for (int i = 1; i < a.Width; i++)
                acc = Look(table, acc, a.Bits[i]);

            // A one-bit reduction of z is still a reduction: z reads as x.
            return acc == Logic4.Z ? Logic4.X : acc;
        }

        public static ReferenceVec ReduceAnd(ReferenceVec a) => Bit(Fold(AndTable, a));
        public static ReferenceVec ReduceOr(ReferenceVec a) => Bit(Fold(OrTable, a));
        public static ReferenceVec ReduceXor(ReferenceVec a) => Bit(Fold(XorTable, a));
        public static ReferenceVec ReduceNand(ReferenceVec a) => Bit(Invert(Fold(AndTable, a)));
        public static ReferenceVec ReduceNor(ReferenceVec a) => Bit(Invert(Fold(OrTable, a)));
        public static ReferenceVec ReduceXnor(ReferenceVec a) => Bit(Invert(Fold(XorTable, a)));

        // -----------------------------------------------------------------
        // Logical
        // -----------------------------------------------------------------

        public static Logic4 Truth(ReferenceVec a)
        {
            if (a.Bits.Any(b => b == Logic4.One)) return Logic4.One;
            if (a.Bits.All(b => b == Logic4.Zero)) return Logic4.Zero;
            return Logic4.X;
        }

        public static ReferenceVec LogicalNot(ReferenceVec a) => Bit(Invert(Truth(a)));
        public static ReferenceVec LogicalAnd(ReferenceVec a, ReferenceVec b) => Bit(Look(AndTable, Truth(a), Truth(b)));
        public static ReferenceVec LogicalOr(ReferenceVec a, ReferenceVec b) => Bit(Look(OrTable, Truth(a), Truth(b)));

        // -----------------------------------------------------------------
        // Arithmetic
        // -----------------------------------------------------------------

        private static ReferenceVec Arith(ReferenceVec a, ReferenceVec b, Func<BigInteger, BigInteger, BigInteger?> op)
        {
            bool signed = Align(ref a, ref b);
            if (!a.Known || !b.Known)
                return AllX(a.Width, signed);

            BigInteger? result = op(a.Number(signed), b.Number(signed));
            return result == null ? AllX(a.Width, signed) : FromNumber(result.Value, a.Width, signed);
        }

        public static ReferenceVec Add(ReferenceVec a, ReferenceVec b) => Arith(a, b, (p, q) => p + q);
        public static ReferenceVec Subtract(ReferenceVec a, ReferenceVec b) => Arith(a, b, (p, q) => p - q);
        public static ReferenceVec Multiply(ReferenceVec a, ReferenceVec b) => Arith(a, b, (p, q) => p * q);
        public static ReferenceVec Divide(ReferenceVec a, ReferenceVec b) => Arith(a, b, (p, q) => q.IsZero ? (BigInteger?)null : BigInteger.Divide(p, q));
        public static ReferenceVec Modulo(ReferenceVec a, ReferenceVec b) => Arith(a, b, (p, q) => q.IsZero ? (BigInteger?)null : BigInteger.Remainder(p, q));

        public static ReferenceVec Negate(ReferenceVec a) =>
            a.Known ? FromNumber(-a.Number(a.Signed), a.Width, a.Signed) : AllX(a.Width, a.Signed);

        // -----------------------------------------------------------------
        // Comparison
        // -----------------------------------------------------------------

        private static ReferenceVec Relate(ReferenceVec a, ReferenceVec b, Func<int, bool> holds)
        {
            bool signed = Align(ref a, ref b);
            if (!a.Known || !b.Known)
                return Bit(Logic4.X);

            return Bit(holds(a.Number(signed).CompareTo(b.Number(signed))) ? Logic4.One : Logic4.Zero);
        }

        public static ReferenceVec LessThan(ReferenceVec a, ReferenceVec b) => Relate(a, b, c => c < 0);
        public static ReferenceVec LessOrEqual(ReferenceVec a, ReferenceVec b) => Relate(a, b, c => c <= 0);
        public static ReferenceVec GreaterThan(ReferenceVec a, ReferenceVec b) => Relate(a, b, c => c > 0);
        public static ReferenceVec GreaterOrEqual(ReferenceVec a, ReferenceVec b) => Relate(a, b, c => c >= 0);

        public static ReferenceVec Equal(ReferenceVec a, ReferenceVec b)
        {
            Align(ref a, ref b);
            bool unknown = false;

            for (int i = 0; i < a.Width; i++)
            {
                bool bothKnown = a.Bits[i] is Logic4.Zero or Logic4.One && b.Bits[i] is Logic4.Zero or Logic4.One;
                if (bothKnown && a.Bits[i] != b.Bits[i])
                    return Bit(Logic4.Zero);
                unknown |= !bothKnown;
            }

            return Bit(unknown ? Logic4.X : Logic4.One);
        }

        public static ReferenceVec NotEqual(ReferenceVec a, ReferenceVec b) => Bit(Invert(Equal(a, b).Bits[0]));

        public static ReferenceVec CaseEqual(ReferenceVec a, ReferenceVec b)
        {
            Align(ref a, ref b);
            return Bit(a.Bits.SequenceEqual(b.Bits) ? Logic4.One : Logic4.Zero);
        }

        public static ReferenceVec CaseNotEqual(ReferenceVec a, ReferenceVec b) => Bit(Invert(CaseEqual(a, b).Bits[0]));

        // -----------------------------------------------------------------
        // Shifts
        // -----------------------------------------------------------------

        private static ReferenceVec Shift(ReferenceVec a, ReferenceVec amount, bool left, bool arithmetic)
        {
            if (!amount.Known)
                return AllX(a.Width, a.Signed);

            BigInteger count = amount.Number(signed: false);
            Logic4 fill = arithmetic && a.Signed ? a.Bits[a.Width - 1] : Logic4.Zero;
            var bits = new Logic4[a.Width];

            for (int i = 0; i < a.Width; i++)
            {
                BigInteger from = left ? i - count : i + count;
                bits[i] = from < 0 ? Logic4.Zero : from >= a.Width ? fill : a.Bits[(int)from];
            }

            return new ReferenceVec(bits, a.Signed);
        }

        public static ReferenceVec ShiftLeft(ReferenceVec a, ReferenceVec n) => Shift(a, n, left: true, arithmetic: false);
        public static ReferenceVec ShiftRight(ReferenceVec a, ReferenceVec n) => Shift(a, n, left: false, arithmetic: false);
        public static ReferenceVec ArithmeticShiftRight(ReferenceVec a, ReferenceVec n) => Shift(a, n, left: false, arithmetic: true);

        // -----------------------------------------------------------------
        // Concatenation, selects, the conditional
        // -----------------------------------------------------------------

        public static ReferenceVec Concat(ReferenceVec high, ReferenceVec low) =>
            new ReferenceVec(low.Bits.Concat(high.Bits).ToArray(), false);

        public static ReferenceVec Replicate(ReferenceVec v, int count) =>
            new ReferenceVec(Enumerable.Repeat(v.Bits, count).SelectMany(b => b).ToArray(), false);

        public ReferenceVec Slice(int lowest, int width) =>
            new ReferenceVec(Enumerable.Range(lowest, width).Select(i => i >= 0 && i < Width ? Bits[i] : Logic4.X).ToArray(), false);

        public ReferenceVec Bit(ReferenceVec index)
        {
            if (!index.Known) return Bit(Logic4.X);
            BigInteger at = index.Number(index.Signed);
            return Bit(at < 0 || at >= Width ? Logic4.X : Bits[(int)at]);
        }

        public static ReferenceVec Conditional(ReferenceVec condition, ReferenceVec t, ReferenceVec f)
        {
            bool signed = Align(ref t, ref f);
            Logic4 truth = Truth(condition);

            if (truth == Logic4.One) return new ReferenceVec(t.Bits, signed);
            if (truth == Logic4.Zero) return new ReferenceVec(f.Bits, signed);

            var bits = new Logic4[t.Width];
            for (int i = 0; i < t.Width; i++)
            {
                bool agree = t.Bits[i] == f.Bits[i] && (t.Bits[i] == Logic4.Zero || t.Bits[i] == Logic4.One);
                bits[i] = agree ? t.Bits[i] : Logic4.X;
            }

            return new ReferenceVec(bits, signed);
        }
    }
}
