using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What is read of the save's own text, beside what Unity's reader makes of it (Ahmad's decisions, 2026-11-03): its stamp,
    /// which says whether another window -- a second tab, or a second BitSorter -- has written the save since this one read
    /// it; and what in it this BitSorter does not know, which a save from a newer one holds and this one would drop as it
    /// writes.
    /// </summary>
    /// <remarks>
    /// The text is JSON, read here by a scanner of its own that keeps nothing of a value but its keys: the view's reader
    /// fills the save's own fields and passes over every key it has none for, without a word, so what it leaves out can only
    /// be found by setting what it read beside what it writes back. Nothing here throws, whatever the text: a save is the one
    /// file a crash truncates and a student edits by hand, and its reader already says what it could not read.
    /// </remarks>
    public static class SaveFile
    {
        /// <summary>The key the stamp is kept under, first in the file so a glance at its head finds it.</summary>
        public const string StampKey = "stamp";

        /// <summary>
        /// The save's keys an older BitSorter wrote and this one reads no more: the 4.x game's boards, free play and blocks,
        /// which went with it in Part 16a. A file that has them is an old one, not a newer one's.
        /// </summary>
        public static readonly IReadOnlyList<string> Retired = new[] { "boards", "freePlay", "blocks" };

        /// <summary>How deep the scanner names keys: past it, a value is passed over whole, so no text can run it out of stack.</summary>
        private const int MostDepth = 64;

        /// <summary>
        /// The stamp <paramref name="text"/> holds at its top -- or null for none -- and whether that is <paramref name="sure"/>:
        /// false when the text ends before the stamp or the end of the save is reached, as the head of a file read alone may.
        /// </summary>
        public static string StampOf(string text, out bool sure)
        {
            var scan = new Scanner(text ?? "");
            sure = true;
            scan.Space();
            if (scan.End)
                return null;
            if (scan.Peek != '{')
                return null;   // no save of BitSorter's, and so no stamp in it
            scan.Next();
            while (true)
            {
                scan.Space();
                if (scan.End)
                {
                    sure = false;
                    return null;
                }
                if (scan.Peek == '}')
                    return null;
                if (scan.Peek == ',')
                {
                    scan.Next();
                    continue;
                }
                string key = scan.String();
                scan.Space();
                if (key == null || scan.End || scan.Peek != ':')
                {
                    sure = !scan.End;   // cut short where the text ended; no save of BitSorter's where it did not
                    return null;
                }
                scan.Next();
                scan.Space();
                if (key == StampKey)
                {
                    if (scan.End)
                    {
                        sure = false;
                        return null;
                    }
                    if (scan.Peek != '"')
                        return null;   // null, or no string: no stamp
                    string stamp = scan.String();
                    sure = stamp != null;
                    return string.IsNullOrEmpty(stamp) ? null : stamp;
                }
                if (!scan.Skip())
                {
                    sure = false;
                    return null;
                }
            }
        }

        /// <summary>
        /// Every key <paramref name="read"/> has that <paramref name="written"/> does not, as a path -- <c>recent</c>,
        /// <c>projects[].project.glow</c> -- in the order first met, <see cref="Retired"/> keys and all under them aside:
        /// what the save read holds that this BitSorter writes no place for, and so would drop as it writes.
        /// </summary>
        public static List<string> NotKept(string read, string written)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Keys(written))
                kept.Add(path);

            var dropped = new List<string>();
            foreach (string path in Keys(read))
                if (!kept.Contains(path) && !IsRetired(path) && !dropped.Contains(path))
                    dropped.Add(path);
            return dropped;
        }

        /// <summary>
        /// Why the save read is a newer BitSorter's, in words -- its format past <paramref name="ours"/>, or a key this one
        /// does not know (<see cref="NotKept"/>) -- or null for one this BitSorter writes all of.
        /// </summary>
        public static string Newer(int format, int ours, string read, string written)
        {
            if (format > ours)
                return "its format is " + format.ToString(CultureInfo.InvariantCulture) + ", and this BitSorter's is " +
                       ours.ToString(CultureInfo.InvariantCulture);
            List<string> dropped = NotKept(read, written);
            return dropped.Count == 0 ? null : "it holds " + dropped[0] + ", which this BitSorter does not know";
        }

        /// <summary>
        /// Where a save from a newer BitSorter at <paramref name="path"/> is copied before this one first writes over it:
        /// beside it, dated <paramref name="when"/>, as the editor's safety copies are.
        /// </summary>
        public static string NewerCopyOf(string path, DateTime when) =>
            path + ".newer-version-" + when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        private static bool IsRetired(string path)
        {
            foreach (string retired in Retired)
                if (path == retired || path.StartsWith(retired + ".", StringComparison.Ordinal) || path.StartsWith(retired + "[]", StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>Every key of <paramref name="text"/>, as a path from its top, each once.</summary>
        private static List<string> Keys(string text)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var scan = new Scanner(text ?? "");
            scan.Space();
            Walk(scan, "", keys, seen, 0);
            return keys;
        }

        /// <summary>Names the keys of the value at the scanner, under <paramref name="path"/>; false where the text gives out.</summary>
        private static bool Walk(Scanner scan, string path, List<string> keys, HashSet<string> seen, int depth)
        {
            scan.Space();
            if (scan.End)
                return false;
            if (depth >= MostDepth)
                return scan.Skip();

            char first = scan.Peek;
            if (first == '{')
            {
                scan.Next();
                while (true)
                {
                    scan.Space();
                    if (scan.End)
                        return false;
                    if (scan.Peek == '}')
                    {
                        scan.Next();
                        return true;
                    }
                    if (scan.Peek == ',')
                    {
                        scan.Next();
                        continue;
                    }
                    string key = scan.String();
                    scan.Space();
                    if (key == null || scan.End || scan.Peek != ':')
                        return false;
                    scan.Next();
                    string at = path.Length == 0 ? key : path + "." + key;
                    if (seen.Add(at))
                        keys.Add(at);
                    if (!Walk(scan, at, keys, seen, depth + 1))
                        return false;
                }
            }

            if (first == '[')
            {
                scan.Next();
                while (true)
                {
                    scan.Space();
                    if (scan.End)
                        return false;
                    if (scan.Peek == ']')
                    {
                        scan.Next();
                        return true;
                    }
                    if (scan.Peek == ',')
                    {
                        scan.Next();
                        continue;
                    }
                    if (!Walk(scan, path + "[]", keys, seen, depth + 1))
                        return false;
                }
            }

            return scan.Skip();
        }

        /// <summary>A place in JSON text, read forward.</summary>
        private sealed class Scanner
        {
            private readonly string _text;
            private int _at;

            public Scanner(string text) => _text = text;

            public bool End => _at >= _text.Length;

            public char Peek => _text[_at];

            public void Next() => _at++;

            public void Space()
            {
                while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                    _at++;
            }

            /// <summary>The string at the scanner, its escapes undone; null where there is none, or it never ends.</summary>
            public string String()
            {
                if (End || Peek != '"')
                    return null;
                _at++;
                var made = new StringBuilder();
                while (_at < _text.Length)
                {
                    char c = _text[_at++];
                    if (c == '"')
                        return made.ToString();
                    if (c != '\\')
                    {
                        made.Append(c);
                        continue;
                    }
                    if (_at >= _text.Length)
                        return null;
                    char e = _text[_at++];
                    switch (e)
                    {
                        case 'b': made.Append('\b'); break;
                        case 'f': made.Append('\f'); break;
                        case 'n': made.Append('\n'); break;
                        case 'r': made.Append('\r'); break;
                        case 't': made.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _text.Length ||
                                !int.TryParse(_text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                return null;
                            made.Append((char)code);
                            _at += 4;
                            break;
                        default: made.Append(e); break;
                    }
                }

                return null;
            }

            /// <summary>
            /// Past the value at the scanner, however deep, without a call for each level; false where the text gives out
            /// before the value ends.
            /// </summary>
            public bool Skip()
            {
                Space();
                if (End)
                    return false;
                if (Peek == '"')
                    return String() != null;
                if (Peek != '{' && Peek != '[')
                {
                    // A number, true, false or null: up to what ends it.
                    while (_at < _text.Length && _text[_at] != ',' && _text[_at] != '}' && _text[_at] != ']' && !char.IsWhiteSpace(_text[_at]))
                        _at++;
                    return true;
                }

                int depth = 0;
                while (_at < _text.Length)
                {
                    char c = _text[_at];
                    if (c == '"')
                    {
                        if (String() == null)
                            return false;
                        continue;
                    }
                    _at++;
                    if (c == '{' || c == '[')
                        depth++;
                    else if ((c == '}' || c == ']') && --depth == 0)
                        return true;
                }

                return false;
            }
        }
    }
}
