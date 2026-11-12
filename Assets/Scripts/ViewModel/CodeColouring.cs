using System;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>What a character of code is, for the colour it is written in (5.1).</summary>
    public enum CodeKind : byte
    {
        /// <summary>A name, a bracket, a separator, space: the text's own colour.</summary>
        Plain,
        Keyword,
        Number,
        String,
        Comment,

        /// <summary>A system task or function: <c>$display</c>, <c>$clog2</c>.</summary>
        System,

        /// <summary>An operator: <c>+ - * / % = ! &amp; | ^ ~ ?</c>, as the browser's mode has them (it reads <c>&lt; &gt; :</c> as plain).</summary>
        Operator,
    }

    /// <summary>
    /// What each character of SystemVerilog is, for colouring it (5.1, Ahmad: "all the text is white" in the desktop's editor):
    /// the browser's editor colours code by CodeMirror's Verilog mode, and this follows that mode's rules, token for token, so
    /// a line reads in the same colours in both builds.
    /// </summary>
    /// <remarks>
    /// One pass, never throwing on code half typed: a comment or a string not closed runs to the end. Keywords are the
    /// simulator's own (<see cref="Keywords"/>), so there is no second list of them to drift. As the browser's mode has it, a
    /// back-tick name (<c>`include</c>) and a delay (<c>#5</c>) are plain, a system name and an operator one colour, and a
    /// sized literal's base, digits and size one number.
    /// </remarks>
    public static class CodeColouring
    {
        /// <summary>The kind of every character of <paramref name="text"/>, one to a character.</summary>
        public static CodeKind[] Classify(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<CodeKind>();
            var kinds = new CodeKind[text.Length];
            Classify(text, kinds);
            return kinds;
        }

        /// <summary>The kinds of <paramref name="text"/>'s characters written into <paramref name="kinds"/>, at least as long: no allocation.</summary>
        public static void Classify(string text, CodeKind[] kinds)
        {
            int n = text?.Length ?? 0;
            int at = 0;
            while (at < n)
            {
                char c = text[at];

                // Comments: to the line's end, or across lines to */ -- or to the end where it never comes.
                if (c == '/' && at + 1 < n && (text[at + 1] == '/' || text[at + 1] == '*'))
                {
                    int end;
                    if (text[at + 1] == '/')
                    {
                        end = text.IndexOf('\n', at);
                        if (end < 0)
                            end = n;
                    }
                    else
                    {
                        end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                        end = end < 0 ? n : end + 2;
                    }
                    at = Fill(kinds, at, end, CodeKind.Comment);
                    continue;
                }

                // A string, its escapes included: to its closing quote, or the line's end where it has none.
                if (c == '"')
                {
                    int end = at + 1;
                    bool escaped = false;
                    while (end < n)
                    {
                        char d = text[end];
                        if (d == '\n' && !escaped)
                            break;
                        end++;
                        if (d == '"' && !escaped)
                            break;
                        escaped = !escaped && d == '\\';
                    }
                    at = Fill(kinds, at, end, CodeKind.String);
                    continue;
                }

                // A system name, $display: the mode's "meta", as an operator is.
                if (c == '$' && at + 1 < n && IsWord(text[at + 1]))
                {
                    at = Fill(kinds, at, WordEnd(text, at + 1), CodeKind.System);
                    continue;
                }

                // A back-tick name, `timescale: plain, as the mode has it. So is a delay, #5.
                if (c == '`' && at + 1 < n && IsWord(text[at + 1]))
                {
                    at = Fill(kinds, at, WordEnd(text, at + 1), CodeKind.Plain);
                    continue;
                }
                if (c == '#')
                {
                    int end = at + 1;
                    while (end < n && (char.IsDigit(text[end]) || text[end] == '_' || text[end] == '.'))
                        end++;
                    at = Fill(kinds, at, end, CodeKind.Plain);
                    continue;
                }

                // A number: sized and based, a real, or a plain decimal.
                int number = NumberEnd(text, at);
                if (number > at)
                {
                    at = Fill(kinds, at, number, CodeKind.Number);
                    continue;
                }

                // A word: a keyword where the simulator reads one.
                if (IsWord(c))
                {
                    int end = WordEnd(text, at);
                    at = Fill(kinds, at, end, Keywords.IsKeyword(text.Substring(at, end - at)) ? CodeKind.Keyword : CodeKind.Plain);
                    continue;
                }

                kinds[at] = IsOperator(c) ? CodeKind.Operator : CodeKind.Plain;
                at++;
            }
        }

        private static int Fill(CodeKind[] kinds, int from, int to, CodeKind kind)
        {
            for (int i = from; i < to; i++)
                kinds[i] = kind;
            return to;
        }

        private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        private static int WordEnd(string text, int at)
        {
            while (at < text.Length && IsWord(text[at]))
                at++;
            return at;
        }

        private static bool IsOperator(char c) =>
            c == '+' || c == '-' || c == '*' || c == '/' || c == '!' || c == '~' || c == '&' || c == '|' || c == '^' || c == '%'
            || c == '=' || c == '?';

        /// <summary>
        /// Where a number starting at <paramref name="at"/> ends, or <paramref name="at"/> where none starts there: a based
        /// literal with or without a size (<c>4'b10x1</c>, <c>'hFF</c>, <c>8'sd5</c>, spaces allowed round the quote and the
        /// base, as the mode allows them), a real (<c>1.5</c>, <c>2e-3</c>), or a decimal (<c>42</c>, <c>1_000</c>). Not inside a
        /// name: <c>a1</c> is a name.
        /// </summary>
        private static int NumberEnd(string text, int at)
        {
            int n = text.Length;
            char c = text[at];
            if (!(char.IsDigit(c) || c == '\''))
                return at;
            if (at > 0 && IsWord(text[at - 1]))
                return at;   // a digit inside a name, or after one

            int i = at;
            while (i < n && (char.IsDigit(text[i]) || text[i] == '_'))
                i++;

            // A base: the size's digits, spaces, a quote, an s, the base letter, spaces, and its digits.
            int j = i;
            while (j < n && text[j] == ' ')
                j++;
            if (j < n && text[j] == '\'')
            {
                int k = j + 1;
                if (k < n && (text[k] == 's' || text[k] == 'S'))
                    k++;
                if (k < n && IsBase(text[k]))
                {
                    char b = char.ToLowerInvariant(text[k]);
                    k++;
                    while (k < n && text[k] == ' ')
                        k++;
                    int digits = k;
                    while (k < n && IsDigitOf(b, text[k]))
                        k++;
                    if (k > digits)
                        return k;
                }
                return i > at ? i : at;   // a quote with no base after it: the size alone, if there was one
            }

            if (i == at)
                return at;   // a quote alone

            // A real: digits, a point and digits, and an exponent; or digits and an exponent.
            int r = i;
            if (r + 1 < n && text[r] == '.' && char.IsDigit(text[r + 1]))
            {
                r += 2;
                while (r < n && (char.IsDigit(text[r]) || text[r] == '_'))
                    r++;
            }
            if (r < n && (text[r] == 'e' || text[r] == 'E'))
            {
                int e = r + 1;
                if (e < n && text[e] == '-')
                    e++;
                if (e < n && char.IsDigit(text[e]))
                {
                    while (e < n && (char.IsDigit(text[e]) || text[e] == '_'))
                        e++;
                    r = e;
                }
            }
            return r;
        }

        private static bool IsBase(char c) => "dDbBoOhH".IndexOf(c) >= 0;

        private static bool IsDigitOf(char b, char c)
        {
            char l = char.ToLowerInvariant(c);
            if (l == '_' || l == 'x' || l == 'z')
                return true;
            switch (b)
            {
                case 'b':
                    return l == '0' || l == '1';
                case 'o':
                    return l >= '0' && l <= '7';
                case 'd':
                    return char.IsDigit(l);
                default:
                    return char.IsDigit(l) || (l >= 'a' && l <= 'f') || l == '?';
            }
        }
    }
}
