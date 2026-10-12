using System;

namespace BitSorter.SystemVerilog
{
    /// <summary>
    /// A SystemVerilog 4-state value of any width: every bit 0, 1, x or z, with a width and a
    /// signedness. Immutable.
    /// </summary>
    /// <remarks>
    /// **Two bit-planes**, as the simulators' own interface (VPI) stores a value: the a-plane holds a
    /// bit's value and the b-plane says it is unknown, so 0 is (0, 0), 1 is (1, 0), z is (0, 1) and x
    /// is (1, 1). Most of what an operator needs is a few word-wide bit operations on the two planes,
    /// whatever the width, rather than a loop over bits.
    ///
    /// **Up to 64 bits nothing is allocated**: both planes sit in the struct as two words. That is
    /// the engine's hot path -- a processor in the course's style evaluates operators millions of
    /// times in a run -- and Tools/ci holds every operator on it to zero allocated bytes. A wider
    /// value keeps its planes in one array, words 0 to n-1 the a-plane and n to 2n-1 the b-plane.
    /// The array is never written after the value is made, so copies of a value share it safely.
    /// Wide values are rare in the course: its one is a 65-bit carry chain.
    ///
    /// **Canonical form**: bits above the width are 0 in both planes, so two equal values have equal
    /// words and nothing compares or hashes stray high bits.
    ///
    /// **Operators work at their operands' width.** SystemVerilog sizes an expression from its
    /// context -- <c>r = a + b</c> adds at the widest of r, a and b -- and that is the elaborator's
    /// job: it extends the operands to the context width first, with <see cref="Extend"/>. Here, a
    /// binary operator extends both operands to the wider of the two, sign-extending only when both
    /// are signed, which is the standard's rule for an expression's signedness (IEEE 1800, 11.8.1).
    ///
    /// <c>default(LogicVec)</c> has width 0 and is not a value: <see cref="IsEmpty"/> says so, and
    /// operators refuse it.
    /// </remarks>
    public readonly partial struct LogicVec : IEquatable<LogicVec>
    {
        /// <summary>The widest value there may be: a million bits, far past any design, and a bound for hostile input.</summary>
        public const int MaxWidth = 1 << 20;

        private readonly int _width;
        private readonly bool _signed;

        /// <summary>Up to 64 bits, the a-plane.</summary>
        private readonly ulong _a;

        /// <summary>Up to 64 bits, the b-plane.</summary>
        private readonly ulong _b;

        /// <summary>Over 64 bits, both planes: a-plane words first, then b-plane words. Never written after construction.</summary>
        private readonly ulong[] _wide;

        /// <summary>A value of up to 64 bits. Bits above the width are cleared.</summary>
        private LogicVec(int width, bool signed, ulong a, ulong b)
        {
            ulong mask = Mask64(width);
            _width = width;
            _signed = signed;
            _a = a & mask;
            _b = b & mask;
            _wide = null;
        }

        /// <summary>A value of over 64 bits, taking ownership of <paramref name="planes"/> and clearing its bits above the width.</summary>
        private LogicVec(int width, bool signed, ulong[] planes)
        {
            int n = WordsFor(width);
            ulong top = TopMask(width);
            planes[n - 1] &= top;
            planes[2 * n - 1] &= top;

            _width = width;
            _signed = signed;
            _a = 0;
            _b = 0;
            _wide = planes;
        }

        /// <summary>The same planes under another signedness. Shares a wide array, which is safe: it is never written.</summary>
        private LogicVec(LogicVec bits, bool signed)
        {
            _width = bits._width;
            _signed = signed;
            _a = bits._a;
            _b = bits._b;
            _wide = bits._wide;
        }

        /// <summary>How many bits.</summary>
        public int Width => _width;

        /// <summary>Whether the bits read as a two's complement number, as a <c>signed</c> declaration makes them.</summary>
        public bool IsSigned => _signed;

        /// <summary>True only of <c>default(LogicVec)</c>, which is not a value.</summary>
        public bool IsEmpty => _width == 0;

        /// <summary>Whether every bit is a known 0 or 1.</summary>
        public bool IsKnown
        {
            get
            {
                if (_wide == null)
                    return _b == 0;

                int n = Words;
                for (int i = 0; i < n; i++)
                {
                    if (_wide[n + i] != 0)
                        return false;
                }

                return true;
            }
        }

        /// <summary>Whether every bit is x.</summary>
        public bool IsAllX => AllBitsAre(Logic4.X);

        /// <summary>Whether every bit is z.</summary>
        public bool IsAllZ => AllBitsAre(Logic4.Z);

        /// <summary>One bit, counted from 0 at the least significant end. Outside the width it is x, as a select reads it.</summary>
        public Logic4 this[int index]
        {
            get
            {
                if (index < 0 || index >= _width)
                    return Logic4.X;

                int word = index >> 6;
                int shift = index & 63;
                int a = (int)((AWord(word) >> shift) & 1);
                int b = (int)((BWord(word) >> shift) & 1);
                return (Logic4)(a | (b << 1));
            }
        }

        // -----------------------------------------------------------------
        // Making values
        // -----------------------------------------------------------------

        /// <summary>A known value from the low bits of <paramref name="value"/>; wider than 64 bits, the rest are 0.</summary>
        public static LogicVec FromUInt64(ulong value, int width, bool signed = false)
        {
            CheckWidth(width);

            if (width <= 64)
                return new LogicVec(width, signed, value, 0);

            ulong[] planes = NewPlanes(width);
            planes[0] = value;
            return new LogicVec(width, signed, planes);
        }

        /// <summary>A known value from <paramref name="value"/> in two's complement; wider than 64 bits, its sign is extended.</summary>
        public static LogicVec FromInt64(long value, int width, bool signed = true)
        {
            CheckWidth(width);

            if (width <= 64)
                return new LogicVec(width, signed, unchecked((ulong)value), 0);

            ulong[] planes = NewPlanes(width);
            int n = WordsFor(width);
            ulong fill = value < 0 ? ulong.MaxValue : 0UL;
            planes[0] = unchecked((ulong)value);
            for (int i = 1; i < n; i++)
                planes[i] = fill;

            return new LogicVec(width, signed, planes);
        }

        /// <summary>Every bit the same: all 0, all 1, all x or all z.</summary>
        public static LogicVec Fill(Logic4 bit, int width, bool signed = false)
        {
            CheckWidth(width);

            ulong a = ((int)bit & 1) != 0 ? ulong.MaxValue : 0UL;
            ulong b = ((int)bit & 2) != 0 ? ulong.MaxValue : 0UL;

            if (width <= 64)
                return new LogicVec(width, signed, a, b);

            ulong[] planes = NewPlanes(width);
            int n = WordsFor(width);
            for (int i = 0; i < n; i++)
            {
                planes[i] = a;
                planes[n + i] = b;
            }

            return new LogicVec(width, signed, planes);
        }

        /// <summary>All x: what an operator gives when an operand it needs is unknown.</summary>
        public static LogicVec AllX(int width, bool signed = false) => Fill(Logic4.X, width, signed);

        /// <summary>
        /// A value from its binary digits, most significant first, one bit per digit: 0, 1, x, z or ?
        /// (which is z). Underscores are skipped. The width is the number of digits.
        /// </summary>
        /// <remarks>Plain digits, not a literal: <see cref="Literal.Parse"/> reads <c>4'b10xz</c>.</remarks>
        public static LogicVec FromBinary(string digits, bool signed = false)
        {
            if (digits == null)
                throw new ArgumentNullException(nameof(digits));

            int width = 0;
            foreach (char c in digits)
            {
                if (c != '_')
                    width++;
            }

            CheckWidth(width);

            var bits = new BitWriter(width);
            int index = width;

            foreach (char c in digits)
            {
                if (c == '_')
                    continue;

                if (!Logic4Text.TryParse(c, out Logic4 bit))
                    throw new FormatException($"'{c}' is not a binary digit (0, 1, x, z or ?).");

                bits.Set(--index, bit);
            }

            return bits.ToValue(signed);
        }

        // -----------------------------------------------------------------
        // Reading values
        // -----------------------------------------------------------------

        /// <summary>
        /// The value as an unsigned number, if every bit is known and it fits in 64 bits.
        /// </summary>
        public bool TryGetUInt64(out ulong value)
        {
            value = 0;
            if (IsEmpty || !IsKnown)
                return false;

            for (int i = 1; i < Words; i++)
            {
                if (AWord(i) != 0)
                    return false;
            }

            value = AWord(0);
            return true;
        }

        /// <summary>
        /// The value as a two's complement number of its full width, if every bit is known and it fits
        /// in 64 bits. A value that is not <see cref="IsSigned"/> still reads its top bit as the sign here.
        /// </summary>
        public bool TryGetInt64(out long value)
        {
            value = 0;
            if (IsEmpty || !IsKnown)
                return false;

            if (_width <= 64)
            {
                value = SignExtend64(_a, _width);
                return true;
            }

            // Wider: it fits if every bit from 63 up equals the sign bit.
            Logic4 sign = this[_width - 1];
            for (int i = 63; i < _width; i++)
            {
                if (this[i] != sign)
                    return false;
            }

            value = unchecked((long)AWord(0));
            return true;
        }

        // -----------------------------------------------------------------
        // Width and signedness
        // -----------------------------------------------------------------

        /// <summary>
        /// The value at another width: the low bits kept when narrower, and when wider the new top bits
        /// filled with copies of the old top bit if <paramref name="signExtend"/> -- x and z included,
        /// as the standard extends them -- or with 0 if not. The signedness is kept.
        /// </summary>
        public LogicVec Extend(int width, bool signExtend)
        {
            Require(this);
            CheckWidth(width);

            if (width == _width)
                return this;

            if (width < _width)
                return Truncate(width);

            Logic4 top = this[_width - 1];
            ulong fillA = signExtend && ((int)top & 1) != 0 ? ulong.MaxValue : 0UL;
            ulong fillB = signExtend && ((int)top & 2) != 0 ? ulong.MaxValue : 0UL;

            if (width <= 64)
            {
                ulong high = Mask64(width) & ~Mask64(_width);
                return new LogicVec(width, _signed, _a | (fillA & high), _b | (fillB & high));
            }

            int n = WordsFor(width);
            int m = Words;
            ulong[] planes = NewPlanes(width);
            ulong aboveOld = ~TopMask(_width);

            for (int i = 0; i < n; i++)
            {
                if (i < m)
                {
                    planes[i] = AWord(i);
                    planes[n + i] = BWord(i);

                    if (i == m - 1)
                    {
                        planes[i] |= fillA & aboveOld;
                        planes[n + i] |= fillB & aboveOld;
                    }
                }
                else
                {
                    planes[i] = fillA;
                    planes[n + i] = fillB;
                }
            }

            return new LogicVec(width, _signed, planes);
        }

        /// <summary>The value at another width, extended by its own signedness: sign-extended if signed, else with 0.</summary>
        public LogicVec Resize(int width) => Extend(width, _signed);

        /// <summary>The same bits read as signed, as <c>$signed</c> does.</summary>
        public LogicVec AsSigned() => new LogicVec(this, true);

        /// <summary>The same bits read as unsigned, as <c>$unsigned</c> does.</summary>
        public LogicVec AsUnsigned() => new LogicVec(this, false);

        private LogicVec Truncate(int width)
        {
            if (width <= 64)
                return new LogicVec(width, _signed, AWord(0), BWord(0));

            int n = WordsFor(width);
            ulong[] planes = NewPlanes(width);
            for (int i = 0; i < n; i++)
            {
                planes[i] = AWord(i);
                planes[n + i] = BWord(i);
            }

            return new LogicVec(width, _signed, planes);
        }

        // -----------------------------------------------------------------
        // Equality
        // -----------------------------------------------------------------

        /// <summary>Same width, same signedness, and every bit the same state, x and z included.</summary>
        public bool Equals(LogicVec other)
        {
            if (_width != other._width || _signed != other._signed)
                return false;

            for (int i = 0; i < Words; i++)
            {
                if (AWord(i) != other.AWord(i) || BWord(i) != other.BWord(i))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) => obj is LogicVec other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _width * 397 ^ (_signed ? 1 : 0);
                for (int i = 0; i < Words; i++)
                {
                    hash = (hash * 397) ^ AWord(i).GetHashCode();
                    hash = (hash * 397) ^ BWord(i).GetHashCode();
                }

                return hash;
            }
        }

