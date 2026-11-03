using System;
using System.Collections.Generic;
using System.Text;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// The text of a memory image, as <c>$readmemh</c> and <c>$readmemb</c> read it and
    /// <c>$writememh</c> and <c>$writememb</c> write it (IEEE 1800, 21.4): words separated by white
    /// space, in hex or binary, with <c>x</c>, <c>z</c> and <c>?</c> digits and underscores; <c>//</c> and
    /// <c>/* */</c> comments; and <c>@</c> followed by a hex address to load the next word there.
    /// </summary>
    /// <remarks>
    /// **Reading makes nothing a word at a time** (the review of 16c): an image is checked, and then stepped through, in
    /// place, and a word is made at the memory's width only once it is known to be loaded. Every word was made first, into
    /// a list, so a few wide words and a long file of short ones made as much as the file had words.
    /// </remarks>
    internal static class MemoryImage
    {
        /// <summary>Steps through an image's tokens -- its words and its <c>@</c> addresses -- past white space and comments, making nothing.</summary>
        public struct Reader
        {
            private readonly string _text;
            private int _i;
            private int _line;

            public Reader(string text)
            {
                _text = text;
                _i = 0;
                _line = 1;
            }

            /// <summary>
            /// The next token: where it starts in the text, how long it is, and its line. False at the end of the text, and at a
            /// <c>/*</c> comment never closed, which <paramref name="error"/> then says.
            /// </summary>
            public bool Next(out int start, out int length, out int line, out string error)
            {
                string text = _text;
                start = length = 0;
                error = null;

                while (_i < text.Length)
                {
                    char c = text[_i];

                    if (c == '\n')
                    {
                        _line++;
                        _i++;
                        continue;
                    }

                    if (char.IsWhiteSpace(c))
                    {
                        _i++;
                        continue;
                    }

                    if (c == '/' && _i + 1 < text.Length && text[_i + 1] == '/')
                    {
                        while (_i < text.Length && text[_i] != '\n')
                            _i++;
                        continue;
                    }

                    if (c == '/' && _i + 1 < text.Length && text[_i + 1] == '*')
                    {
                        int end = text.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                        if (end < 0)
                        {
                            line = _line;
                            error = $"line {_line}: a /* comment is never closed";
                            _i = text.Length;
                            return false;
                        }

                        for (int k = _i; k < end; k++)
                            if (text[k] == '\n')
                                _line++;
                        _i = end + 2;
                        continue;
                    }

                    start = _i;
                    while (_i < text.Length && !char.IsWhiteSpace(text[_i]) && !(text[_i] == '/' && _i + 1 < text.Length && (text[_i + 1] == '/' || text[_i + 1] == '*')))
                        _i++;
                    length = _i - start;
                    line = _line;
                    return true;
                }

                line = _line;
                return false;
            }
        }

        /// <summary>
        /// Whether every token of the image is a word or an address, or false and what is wrong, with its line: an image with
        /// a fault anywhere in it loads nothing, so the whole of it is checked before a word is loaded.
        /// </summary>
        public static bool TryCheck(string text, bool hex, out string error)
        {
            var reader = new Reader(text);
            while (reader.Next(out int start, out int length, out int line, out error))
            {
                if (text[start] == '@')
                {
                    if (!TryAddress(text, start, length, out _))
                    {
                        error = $"line {line}: {text.Substring(start, length)} is not an address; one is written @ and hex digits, as @1f";
                        return false;
                    }

                    continue;
                }

                if (!IsWord(text, start, length, hex))
                {
                    error = $"line {line}: {text.Substring(start, length)} is not a {(hex ? "hex" : "binary")} word";
                    return false;
                }
            }

            return error == null;
        }

        /// <summary>The address an <c>@</c> token at <paramref name="start"/> names; false if its digits are not one.</summary>
        public static bool TryAddress(string text, int start, int length, out long address)
        {
            address = 0;
            if (length < 2)
                return false;

            for (int k = start + 1; k < start + length; k++)
            {
                char c = text[k];
                if (c == '_')
                    continue;

                int d = HexValue(c);
                if (d < 0 || address > (long.MaxValue >> 4))
                    return false;
                address = address * 16 + d;
            }

            return true;
        }

        private static bool IsWord(string text, int start, int length, bool hex)
        {
            bool any = false;
            for (int k = start; k < start + length; k++)
            {
                char c = text[k];
                if (c == '_')
                    continue;

                any = true;
                char lower = char.ToLowerInvariant(c);
                if (lower == 'x' || lower == 'z' || lower == '?')
                    continue;
                if (hex ? HexValue(c) < 0 : c != '0' && c != '1')
                    return false;
            }

            return any;
        }

        /// <summary>
        /// A checked word's bits, fitted to the memory's width as a literal is: cut from the left, or extended with 0 -- or
        /// with x or z when its first digit is one. Only the bits that are kept are read.
        /// </summary>
        public static LogicVec Word(string text, int start, int length, bool hex, int width)
        {
            var bits = new char[width];
            int filled = 0;
            char extension = '0';

            // From its last digit back, each digit's bits from the lowest, until the word is full.
            for (int k = start + length - 1; k >= start && filled < width; k--)
            {
                char c = text[k];
                if (c == '_')
                    continue;

                char lower = char.ToLowerInvariant(c);
                bool unknown = lower == 'x' || lower == 'z' || lower == '?';
                char digit = lower == 'x' ? 'x' : 'z';
                extension = unknown ? digit : '0';

                int d = unknown ? 0 : HexValue(c);
                for (int b = 0; b < (hex ? 4 : 1) && filled < width; b++, filled++)
                    bits[width - 1 - filled] = unknown ? digit : (d >> b & 1) == 1 ? '1' : '0';
            }

            // Short of the width: the last digit read was the first one written, and says what the rest is.
            for (; filled < width; filled++)
                bits[width - 1 - filled] = extension;

            return LogicVec.FromBinary(new string(bits));
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>Words one to a line, in hex or binary, as <c>$writememh</c> and <c>$writememb</c> write them.</summary>
        public static string Write(IEnumerable<LogicVec> words, bool hex)
        {
            var text = new StringBuilder();
            foreach (LogicVec word in words)
                text.Append(word.Format(hex ? Radix.Hex : Radix.Binary)).Append('\n');
            return text.ToString();
        }
    }
}
