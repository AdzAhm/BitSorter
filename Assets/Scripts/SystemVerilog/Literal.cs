using System;

namespace BitSorter.SystemVerilog
{
    /// <summary>What reading a number literal gave: a value, or why not, and anything worth a warning.</summary>
    public readonly struct LiteralResult
    {
        public readonly LogicVec Value;

        /// <summary>Why the text is not a literal, in plain words; null when it is one.</summary>
        public readonly string Error;

        /// <summary>Something legal but suspicious -- digits truncated away -- or null.</summary>
        public readonly string Warning;

        /// <summary>
        /// An unbased, unsized literal -- <c>'0</c>, <c>'1</c>, <c>'x</c>, <c>'z</c> -- which fills
        /// whatever width its context gives it. <see cref="Value"/> is then its one bit.
        /// </summary>
        public readonly bool FillsContext;

        internal LiteralResult(LogicVec value, string error, string warning, bool fillsContext)
        {
            Value = value;
            Error = error;
            Warning = warning;
            FillsContext = fillsContext;
        }

        public bool Ok => Error == null;

        internal static LiteralResult Fail(string error) => new LiteralResult(default, error, null, false);
    }

    /// <summary>
    /// Reads SystemVerilog number literals: <c>42</c>, <c>8'hA5</c>, <c>4'b1x0z</c>, <c>'d7</c>,
    /// <c>16'sd5</c>, <c>'1</c>.
    /// </summary>
    /// <remarks>
    /// The rules are IEEE 1800, 5.7.1:
    /// - A plain decimal number is signed and 32 bits wide.
    /// - A based literal (<c>'b</c>, <c>'o</c>, <c>'d</c>, <c>'h</c>) is unsigned unless it says
    ///   <c>s</c>, and 32 bits wide unless it gives a size.
    /// - Digits may be x or z (or <c>?</c>, another way of writing z), and underscores may separate
    ///   them anywhere but first.
    /// - A literal with fewer digits than its size is padded on the left: with x or z if its leftmost
    ///   digit is x or z, otherwise with 0. So <c>8'hx</c> is eight x bits and <c>8'h1</c> is 1.
    /// - One with more is truncated to its size; that is legal but lost something, so it warns
    ///   when a truncated bit was not 0.
    ///
    /// Text arrives as one token, without spaces; the lexer joins <c>8 'h FF</c>, which the standard
    /// allows, before it gets here.
    /// </remarks>
    public static class Literal
    {
        /// <summary>The width of an unsized literal, at least.</summary>
        public const int UnsizedWidth = 32;

        public static LiteralResult Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return LiteralResult.Fail("There is no number here.");

            int quote = text.IndexOf('\'');

            if (quote < 0)
                return PlainDecimal(text);

            string size = text.Substring(0, quote);
            string rest = text.Substring(quote + 1);

            if (size.Length == 0 && rest.Length == 1 && Logic4Text.TryParse(rest[0], out Logic4 fill) && rest[0] != '?')
                return new LiteralResult(LogicVec.OneBit(fill), null, null, fillsContext: true);

            int width = UnsizedWidth;
            bool sized = size.Length > 0;

            if (sized)
            {
                if (!TryDigits(size, out string sizeDigits) || !int.TryParse(sizeDigits, out width))
                    return LiteralResult.Fail($"'{Shown(size)}' is not a size: a size is a positive whole number.");

                if (width < 1)
                    return LiteralResult.Fail("A literal's size must be at least 1.");

                if (width > LogicVec.MaxWidth)
                    return LiteralResult.Fail($"A literal of {width} bits is wider than BitSorter allows ({LogicVec.MaxWidth}).");
            }

            bool signed = false;
            if (rest.Length > 0 && (rest[0] == 's' || rest[0] == 'S'))
            {
                signed = true;
                rest = rest.Substring(1);
            }

            if (rest.Length == 0)
                return LiteralResult.Fail("A literal's apostrophe must be followed by a base: b, o, d or h.");

            char baseChar = char.ToLowerInvariant(rest[0]);
            string digits = rest.Substring(1);

            if (digits.Length == 0)
                return LiteralResult.Fail($"The literal '{Shown(text)}' has a base but no digits.");

            if (digits[0] == '_')
                return LiteralResult.Fail("A number may not start with an underscore.");

            digits = digits.Replace("_", string.Empty);

            switch (baseChar)
            {
                case 'b': return Based(text, digits, 1, width, sized, signed);
                case 'o': return Based(text, digits, 3, width, sized, signed);
                case 'h': return Based(text, digits, 4, width, sized, signed);
                case 'd': return BasedDecimal(text, digits, width, sized, signed);
                default:
                    return LiteralResult.Fail($"'{rest[0]}' is not a base: a literal's base is b, o, d or h.");
            }
        }