        public static bool operator ==(LogicVec left, LogicVec right) => left.Equals(right);
        public static bool operator !=(LogicVec left, LogicVec right) => !left.Equals(right);

        /// <summary>A binary literal, as SystemVerilog writes it: <c>4'b10xz</c>, or <c>4'sb10xz</c> when signed.</summary>
        public override string ToString() => IsEmpty ? "(no value)" : ToLiteral(Radix.Binary);

        // -----------------------------------------------------------------
        // The planes, for the operators
        // -----------------------------------------------------------------

        /// <summary>How many 64-bit words each plane takes.</summary>
        internal int Words => WordsFor(_width);

        /// <summary>A word of the a-plane, or 0 past the width's words.</summary>
        internal ulong AWord(int i) =>
            _wide == null ? (i == 0 ? _a : 0UL) : (i < Words ? _wide[i] : 0UL);

        /// <summary>A word of the b-plane, or 0 past the width's words.</summary>
        internal ulong BWord(int i) =>
            _wide == null ? (i == 0 ? _b : 0UL) : (i < Words ? _wide[Words + i] : 0UL);

        internal static int WordsFor(int width) => (width + 63) >> 6;

        /// <summary>The bits of the top word that are inside the width.</summary>
        internal static ulong TopMask(int width)
        {
            int rest = width & 63;
            return rest == 0 ? ulong.MaxValue : (1UL << rest) - 1;
        }

