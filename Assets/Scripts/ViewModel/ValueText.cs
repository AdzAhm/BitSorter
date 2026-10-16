using System;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The ways a waveform row can be read: <see cref="Radix"/>'s five, as a student sets them in
    /// ModelSim's Radix menu (decision D11), and <see cref="Symbolic"/>, an enum's names.
    /// </summary>
    /// <remarks>
    /// **Its own enum, not <see cref="Radix"/> grown by one.** <see cref="Radix"/> is also how a value
    /// is written out as a literal for exported code (<see cref="LogicVec.ToLiteral"/>), and a literal
    /// has no symbolic form: a sixth member there would be a case every printer has to refuse. A name is
    /// the waveform's business alone.
    /// </remarks>
    public enum WaveRadix
    {
        Binary,
        Octal,
        Hex,

        /// <summary>The bits read as an unsigned number.</summary>
        Unsigned,

        /// <summary>The bits read as a two's complement number, whether or not the signal was declared <c>signed</c>.</summary>
        Signed,

        /// <summary>The name of the enum member holding the value, and hex where none does.</summary>
        Symbolic,
    }

    /// <summary>
    /// A value as the waveform writes it, in the radix its row is read in -- and how wide that text can
    /// ever get, so a column of values can be sized once and never move while the run plays.
    /// </summary>
    /// <remarks>
    /// **One bit is a level in every radix**: 0, 1, x or z. A one-bit row is drawn as a line high or
    /// low, not as a bus with a number in it, and its text says which line. Read as two's complement a
    /// high one-bit signal is -1, which is true and of no use to anyone reading a waveform. The one
    /// exception is <see cref="WaveRadix.Symbolic"/> on an enum one bit wide -- <c>typedef enum logic
    /// {OFF, ON}</c> -- where the name is the whole point of the radix and wins.
    ///
    /// **A bus in binary, octal or hex is <see cref="LogicVec.Format"/>'s digits**, which follow
    /// <c>$display</c> for a digit not fully known (IEEE 1800, 21.2.1.3): every bit x is <c>x</c>, every
    /// bit z is <c>z</c>, some bits x is <c>X</c>, some bits z and none x is <c>Z</c>. So
    /// <c>8'b1x00_0011</c> reads <c>X3</c>: the low digit is known, and the text says so.
    /// <see cref="WorkbenchModel.TextOf"/> writes any partly unknown bus as a plain <c>x</c>, which hides
    /// exactly that; this replaces it. **Every digit the width has is written, leading zeros
    /// included** -- 8'h0a is <c>0A</c>, 8 bits of binary are always eight digits -- because that is what
    /// <see cref="LogicVec.Format"/> gives, and it is what makes a bus's text the same length whatever
    /// it holds.
    ///
    /// **The letters A to F are upper case, and only those.** The workbench has always written hex in
    /// capitals. Upper-casing the whole string would be the bug to avoid: in <c>$display</c>'s rule the
    /// case of an x or a z *is* the information -- <c>x</c> says a digit is entirely unknown, <c>X</c>
    /// that part of it is known -- so <c>ToUpperInvariant</c> would claim a known bit that is not there.
    ///
    /// **A decimal is one number, so one unknown bit leaves it none**, and the whole value is the one
    /// "digit" of the same rule (IEEE 1800, 21.2.1.3): <c>x</c> if every bit is x, <c>X</c> if some
    /// are, <c>z</c> if every bit is z, <c>Z</c> if some are and none is x. Unsigned and signed are the
    /// radix's reading of the bits, never the declaration's: choosing Signed on a bus declared without
    /// <c>signed</c> is how its bits are seen as two's complement, which is what the menu is for.
    ///
    /// **A name, or hex.** <see cref="WaveRadix.Symbolic"/> asks <see cref="EnumInfo.TryName"/>, and where
    /// no member holds the value -- a state register before reset, all x, or holding a code its enum
    /// never names -- it falls back to hex, so a state machine's row still says what is there. Symbolic
    /// on a signal with no enum is hex too: a radix set for a whole waveform never leaves a row blank.
    ///
    /// A bus's number allocates its string (a level or a name is one already made), so this is for a
    /// value as it changes, never for every frame: <see cref="TextCache"/> keeps what it formats, and
    /// playback asks this once per change and radix.
    /// </remarks>
    public static class ValueText
    {
        /// <summary>log10(2), to the double nearest: how many decimal digits one more bit costs.</summary>
        private const double Log10Of2 = 0.30102999566398119521;

        /// <summary>
        /// <paramref name="value"/> written in <paramref name="radix"/>: <c>1</c>, <c>3A</c>, <c>X3</c>,
        /// <c>253</c>, <c>-3</c>, <c>IDLE</c>. <paramref name="enumInfo"/> is the signal's enum, read only
        /// in <see cref="WaveRadix.Symbolic"/>.
        /// </summary>
        public static string Of(LogicVec value, WaveRadix radix, EnumInfo enumInfo = null)
        {
            if (value.IsEmpty)
                throw new ArgumentException("default(LogicVec) is not a value: it has no width.", nameof(value));

            if (radix == WaveRadix.Symbolic && enumInfo != null && enumInfo.TryName(value, out string name))
                return name;

            if (value.Width == 1)
                return Level(value[0]);

            switch (radix)
            {
                case WaveRadix.Binary: return value.Format(Radix.Binary);
                case WaveRadix.Octal: return value.Format(Radix.Octal);
                case WaveRadix.Hex: return HexLettersUpper(value.Format(Radix.Hex));
                case WaveRadix.Unsigned: return value.Format(Radix.Unsigned);
                case WaveRadix.Signed: return value.Format(Radix.Signed);
                case WaveRadix.Symbolic: return HexLettersUpper(value.Format(Radix.Hex));
                default: throw new ArgumentOutOfRangeException(nameof(radix), radix, "Not a radix a waveform reads in.");
            }
        }

        /// <summary>
        /// The most characters <see cref="Of"/> can give for a value <paramref name="width"/> bits wide in
        /// <paramref name="radix"/>: a values column this wide never has to move while the run plays.
        /// </summary>
        /// <remarks>
        /// Binary, octal and hex are as many digits as the width has, every value alike. Unsigned is the
        /// digits of 2^w - 1, and signed one more than the digits of 2^(w-1) for the minus sign of the
        /// most negative value -- 8 bits are <c>255</c> and <c>-128</c>, 3 and 4. An unknown decimal is one
        /// letter, never wider. Symbolic is the longest name a member of that width has, or the hex width
        /// if that is longer, since a value no member holds is written in hex.
        ///
        /// **The decimal digits are worked out, not counted off a formatted extreme**: a 65-bit carry
        /// chain's 2^65 does not fit in a <c>ulong</c>, and this layer has no <c>BigInteger</c>. 2^w has
        /// floor(w log10 2) + 1 digits, and 2^w - 1 the same, since no power of two is a power of ten.
        /// The floor is exact for every width a value may have: up to <see cref="LogicVec.MaxWidth"/>, the
        /// nearest w log10 2 comes to a whole number is about 1.6e-7 above one, at w = 325,147, where a
        /// double's error in the product is around 1e-11. A test holds the formula to <c>BigInteger</c>
        /// there and at every width up to 300.
        /// </remarks>
        public static int MaxChars(int width, WaveRadix radix, EnumInfo enumInfo = null)
        {
            if (width < 1 || width > LogicVec.MaxWidth)
                throw new ArgumentOutOfRangeException(nameof(width), width, $"A value's width is from 1 to {LogicVec.MaxWidth}.");

            int numeric;
            if (width == 1)
            {
                numeric = 1;
            }
            else
            {
                switch (radix)
                {
                    case WaveRadix.Binary: numeric = width; break;
                    case WaveRadix.Octal: numeric = (width + 2) / 3; break;
                    case WaveRadix.Hex: numeric = (width + 3) / 4; break;
                    case WaveRadix.Unsigned: numeric = DigitsOfPowerOfTwo(width); break;
                    case WaveRadix.Signed: numeric = 1 + DigitsOfPowerOfTwo(width - 1); break;
                    case WaveRadix.Symbolic: numeric = (width + 3) / 4; break;
                    default: throw new ArgumentOutOfRangeException(nameof(radix), radix, "Not a radix a waveform reads in.");
                }
            }

            if (radix != WaveRadix.Symbolic || enumInfo == null)
                return numeric;

            // Only a member as wide as the signal can be named on it: TryName compares at the value's width.
            int longest = numeric;
            foreach (EnumMemberInfo member in enumInfo.Members)
            {
                if (member.Value.Width == width && member.Name.Length > longest)
                    longest = member.Name.Length;
            }

            return longest;
        }

        /// <summary>One bit's level: the schematic's and the waveform's text for a single wire.</summary>
        private static string Level(Logic4 bit)
        {
            switch (bit)
            {
                case Logic4.Zero: return "0";
                case Logic4.One: return "1";
                case Logic4.Z: return "z";
                default: return "x";
            }
        }

        /// <summary>How many decimal digits 2^<paramref name="power"/> has, and 2^power - 1 too: see <see cref="MaxChars"/>.</summary>
        private static int DigitsOfPowerOfTwo(int power) => (int)Math.Floor(power * Log10Of2) + 1;

        /// <summary>
        /// Hex digits with a to f in capitals and every x, X, z and Z left as it is: see the remarks on
        /// <see cref="ValueText"/> for why the case of an x matters. The string is handed back unchanged
        /// when it has no letter to raise, which saves the second allocation on a value like <c>30</c>.
        /// </summary>
        private static string HexLettersUpper(string digits)
        {
            int first = -1;
            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] >= 'a' && digits[i] <= 'f')
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
                return digits;

            return string.Create(digits.Length, digits, static (chars, source) =>
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    char c = source[i];
                    chars[i] = c >= 'a' && c <= 'f' ? (char)(c - 'a' + 'A') : c;
                }
            });
        }
    }
}
