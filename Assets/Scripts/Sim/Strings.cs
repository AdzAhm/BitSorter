using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// SystemVerilog's <c>string</c> (Part L6), as BitSorter holds it: a 2-state vector of
    /// <see cref="PackedType.StringCharacters"/> bytes, its characters at the bottom and zero bytes above --
    /// the layout a string literal has already, so <c>s = "abc"</c> and <c>s == "abc"</c> need nothing more.
    /// A string never holds a zero byte (IEEE 1800, 6.16), so a zero byte is only ever room left over.
    /// </summary>
    /// <remarks>
    /// The standard's string grows as it needs to; this one stops at <see cref="PackedType.StringCharacters"/>
    /// characters and keeps the first of a longer one, which every testbench line seen so far fits in.
    /// </remarks>
    internal static class Strings
    {
        /// <summary>The characters a value's bytes spell, from the top: zero bytes left out, an unknown bit read as 0.</summary>
        public static string Text(in LogicVec value)
        {
            var text = new StringBuilder();
            for (int lowest = (value.Width - 1) / 8 * 8; lowest >= 0; lowest -= 8)
            {
                value.Slice(lowest, Math.Min(8, value.Width - lowest)).ToTwoState().TryGetUInt64(out ulong code);
                if (code != 0)
                    text.Append((char)code);
            }

            return text.ToString();
        }

        /// <summary>A string as <paramref name="width"/> bits: its characters at the bottom; past the room, the first ones kept.</summary>
        public static LogicVec Value(string text, int width)
        {
            int room = width / 8;
            if (text.Length > room)
                text = text.Substring(0, room);

            if (text.Length == 0)
                return LogicVec.FromUInt64(0, width).ToTwoState();

            var bits = new StringBuilder(width);
            bits.Append('0', width - text.Length * 8);
            foreach (char c in text)
                for (int b = 7; b >= 0; b--)
                    bits.Append(((c & 0xFF) >> b & 1) == 1 ? '1' : '0');
            return LogicVec.FromBinary(bits.ToString()).ToTwoState();
        }

        /// <summary>A whole number a string's leading digits spell, in <paramref name="radix"/>: what <c>atoi</c> and its kind read (IEEE 1800, 6.16.9).</summary>
        public static long Parse(string text, int radix)
        {
            long value = 0;
            int i = 0;
            bool negative = false;
            if (radix == 10 && i < text.Length && (text[i] == '-' || text[i] == '+'))
                negative = text[i++] == '-';

            for (; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (c == '_')
                    continue;
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : 99;
                if (digit >= radix)
                    break;
                value = unchecked(value * radix + digit);
            }

            return negative ? -value : value;
        }
    }

    /// <summary>What a string method gives (IEEE 1800, 6.16).</summary>
    internal enum StringMethod : byte
    {
        Len, Getc, ToUpper, ToLower, Substr, Compare, ICompare, AtoI, AtoHex, AtoOct, AtoBin,

        /// <summary><c>s.putc(i, c)</c>: the string with character i replaced, as a statement writes it back.</summary>
        Putc,
    }

    public abstract partial class Expr
    {
        /// <summary>A value given to a <c>string</c>: its bytes as characters, zero bytes left out, at the bottom of <paramref name="width"/> bits.</summary>
        internal static Expr StringOf(Expr a, int width) => new StringOfExpr(a, width);

        /// <summary><c>{s, "-", t}</c> with a string in it: the characters one after another, <paramref name="count"/> times over.</summary>
        internal static Expr StringJoin(Expr[] parts, int count, int width) => new StringJoinExpr(parts, count, width);

        /// <summary>Two strings compared as text, character by character (IEEE 1800, 6.16).</summary>
        internal static Expr StringCompare(BinaryOp op, Expr a, Expr b) => new StringCompareExpr(op, a, b);

        /// <summary>A method of a string: <c>s.len()</c>, <c>s.substr(1, 3)</c>, <c>s.atoi()</c>.</summary>
        internal static Expr StringCall(StringMethod method, Expr target, Expr[] arguments, int width) => new StringMethodExpr(method, target, arguments, width);

        /// <summary><c>$sformatf("...", ...)</c>: a line formatted as <c>$display</c> would print it, as a string.</summary>
        internal static Expr Format(PrintSegment[] segments, int width) => new FormatExpr(segments, width);

        private sealed class StringOfExpr : Expr
        {
            private readonly Expr _a;
            private readonly int _width;

            public StringOfExpr(Expr a, int width)
            {
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _width = width;
            }

            public override int Width => _width;
            public override LogicVec Evaluate(LogicVec[] values) => Strings.Value(Strings.Text(_a.Evaluate(values)), _width);
            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);
        }

        private sealed class StringJoinExpr : Expr
        {
            private readonly Expr[] _parts;
            private readonly int _count;
            private readonly int _width;

            public StringJoinExpr(Expr[] parts, int count, int width)
            {
                _parts = parts ?? throw new ArgumentNullException(nameof(parts));
                _count = count;
                _width = width;
            }

            public override int Width => _width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                var text = new StringBuilder();
                foreach (Expr part in _parts)
                    text.Append(Strings.Text(part.Evaluate(values)));

                string once = text.ToString();
                for (int i = 1; i < _count; i++)
                    text.Append(once);
                return Strings.Value(text.ToString(), _width);
            }

            public override void CollectReads(ICollection<int> reads)
            {
                foreach (Expr part in _parts)
                    part.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                foreach (Expr part in _parts)
                    part.CollectCalls(calls);
            }

            internal override int Height => 1 + MaxHeight(_parts);
        }

        private sealed class StringCompareExpr : Expr
        {
            private readonly BinaryOp _op;
            private readonly Expr _a, _b;

            public StringCompareExpr(BinaryOp op, Expr a, Expr b)
            {
                _op = op;
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _b = b ?? throw new ArgumentNullException(nameof(b));
            }

            public override int Width => 1;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                int order = string.CompareOrdinal(Strings.Text(_a.Evaluate(values)), Strings.Text(_b.Evaluate(values)));
                bool holds;
                switch (_op)
                {
                    case BinaryOp.Less: holds = order < 0; break;
                    case BinaryOp.LessOrEqual: holds = order <= 0; break;
                    case BinaryOp.Greater: holds = order > 0; break;
                    case BinaryOp.GreaterOrEqual: holds = order >= 0; break;
                    case BinaryOp.NotEqual: case BinaryOp.CaseNotEqual: holds = order != 0; break;
                    default: holds = order == 0; break;
                }

                return LogicVec.FromUInt64(holds ? 1UL : 0UL, 1);
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _a.CollectReads(reads);
                _b.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _a.CollectCalls(calls);
                _b.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_a), HeightOf(_b));
        }

        private sealed class StringMethodExpr : Expr
        {
            private readonly StringMethod _method;
            private readonly Expr _target;
            private readonly Expr[] _arguments;
            private readonly int _width;

            public StringMethodExpr(StringMethod method, Expr target, Expr[] arguments, int width)
            {
                _method = method;
                _target = target ?? throw new ArgumentNullException(nameof(target));
                _arguments = arguments ?? Array.Empty<Expr>();
                _width = width;
            }

            public override int Width => _width;

            private long Number(int i, LogicVec[] values, long fallback)
            {
                if (i >= _arguments.Length)
                    return fallback;
                LogicVec v = _arguments[i].Evaluate(values);
                if (!v.IsKnown)
                    return -1;
                if (v.IsSigned)
                    return v.TryGetInt64(out long s) ? s : -1;
                return v.TryGetUInt64(out ulong u) && u <= int.MaxValue ? (long)u : -1;
            }

            public override LogicVec Evaluate(LogicVec[] values)
            {
                string text = Strings.Text(_target.Evaluate(values));
                switch (_method)
                {
                    case StringMethod.Len:
                        return LogicVec.FromInt64(text.Length, 32);

                    case StringMethod.Getc:
                    {
                        long i = Number(0, values, -1);
                        return LogicVec.FromUInt64(i >= 0 && i < text.Length ? (ulong)(text[(int)i] & 0xFF) : 0UL, 8);
                    }

                    case StringMethod.ToUpper:
                        return Strings.Value(text.ToUpperInvariant(), _width);

                    case StringMethod.ToLower:
                        return Strings.Value(text.ToLowerInvariant(), _width);

                    case StringMethod.Substr:
                    {
                        long i = Number(0, values, -1), j = Number(1, values, -1);
                        return Strings.Value(i < 0 || j < i || j >= text.Length ? string.Empty : text.Substring((int)i, (int)(j - i + 1)), _width);
                    }

                    case StringMethod.Compare:
                    case StringMethod.ICompare:
                    {
                        string other = _arguments.Length > 0 ? Strings.Text(_arguments[0].Evaluate(values)) : string.Empty;
                        int order = _method == StringMethod.Compare
                            ? string.CompareOrdinal(text, other)
                            : string.CompareOrdinal(text.ToLowerInvariant(), other.ToLowerInvariant());
                        return LogicVec.FromInt64(Math.Sign(order), 32);
                    }

                    case StringMethod.Putc:
                    {
                        // Out of range, or a zero character, leaves the string as it was (IEEE 1800, 6.16.2).
                        long i = Number(0, values, -1);
                        LogicVec c = _arguments.Length > 1 ? _arguments[1].Evaluate(values) : LogicVec.FromUInt64(0, 8);
                        c.Slice(0, Math.Min(8, c.Width)).ToTwoState().TryGetUInt64(out ulong code);
                        if (i < 0 || i >= text.Length || code == 0)
                            return Strings.Value(text, _width);
                        char[] chars = text.ToCharArray();
                        chars[i] = (char)code;
                        return Strings.Value(new string(chars), _width);
                    }

                    case StringMethod.AtoI: return LogicVec.FromInt64(Strings.Parse(text, 10), 32);
                    case StringMethod.AtoHex: return LogicVec.FromInt64(Strings.Parse(text, 16), 32);
                    case StringMethod.AtoOct: return LogicVec.FromInt64(Strings.Parse(text, 8), 32);
                    default: return LogicVec.FromInt64(Strings.Parse(text, 2), 32);
                }
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _target.CollectReads(reads);
                foreach (Expr argument in _arguments)
                    argument.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _target.CollectCalls(calls);
                foreach (Expr argument in _arguments)
                    argument.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_target), MaxHeight(_arguments));
        }

        private sealed class FormatExpr : Expr
        {
            private readonly PrintSegment[] _segments;
            private readonly int _width;

            public FormatExpr(PrintSegment[] segments, int width)
            {
                _segments = segments ?? throw new ArgumentNullException(nameof(segments));
                _width = width;
            }

            public override int Width => _width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                var text = new StringBuilder();
                foreach (PrintSegment segment in _segments)
                    text.Append(segment.Conversion == '\0' ? segment.Text : Formatting.Convert(segment, values));
                return Strings.Value(text.ToString(), _width);
            }

            public override void CollectReads(ICollection<int> reads)
            {
                foreach (PrintSegment segment in _segments)
                    segment.Argument?.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                foreach (PrintSegment segment in _segments)
                    segment.Argument?.CollectCalls(calls);
            }

            internal override int Height => 1 + MaxHeight(_segments, s => s.Argument);
        }
    }
}