        /// <summary>A number with no apostrophe: decimal, signed, 32 bits -- wider only if it cannot fit.</summary>
        private static LiteralResult PlainDecimal(string text)
        {
            if (!TryDigits(text, out string digits))
                return LiteralResult.Fail($"'{Shown(text)}' is not a number.");

            if (!DecimalNumber.TryRead(digits, out DecimalNumber value))
                return LiteralResult.Fail($"The number {Shown(text)} is wider than BitSorter allows.");

            int needed = value.Bits + 1;
            if (needed <= UnsizedWidth)
                return new LiteralResult(value.ToValue(UnsizedWidth, true), null, null, false);

            if (needed > LogicVec.MaxWidth)
                return LiteralResult.Fail($"The number {Shown(text)} is wider than BitSorter allows.");

            return new LiteralResult(value.ToValue(needed, true), null,
                $"The unsized number {Shown(text)} needs {needed} bits; it is wider than the usual {UnsizedWidth}.", false);
        }

        /// <summary>Binary, octal or hex digits, each worth <paramref name="bitsPerDigit"/> bits.</summary>
        private static LiteralResult Based(string text, string digits, int bitsPerDigit, int width, bool sized, bool signed)
        {
            long bitCount = (long)digits.Length * bitsPerDigit;
            if (bitCount > LogicVec.MaxWidth)
                return LiteralResult.Fail($"The literal '{Shown(text)}' is wider than BitSorter allows.");

            // Bits from the least significant up, one entry a bit.
            var bits = new Logic4[bitCount];

            for (int d = 0; d < digits.Length; d++)
            {
                char c = digits[digits.Length - 1 - d];
                Logic4 whole;
                int value;

                if (Logic4Text.TryParse(c, out whole) && !whole.IsKnown())
                {
                    value = -1;
                }
                else
                {
                    value = DigitValue(c);
                    if (value < 0 || value >= (1 << bitsPerDigit))
                        return LiteralResult.Fail($"'{c}' is not a digit of a {BaseName(bitsPerDigit)} literal.");
                }

                for (int b = 0; b < bitsPerDigit; b++)
                    bits[d * bitsPerDigit + b] = value < 0 ? whole : (((value >> b) & 1) != 0 ? Logic4.One : Logic4.Zero);
            }

            if (!sized)
                width = (int)Math.Max(UnsizedWidth, bitCount);

            return Fit(text, bits, width, signed);
        }

        /// <summary>
        /// Decimal digits after <c>'d</c>: a whole number, or a single x or z standing for every bit.
        /// </summary>
        private static LiteralResult BasedDecimal(string text, string digits, int width, bool sized, bool signed)
        {
            if (digits.Length == 1 && Logic4Text.TryParse(digits[0], out Logic4 fill) && !fill.IsKnown())
                return new LiteralResult(LogicVec.Fill(fill, width, signed), null, null, false);

            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9')
                    return LiteralResult.Fail(
                        $"'{digits[i]}' is not a decimal digit. A decimal literal is a whole number, or a single x or z.");
            }

            if (!DecimalNumber.TryRead(digits, out DecimalNumber value))
                return LiteralResult.Fail($"The literal '{Shown(text)}' is wider than BitSorter allows.");
            int needed = Math.Max(1, value.Bits);

            if (!sized)
                width = Math.Max(UnsizedWidth, needed);

            if (needed > LogicVec.MaxWidth)
                return LiteralResult.Fail($"The literal '{Shown(text)}' is wider than BitSorter allows.");

            string warning = needed > width
                ? $"The literal '{Shown(text)}' needs {needed} bits but is sized {width}; its top bits are lost."
                : null;

