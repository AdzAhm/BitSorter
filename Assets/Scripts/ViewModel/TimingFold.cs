using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>Which way a TIMING row's arrow points: none, shut (a click opens it), or open (a click shuts it).</summary>
    public enum TimingArrow { None, Shut, Open }

    /// <summary>A row of the TIMING tab on screen: the line it is part of, which part, what it says, and its arrow.</summary>
    public readonly struct TimingRow
    {
        public TimingRow(int line, int part, string text, TimingArrow arrow)
        {
            Line = line;
            Part = part;
            Text = text;
            Arrow = arrow;
        }

        /// <summary>The line's place in the view's lines: what a click on the row picks.</summary>
        public int Line { get; }

        /// <summary>-1 for the line's own row, with its label and numbers; 0, 1 and on for the rows its text runs on to once opened.</summary>
        public int Part { get; }

        /// <summary>What the row says: the whole line, or the piece of it on this row once the line is opened.</summary>
        public string Text { get; }

        public TimingArrow Arrow { get; }
    }

    /// <summary>
    /// The TIMING tab's lines as rows (Part 15h). A section -- a labelled line and the unlabelled lines under it, PATHS and its
    /// paths, MEASURED and its delays -- folds under an arrow, shut to begin with when it holds more than
    /// <see cref="ShutPast"/>; and a line too long for its row has an arrow that opens it onto the rows under it. Ahmad's
    /// imported ALU listed two hundred paths, each cut short, with no way to fold them away or to read one through
    /// (2026-10-28).
    /// </summary>
    /// <remarks>
    /// A choice is kept by what it is about, not where it is: a section by its label, a line by its label and text. The view
    /// is worked out again after every edit, and the paths move up and down the list, so a path opened stays open while it
    /// reads the same, and PATHS stays shut once shut. The rows are made again only when the lines, the room or a choice
    /// change, so the wheel only moves through them.
    /// </remarks>
    public sealed class TimingFold
    {
        /// <summary>A section of more lines than this starts shut.</summary>
        public const int ShutPast = 8;

        private readonly Dictionary<string, bool> _chosen = new Dictionary<string, bool>();
        private readonly List<TimingRow> _rows = new List<TimingRow>();
        private readonly List<string> _pieces = new List<string>();
        private IReadOnlyList<TimingLine> _lines;
        private float _room = float.NaN;
        private bool _stale = true;

        /// <summary>The rows as last made.</summary>
        public IReadOnlyList<TimingRow> Rows => _rows;

        /// <summary>
        /// The rows of <paramref name="lines"/> with <paramref name="room"/> for each row's text, measured by
        /// <paramref name="width"/>: made again only when the lines, the room or a choice have changed since.
        /// </summary>
        public IReadOnlyList<TimingRow> Of(IReadOnlyList<TimingLine> lines, float room, Func<string, float> width)
        {
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));
            if (width == null)
                throw new ArgumentNullException(nameof(width));
            if (!_stale && ReferenceEquals(lines, _lines) && room == _room)
                return _rows;

            _stale = false;
            _lines = lines;
            _room = room;
            _rows.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                TimingLine line = lines[i];
                int under = Under(lines, i);
                // No room yet -- a dock not laid out -- is no line too long: nothing to open until there is.
                bool overflows = room > 0f && width(line.Text) > room;
                if (under == 0 && !overflows)
                {
                    _rows.Add(new TimingRow(i, -1, line.Text, TimingArrow.None));
                    continue;
                }

                bool open = _chosen.TryGetValue(Key(lines, i), out bool chosen) ? chosen : under > 0 && under <= ShutPast;
                if (open && overflows)
                {
                    Wrap(line.Text, room, width, _pieces);
                    _rows.Add(new TimingRow(i, -1, _pieces[0], TimingArrow.Open));
                    for (int k = 1; k < _pieces.Count; k++)
                        _rows.Add(new TimingRow(i, k - 1, _pieces[k], TimingArrow.None));
                }
                else
                {
                    _rows.Add(new TimingRow(i, -1, line.Text, open ? TimingArrow.Open : TimingArrow.Shut));
                }

                if (!open)
                    i += under;
            }
            return _rows;
        }

        /// <summary>Opens row <paramref name="row"/>'s line if it is shut, or shuts it if it is open. True when anything changed.</summary>
        public bool Toggle(int row)
        {
            if (_lines == null || row < 0 || row >= _rows.Count || _rows[row].Arrow == TimingArrow.None)
                return false;
            _chosen[Key(_lines, _rows[row].Line)] = _rows[row].Arrow == TimingArrow.Shut;
            _stale = true;
            return true;
        }

        /// <summary>The first row line <paramref name="line"/> is on; -1 while it is folded away.</summary>
        public int RowOf(int line)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Line == line)
                    return i;
            return -1;
        }

        /// <summary>How many lines a section's line has under it: the unlabelled lines after a labelled one, up to the next labelled one.</summary>
        public static int Under(IReadOnlyList<TimingLine> lines, int line)
        {
            if (lines[line].Label.Length == 0)
                return 0;
            int under = 0;
            while (line + 1 + under < lines.Count && lines[line + 1 + under].Label.Length == 0)
                under++;
            return under;
        }

        private static string Key(IReadOnlyList<TimingLine> lines, int line) =>
            Under(lines, line) > 0 ? "\u0001" + lines[line].Label : lines[line].Label + "\u0001" + lines[line].Text;

        /// <summary>
        /// <paramref name="text"/> in rows of <paramref name="room"/>: cut after each ", " and at the wide gap between a path's
        /// ends and its gates, so a name is never split; a piece too long for a row of its own cut at its spaces; and a word too
        /// long for one, on a row by itself, to be cut short as it is drawn.
        /// </summary>
        internal static void Wrap(string text, float room, Func<string, float> width, List<string> into)
        {
            into.Clear();
            var pieces = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ',' && i + 1 < text.Length && text[i + 1] == ' ')
                {
                    pieces.Add(text.Substring(start, i + 2 - start));
                    start = i + 2;
                    i++;
                }
                else if (text[i] == ' ' && i + 2 < text.Length && text[i + 1] == ' ' && text[i + 2] == ' ')
                {
                    pieces.Add(text.Substring(start, i + 3 - start));
                    start = i + 3;
                    i += 2;
                }
            }
            if (start < text.Length)
                pieces.Add(text.Substring(start));

            string row = "";
            foreach (string piece in pieces)
            {
                if (width((row + piece).TrimEnd()) <= room)
                {
                    row += piece;
                    continue;
                }
                if (row.Length > 0)
                    into.Add(row.TrimEnd());
                row = "";
                if (width(piece.TrimEnd()) <= room)
                {
                    row = piece;
                    continue;
                }

                // A piece too long for a row: its words, as many to a row as fit.
                foreach (string word in piece.Split(' '))
                {
                    if (word.Length == 0)
                        continue;
                    string longer = row.Length == 0 ? word : row + " " + word;
                    if (row.Length == 0 || width(longer) <= room)
                    {
                        row = longer;
                        continue;
                    }
                    into.Add(row);
                    row = word;
                }
                row += " ";
            }
            if (row.TrimEnd().Length > 0 || into.Count == 0)
                into.Add(row.TrimEnd());
        }
    }
}
