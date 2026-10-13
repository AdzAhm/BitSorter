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
    internal static class MemoryImage
    {
        /// <summary>One thing read: a word, or an <c>@</c> address the next word goes to.</summary>
        public readonly struct Item
        {
            public Item(LogicVec word, long address, bool isAddress, int line)
            {
                Word = word;
                Address = address;
                IsAddress = isAddress;
                Line = line;
            }

            public readonly LogicVec Word;
            public readonly long Address;
            public readonly bool IsAddress;
            public readonly int Line;
        }

        /// <summary>
        /// The image's words, each at <paramref name="width"/> bits, and its addresses; or false and
        /// what is wrong, with its line.
        /// </summary>
        public static bool TryRead(string text, bool hex, int width, List<Item> into, out string error)
        {
            error = null;
            int line = 1;
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];

                if (c == '\n')
                {
                    line++;
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                        i++;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        error = $"line {line}: a /* comment is never closed";
                        return false;
                    }

                    for (int k = i; k < end; k++)
                        if (text[k] == '\n')
                            line++;
                    i = end + 2;
                    continue;
                }

                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && !(text[i] == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*')))
                    i++;
                string token = text.Substring(start, i - start);

                if (token[0] == '@')
                {
                    if (!TryAddress(token.Substring(1), out long address))
                    {
                        error = $"line {line}: {token} is not an address; one is written @ and hex digits, as @1f";
                        return false;
                    }

                    into.Add(new Item(default, address, true, line));
                    continue;
                }

                if (!TryWord(token, hex, width, out LogicVec word))
                {
                    error = $"line {line}: {token} is not a {(hex ? "hex" : "binary")} word";
                    return false;
                }

                into.Add(new Item(word, 0, false, line));
            }

            return true;
        }

        private static bool TryAddress(string digits, out long address)
        {
            address = 0;
            if (digits.Length == 0)
                return false;

            foreach (char c in digits)
            {
                if (c == '_')
                    continue;

                int d = HexValue(c);
                if (d < 0 || address > (long.MaxValue >> 4))
                    return false;
                address = address * 16 + d;
            }

            return true;
        }

        /// <summary>A word's bits, most significant first, fitted to the memory's width as a literal is: cut, or extended with 0 -- or with x or z when its first digit is one.</summary>
        private static bool TryWord(string token, bool hex, int width, out LogicVec word)
        {
            word = default;
            var bits = new StringBuilder(token.Length * 4);

            foreach (char c in token)
            {
                if (c == '_')
                    continue;

                char lower = char.ToLowerInvariant(c);
                if (lower == 'x' || lower == 'z' || lower == '?')
                {
                    bits.Append(lower == 'x' ? 'x' : 'z', hex ? 4 : 1);
                    continue;
                }

                if (hex)
                {
                    int d = HexValue(c);
                    if (d < 0)
                        return false;
                    for (int b = 3; b >= 0; b--)
                        bits.Append((d >> b & 1) == 1 ? '1' : '0');
                }
                else
                {
                    if (c != '0' && c != '1')
                        return false;
                    bits.Append(c);
                }
            }

            if (bits.Length == 0)
                return false;

            string all = bits.ToString();
            if (all.Length > width)
                all = all.Substring(all.Length - width);
            else if (all.Length < width)
                all = new string(all[0] == 'x' || all[0] == 'z' ? all[0] : '0', width - all.Length) + all;

            word = LogicVec.FromBinary(all);
            return true;
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
