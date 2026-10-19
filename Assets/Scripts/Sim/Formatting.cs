using System;
using System.Text;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>What a print task is, which decides when it prints and how its line is marked.</summary>
    internal enum PrintKind : byte
    {
        /// <summary><c>$display</c>: now, ending the line.</summary>
        Display,

        /// <summary><c>$write</c>: now, leaving the line open.</summary>
        Write,

        /// <summary><c>$strobe</c>: at the end of the time step, with the values it settles on.</summary>
        Strobe,

        /// <summary><c>$monitor</c>: at the end of every time step in which one of its values changed.</summary>
        Monitor,

        /// <summary><c>$monitoron</c> and <c>$monitoroff</c>.</summary>
        MonitorOn,
        MonitorOff,
    }

    /// <summary>
    /// One piece of a print task's line: literal text, or one argument written by a conversion
    /// (<c>%d</c>, <c>%h</c>, <c>%b</c>, <c>%o</c>, <c>%c</c>, <c>%s</c>, <c>%t</c>, <c>%m</c>), worked out
    /// from the format when the design is built.
    /// </summary>
    internal sealed class PrintSegment
    {
        public string Text;

        /// <summary>The conversion's letter, lower case; <c>'\0'</c> for literal text.</summary>
        public char Conversion;

        /// <summary>The width written (<c>%5d</c>), 0 for the smallest (<c>%0d</c>), -1 for the default.</summary>
        public int Width = -1;

        public bool LeftAlign;
        public bool ZeroPad;
        public Expr Argument;

        /// <summary>An argument whose change does not make <c>$monitor</c> print: <c>$time</c>.</summary>
        public bool Untracked;

        /// <summary>
        /// An argument that is a string -- an enum's <c>.name()</c> -- rather than a vector: <c>%s</c> writes
        /// its text exactly, where a vector's leading zero bytes would be spaces.
        /// </summary>
        public bool IsString;
    }

    /// <summary>A compiled print task.</summary>
    internal sealed class PrintSpec
    {
        public PrintKind Kind;
        public Severity Severity;

        /// <summary>For <c>$info</c>, <c>$warning</c> and <c>$error</c>: what goes before the message.</summary>
        public string Prefix;

        public PrintSegment[] Segments;

        public string Render(LogicVec[] values)
        {
            var line = new StringBuilder(Prefix);
            foreach (PrintSegment segment in Segments)
                line.Append(segment.Conversion == '\0' ? segment.Text : Formatting.Convert(segment, values));
            return line.ToString();
        }
    }

    /// <summary>
    /// <c>$display</c>'s conversions (IEEE 1800, 21.2.1), as Icarus writes them -- which the probes of
    /// 2026-10-13 pinned: <c>%d</c> pads to the widest value the bits can hold (two places for four
    /// bits, four for a signed byte's <c>-128</c>), <c>%h</c>, <c>%o</c> and <c>%b</c> write every digit,
    /// <c>%t</c> pads to twenty, and a width of 0 gives the fewest characters. A digit with unknown bits
    /// is <c>x</c>, <c>X</c>, <c>z</c> or <c>Z</c> by <see cref="LogicVec.Format"/>'s rule.
    /// </summary>
    internal static class Formatting
    {
        private const double Log10Of2 = 0.30102999566398120;

        public static string Convert(PrintSegment segment, LogicVec[] values)
        {
            switch (segment.Conversion)
            {
                case 'm':
                    return Pad(segment.Text, segment);
            }

            LogicVec value = segment.Argument.Evaluate(values);
            string digits;
            int width;

            switch (segment.Conversion)
            {
                case 'b':
                    digits = value.Format(Radix.Binary);
                    width = value.Width;
                    break;
                case 'o':
                    digits = value.Format(Radix.Octal);
                    width = (value.Width + 2) / 3;
                    break;
                case 'h':
                    digits = value.Format(Radix.Hex);
                    width = (value.Width + 3) / 4;
                    break;
                case 'c':
                    return Pad(Character(value), segment);
                case 's':
                {
                    // Every byte is a character, and a leading zero byte a space, as Icarus shows a
                    // vector wider than its text; %0s drops them, and a width asked for pads the text.
                    // A string -- an enum's .name() -- is its text exactly.
                    string text = Characters(value);
                    if (segment.Width < 0 && !segment.IsString)
                        return text.PadLeft((value.Width + 7) / 8);
                    return Pad(text, segment);
                }
                case 't':
                    digits = value.AsUnsigned().Format(Radix.Unsigned);
                    width = 20;
                    break;
                default:
                    digits = value.Format(value.IsSigned ? Radix.Signed : Radix.Unsigned);
                    width = DecimalWidth(value.Width, value.IsSigned);
                    break;
            }

            if (segment.Width == 0)
                return Smallest(digits, segment.Conversion);

            int field = segment.Width > 0 ? segment.Width : width;
            if (digits.Length >= field)
                return digits;

            if (segment.LeftAlign)
                return digits.PadRight(field);

            if (segment.ZeroPad && value.IsKnown)
                return digits.StartsWith("-", StringComparison.Ordinal)
                    ? "-" + digits.Substring(1).PadLeft(field - 1, '0')
                    : digits.PadLeft(field, '0');

            // A bit-wise radix keeps its leading zeros as digits: %h of 8'h05 is 05.
            return segment.Conversion == 'h' || segment.Conversion == 'b' || segment.Conversion == 'o'
                ? digits.PadLeft(field, segment.Width > 0 ? ' ' : '0')
                : digits.PadLeft(field);
        }

        /// <summary>How many places the widest value of <paramref name="bits"/> bits takes in decimal, its sign included.</summary>
        public static int DecimalWidth(int bits, bool signed)
        {
            if (signed)
                return bits <= 1 ? 2 : (int)Math.Floor((bits - 1) * Log10Of2) + 2;
            return (int)Math.Floor(bits * Log10Of2) + 1;
        }

        /// <summary>%0d, %0h: no padding, and a bit-wise radix without its leading zeros.</summary>
        private static string Smallest(string digits, char conversion)
        {
            if (conversion != 'h' && conversion != 'b' && conversion != 'o')
                return digits;

            int first = 0;
            while (first < digits.Length - 1 && digits[first] == '0')
                first++;
            return digits.Substring(first);
        }

        private static string Pad(string text, PrintSegment segment)
        {
            if (segment.Width <= 0 || text.Length >= segment.Width)
                return text;
            return segment.LeftAlign ? text.PadRight(segment.Width) : text.PadLeft(segment.Width);
        }

        private static string Character(LogicVec value)
        {
            LogicVec low = value.Width > 8 ? value.Slice(0, 8) : value;
            return low.TryGetUInt64(out ulong code) ? ((char)code).ToString() : "?";
        }

        /// <summary>%s of a value: its bits eight at a time as characters, the first from the top, leading zero bytes dropped.</summary>
        private static string Characters(LogicVec value)
        {
            var text = new StringBuilder();
            int bytes = (value.Width + 7) / 8;
            for (int b = bytes - 1; b >= 0; b--)
            {
                int lowest = b * 8;
                LogicVec part = value.Slice(lowest, Math.Min(8, value.Width - lowest));
                if (!part.TryGetUInt64(out ulong code))
                    text.Append('?');
                else if (code != 0 || text.Length > 0)
                    text.Append((char)code);
            }

            return text.ToString();
        }
    }
}
