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
    ///
    /// **It makes nothing for a value or a key it meets, only for each kind of key** (the newer-save review): the save is
    /// read in the first frame, beside the launch's open, and a browser frees memory only between frames. A string is passed
    /// over where it lies, never built; a key is matched where it lies against the keys met before at its place, and made
    /// only the first time. A 5 MB save made 105 MB when every string was built again and every key's path made each time.
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
                bool key = scan.Key(out int start, out int length, out string escaped);
                scan.Space();
                if (!key || scan.End || scan.Peek != ':')
                {
                    sure = !scan.End;   // cut short where the text ended; no save of BitSorter's where it did not
                    return null;
                }
                scan.Next();
                scan.Space();
                if (scan.Is(StampKey, start, length, escaped))
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
            List<KeyNode> writes = Keys(written);
            var kept = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < writes.Count; i++)
                kept.Add(writes[i].Path);

            List<KeyNode> reads = Keys(read);
            var dropped = new List<string>();
            for (int i = 0; i < reads.Count; i++)
            {
                string path = reads[i].Path;
                if (!kept.Contains(path) && !IsRetired(path) && !dropped.Contains(path))
                    dropped.Add(path);
            }
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
            for (int i = 0; i < Retired.Count; i++)
            {
                string retired = Retired[i];
                if (path == retired || path.StartsWith(retired + ".", StringComparison.Ordinal) || path.StartsWith(retired + "[]", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Every key of <paramref name="text"/>, each place once, in the order first met.</summary>
        private static List<KeyNode> Keys(string text)
        {
            var met = new List<KeyNode>();
            var scan = new Scanner(text ?? "");
            scan.Space();
            Walk(scan, new KeyNode("", ""), met, 0);
            return met;
        }

        /// <summary>Names the keys of the value at the scanner, under <paramref name="node"/>; false where the text gives out.</summary>
        private static bool Walk(Scanner scan, KeyNode node, List<KeyNode> met, int depth)
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
                    bool key = scan.Key(out int start, out int length, out string escaped);
                    scan.Space();
                    if (!key || scan.End || scan.Peek != ':')
                        return false;
                    scan.Next();
                    if (!Walk(scan, node.Child(scan, start, length, escaped, met), met, depth + 1))
                        return false;
                }
            }

            if (first == '[')
            {
                scan.Next();
                KeyNode element = node.Element();
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
                    if (!Walk(scan, element, met, depth + 1))
                        return false;
                }
            }

            return scan.Skip();
        }

        /// <summary>
        /// A place keys are met at, by its path from the top: a key, or every element of an array (<see cref="Element"/>). Made
        /// once for each place, however many times the save has it.
        /// </summary>
        private sealed class KeyNode
        {
            /// <summary>The path from the top: <c>projects[].project.modules</c>.</summary>
            public readonly string Path;

            /// <summary>The key, as the save names it once its escapes are undone; empty for the top and an array's element.</summary>
            private readonly string _name;

            private List<KeyNode> _keys;
            private KeyNode _element;

            public KeyNode(string path, string name)
            {
                Path = path;
                _name = name;
            }

            /// <summary>
            /// The key under this place that <paramref name="scan"/>'s text has at <paramref name="start"/> for
            /// <paramref name="length"/> characters -- or, for one with an escape in it, <paramref name="escaped"/> -- found
            /// among those met before, by index, or made and added to <paramref name="met"/> the first time.
            /// </summary>
            public KeyNode Child(Scanner scan, int start, int length, string escaped, List<KeyNode> met)
            {
                if (_keys != null)
                    for (int i = 0; i < _keys.Count; i++)
                        if (scan.Is(_keys[i]._name, start, length, escaped))
                            return _keys[i];

                string name = escaped ?? scan.Cut(start, length);
                var made = new KeyNode(Path.Length == 0 ? name : Path + "." + name, name);
                (_keys ??= new List<KeyNode>()).Add(made);
                met.Add(made);
                return made;
            }

            /// <summary>Every element of an array at this place: one place, made the first time and never itself a key.</summary>
            public KeyNode Element() => _element ??= new KeyNode(Path + "[]", "");
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

            /// <summary>The text from <paramref name="start"/>, <paramref name="length"/> characters long.</summary>
            public string Cut(int start, int length) => _text.Substring(start, length);

            /// <summary>
            /// Whether the key at <paramref name="start"/> for <paramref name="length"/> characters -- or
            /// <paramref name="escaped"/>, one with an escape in it -- is <paramref name="name"/>, compared where it lies.
            /// </summary>
            public bool Is(string name, int start, int length, string escaped) =>
                escaped != null
                    ? string.Equals(name, escaped, StringComparison.Ordinal)
                    : name.Length == length && string.CompareOrdinal(name, 0, _text, start, length) == 0;

            /// <summary>
            /// Past the key at the scanner: where its text lies, <paramref name="start"/> for <paramref name="length"/>
            /// characters, or for one with an escape in it -- seldom -- the key itself, <paramref name="escaped"/>, its escapes
            /// undone; false where there is none, or it never ends, the scanner left where <see cref="String"/> leaves it.
            /// </summary>
            public bool Key(out int start, out int length, out string escaped)
            {
                start = 0;
                length = 0;
                escaped = null;
                if (End || Peek != '"')
                    return false;
                for (int at = _at + 1; at < _text.Length; at++)
                {
                    char c = _text[at];
                    if (c == '"')
                    {
                        start = _at + 1;
                        length = at - start;
                        _at = at + 1;
                        return true;
                    }
                    if (c == '\\')
                    {
                        escaped = String();
                        return escaped != null;
                    }
                }
                _at = _text.Length;   // it never ends
                return false;
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
                            if (!Hex4(out int code))
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
            /// Past the string at the scanner as <see cref="String"/> reads it, without making it; false where there is none,
            /// or it never ends.
            /// </summary>
            public bool SkipString()
            {
                if (End || Peek != '"')
                    return false;
                _at++;
                while (_at < _text.Length)
                {
                    char c = _text[_at++];
                    if (c == '"')
                        return true;
                    if (c != '\\')
                        continue;
                    if (_at >= _text.Length)
                        return false;
                    if (_text[_at++] == 'u')
                    {
                        if (!Hex4(out _))
                            return false;
                        _at += 4;
                    }
                }

                return false;
            }

            /// <summary>The four hex digits of a <c>\u</c> escape at the scanner, read where they lie; false where there are not four.</summary>
            private bool Hex4(out int code)
            {
                code = 0;
                if (_at + 4 > _text.Length)
                    return false;
                for (int i = 0; i < 4; i++)
                {
                    char h = _text[_at + i];
                    int digit = h >= '0' && h <= '9' ? h - '0'
                        : h >= 'a' && h <= 'f' ? h - 'a' + 10
                        : h >= 'A' && h <= 'F' ? h - 'A' + 10
                        : -1;
                    if (digit < 0)
                        return false;
                    code = code * 16 + digit;
                }
                return true;
            }

            /// <summary>
            /// Past the value at the scanner, however deep, without a call for each level and without making any of it; false
            /// where the text gives out before the value ends.
            /// </summary>
            public bool Skip()
            {
                Space();
                if (End)
                    return false;
                if (Peek == '"')
                    return SkipString();
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
                        if (!SkipString())
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
