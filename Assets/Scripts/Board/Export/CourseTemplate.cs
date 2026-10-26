using System;
using System.Text.RegularExpressions;

namespace BitSorter.Board
{
    /// <summary>
    /// A course's homework file (Part 13e): a module whose body says where the student's code goes -- <c>// Put your code
    /// here</c>, and <c>// End of your code</c> after it -- with everything else given. A submission is the file as given,
    /// the student's code between the two.
    /// </summary>
    /// <remarks>
    /// Read in any case, since a few of the course's files open with <c>// put your code here</c>; and a file with no end
    /// marker takes the student's code up to its <c>endmodule</c>. One region a file: the course's have one each. A marker
    /// is a comment on a line of its own, as every one of the course's is, so a comment that quotes the markers -- "write
    /// between // Put your code here and ..." -- is not taken for one.
    /// </remarks>
    public sealed class CourseTemplate
    {
        private static readonly Regex Opener = new Regex(@"(?m)^([ \t]*)//\s*put\s+your\s+code\s+here\b[^\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Ender = new Regex(@"(?m)^[ \t]*//\s*end\s+of\s+your\s+code\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex EndModule = new Regex(@"(?m)^[ \t]*endmodule\b", RegexOptions.CultureInvariant);

        private CourseTemplate(string text, int start, int end, bool hasEnd, string indent)
        {
            Text = text;
            RegionStart = start;
            RegionEnd = end;
            HasEndMarker = hasEnd;
            Indent = indent;
        }

        /// <summary>The file as given, every character of it.</summary>
        public string Text { get; }

        /// <summary>Where the student's code begins: the start of the line after the opening marker's.</summary>
        public int RegionStart { get; }

        /// <summary>Where it ends: the start of the end marker's line, or of the <c>endmodule</c>'s when there is none.</summary>
        public int RegionEnd { get; }

        /// <summary>Whether the file closes the region with <c>// End of your code</c>.</summary>
        public bool HasEndMarker { get; }

        /// <summary>The opening marker's indent, which the student's code is given.</summary>
        public string Indent { get; }

        /// <summary>What the file gives in the student's place now: usually nothing.</summary>
        public string Region => Text.Substring(RegionStart, RegionEnd - RegionStart);

        /// <summary>Whether <paramref name="text"/> is a course template, and it as one.</summary>
        public static bool TryRead(string text, out CourseTemplate template)
        {
            template = null;
            if (string.IsNullOrEmpty(text))
                return false;

            Match opener = Opener.Match(text);
            if (!opener.Success)
                return false;

            string indent = opener.Groups[1].Value;
            int afterOpener = text.IndexOf('\n', opener.Index);
            int start = afterOpener < 0 ? text.Length : afterOpener + 1;

            Match ender = Ender.Match(text, start);
            if (ender.Success)
            {
                template = new CourseTemplate(text, start, ender.Index, true, indent);
                return true;
            }

            Match endmodule = EndModule.Match(text, start);
            if (!endmodule.Success)
                return false;
            template = new CourseTemplate(text, start, endmodule.Index, false, indent);
            return true;
        }

        /// <summary>
        /// The file with <paramref name="code"/> in the student's place, each line given the marker's indent unless it has one
        /// already, and ended as the file's own lines are -- the course's files end theirs in CR LF; everything the file gives
        /// kept as it is, every character.
        /// </summary>
        public string Fill(string code)
        {
            string body = Indented(code ?? "");
            return Text.Substring(0, RegionStart) + body + Text.Substring(RegionEnd);
        }

        /// <summary>
        /// The student's code in a text that keeps this template's markers -- what is between them -- or null when the markers
        /// are gone from it.
        /// </summary>
        public static string RegionIn(string text)
        {
            return TryRead(text, out CourseTemplate inside) ? inside.Region : null;
        }

        private string Indented(string code)
        {
            string[] lines = code.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length == 1 && lines[0].Trim().Length == 0)
                return "";

            // Already indented as a whole (a written module's own lines): kept as they are.
            bool indented = true;
            foreach (string line in lines)
                if (line.Trim().Length > 0 && !(line.StartsWith(" ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal)))
                    indented = false;

            string newline = Text.Contains("\r\n") ? "\r\n" : "\n";
            var filled = new System.Text.StringBuilder();
            foreach (string line in lines)
                filled.Append(line.Trim().Length == 0 ? "" : indented ? line : Indent + line).Append(newline);
            return filled.ToString();
        }
    }
}