        /// <summary>The low <paramref name="width"/> bits of a word, for a width of 64 or fewer.</summary>
        internal static ulong Mask64(int width) => width >= 64 ? ulong.MaxValue : (1UL << width) - 1;

        /// <summary>A word holding a <paramref name="width"/>-bit two's complement number, read as a signed 64-bit one.</summary>
        internal static long SignExtend64(ulong value, int width)
        {
            if (width >= 64)
                return unchecked((long)value);

            int shift = 64 - width;
            return unchecked((long)(value << shift)) >> shift;
        }

        internal static ulong[] NewPlanes(int width) => new ulong[2 * WordsFor(width)];

        internal static void CheckWidth(int width)
        {
            if (width < 1 || width > MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width), width, $"A value's width must be from 1 to {MaxWidth}.");
        }

        internal static void Require(in LogicVec value)
        {
            if (value.IsEmpty)
                throw new ArgumentException("default(LogicVec) is not a value: it has no width.");
        }

        private bool AllBitsAre(Logic4 bit)
        {
            if (IsEmpty)
                return false;

            ulong a = ((int)bit & 1) != 0 ? ulong.MaxValue : 0UL;
            ulong b = ((int)bit & 2) != 0 ? ulong.MaxValue : 0UL;
            int n = Words;

            for (int i = 0; i < n; i++)
            {
                ulong mask = i == n - 1 ? TopMask(_width) : ulong.MaxValue;
                if (AWord(i) != (a & mask) || BWord(i) != (b & mask))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Builds a value bit by bit, for the paths where word operations would be more code than they
        /// save: literals, concatenations of awkward widths, selects that run off the end. Allocates
        /// only when the value is wider than 64 bits.
        /// </summary>
        internal struct BitWriter
        {
            private readonly int _width;
            private ulong _a;
            private ulong _b;
            private readonly ulong[] _planes;

            public BitWriter(int width)
            {
                _width = width;
                _a = 0;
                _b = 0;
                _planes = width > 64 ? NewPlanes(width) : null;
            }

            public void Set(int index, Logic4 bit)
            {
                ulong one = 1UL << (index & 63);
                bool a = ((int)bit & 1) != 0;
                bool b = ((int)bit & 2) != 0;

                if (_planes == null)
                {
                    _a = a ? _a | one : _a & ~one;
                    _b = b ? _b | one : _b & ~one;
                    return;
                }

                int n = WordsFor(_width);
                int word = index >> 6;
                _planes[word] = a ? _planes[word] | one : _planes[word] & ~one;
                _planes[n + word] = b ? _planes[n + word] | one : _planes[n + word] & ~one;
            }

            public LogicVec ToValue(bool signed) =>
                _planes == null ? new LogicVec(_width, signed, _a, _b) : new LogicVec(_width, signed, _planes);
        }

        /// <summary>A value from two words, for the operators' fast path.</summary>
        internal static LogicVec Narrow(int width, bool signed, ulong a, ulong b) => new LogicVec(width, signed, a, b);

        /// <summary>A value from planes the caller filled, for the operators' wide path.</summary>
        internal static LogicVec Wide(int width, bool signed, ulong[] planes) => new LogicVec(width, signed, planes);

        /// <summary>Up to 64 bits, the a-plane word, without a bounds check.</summary>
        internal ulong A => _a;

        /// <summary>Up to 64 bits, the b-plane word, without a bounds check.</summary>
        internal ulong B => _b;

        /// <summary>Over 64 bits, the planes array; null otherwise.</summary>
        internal ulong[] Planes => _wide;
    }
}
