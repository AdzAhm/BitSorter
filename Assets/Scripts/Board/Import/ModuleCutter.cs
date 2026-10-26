using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>
    /// One file's modules cut into texts of their own (Part 13b): a written module is its own file in a project, so a file
    /// of several is cut at each <c>endmodule</c>. A module's text is the comments and directives just above it, then
    /// the module itself; in front go the <c>`define</c>s, <c>`include</c>s and conditionals the file made between
    /// earlier modules, which the module saw there. Left out: a guard round the whole file, a <c>`timescale</c> (the project
    /// keeps the unit), and an include of a file whose modules are the project's own. An include of a file that declares
    /// something goes inside the module, at the head of its body, which means the same to that module. A file whose
    /// conditionals choose between modules cannot be cut, and says so.
    /// </summary>
    internal sealed class ModuleCutter
    {
        private static readonly Regex EndModule = new Regex(@"\bendmodule\b", RegexOptions.CultureInvariant);
        private static readonly Regex Include = new Regex("`include\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex Defined = new Regex(@"`define\s+([A-Za-z_]\w*)", RegexOptions.CultureInvariant);
        private static readonly Regex Parameters = new Regex(@"\b(?:localparam|parameter)\b[^;]*", RegexOptions.CultureInvariant);
        private static readonly Regex Assigned = new Regex(@"([A-Za-z_]\w*)\s*=(?!=)", RegexOptions.CultureInvariant);
        private static readonly Regex Typedef = new Regex(@"\btypedef\b[^;]*?([A-Za-z_]\w*)\s*;", RegexOptions.CultureInvariant);

        private readonly string _file;
        private readonly string[] _lines;
        private readonly string[] _code;
        private readonly List<(int Start, int End)> _bodies = new List<(int, int)>();
        private readonly HashSet<int> _guard = new HashSet<int>();
        private readonly HashSet<string> _moduleFiles;
        private readonly HashSet<string> _declaring;
        private readonly Func<string, string> _textOf;

        /// <param name="file">The file's name, as the notes say it.</param>
        /// <param name="text">Its text.</param>
        /// <param name="moduleLines">The line, from 1, of every module the file holds, each <c>module</c> keyword's.</param>
        /// <param name="moduleFiles">The bare names of files whose modules are the project's own.</param>
        /// <param name="declaring">The bare names of the project's files that declare something.</param>
        /// <param name="textOf">A project file's text by its bare name, or null.</param>
        public ModuleCutter(string file, string text, IEnumerable<int> moduleLines, HashSet<string> moduleFiles, HashSet<string> declaring,
            Func<string, string> textOf)
        {
            _file = file;
            _moduleFiles = moduleFiles;
            _declaring = declaring;
            _textOf = textOf;
            _lines = LinesOf(text);
            _code = new string[_lines.Length];
            bool inComment = false;
            for (int l = 0; l < _lines.Length; l++)
                _code[l] = StripComments(_lines[l], ref inComment);

            var starts = new SortedSet<int>();
            foreach (int line in moduleLines)
                if (line >= 1 && line <= _lines.Length)
                    starts.Add(line - 1);
            int[] ordered = new int[starts.Count];
            starts.CopyTo(ordered);
            for (int i = 0; i < ordered.Length; i++)
            {
                int limit = i + 1 < ordered.Length ? ordered[i + 1] - 1 : _lines.Length - 1;
                int end = limit;
                for (int l = ordered[i]; l <= limit; l++)
                    if (EndModule.IsMatch(_code[l]))
                    {
                        end = l;
                        break;
                    }

                _bodies.Add((ordered[i], end));
            }

            FindTheGuard();
            Problem = CheckTheConditionals();
        }

        /// <summary>Why the file's modules cannot be cut into texts of their own, or null when they can.</summary>
        public string Problem { get; }

        /// <summary>The module whose <c>module</c> is on <paramref name="line"/> (from 1), as a text of its own; null with a reason when it cannot be.</summary>
        public string Cut(ModuleDeclaration module, out string problem)
        {
            problem = Problem;
            int own = _bodies.FindIndex(b => b.Start == module.Location.Line - 1);
            if (own < 0)
            {
                problem = $"{module.Name} could not be found in {_file}.";
                return null;
            }

            (int start, int end) = _bodies[own];
            var kept = new List<string>();
            var inside = new List<string>();
            var insideNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // What the body includes itself is not included again at its head.
            for (int l = start; l <= end; l++)
                if (IncludedName(l) is string name)
                    insideNames.Add(name);

            void ToTheHead(string name)
            {
                if (insideNames.Add(name))
                    inside.Add("    `include \"" + name + "\"");
            }

            // What the file said between earlier modules: its defines, includes and the conditionals round them.
            int gap = 0;
            for (int i = 0; i < own; i++)
            {
                int depth = 0;
                for (int l = gap; l < _bodies[i].Start; l++)
                {
                    if (_guard.Contains(l))
                        continue;
                    string directive = DirectiveOf(_code[l]);
                    switch (directive)
                    {
                        case "ifdef":
                        case "ifndef":
                            depth++;
                            kept.Add(_lines[l]);
                            break;
                        case "endif":
                            depth--;
                            kept.Add(_lines[l]);
                            break;
                        case "elsif":
                        case "else":
                            kept.Add(_lines[l]);
                            break;
                        case "define":
                        case "undef":
                        case "default_nettype":
                            l = Take(l, kept);
                            break;
                        case "include":
                            string included = IncludedName(l);
                            if (included == null || _moduleFiles.Contains(included))
                                break;
                            if (depth == 0 && _declaring.Contains(included))
                                ToTheHead(included);
                            else
                                kept.Add(Bare(l));
                            break;
                    }
                }

                gap = _bodies[i].End + 1;
            }

            // The gap just before it, as written: its comments, and its directives but the ones left out.
            {
                int depth = 0;
                for (int l = gap; l < start; l++)
                {
                    if (_guard.Contains(l))
                        continue;
                    string directive = DirectiveOf(_code[l]);
                    if (directive == "timescale")
                        continue;
                    if (directive == "ifdef" || directive == "ifndef")
                        depth++;
                    else if (directive == "endif")
                        depth--;
                    if (directive == "include")
                    {
                        string included = IncludedName(l);
                        if (included == null || _moduleFiles.Contains(included))
                            continue;
                        if (depth == 0 && _declaring.Contains(included))
                        {
                            ToTheHead(included);
                            continue;
                        }

                        kept.Add(Bare(l));
                        continue;
                    }

                    kept.Add(_lines[l]);
                }
            }

            // Its header must not use what goes to the head of its body, which it would then come before.
            int head = end;
            foreach (ModuleItem item in module.Items)
            {
                if (item.Location.File == null || SourcePaths.Normalise(item.Location.File) != SourcePaths.Normalise(module.Location.File)
                    || item.Location.Line - 1 <= start)
                    continue;
                head = Math.Min(head, item.Location.Line - 1);
                break;
            }

            if (inside.Count > 0 && problem == null)
                problem = HeaderUses(module.Name, start, head, inside);

            // And the module itself.
            for (int l = start; l <= end; l++)
            {
                if (l == head && inside.Count > 0)
                {
                    kept.AddRange(inside);
                    kept.Add("");
                }

                string directive = DirectiveOf(_code[l]);
                if (directive == "timescale" || directive == "include" && IncludedName(l) is string name && _moduleFiles.Contains(name))
                    kept.Add("");
                else
                    kept.Add(directive == "include" ? Bare(l) : _lines[l]);
            }

            int from = 0;
            while (from < kept.Count && kept[from].Trim().Length == 0)
                from++;
            var source = new StringBuilder();
            for (int i = from; i < kept.Count; i++)
                source.Append(kept[i]).Append('\n');
            return source.ToString();
        }

        /// <summary>The text of a module whose file cannot be cut: the module alone, from its <c>module</c> to its <c>endmodule</c>.</summary>
        public string Alone(ModuleDeclaration module)
        {
            int own = _bodies.FindIndex(b => b.Start == module.Location.Line - 1);
            if (own < 0)
                return "";
            var source = new StringBuilder();
            for (int l = _bodies[own].Start; l <= _bodies[own].End; l++)
                source.Append(_lines[l]).Append('\n');
            return source.ToString();
        }

        /// <summary>A guard round the whole file: <c>`ifndef X</c> and <c>`define X</c> its first code, and the <c>`endif</c> that closes them its last.</summary>
        private void FindTheGuard()
        {
            var code = new List<int>();
            for (int l = 0; l < _code.Length; l++)
                if (_code[l].Trim().Length > 0)
                    code.Add(l);
            if (code.Count < 3)
                return;

            string[] first = Words(_code[code[0]]);
            string[] second = Words(_code[code[1]]);
            if (first.Length != 2 || first[0] != "`ifndef" || second.Length < 2 || second[0] != "`define" || second[1] != first[1])
                return;

            int depth = 0;
            for (int c = 0; c < code.Count; c++)
            {
                string directive = DirectiveOf(_code[code[c]]);
                if (directive == "ifdef" || directive == "ifndef")
                    depth++;
                else if (directive == "endif" && --depth == 0)
                {
                    if (c == code.Count - 1)
                    {
                        _guard.Add(code[0]);
                        _guard.Add(code[1]);
                        _guard.Add(code[c]);
                    }

                    return;
                }
            }
        }

        /// <summary>Whether every gap between modules opens and closes its own conditionals: if one runs across a module, the file chooses between its modules.</summary>
        private string CheckTheConditionals()
        {
            int gap = 0;
            for (int i = 0; i <= _bodies.Count; i++)
            {
                int to = i < _bodies.Count ? _bodies[i].Start : _lines.Length;
                int depth = 0;
                for (int l = gap; l < to; l++)
                {
                    if (_guard.Contains(l))
                        continue;
                    string directive = DirectiveOf(_code[l]);
                    if (directive == "ifdef" || directive == "ifndef")
                        depth++;
                    else if (directive == "endif" && --depth < 0)
                        break;
                }

                if (depth != 0)
                    return $"{_file} chooses between its modules with `ifdef or `ifndef, so they cannot be cut into files of their own: give each module a file of its own.";
                if (i < _bodies.Count)
                    gap = _bodies[i].End + 1;
            }

            return null;
        }

        /// <summary>Whether the header, from <paramref name="start"/> to before <paramref name="head"/>, uses something only a file going inside the body declares.</summary>
        private string HeaderUses(string module, int start, int head, List<string> inside)
        {
            var header = new StringBuilder();
            for (int l = start; l < head; l++)
                header.Append(_code[l]).Append('\n');
            string text = header.ToString();

            foreach (string line in inside)
            {
                Match include = Include.Match(line);
                string name = include.Groups[1].Value;
                string declared = _textOf(name);
                if (declared == null)
                    continue;
                foreach (string used in DeclaredIn(declared))
                    if (Regex.IsMatch(text, @"(?<![\w$])`?" + Regex.Escape(used) + @"(?![\w$])"))
                        return $"{module}'s header uses {used}, which {name} declares outside any module: in a project each module is a file of " +
                            $"its own and includes {name} inside itself, after its header. Give the header what it needs another way.";
            }

            return null;
        }

        /// <summary>The names a file declares: its macros, constants and types.</summary>
        private static IEnumerable<string> DeclaredIn(string text)
        {
            bool inComment = false;
            var code = new StringBuilder();
            foreach (string line in LinesOf(text))
                code.Append(StripComments(line, ref inComment)).Append('\n');
            string all = code.ToString();
            foreach (Match m in Defined.Matches(all))
                yield return m.Groups[1].Value;
            foreach (Match statement in Parameters.Matches(all))
                foreach (Match m in Assigned.Matches(statement.Value))
                    yield return m.Groups[1].Value;
            foreach (Match m in Typedef.Matches(all))
                yield return m.Groups[1].Value;
        }

        /// <summary>Line <paramref name="l"/> taken, and the lines a <c>\</c> at its end carries on to; the last line taken.</summary>
        private int Take(int l, List<string> into)
        {
            into.Add(_lines[l]);
            while (_lines[l].TrimEnd().EndsWith("\\", StringComparison.Ordinal) && l + 1 < _lines.Length)
                into.Add(_lines[++l]);
            return l;
        }

        /// <summary>The bare name line <paramref name="l"/> includes, or null when it includes nothing.</summary>
        private string IncludedName(int l)
        {
            if (DirectiveOf(_code[l]) != "include")
                return null;
            Match include = Include.Match(_lines[l]);
            return include.Success ? NameOf(include.Groups[1].Value) : null;
        }

        /// <summary>Line <paramref name="l"/>, an include, naming its file by its bare name: a project keeps its files by those.</summary>
        private string Bare(int l)
        {
            Match include = Include.Match(_lines[l]);
            if (!include.Success)
                return _lines[l];
            string name = NameOf(include.Groups[1].Value);
            return name == include.Groups[1].Value ? _lines[l] : _lines[l].Substring(0, include.Groups[1].Index) + name + _lines[l].Substring(include.Groups[1].Index + include.Groups[1].Length);
        }

        /// <summary>The directive a line of code starts with, without its backtick; null when it starts with none.</summary>
        internal static string DirectiveOf(string code)
        {
            string trimmed = code.TrimStart();
            if (trimmed.Length < 2 || trimmed[0] != '`')
                return null;
            int end = 1;
            while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_'))
                end++;
            return trimmed.Substring(1, end - 1);
        }

        private static string[] Words(string code) => code.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        internal static string[] LinesOf(string text) => (text ?? "").Replace("\r\n", "\n").Split('\n');

        /// <summary>A path's last part: the name a project keeps a file by.</summary>
        internal static string NameOf(string path)
        {
            string normal = SourcePaths.Normalise(path ?? "");
            int slash = normal.LastIndexOf('/');
            return slash < 0 ? normal : normal.Substring(slash + 1);
        }

        /// <summary>
        /// A line without its comments; <paramref name="inComment"/> carries a block comment from line to line. A string's
        /// text is kept as it is, so a <c>//</c> inside quotes is not taken for a comment.
        /// </summary>
        internal static string StripComments(string line, ref bool inComment)
        {
            var kept = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (inComment)
                {
                    if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        inComment = false;
                        i++;
                    }

                    continue;
                }

                if (line[i] == '"')
                {
                    kept.Append(line[i]);
                    for (i++; i < line.Length; i++)
                    {
                        kept.Append(line[i]);
                        if (line[i] == '\\' && i + 1 < line.Length)
                            kept.Append(line[++i]);
                        else if (line[i] == '"')
                            break;
                    }

                    continue;
                }

                if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    break;
                if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    inComment = true;
                    i++;
                    continue;
                }

                kept.Append(line[i]);
            }

            return kept.ToString();
        }
    }
}
