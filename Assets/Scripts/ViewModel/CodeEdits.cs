using System;
using System.Text;

namespace BitSorter.ViewModel
{
    /// <summary>A text edited, and where its selection is after: from <see cref="Start"/> to <see cref="End"/>, the caret at <see cref="End"/>.</summary>
    public readonly struct CodeEdit
    {
        public CodeEdit(string text, int start, int end)
        {
            Text = text;
            Start = start;
            End = end;
        }

        public string Text { get; }
        public int Start { get; }
        public int End { get; }
    }

    /// <summary>
    /// The code editor's edits of whole lines (Part 14g), Unity-free: Tab and Shift+Tab indent and outdent the lines the
    /// selection touches by four spaces, and Ctrl+/ comments them out with <c>//</c> or back in -- what the browser's
    /// CodeMirror does with the same keys, so the editor in a desktop build answers them alike.
    /// </summary>
    /// <remarks>
    /// **Four spaces, never a tab**: the browser's editor indents by four (its <c>indentUnit</c>), and a tab in the text
    /// would line up differently in each editor and in the course's own. Every edit keeps the selection on the same
    /// characters, so a second press acts on the same lines.
    /// </remarks>
    public static class CodeEdits
    {
        /// <summary>One step of indentation.</summary>
        public const string Unit = "    ";

        /// <summary>The lines from <paramref name="start"/> to <paramref name="end"/> indented by <see cref="Unit"/> (Tab).</summary>
        public static CodeEdit Indent(string text, int start, int end) =>
            EachLine(text, start, end, line => (line.Length == 0 ? line : Unit + line, 0));

        /// <summary>The lines from <paramref name="start"/> to <paramref name="end"/> outdented by up to <see cref="Unit"/> (Shift+Tab).</summary>
        public static CodeEdit Outdent(string text, int start, int end) =>
            EachLine(text, start, end, line =>
            {
                int spaces = 0;
                while (spaces < Unit.Length && spaces < line.Length && line[spaces] == ' ')
                    spaces++;
                if (spaces == 0 && line.Length > 0 && line[0] == '\t')
                    spaces = 1;
                return (line.Substring(spaces), 0);
            });

        /// <summary>
        /// The lines from <paramref name="start"/> to <paramref name="end"/> commented out with <c>// </c> at the shallowest
        /// indentation among them, or, when every line with anything on it already starts with <c>//</c>, back in (Ctrl+/).
        /// Blank lines are left as they are.
        /// </summary>
        public static CodeEdit ToggleComment(string text, int start, int end)
        {
            text = text ?? string.Empty;
            start = Clamp(start, text);
            end = Clamp(end, text);
            (int first, int last) = LinesOf(text, start, end);
            string[] lines = Lines(text);

            bool commented = true;
            int shallowest = int.MaxValue;
            for (int i = first; i <= last; i++)
            {
                string trimmed = lines[i].TrimStart(' ', '\t');
                if (trimmed.Length == 0)
                    continue;
                commented &= trimmed.StartsWith("//", StringComparison.Ordinal);
                shallowest = Math.Min(shallowest, lines[i].Length - trimmed.Length);
            }

            if (shallowest == int.MaxValue)
                return new CodeEdit(text, start, end);   // only blank lines: nothing to comment

            return EachLine(text, start, end, line =>
            {
                string trimmed = line.TrimStart(' ', '\t');
                if (trimmed.Length == 0)
                    return (line, 0);
                int indent = line.Length - trimmed.Length;
                if (commented)
                {
                    string after = trimmed.Substring(2);
                    return (line.Substring(0, indent) + (after.StartsWith(" ", StringComparison.Ordinal) ? after.Substring(1) : after), indent);
                }

                return (line.Substring(0, shallowest) + "// " + line.Substring(shallowest), shallowest);
            });
        }

        /// <summary>
        /// Every line the selection touches changed by <paramref name="change"/> -- one insertion or one deletion in the line,
        /// at the column it returns -- and the selection kept on the same characters.
        /// </summary>
        private static CodeEdit EachLine(string text, int start, int end, Func<string, (string Line, int At)> change)
        {
            text = text ?? string.Empty;
            start = Clamp(start, text);
            end = Clamp(end, text);
            (int first, int last) = LinesOf(text, start, end);
            string[] lines = Lines(text);

            var edited = new StringBuilder(text.Length + 16);
            int offset = 0, newStart = start, newEnd = end;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                (string next, int at) = i >= first && i <= last ? change(line) : (line, 0);
                newStart = Moved(start, newStart, offset, line, next, at);
                newEnd = Moved(end, newEnd, offset, line, next, at);
                edited.Append(next);
                if (i < lines.Length - 1)
                    edited.Append('\n');
                offset += line.Length + 1;
            }

            string result = edited.ToString();
            return new CodeEdit(result, Clamp(newStart, result), Clamp(newEnd, result));
        }

        /// <summary>
        /// Where <paramref name="point"/> in the old text lands, so far at <paramref name="movedSoFar"/>, once the line starting
        /// at <paramref name="offset"/> has changed from <paramref name="line"/> to <paramref name="next"/> by one insertion or
        /// deletion at column <paramref name="at"/>: a point after the line moves by the change; one on it moves only if it is
        /// past the change, and never into what was deleted.
        /// </summary>
        private static int Moved(int point, int movedSoFar, int offset, string line, string next, int at)
        {
            int delta = next.Length - line.Length;
            if (delta == 0 || point < offset)
                return movedSoFar;
            if (point > offset + line.Length)
                return movedSoFar + delta;

            int column = point - offset;
            if (delta > 0)
                return column >= at ? movedSoFar + delta : movedSoFar;
            return column <= at ? movedSoFar : movedSoFar - column + Math.Max(at, column + delta);
        }

        /// <summary>The first and last line, from 0, that the selection from <paramref name="start"/> to <paramref name="end"/> touches.</summary>
        private static (int First, int Last) LinesOf(string text, int start, int end)
        {
            int from = Math.Min(start, end), to = Math.Max(start, end);

            // A selection that ends at the very start of a line does not take that line too, as editors do.
            if (to > from && text[to - 1] == '\n')
                to--;
            return (LineAt(text, from), LineAt(text, to));
        }

        private static int LineAt(string text, int offset)
        {
            int line = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
                if (text[i] == '\n')
                    line++;
            return line;
        }

        private static string[] Lines(string text) => text.Split('\n');

        private static int Clamp(int at, string text) => Math.Max(0, Math.Min(at, text.Length));
    }
}
