using System;
using System.Numerics;
using System.Text;

namespace BitSorter.SystemVerilog
{
    /// <summary>Writing a value out: in a radix, for a waveform, and as a literal, for exported code.</summary>
    /// <remarks>
    /// The rules for a digit that is not fully known are <c>$display</c>'s (IEEE 1800, 21.2.1.3),
    /// which ModelSim's waveform follows: a digit whose bits are all x is <c>x</c>, all z is <c>z</c>;
    /// one with only some unknown bits is <c>X</c>, or <c>Z</c> if those are all z. A decimal
    /// number is one digit for this purpose -- a value with any unknown bit has no decimal value.
    ///
    /// Formatting allocates its string. It is for display and export, never the engine's hot path;
    /// the waveform caches a value's text when the value changes rather than formatting every frame.
    /// </remarks>
    public readonly partial struct LogicVec
    {
        /// <summary>
        /// The value's digits in <paramref name="radix"/>, with no width or base: <c>1x0z</c>, <c>a5</c>,
        /// <c>165</c>, <c>-3</c>.
        /// </summary>
        public string Format(Radix radix)
        {
            Require(this);

            switch (radix)
            {
                case Radix.Binary: return Grouped(1);
                case Radix.Octal: return Grouped(3);
                case Radix.Hex: return Grouped(4);
                default: return Decimal(radix == Radix.Signed);
            }
        }

        /// <summary>
        /// The value as a SystemVerilog literal: <c>4'b1x0z</c>, <c>8'ha5</c>, <c>8'd165</c>, with an
        /// <c>s</c> when signed. A negative signed decimal is written <c>-8'sd3</c>, unary minus on
        /// a literal, because a literal itself cannot be negative.
        /// </summary>
        public string ToLiteral(Radix radix)
        {
            Require(this);

            string sign = _signed ? "s" : string.Empty;

            switch (radix)
            {
                case Radix.Binary: return $"{_width}'{sign}b{Format(Radix.Binary)}";
                case Radix.Octal: return $"{_width}'{sign}o{Format(Radix.Octal)}";
                case Radix.Hex: return $"{_width}'{sign}h{Format(Radix.Hex)}";
            }

            string digits = Decimal(radix == Radix.Signed);

            // A decimal literal can only be all x or all z when it is not a number.
            if (!IsKnown)
                return $"{_width}'{sign}d{(IsAllZ ? "z" : "x")}";

            return digits.StartsWith("-", StringComparison.Ordinal)
                ? $"-{_width}'sd{digits.Substring(1)}"
                : $"{_width}'{sign}d{digits}";
        }

        /// <summary>Binary, octal or hex: the bits taken <paramref name="bitsPerDigit"/> at a time from the least significant.</summary>
        private string Grouped(int bitsPerDigit)
        {
            int digits = (_width + bitsPerDigit - 1) / bitsPerDigit;
            var text = new StringBuilder(digits);

            for (int d = digits - 1; d >= 0; d--)
            {
                int lowest = d * bitsPerDigit;
                int count = Math.Min(bitsPerDigit, _width - lowest);
                text.Append(DigitOf(lowest, count));
            }

            return text.ToString();
        }

        private char DigitOf(int lowest, int count)
        {
            int value = 0, xs = 0, zs = 0;

            for (int i = 0; i < count; i++)
            {
                switch (this[lowest + i])
                {
                    case Logic4.One: value |= 1 << i; break;
                    case Logic4.X: xs++; break;
                    case Logic4.Z: zs++; break;
                }
            }

            if (xs == count) return 'x';
            if (zs == count) return 'z';
            if (xs > 0) return 'X';
            if (zs > 0) return 'Z';
            return "0123456789abcdef"[value];
        }

        private string Decimal(bool signed)
        {
            if (!IsKnown)
            {
                if (IsAllX) return "x";
                if (IsAllZ) return "z";

                for (int i = 0; i < _width; i++)
                {
                    if (this[i] == Logic4.X)
                        return "X";
                }

                return "Z";
            }

            if (_width <= 64)
            {
                return signed
                    ? SignExtend64(_a, _width).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : _a.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            BigInteger number = signed ? ToSignedBig(this) : ToUnsignedBig(this);
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
