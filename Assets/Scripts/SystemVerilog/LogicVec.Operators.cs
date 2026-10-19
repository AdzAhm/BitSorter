using System;
using System.Numerics;

namespace BitSorter.SystemVerilog
{
    /// <summary>The SystemVerilog operators on 4-state values, as IEEE 1800 section 11.4 defines them.</summary>
    /// <remarks>
    /// Each works at its operands' width -- see the remarks on <see cref="LogicVec"/> for why the
    /// context's width is the elaborator's business -- and states its rule for x and z, because those
    /// rules are what makes a waveform in ModelSim look the way it does. In every one, a z operand bit
    /// counts as x: z is a state of a wire, and an operator reading one does not know its value.
    /// </remarks>
    public readonly partial struct LogicVec
    {
        private static readonly LogicVec Bit0 = new LogicVec(1, false, 0UL, 0UL);
        private static readonly LogicVec Bit1 = new LogicVec(1, false, 1UL, 0UL);
        private static readonly LogicVec BitX = new LogicVec(1, false, 1UL, 1UL);

        /// <summary>A one-bit unsigned result: what every comparison, reduction and logical operator gives.</summary>
        public static LogicVec OneBit(Logic4 bit)
        {
            switch (bit)
            {
                case Logic4.Zero: return Bit0;
                case Logic4.One: return Bit1;
                case Logic4.Z: return new LogicVec(1, false, 0UL, 1UL);
                default: return BitX;
            }
        }

        /// <summary>
        /// Brings two operands to one width -- the wider of the two -- sign-extending both only if
        /// both are signed, and says whether the result is signed. The standard's rule: an expression
        /// is signed only if every operand is (IEEE 1800, 11.8.1), and that decides how each extends.
        /// </summary>
        private static bool Align(ref LogicVec a, ref LogicVec b)
        {
            Require(a);
            Require(b);

            bool signed = a._signed && b._signed;
            int width = Math.Max(a._width, b._width);
            a = a.Extend(width, signed);
            b = b.Extend(width, signed);
            return signed;
        }

        // -----------------------------------------------------------------
        // Bitwise: & | ^ ~^ ~
        // -----------------------------------------------------------------

        private enum BitOp { And, Or, Xor, Xnor }

        /// <summary>
        /// One word of a bitwise operator, both planes at once. A bit is known 0 where both planes are
        /// clear and known 1 where only the a-plane is set; anything else is unknown.
        /// </summary>
        private static void BitWord(BitOp op, ulong pa, ulong pb, ulong qa, ulong qb, out ulong ra, out ulong rb)
        {
            ulong p0 = ~pa & ~pb, p1 = pa & ~pb;
            ulong q0 = ~qa & ~qb, q1 = qa & ~qb;
            ulong zero, one;

            switch (op)
            {
                case BitOp.And:
                    // 0 wins: a known 0 on either side makes the bit 0, even against x.
                    zero = p0 | q0;
                    one = p1 & q1;
                    break;

                case BitOp.Or:
                    // 1 wins: a known 1 on either side makes the bit 1, even against x.
                    one = p1 | q1;
                    zero = p0 & q0;
                    break;

                case BitOp.Xor:
                    // Nothing wins: an unknown on either side makes the bit x.
                    one = (pa ^ qa) & ~(pb | qb);
                    zero = ~(pa ^ qa) & ~(pb | qb);
                    break;

                default:
                    one = ~(pa ^ qa) & ~(pb | qb);
                    zero = (pa ^ qa) & ~(pb | qb);
                    break;
            }

            ulong x = ~(zero | one);
            ra = one | x;
            rb = x;
        }

        private static LogicVec Bitwise(BitOp op, LogicVec p, LogicVec q)
        {
            bool signed = Align(ref p, ref q);
            int width = p._width;

            if (width <= 64)
            {
                BitWord(op, p._a, p._b, q._a, q._b, out ulong ra, out ulong rb);
                return new LogicVec(width, signed, ra, rb);
            }

            int n = WordsFor(width);
            ulong[] planes = NewPlanes(width);
            for (int i = 0; i < n; i++)
                BitWord(op, p._wide[i], p._wide[n + i], q._wide[i], q._wide[n + i], out planes[i], out planes[n + i]);

            return new LogicVec(width, signed, planes);
        }

        /// <summary><c>a &amp; b</c>. A known 0 on either side gives 0, even against x; two 1s give 1; anything else x.</summary>
        public static LogicVec And(LogicVec a, LogicVec b) => Bitwise(BitOp.And, a, b);

        /// <summary><c>a | b</c>. A known 1 on either side gives 1, even against x; two 0s give 0; anything else x.</summary>
        public static LogicVec Or(LogicVec a, LogicVec b) => Bitwise(BitOp.Or, a, b);

        /// <summary><c>a ^ b</c>. x wherever either bit is unknown.</summary>
        public static LogicVec Xor(LogicVec a, LogicVec b) => Bitwise(BitOp.Xor, a, b);

        /// <summary><c>a ~^ b</c>. x wherever either bit is unknown.</summary>
        public static LogicVec Xnor(LogicVec a, LogicVec b) => Bitwise(BitOp.Xnor, a, b);

        /// <summary><c>~a</c>. 0 and 1 swap; x and z both give x.</summary>
        public static LogicVec Not(LogicVec a)
        {
            Require(a);

            if (a._width <= 64)
                return new LogicVec(a._width, a._signed, (~a._a & ~a._b) | a._b, a._b);

            int n = a.Words;
            ulong[] planes = NewPlanes(a._width);
            for (int i = 0; i < n; i++)
            {
                ulong va = a._wide[i], vb = a._wide[n + i];
                planes[i] = (~va & ~vb) | vb;
                planes[n + i] = vb;
            }

            return new LogicVec(a._width, a._signed, planes);
        }

        // -----------------------------------------------------------------
        // Reduction: &a |a ^a ~&a ~|a ~^a
        // -----------------------------------------------------------------

        /// <summary>Whether any bit inside the width is a known 0, a known 1, or unknown.</summary>
        private void Census(out bool anyZero, out bool anyOne, out bool anyUnknown)
        {
            anyZero = anyOne = anyUnknown = false;
            int n = Words;

            for (int i = 0; i < n; i++)
            {
                ulong mask = i == n - 1 ? TopMask(_width) : ulong.MaxValue;
                ulong a = AWord(i), b = BWord(i);

                anyZero |= (~a & ~b & mask) != 0;
                anyOne |= (a & ~b) != 0;
                anyUnknown |= b != 0;
            }
        }

        /// <summary><c>&amp;a</c>: 0 if any bit is a known 0, else x if any is unknown, else 1.</summary>
        public static LogicVec ReduceAnd(LogicVec a)
        {
            Require(a);
            a.Census(out bool zero, out _, out bool unknown);
            return zero ? Bit0 : unknown ? BitX : Bit1;
        }

        /// <summary><c>|a</c>: 1 if any bit is a known 1, else x if any is unknown, else 0.</summary>
        public static LogicVec ReduceOr(LogicVec a)
        {
            Require(a);
            a.Census(out _, out bool one, out bool unknown);
            return one ? Bit1 : unknown ? BitX : Bit0;
        }

        /// <summary><c>^a</c>, the parity: x if any bit is unknown.</summary>
        public static LogicVec ReduceXor(LogicVec a)
        {
            Require(a);
            if (!a.IsKnown)
                return BitX;

            int ones = 0;
            for (int i = 0; i < a.Words; i++)
                ones += PopCount(a.AWord(i));

            return (ones & 1) != 0 ? Bit1 : Bit0;
        }

        /// <summary><c>~&amp;a</c>.</summary>
        public static LogicVec ReduceNand(LogicVec a) => Not(ReduceAnd(a));

        /// <summary><c>~|a</c>.</summary>
        public static LogicVec ReduceNor(LogicVec a) => Not(ReduceOr(a));

        /// <summary><c>~^a</c>.</summary>
        public static LogicVec ReduceXnor(LogicVec a) => Not(ReduceXor(a));

        /// <summary>Set bits in a word. Written out: BitOperations is not in netstandard2.1, which Unity builds against.</summary>
        private static int PopCount(ulong v)
        {
            v -= (v >> 1) & 0x5555555555555555UL;
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }

        // -----------------------------------------------------------------
        // Logical: ! && ||, and the truth of a value
        // -----------------------------------------------------------------

        /// <summary>
        /// What a value means as a condition: 1 if any bit is a known 1, 0 if every bit is a known 0,
        /// and x otherwise -- a value with a 1 in it is true whatever its x bits are.
        /// </summary>
        public static Logic4 Truth(LogicVec a)
        {
            Require(a);
            a.Census(out _, out bool one, out bool unknown);
            return one ? Logic4.One : unknown ? Logic4.X : Logic4.Zero;
        }

        /// <summary>
        /// Whether an <c>if</c> takes its branch: only on a known true. An x condition takes the
        /// <c>else</c>, as the standard says (IEEE 1800, 12.4).
        /// </summary>
        public bool IsTrue => Truth(this) == Logic4.One;

        /// <summary><c>!a</c>, one bit: the truth of <paramref name="a"/> inverted, x staying x.</summary>
        public static LogicVec LogicalNot(LogicVec a)
        {
            Logic4 truth = Truth(a);
            return truth == Logic4.One ? Bit0 : truth == Logic4.Zero ? Bit1 : BitX;
        }

        /// <summary><c>a &amp;&amp; b</c>, one bit: 0 if either is false, even against x; 1 if both are true; else x.</summary>
        public static LogicVec LogicalAnd(LogicVec a, LogicVec b)
        {
            Logic4 p = Truth(a), q = Truth(b);
            if (p == Logic4.Zero || q == Logic4.Zero) return Bit0;
            return p == Logic4.One && q == Logic4.One ? Bit1 : BitX;
        }

        /// <summary><c>a || b</c>, one bit: 1 if either is true, even against x; 0 if both are false; else x.</summary>
        public static LogicVec LogicalOr(LogicVec a, LogicVec b)
        {
            Logic4 p = Truth(a), q = Truth(b);
            if (p == Logic4.One || q == Logic4.One) return Bit1;
            return p == Logic4.Zero && q == Logic4.Zero ? Bit0 : BitX;
        }

        // -----------------------------------------------------------------
        // Arithmetic: + - * / % and unary -
        // -----------------------------------------------------------------
        //
        // The standard's rule for all of them is blunt: if any bit of any operand is x or z, every bit
        // of the result is x. A sum cannot be partly known, because a carry depends on every bit below
        // it. Division or modulo by zero is x too. Otherwise the result is the exact answer modulo
        // 2^width, which for addition, subtraction and multiplication is the same bits signed or not.

        /// <summary><c>a + b</c>.</summary>
        public static LogicVec Add(LogicVec a, LogicVec b)
        {
            bool signed = Align(ref a, ref b);
            if (!a.IsKnown || !b.IsKnown)
                return AllX(a._width, signed);

            if (a._width <= 64)
                return new LogicVec(a._width, signed, unchecked(a._a + b._a), 0);

            int n = a.Words;
            ulong[] planes = NewPlanes(a._width);
            ulong carry = 0;
            for (int i = 0; i < n; i++)
            {
                ulong x = a._wide[i], y = b._wide[i];
                ulong sum = unchecked(x + y);
                ulong carryOut = sum < x ? 1UL : 0UL;
                ulong total = unchecked(sum + carry);
                if (total < sum) carryOut = 1;
                planes[i] = total;
                carry = carryOut;
            }

            return new LogicVec(a._width, signed, planes);
        }

        /// <summary><c>a - b</c>.</summary>
        public static LogicVec Subtract(LogicVec a, LogicVec b)
        {
            bool signed = Align(ref a, ref b);
            if (!a.IsKnown || !b.IsKnown)
                return AllX(a._width, signed);

            if (a._width <= 64)
                return new LogicVec(a._width, signed, unchecked(a._a - b._a), 0);

            int n = a.Words;
            ulong[] planes = NewPlanes(a._width);
            ulong borrow = 0;
            for (int i = 0; i < n; i++)
            {
                ulong x = a._wide[i], y = b._wide[i];
                ulong diff = unchecked(x - y);
                ulong borrowOut = x < y ? 1UL : 0UL;
                ulong total = unchecked(diff - borrow);
                if (diff < borrow) borrowOut = 1;
                planes[i] = total;
                borrow = borrowOut;
            }

            return new LogicVec(a._width, signed, planes);
        }

        /// <summary><c>a * b</c>. Up to 64 bits the low word of the machine product is exact modulo 2^width.</summary>
        public static LogicVec Multiply(LogicVec a, LogicVec b)
        {
            bool signed = Align(ref a, ref b);
            if (!a.IsKnown || !b.IsKnown)
                return AllX(a._width, signed);

            if (a._width <= 64)
                return new LogicVec(a._width, signed, unchecked(a._a * b._a), 0);

            return FromBigInteger(ToUnsignedBig(a) * ToUnsignedBig(b), a._width, signed);
        }

        /// <summary>
        /// <c>a / b</c>, rounding towards zero, as the standard does. x when <paramref name="b"/> is 0.
        /// Signed only when both are: the most negative number divided by -1 wraps to itself.
        /// </summary>
        public static LogicVec Divide(LogicVec a, LogicVec b) => DivideOrModulo(a, b, modulo: false);

        /// <summary><c>a % b</c>, taking the sign of <paramref name="a"/>, as the standard does. x when <paramref name="b"/> is 0.</summary>
        public static LogicVec Modulo(LogicVec a, LogicVec b) => DivideOrModulo(a, b, modulo: true);

        private static LogicVec DivideOrModulo(LogicVec a, LogicVec b, bool modulo)
        {
            bool signed = Align(ref a, ref b);
            int width = a._width;

            if (!a.IsKnown || !b.IsKnown || IsZeroKnown(b))
                return AllX(width, signed);

            if (width <= 64)
            {
                if (!signed)
                    return new LogicVec(width, false, modulo ? a._a % b._a : a._a / b._a, 0);

                long x = SignExtend64(a._a, width), y = SignExtend64(b._a, width);

                // The one signed division a machine refuses: it overflows, and in SystemVerilog it wraps.
                if (x == long.MinValue && y == -1)
                    return new LogicVec(width, true, modulo ? 0UL : unchecked((ulong)x), 0);

                long result = modulo ? x % y : x / y;
                return new LogicVec(width, true, unchecked((ulong)result), 0);
            }

            BigInteger p = signed ? ToSignedBig(a) : ToUnsignedBig(a);
            BigInteger q = signed ? ToSignedBig(b) : ToUnsignedBig(b);
            BigInteger r = modulo ? BigInteger.Remainder(p, q) : BigInteger.Divide(p, q);
            return FromBigInteger(r, width, signed);
        }

        /// <summary><c>-a</c>: the two's complement of <paramref name="a"/> at its width, or all x if any bit is unknown.</summary>
        public static LogicVec Negate(LogicVec a)
        {
            Require(a);
            if (!a.IsKnown)
                return AllX(a._width, a._signed);

            return Subtract(Fill(Logic4.Zero, a._width, a._signed), a);
        }

        private static bool IsZeroKnown(in LogicVec v)
        {
            for (int i = 0; i < v.Words; i++)
            {
                if (v.AWord(i) != 0)
                    return false;
            }

            return true;
        }

        // -----------------------------------------------------------------
        // Relational: < <= > >=
        // -----------------------------------------------------------------

        private enum Relation { Less, LessOrEqual, Greater, GreaterOrEqual }

        /// <summary><c>a &lt; b</c>, one bit; x if any bit of either is unknown. Signed only when both are.</summary>
        public static LogicVec LessThan(LogicVec a, LogicVec b) => Relate(Relation.Less, a, b);

        /// <summary><c>a &lt;= b</c>.</summary>
        public static LogicVec LessOrEqual(LogicVec a, LogicVec b) => Relate(Relation.LessOrEqual, a, b);

        /// <summary><c>a &gt; b</c>.</summary>
        public static LogicVec GreaterThan(LogicVec a, LogicVec b) => Relate(Relation.Greater, a, b);

        /// <summary><c>a &gt;= b</c>.</summary>
        public static LogicVec GreaterOrEqual(LogicVec a, LogicVec b) => Relate(Relation.GreaterOrEqual, a, b);

        private static LogicVec Relate(Relation relation, LogicVec a, LogicVec b)
        {
            bool signed = Align(ref a, ref b);
            if (!a.IsKnown || !b.IsKnown)
                return BitX;

            int order = CompareKnown(a, b, signed);
            bool holds;

            switch (relation)
            {
                case Relation.Less: holds = order < 0; break;
                case Relation.LessOrEqual: holds = order <= 0; break;
                case Relation.Greater: holds = order > 0; break;
                default: holds = order >= 0; break;
            }

            return holds ? Bit1 : Bit0;
        }

        /// <summary>Orders two known values of one width, as two's complement numbers if <paramref name="signed"/>.</summary>
        private static int CompareKnown(in LogicVec a, in LogicVec b, bool signed)
        {
            int width = a._width;

            if (width <= 64)
            {
                if (signed)
                    return SignExtend64(a._a, width).CompareTo(SignExtend64(b._a, width));

                return a._a.CompareTo(b._a);
            }

            if (signed)
            {
                Logic4 sa = a[width - 1], sb = b[width - 1];
                if (sa != sb)
                    return sa == Logic4.One ? -1 : 1;
            }

            for (int i = a.Words - 1; i >= 0; i--)
            {
                int order = a.AWord(i).CompareTo(b.AWord(i));
                if (order != 0)
                    return order;
            }

            return 0;
        }

        // -----------------------------------------------------------------
        // Equality: == != === !==
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>a == b</c>, one bit. 0 as soon as one bit is known on both sides and differs, x and z
        /// elsewhere notwithstanding; otherwise x if any bit is unknown; otherwise 1. The standard's
        /// rule: x only when the unknown bits could decide the answer (IEEE 1800, 11.4.5).
        /// </summary>
        public static LogicVec Equal(LogicVec a, LogicVec b)
        {
            Align(ref a, ref b);
            bool unknown = false;

            for (int i = 0; i < a.Words; i++)
            {
                ulong eitherUnknown = a.BWord(i) | b.BWord(i);
                if (((a.AWord(i) ^ b.AWord(i)) & ~eitherUnknown) != 0)
                    return Bit0;

                unknown |= eitherUnknown != 0;
            }

            return unknown ? BitX : Bit1;
        }

        /// <summary><c>a != b</c>, one bit: <see cref="Equal"/> inverted, x staying x.</summary>
        public static LogicVec NotEqual(LogicVec a, LogicVec b) => Not(Equal(a, b));

        /// <summary><c>a === b</c>, one bit, never x: 1 only if every bit is in the same state, x and z included.</summary>
        public static LogicVec CaseEqual(LogicVec a, LogicVec b)
        {
            Align(ref a, ref b);

            for (int i = 0; i < a.Words; i++)
            {
                if (a.AWord(i) != b.AWord(i) || a.BWord(i) != b.BWord(i))
                    return Bit0;
            }

            return Bit1;
        }

        /// <summary><c>a !== b</c>, one bit, never x.</summary>
        public static LogicVec CaseNotEqual(LogicVec a, LogicVec b) => Not(CaseEqual(a, b));

        /// <summary>
        /// <c>a ==? b</c>, one bit (IEEE 1800, 11.4.6): an x or z bit of <paramref name="b"/> matches anything;
        /// otherwise as <see cref="Equal"/> -- 0 on a known bit that differs, else x if a bit of
        /// <paramref name="a"/> is x or z where <paramref name="b"/> asks for a value, else 1. What
        /// <c>inside</c> compares a value with a set member by.
        /// </summary>
        public static LogicVec WildcardEqual(LogicVec a, LogicVec b)
        {
            Align(ref a, ref b);
            bool unknown = false;

            for (int i = 0; i < a.Words; i++)
            {
                ulong asked = ~b.BWord(i);
                if (((a.AWord(i) ^ b.AWord(i)) & asked & ~a.BWord(i)) != 0)
                    return Bit0;

                unknown |= (a.BWord(i) & asked) != 0;
            }

            return unknown ? BitX : Bit1;
        }

        /// <summary><c>a !=? b</c>: <see cref="WildcardEqual"/> inverted, x staying x.</summary>
        public static LogicVec WildcardNotEqual(LogicVec a, LogicVec b) => Not(WildcardEqual(a, b));

        // -----------------------------------------------------------------
        // Bit counting: $countones, $onehot, $onehot0, $isunknown (IEEE 1800, 20.9)
        // -----------------------------------------------------------------

        /// <summary><c>$countones(a)</c>: how many bits are 1, as an <c>int</c>; x and z are not counted.</summary>
        public static LogicVec CountOnes(LogicVec a)
        {
            Require(a);
            int ones = 0;
            for (int i = 0; i < a.Words; i++)
                ones += PopCount(a.AWord(i) & ~a.BWord(i));
            return FromInt64(ones, 32, signed: true);
        }

        /// <summary><c>$onehot(a)</c>: 1 if exactly one bit is 1, x and z counting as not 1.</summary>
        public static LogicVec OneHot(LogicVec a) => CountOf(a) == 1 ? Bit1 : Bit0;

        /// <summary><c>$onehot0(a)</c>: 1 if at most one bit is 1.</summary>
        public static LogicVec OneHot0(LogicVec a) => CountOf(a) <= 1 ? Bit1 : Bit0;

        /// <summary><c>$isunknown(a)</c>: 1 if any bit is x or z.</summary>
        public static LogicVec IsUnknown(LogicVec a)
        {
            Require(a);
            return a.IsKnown ? Bit0 : Bit1;
        }

        private static int CountOf(LogicVec a)
        {
            Require(a);
            int ones = 0;
            for (int i = 0; i < a.Words; i++)
                ones += PopCount(a.AWord(i) & ~a.BWord(i));
            return ones;
        }

        // -----------------------------------------------------------------
        // Shifts: << >> <<< >>>
        // -----------------------------------------------------------------
        //
        // The result is the left operand's width and signedness. The shift amount is always read as
        // unsigned, and if any bit of it is x or z the whole result is x. The vacated bits fill with 0,
        // except that >>> on a signed value fills with copies of its sign bit -- x and z included. On
        // an unsigned value >>> is the same as >>: the course's own ALU shifts unsigned operands with
        // >>>, which the standard makes a logical shift (Docs/architecture-5.md, section 7).

        /// <summary><c>a &lt;&lt; amount</c>.</summary>
        public static LogicVec ShiftLeft(LogicVec a, LogicVec amount) =>
            amount.IsKnown ? ShiftLeft(a, ShiftAmount(amount)) : AllX(a._width, a._signed);

        /// <summary><c>a &gt;&gt; amount</c>, filling with 0.</summary>
        public static LogicVec ShiftRight(LogicVec a, LogicVec amount) =>
            amount.IsKnown ? ShiftRight(a, ShiftAmount(amount), arithmetic: false) : AllX(a._width, a._signed);

        /// <summary><c>a &lt;&lt;&lt; amount</c>, which is <c>&lt;&lt;</c>.</summary>
        public static LogicVec ArithmeticShiftLeft(LogicVec a, LogicVec amount) => ShiftLeft(a, amount);

        /// <summary><c>a &gt;&gt;&gt; amount</c>: copies of the sign bit fill in if <paramref name="a"/> is signed, 0 if not.</summary>
        public static LogicVec ArithmeticShiftRight(LogicVec a, LogicVec amount) =>
            amount.IsKnown ? ShiftRight(a, ShiftAmount(amount), arithmetic: a._signed) : AllX(a._width, a._signed);

        /// <summary>A known shift amount as a count, any count past the widest value standing for "everything".</summary>
        private static int ShiftAmount(in LogicVec amount)
        {
            Require(amount);
            for (int i = 1; i < amount.Words; i++)
            {
                if (amount.AWord(i) != 0)
                    return int.MaxValue;
            }

            ulong count = amount.AWord(0);
            return count > MaxWidth ? int.MaxValue : (int)count;
        }

        /// <summary><c>a &lt;&lt; count</c> for a count already known.</summary>
        public static LogicVec ShiftLeft(LogicVec a, int count)
        {
            Require(a);
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "A shift count cannot be negative.");

            if (count >= a._width)
                return Fill(Logic4.Zero, a._width, a._signed);

            if (a._width <= 64)
                return new LogicVec(a._width, a._signed, a._a << count, a._b << count);

            return ShiftWide(a, count, left: true, fillA: 0, fillB: 0);
        }

