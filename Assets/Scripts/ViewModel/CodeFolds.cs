using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A block of code that folds (5.1): lines <see cref="FirstLine"/> to <see cref="LastLine"/>, counted from 0. Folded, it
    /// shows as its first line, a mark, and its last line's text from <see cref="To"/> on: <c>module ha ( … endmodule</c>.
    /// </summary>
    public readonly struct CodeFold : IEquatable<CodeFold>
    {
        public CodeFold(int firstLine, int lastLine, int from, int to)
        {
            FirstLine = firstLine;
            LastLine = lastLine;
            From = from;
            To = to;
        }

        /// <summary>The line it starts on, where its marker is: from 0.</summary>
        public int FirstLine { get; }

        /// <summary>The line it ends on.</summary>
        public int LastLine { get; }

        /// <summary>Where what it hides begins: the end of its first line, its line break.</summary>
        public int From { get; }

        /// <summary>Where what it hides ends: the first character of its last line that is not a space.</summary>
        public int To { get; }

        public bool Equals(CodeFold other) => FirstLine == other.FirstLine && LastLine == other.LastLine && From == other.From && To == other.To;
        public override bool Equals(object obj) => obj is CodeFold other && Equals(other);
        public override int GetHashCode() => (FirstLine * 397) ^ LastLine;
        public override string ToString() => (FirstLine + 1) + "-" + (LastLine + 1);
    }

    /// <summary>
    /// Where SystemVerilog folds (5.1, Ahmad, pointing at Notepad++'s margin: "the +/- thing in code that expands and
    /// unexpands things"): every block of more than one line -- a keyword's, <c>module</c> to <c>endmodule</c>,
    /// <c>begin</c> to <c>end</c> and their like; a bracket's, a port list or an instance's connections; and a comment's,
    /// a <c>/* */</c> or a run of lines that are only <c>//</c> comments. One fold to a line, the one that reaches furthest.
    /// </summary>
    /// <remarks>
    /// Comments and strings are passed over when keywords and brackets are looked for (<see cref="CodeColouring"/>). An
    /// ending meets the nearest opening of its kind, any left open between dropped -- an <c>extern function</c> has no
    /// <c>endfunction</c> -- and an ending with no opening, or an opening never ended, folds nothing. The browser's editor
    /// folds by the same rules (<c>Tools/codemirror/src/folds.js</c>), and a fixture both are tested on holds them to one answer.
    /// </remarks>
    public static class CodeFolds
    {
        // Each opening word and the words that end it.
        private static readonly Dictionary<string, string[]> Endings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["module"] = new[] { "endmodule" },
            ["macromodule"] = new[] { "endmodule" },
            ["begin"] = new[] { "end" },
            ["case"] = new[] { "endcase" },
            ["casex"] = new[] { "endcase" },
            ["casez"] = new[] { "endcase" },
            ["function"] = new[] { "endfunction" },
            ["task"] = new[] { "endtask" },
            ["generate"] = new[] { "endgenerate" },
            ["fork"] = new[] { "join", "join_any", "join_none" },
            ["interface"] = new[] { "endinterface" },
            ["package"] = new[] { "endpackage" },
            ["class"] = new[] { "endclass" },
        };

        private static readonly HashSet<string> Enders = AllEnders();

        private static HashSet<string> AllEnders()
        {
            var enders = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] words in Endings.Values)
                foreach (string word in words)
                    enders.Add(word);
            return enders;
        }

        /// <summary>The folds of <paramref name="text"/>, by their first lines, one to a line.</summary>
        public static IReadOnlyList<CodeFold> Of(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<CodeFold>();
            CodeKind[] kinds = CodeColouring.Classify(text);
            int[] lineStarts = LineStarts(text);
            var best = new Dictionary<int, CodeFold>();

            void Found(int open, int close)
            {
                int first = LineOf(lineStarts, open), last = LineOf(lineStarts, close);
                if (last <= first)
                    return;
                int from = EndOfLine(text, lineStarts, first);
                int to = FirstNonSpace(text, lineStarts, last);
                if (!best.TryGetValue(first, out CodeFold kept) || last > kept.LastLine)
                    best[first] = new CodeFold(first, last, from, to);
            }

            var words = new List<(string Word, int At)>();
            var brackets = new List<(char Bracket, int At)>();
            string previous = null;
            int at = 0;
            while (at < text.Length)
            {
                char c = text[at];
                CodeKind kind = kinds[at];

                // A /* comment that runs onto another line.
                if (kind == CodeKind.Comment && c == '/' && at + 1 < text.Length && text[at + 1] == '*')
                {
                    int end = at;
                    while (end < text.Length && kinds[end] == CodeKind.Comment)
                        end++;
                    Found(at, end - 1);
                    at = end;
                    continue;
                }
                if (kind == CodeKind.Comment || kind == CodeKind.String)
                {
                    at++;
                    continue;
                }

                if (kind == CodeKind.Keyword)
                {
                    int end = at;
                    while (end < text.Length && kinds[end] == CodeKind.Keyword && IsWordChar(text[end]))
                        end++;
                    string word = text.Substring(at, end - at);
                    if (Enders.Contains(word))
                        Close(words, word, at, Found);
                    else if (Endings.ContainsKey(word) && !(word == "fork" && (previous == "wait" || previous == "disable"))
                             && !(word == "class" && (previous == "typedef" || previous == "interface")))
                        words.Add((word, at));
                    previous = word;
                    at = end;
                    continue;
                }

                if (c == '(' || c == '[' || c == '{')
                    brackets.Add((c, at));
                else if (c == ')' || c == ']' || c == '}')
                {
                    char opening = c == ')' ? '(' : c == ']' ? '[' : '{';
                    for (int i = brackets.Count - 1; i >= 0; i--)
                    {
                        if (brackets[i].Bracket != opening)
                            continue;
                        Found(brackets[i].At, at);
                        brackets.RemoveRange(i, brackets.Count - i);
                        break;
                    }
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int end = at;
                    while (end < text.Length && IsWordChar(text[end]))
                        end++;
                    previous = text.Substring(at, end - at);
                    at = end;
                    continue;
                }
                at++;
            }

            // Runs of two or more lines that are only // comments.
            int run = -1;
            for (int line = 0; line <= lineStarts.Length; line++)
            {
                bool comment = line < lineStarts.Length && IsLineComment(text, kinds, lineStarts, line);
                if (comment && run < 0)
                    run = line;
                if (!comment && run >= 0)
                {
                    if (line - 1 > run)
                        Found(lineStarts[run], lineStarts[line - 1]);
                    run = -1;
                }
            }

            var folds = new List<CodeFold>(best.Values);
            folds.Sort((a, b) => a.FirstLine.CompareTo(b.FirstLine));
            return folds;
        }

        private static void Close(List<(string Word, int At)> words, string ender, int at, Action<int, int> found)
        {
            for (int i = words.Count - 1; i >= 0; i--)
            {
                if (Array.IndexOf(Endings[words[i].Word], ender) < 0)
                    continue;
                found(words[i].At, at);
                words.RemoveRange(i, words.Count - i);
                return;
            }
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        /// <summary>Where each line of <paramref name="text"/> starts.</summary>
        public static int[] LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < (text?.Length ?? 0); i++)
                if (text[i] == '\n')
                    starts.Add(i + 1);
            return starts.ToArray();
        }

        /// <summary>The line, from 0, of offset <paramref name="at"/>.</summary>
        public static int LineOf(int[] lineStarts, int at)
        {
            int index = Array.BinarySearch(lineStarts, at);
            return index >= 0 ? index : ~index - 1;
        }

        private static int EndOfLine(string text, int[] lineStarts, int line) =>
            line + 1 < lineStarts.Length ? lineStarts[line + 1] - 1 : text.Length;

        private static int FirstNonSpace(string text, int[] lineStarts, int line)
        {
            int end = EndOfLine(text, lineStarts, line);
            int at = lineStarts[line];
            while (at < end && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r'))
                at++;
            return at;
        }

        private static bool IsLineComment(string text, CodeKind[] kinds, int[] lineStarts, int line)
        {
            int at = FirstNonSpace(text, lineStarts, line);
            return at + 1 < text.Length && text[at] == '/' && text[at + 1] == '/' && kinds[at] == CodeKind.Comment;
        }
    }
}