            return new LiteralResult(value.ToValue(width, signed), null, warning, false);
        }

        /// <summary>Pads or truncates the bits to the width, by the standard's rule, and builds the value.</summary>
        private static LiteralResult Fit(string text, Logic4[] bits, int width, bool signed)
        {
            Logic4 leftmost = bits[bits.Length - 1];
            Logic4 pad = leftmost.IsKnown() ? Logic4.Zero : leftmost;
            bool lost = false;

            for (int i = width; i < bits.Length; i++)
                lost |= bits[i] != Logic4.Zero;

            var writer = new LogicVec.BitWriter(width);
            for (int i = 0; i < width; i++)
                writer.Set(i, i < bits.Length ? bits[i] : pad);

            string warning = lost
                ? $"The literal '{Shown(text)}' has more digits than its {width} bits hold; the top ones are lost."
                : null;

            return new LiteralResult(writer.ToValue(signed), null, warning, false);
        }

        /// <summary>
        /// The literal as a message names it: whole, or cut short past forty characters, since a number of a hundred thousand
        /// digits said in full is a message of a hundred thousand characters.
        /// </summary>
        private static string Shown(string text) =>
            text.Length <= 40 ? text : text.Substring(0, 20) + "..." + text.Substring(text.Length - 8) +
                " (" + text.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " characters)";

        /// <summary>The digits of a whole number with any underscores taken out, if that is all it is.</summary>
        private static bool TryDigits(string text, out string digits)
        {
            digits = null;
            if (text.Length == 0 || text[0] == '_')
                return false;

            foreach (char c in text)
            {
                if ((c < '0' || c > '9') && c != '_')
                    return false;
            }

            digits = text.Replace("_", string.Empty);
            return true;
        }

        private static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static string BaseName(int bitsPerDigit) =>
            bitsPerDigit == 1 ? "binary" : bitsPerDigit == 3 ? "octal" : "hex";

        /// <summary>
        /// A whole number read from its decimal digits, in 32-bit words, least significant first: worked in place, nine digits
        /// a multiplication, once the digits alone have said it may fit in a value at all (the 16d sweep). BigInteger.Parse
        /// on Unity's runtimes makes a new number for every digit, and counting the bits by shifting made one for every bit --
        /// 6.9 GB for a hundred thousand digits under .NET 10, in one frame.
        /// </summary>
        private readonly struct DecimalNumber
        {
            /// <summary>log2(10), a little under, so a number is refused only when it certainly needs more than a value's bits.</summary>
            private const double BitsPerDigitAtLeast = 3.3219;

            /// <summary>log2(10), a little over, so the words made always hold the number.</summary>
            private const double BitsPerDigitAtMost = 3.3220;

            private static readonly ulong[] Pow10 = { 1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000, 1_000_000_000 };

            private readonly uint[] _words;

            /// <summary>How many bits the number needs: 0 for zero.</summary>
            public readonly int Bits;

            private DecimalNumber(uint[] words, int bits)
            {
                _words = words;
                Bits = bits;
            }

            /// <summary>False, having made nothing, when the digits need more bits than any value has.</summary>
            public static bool TryRead(string digits, out DecimalNumber value)
            {
                int start = 0;
                while (start < digits.Length - 1 && digits[start] == '0')
                    start++;
                int count = digits.Length - start;

                // At least 10^(count - 1): more than a value may hold, whatever the digits are.
                if ((count - 1) * BitsPerDigitAtLeast >= LogicVec.MaxWidth)
                {
                    value = default;
                    return false;
                }

                var words = new uint[(int)(count * BitsPerDigitAtMost / 32) + 2];
                int used = 0;
                int at = start;
                int chunk = count % 9 == 0 ? 9 : count % 9;
                while (at < digits.Length)
                {
                    uint part = 0;
                    for (int end = at + chunk; at < end; at++)
                        part = part * 10 + (uint)(digits[at] - '0');

                    // words = words * 10^chunk + part
                    ulong scale = Pow10[chunk], carry = part;
                    for (int k = 0; k < used; k++)
                    {
                        ulong product = words[k] * scale + carry;
                        words[k] = (uint)product;
                        carry = product >> 32;
                    }
                    if (carry != 0)
                        words[used++] = (uint)carry;
                    chunk = 9;
                }

                int bits = 0;
                if (used > 0)
                {
                    bits = (used - 1) * 32;
                    for (uint top = words[used - 1]; top != 0; top >>= 1)
                        bits++;
                }

                value = new DecimalNumber(words, bits);
                return true;
            }

            /// <summary>The number modulo 2^<paramref name="width"/>, as a known value.</summary>
            public LogicVec ToValue(int width, bool signed)
            {
                if (width <= 64)
                    return LogicVec.FromUInt64(Word(0), width, signed);

                ulong[] planes = LogicVec.NewPlanes(width);
                int n = LogicVec.WordsFor(width);
                for (int i = 0; i < n && 2 * i < _words.Length; i++)
                    planes[i] = Word(i);
                return LogicVec.Wide(width, signed, planes);
            }

            /// <summary>Bits 64i to 64i + 63.</summary>
            private ulong Word(int i)
            {
                ulong low = 2 * i < _words.Length ? _words[2 * i] : 0U;
                ulong high = 2 * i + 1 < _words.Length ? _words[2 * i + 1] : 0U;
                return low | (high << 32);
            }
        }
    }
}
