using System;
using System.Collections.Generic;
using System.Text;

namespace BitSorter.Board
{
    /// <summary>
    /// What written code says of its own lines in comments (Ahmad, 2026-11-02: every part of the processors described): a
    /// part of the drawing of code is described first by the comment for the line it comes from, and a block with none of
    /// its own by the comment over its module. General, so any commented design describes itself, a student's included.
    /// </summary>
    public static class CodeComments
    {
        /// <summary>
        /// The comment the code writes for line <paramref name="line"/> (from 1) of <paramref name="text"/>: a <c>//</c> comment
        /// at the end of that line, else the <c>//</c> lines right above it -- a blank line or code ends them -- joined with
        /// spaces; null for none. A banner (<c>// ---- Fetch ----</c>, a line of rule characters alone among them) ends them as
        /// a divider, and so does a commented-out line of code, one ending in <c>;</c>.
        /// </summary>
        public static string For(string text, int line)
        {
            if (string.IsNullOrEmpty(text) || line < 1)
                return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (line > lines.Length)
                return null;

            string trailing = Trailing(lines[line - 1]);
            if (trailing != null)
                return trailing;

            return Above(lines, line - 1);
        }

        /// <summary>
        /// The comment over the module <paramref name="text"/> writes: the <c>//</c> lines right above its <c>module</c>, as
        /// <see cref="For"/> reads a line's, else one at the end of the <c>module</c> line; null for none.
        /// </summary>
        public static string OverModule(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("module ", StringComparison.Ordinal) || trimmed.StartsWith("module\t", StringComparison.Ordinal))
                    return Above(lines, i) ?? Trailing(lines[i]);
            }
            return null;
        }

        /// <summary>
        /// A comment's first sentence -- up to a full stop followed by a capital -- what a one-line description takes; the
        /// whole comment where it is one sentence.
        /// </summary>
        public static string FirstSentence(string comment)
        {
            if (string.IsNullOrEmpty(comment))
                return comment;
            for (int i = 1; i + 2 < comment.Length; i++)
                if (comment[i] == '.' && comment[i + 1] == ' ' && char.IsUpper(comment[i + 2]) && char.IsLetterOrDigit(comment[i - 1])
                    && (i < 2 || comment[i - 2] != '.'))   // not the end of e.g. or i.e.
                    return comment.Substring(0, i + 1);
            return comment;
        }

        /// <summary>The <c>//</c> lines right above line <paramref name="index"/> (from 0), joined; null for none.</summary>
        private static string Above(string[] lines, int index)
        {
            var above = new List<string>();
            for (int i = index - 1; i >= 0; i--)
            {
                string trimmed = lines[i].Trim();
                // What an import's cutter put right over the first thing of a module's body: an include, and the blank line
                // after it. Only there -- an include of the code's own further up ends what speaks for the line, as code does.
                if (above.Count == 0 && (IsInclude(trimmed) || trimmed.Length == 0 && i > 0 && IsInclude(lines[i - 1].Trim())))
                    continue;
                if (!trimmed.StartsWith("//", StringComparison.Ordinal))
                    break;
                string words = Words(trimmed.Substring(2));
                if (words == null)
                    break;
                above.Add(words);
            }
            if (above.Count == 0)
                return null;
            above.Reverse();
            return string.Join(" ", above);
        }

        /// <summary>
        /// An <c>`include</c> line, passed over on the way up: an import's module cutter puts a file's includes at the head of
        /// each module's body, between a comment and the first thing it says something of.
        /// </summary>
        private static bool IsInclude(string trimmed) => trimmed.StartsWith("`include", StringComparison.Ordinal);

        /// <summary>
        /// The <c>//</c> comment at the end of a line of code, outside any string or <c>/* */</c> comment; null for none or a
        /// line that is all comment.
        /// </summary>
        private static string Trailing(string line)
        {
            bool inString = false, inBlock = false;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                char c = line[i], next = line[i + 1];
                if (inBlock)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlock = false;
                        i++;
                    }
                }
                else if (inString)
                {
                    if (c == '\\')
                        i++;   // what it escapes, a quote or a backslash, is part of the string
                    else if (c == '"')
                        inString = false;
                }
                else if (c == '"')
                    inString = true;
                else if (c == '/' && next == '*')
                {
                    inBlock = true;
                    i++;
                }
                else if (c == '/' && next == '/')
                {
                    if (line.Substring(0, i).Trim().Length == 0)
                        return null;   // a line of comment alone: one of the lines above the next
                    return Words(line.Substring(i + 2));
                }
            }
            return null;
        }

        /// <summary>
        /// A comment's words, trimmed; null for none: a divider of rule characters alone, a banner that starts with a run of
        /// them (<c>// ===== Fetch =====</c>), or a line of code commented out -- one ending in <c>;</c> with an <c>=</c> in it
        /// or a keyword first. A line of prose may end in a semicolon too, and goes on.
        /// </summary>
        private static string Words(string comment)
        {
            string words = comment.TrimStart('/').Trim();   // a /// comment's third slash is not a word of it
            if (words.Length == 0 || IsCode(words))
                return null;
            int rule = 0;
            while (rule < words.Length && IsRule(words[rule]))
                rule++;
            if (rule >= 3)
                return null;
            foreach (char c in words)
                if (char.IsLetterOrDigit(c))
                    return words;
            return null;
        }

        private static readonly string[] CodeFirst =
            { "assign ", "always", "logic ", "wire ", "reg ", "input ", "output ", "inout ", "$" };

        private static bool IsCode(string words)
        {
            if (!words.EndsWith(";", StringComparison.Ordinal))
                return false;
            if (words.IndexOf('=') >= 0)
                return true;
            foreach (string first in CodeFirst)
                if (words.StartsWith(first, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool IsRule(char c) => c == '-' || c == '=' || c == '*' || c == '#' || c == '~' || c == '_' || c == '+';
    }
}