        /// <summary><c>a &gt;&gt; count</c> or, if <paramref name="arithmetic"/>, a signed <c>&gt;&gt;&gt;</c>, for a count already known.</summary>
        public static LogicVec ShiftRight(LogicVec a, int count, bool arithmetic)
        {
            Require(a);
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "A shift count cannot be negative.");

            Logic4 sign = arithmetic ? a[a._width - 1] : Logic4.Zero;
            ulong fillA = ((int)sign & 1) != 0 ? ulong.MaxValue : 0UL;
            ulong fillB = ((int)sign & 2) != 0 ? ulong.MaxValue : 0UL;

            if (count >= a._width)
                return Fill(sign, a._width, a._signed);

            if (a._width <= 64)
            {
                ulong all = Mask64(a._width);
                ulong vacated = all & ~(all >> count);
                return new LogicVec(a._width, a._signed,
                    (a._a >> count) | (fillA & vacated), (a._b >> count) | (fillB & vacated));
            }

            return ShiftWide(a, count, left: false, fillA, fillB);
        }

        /// <summary>A shift of a value over 64 bits, by whole words and then by the bits left over.</summary>
        private static LogicVec ShiftWide(in LogicVec a, int count, bool left, ulong fillA, ulong fillB)
        {
            int n = a.Words;
            int width = a._width;
            ulong[] planes = NewPlanes(width);

            for (int plane = 0; plane < 2; plane++)
            {
                ulong fill = plane == 0 ? fillA : fillB;
                int offset = plane * n;

                // Read the source a bit at a time through whole words: bit i of the result is bit
                // i - count (left) or i + count (right) of the source, or the fill outside it.
                for (int word = 0; word < n; word++)
                {
                    ulong result = 0;
                    for (int bit = 0; bit < 64; bit++)
                    {
                        int index = word * 64 + bit;
                        if (index >= width)
                            break;

                        int from = left ? index - count : index + count;
                        ulong value;

                        if (from < 0)
                            value = 0;
                        else if (from >= width)
                            value = fill & 1;
                        else
                            value = (a._wide[offset + (from >> 6)] >> (from & 63)) & 1;

                        result |= value << bit;
                    }

                    planes[offset + word] = result;
                }
            }

            return new LogicVec(width, a._signed, planes);
        }

        // -----------------------------------------------------------------
        // Concatenation, replication and selects
        // -----------------------------------------------------------------

        /// <summary><c>{high, low}</c>: unsigned, as wide as both, <paramref name="high"/> in the top bits.</summary>
        public static LogicVec Concat(LogicVec high, LogicVec low)
        {
            Require(high);
            Require(low);

            int width = high._width + low._width;
            CheckWidth(width);

            if (width <= 64)
                return new LogicVec(width, false, (high._a << low._width) | low._a, (high._b << low._width) | low._b);

            var bits = new BitWriter(width);
            CopyInto(ref bits, low, 0);
            CopyInto(ref bits, high, low._width);
            return bits.ToValue(false);
        }

        /// <summary>
        /// <c>{v0, v1, ...}</c>, the first the most significant: unsigned, as wide as all of them.
        /// A span rather than <c>params</c>, so the engine can concatenate without an array a call.
        /// </summary>
        public static LogicVec Concat(ReadOnlySpan<LogicVec> mostSignificantFirst)
        {
            if (mostSignificantFirst.Length == 0)
                throw new ArgumentException("A concatenation needs at least one part.", nameof(mostSignificantFirst));

            LogicVec result = mostSignificantFirst[0].AsUnsigned();
            for (int i = 1; i < mostSignificantFirst.Length; i++)
                result = Concat(result, mostSignificantFirst[i]);

            return result;
        }

        /// <summary><c>{count{v}}</c>: <paramref name="v"/> repeated, unsigned.</summary>
        public static LogicVec Replicate(LogicVec v, int count)
        {
            Require(v);
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), count, "A replication repeats at least once.");

            CheckWidth((int)Math.Min((long)v._width * count, MaxWidth + 1L));

            LogicVec result = v.AsUnsigned();
            for (int i = 1; i < count; i++)
                result = Concat(result, v);

            return result;
        }

        /// <summary>
        /// The <paramref name="width"/> bits starting at bit <paramref name="lowest"/>, unsigned, as a
        /// part-select reads them: any bit outside the value is x.
        /// </summary>
        public LogicVec Slice(int lowest, int width)
        {
            Require(this);
            CheckWidth(width);

            if (lowest >= 0 && (long)lowest + width <= _width && width <= 64)
            {
                ulong a = ReadWord(lowest, isB: false);
                ulong b = ReadWord(lowest, isB: true);
                return new LogicVec(width, false, a, b);
            }

            var bits = new BitWriter(width);
            for (int i = 0; i < width; i++)
                bits.Set(i, this[lowest + i]);

            return bits.ToValue(false);
        }

        /// <summary>
        /// <c>v[index]</c>, one bit: x if the index has an unknown bit, is negative, or is past the
        /// value's width.
        /// </summary>
        public LogicVec Bit(LogicVec index)
        {
            Require(this);
            Require(index);

            if (!index.IsKnown)
                return BitX;

            if (index._signed && index[index._width - 1] == Logic4.One)
                return BitX;

            int at = ShiftAmount(index);
            return at >= _width ? BitX : OneBit(this[at]);
        }

        /// <summary>
        /// <c>condition ? whenTrue : whenFalse</c>. A known true or false picks its operand; an x or z
        /// condition gives every bit both operands agree on, and x where they differ or are unknown
        /// (IEEE 1800, 11.4.11). The width is the wider operand's; signed only when both are.
        /// </summary>
        public static LogicVec Conditional(LogicVec condition, LogicVec whenTrue, LogicVec whenFalse)
        {
            Logic4 truth = Truth(condition);
            bool signed = Align(ref whenTrue, ref whenFalse);

            if (truth == Logic4.One)
                return whenTrue._signed == signed ? whenTrue : new LogicVec(whenTrue, signed);

            if (truth == Logic4.Zero)
                return whenFalse._signed == signed ? whenFalse : new LogicVec(whenFalse, signed);

            int width = whenTrue._width;

            if (width <= 64)
            {
                ulong agree = ~(whenTrue._b | whenFalse._b) & ~(whenTrue._a ^ whenFalse._a);
                return new LogicVec(width, signed, (whenTrue._a & agree) | ~agree, ~agree);
            }

            int n = whenTrue.Words;
            ulong[] planes = NewPlanes(width);
            for (int i = 0; i < n; i++)
            {
                ulong agree = ~(whenTrue._wide[n + i] | whenFalse._wide[n + i]) & ~(whenTrue._wide[i] ^ whenFalse._wide[i]);
                planes[i] = (whenTrue._wide[i] & agree) | ~agree;
                planes[n + i] = ~agree;
            }

            return new LogicVec(width, signed, planes);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>The 64 bits of one plane starting at <paramref name="lowest"/>, for a range known to be inside the value.</summary>
        private ulong ReadWord(int lowest, bool isB)
        {
            int word = lowest >> 6;
            int shift = lowest & 63;
            ulong low = isB ? BWord(word) : AWord(word);

            if (shift == 0)
                return low;

            ulong high = isB ? BWord(word + 1) : AWord(word + 1);
            return (low >> shift) | (high << (64 - shift));
        }

        private static void CopyInto(ref BitWriter bits, in LogicVec source, int offset)
        {
            for (int i = 0; i < source._width; i++)
                bits.Set(offset + i, source[i]);
        }

        /// <summary>A known value read as an unsigned number, for the wide paths.</summary>
        internal static BigInteger ToUnsignedBig(in LogicVec v)
        {
            var bytes = new byte[v.Words * 8 + 1];
            for (int i = 0; i < v.Words; i++)
            {
                ulong word = v.AWord(i);
                for (int k = 0; k < 8; k++)
                    bytes[i * 8 + k] = (byte)(word >> (8 * k));
            }

            // The extra zero byte keeps BigInteger from reading the top bit as a sign.
            return new BigInteger(bytes);
        }

        /// <summary>A known value read as a two's complement number of its width.</summary>
        internal static BigInteger ToSignedBig(in LogicVec v)
        {
            BigInteger unsigned = ToUnsignedBig(v);
            return v[v._width - 1] == Logic4.One ? unsigned - (BigInteger.One << v._width) : unsigned;
        }

        /// <summary>A number modulo 2^width, as a known value.</summary>
        internal static LogicVec FromBigInteger(BigInteger value, int width, bool signed)
        {
            BigInteger modulus = BigInteger.One << width;
            BigInteger reduced = BigInteger.Remainder(value, modulus);
            if (reduced.Sign < 0)
                reduced += modulus;

            byte[] bytes = reduced.ToByteArray();

            if (width <= 64)
            {
                ulong word = 0;
                for (int k = 0; k < Math.Min(8, bytes.Length); k++)
                    word |= (ulong)bytes[k] << (8 * k);

                return new LogicVec(width, signed, word, 0);
            }

            int n = WordsFor(width);
            ulong[] planes = NewPlanes(width);
            for (int k = 0; k < bytes.Length && k < n * 8; k++)
                planes[k >> 3] |= (ulong)bytes[k] << (8 * (k & 7));

            return new LogicVec(width, signed, planes);
        }
    }
}
