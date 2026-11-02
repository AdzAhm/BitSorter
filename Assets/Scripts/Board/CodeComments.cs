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
        /// spaces; null for none. A line of nothing but rule characters (<c>// ----</c>) is a divider, not a comment.
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
        /// <see cref="For"/> reads a line's; null for none.
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
                    return Above(lines, i);
            }
            return null;
        }

        /// <summary>The <c>//</c> lines right above line <paramref name="index"/> (from 0), joined; null for none.</summary>
        private static string Above(string[] lines, int index)
        {
            var above = new List<string>();
            for (int i = index - 1; i >= 0; i--)
            {
                string trimmed = lines[i].Trim();
                if (IsInclude(trimmed) || trimmed.Length == 0 && i > 0 && IsInclude(lines[i - 1].Trim()))
                    continue;   // what an import's cutter put there: an include, and the blank line after it
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

        /// <summary>The <c>//</c> comment at the end of a line of code, outside any string; null for none or a line that is all comment.</summary>
        private static string Trailing(string line)
        {
            bool inString = false;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                    inString = !inString;
                else if (!inString && c == '/' && line[i + 1] == '/')
                {
                    if (line.Substring(0, i).Trim().Length == 0)
                        return null;   // a line of comment alone: one of the lines above the next
                    return Words(line.Substring(i + 2));
                }
            }
            return null;
        }

        /// <summary>A comment's words, trimmed; null for none, or for a divider of rule characters alone.</summary>
        private static string Words(string comment)
        {
            string words = comment.Trim();
            if (words.Length == 0)
                return null;
            foreach (char c in words)
                if (char.IsLetterOrDigit(c))
                    return words;
            return null;
        }
    }
}
