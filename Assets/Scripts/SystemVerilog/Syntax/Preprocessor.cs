using System.Collections.Generic;
using System.Linq;

namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>
    /// Compiler directives, worked on the token stream: <c>`include</c> splices in another file's
    /// tokens, <c>`define</c> names a run of tokens that <c>`NAME</c> then stands for, <c>`ifdef</c>
    /// and its family keep or drop a stretch, and <c>`timescale</c> is noted.
    /// </summary>
    /// <remarks>
    /// - **An included file keeps its own name on its tokens**, so a diagnostic inside it points into
    ///   it, not at the <c>`include</c>.
    /// - **A macro is a name for tokens, which may take arguments** (Part L6): <c>`define MAX(a, b = 0)
    ///   ((a) &gt; (b) ? (a) : (b))</c>, used as <c>`MAX(x, y)</c> -- its arguments perhaps over several
    ///   lines, each given or left to its default. <c>``</c> joins two tokens into one and <c>`"...`"</c> is
    ///   a string with the arguments put in it (IEEE 1800, 22.5.1). What a use gives is expanded again, so
    ///   a macro may use another.
    /// - <c>`timescale</c> only labels time (Docs/architecture-5.md, section 6), so it is recorded and
    ///   the time unit stays the simulator's own.
    /// - It never throws: a missing file, a cycle or a runaway macro is a diagnostic.
    /// </remarks>
    public sealed class Preprocessor
    {
        /// <summary>How deep includes and macro uses may nest before the reader gives up on them.</summary>
        public const int MaxDepth = 32;

        /// <summary>
        /// How much macros and includes may make in one import, every file read together (<see cref="ExpansionBudget"/>) --
        /// each use of a macro, each token one makes, each argument put in and each character a <c>`"...`"</c> spells out,
        /// and each token of a file included -- before the reader stops expanding them (Part 13f). Depth alone does not bound
        /// it: macros that each use the one before twice, or files that each include the next twice, make two to the power
        /// of the chain's length, and a chain of 31 is inside <see cref="MaxDepth"/>. A course project makes a few thousand.
        /// Charged before what it pays for is made, so nothing past it is ever allocated.
        /// </summary>
        public const int MaxExpansion = 1_000_000;

        private readonly ExpansionBudget _budget;
        private Token _outermostUse;

        private readonly ISourceFiles _files;
        private readonly DiagnosticBag _diagnostics;
        private readonly Dictionary<string, Macro> _macros = new Dictionary<string, Macro>();

        /// <summary>A <c>`define</c>: its body, and its arguments' names and defaults when it takes any.</summary>
        private sealed class Macro
        {
            public List<Token> Body;

            /// <summary>Null for a macro with no argument list.</summary>
            public List<string> Parameters;

            /// <summary>Each argument's default, null where it has none.</summary>
            public List<List<Token>> Defaults;
        }
        private readonly Stack<Conditional> _conditionals = new Stack<Conditional>();
        private readonly List<Token> _output = new List<Token>();
        private readonly List<string> _includeStack = new List<string>();

        private Preprocessor(ISourceFiles files, DiagnosticBag diagnostics, ExpansionBudget budget)
        {
            _files = files;
            _diagnostics = diagnostics;
            _budget = budget ?? new ExpansionBudget();
        }

        /// <summary>What a <c>`timescale</c> said, if anything did: <c>1ns/1ps</c>.</summary>
        public string Timescale { get; private set; }

        /// <summary>Every file read, the first included, in the order they were read.</summary>
        public List<string> FilesRead { get; } = new List<string>();

        /// <summary>
        /// The tokens of <paramref name="path"/>, with every directive worked and every include spliced in. Files read
        /// together share one <paramref name="budget"/> for what their macros and includes make; null for one of its own.
        /// </summary>
        public static List<Token> Run(string path, string text, ISourceFiles files, DiagnosticBag diagnostics, out Preprocessor done,
            ExpansionBudget budget = null)
        {
            var preprocessor = new Preprocessor(files, diagnostics, budget);
            preprocessor.ProcessFile(SourcePaths.Normalise(path), text);

            foreach (Conditional open in preprocessor._conditionals)
                diagnostics.Error(open.Start, DiagnosticCodes.UnbalancedConditional, $"This {open.Directive} is never closed with `endif.");

            Token end = preprocessor._output.Count > 0 && preprocessor._output[preprocessor._output.Count - 1].Kind == TokenKind.EndOfFile
                ? null
                : new Token(TokenKind.EndOfFile, string.Empty, new SourceLocation(path, 1, 1), 1);
            if (end != null)
                preprocessor._output.Add(end);

            done = preprocessor;
            return preprocessor._output;
        }

        private bool Active => _conditionals.Count == 0 || _conditionals.Peek().Active;

        private void ProcessFile(string path, string text, Token includedAt = null)
        {
            FilesRead.Add(path);
            List<Token> tokens = Lexer.Lex(text, path, _diagnostics);
            if (includedAt != null && !Spend(tokens.Count + 1, includedAt))
                return;   // an include past what an import may make: the read stops splicing them in
            _includeStack.Add(path);
            Process(tokens, 0, depth: _includeStack.Count);
            _includeStack.RemoveAt(_includeStack.Count - 1);
        }

        private void Process(List<Token> tokens, int macroDepth, int depth)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];

                if (token.Kind == TokenKind.EndOfFile)
                {
                    // Only the outermost file's end counts; an included file's end is just the end of its splice.
                    if (depth == 1 && macroDepth == 0)
                        _output.Add(token);
                    continue;
                }

                // A runaway expansion unwinds at once, every use on the way out with it.
                if (macroDepth > 0 && _budget.Runaway)
                    return;

                if (token.Kind != TokenKind.Directive)
                {
                    if (Active && (macroDepth == 0 || Spend(1, null)))
                        _output.Add(token);
                    continue;
                }

                List<Token> rest = RestOfLine(tokens, i);
                int consumed = rest.Count;

                switch (token.Text)
                {
                    case "`ifdef":
                    case "`ifndef":
                    {
                        bool defined = rest.Count > 0 && _macros.ContainsKey(rest[0].Text);
                        bool holds = token.Text == "`ifdef" ? defined : !defined;
                        bool parent = Active;
                        _conditionals.Push(new Conditional(token.Text, token.Location, parent && holds, holds, parent));
                        consumed = rest.Count > 0 ? 1 : 0;
                        if (rest.Count == 0)
                            _diagnostics.Error(token.Location, DiagnosticCodes.Expected, $"{token.Text} needs the name of a macro.");
                        break;
                    }

                    case "`elsif":
                    {
                        consumed = rest.Count > 0 ? 1 : 0;
                        if (_conditionals.Count == 0)
                        {
                            _diagnostics.Error(token.Location, DiagnosticCodes.UnbalancedConditional, "`elsif without an `ifdef before it.");
                            break;
                        }

                        Conditional top = _conditionals.Pop();
                        bool holds = !top.Taken && rest.Count > 0 && _macros.ContainsKey(rest[0].Text);
                        _conditionals.Push(new Conditional(top.Directive, top.Start, top.Parent && holds, top.Taken || holds, top.Parent));
                        break;
                    }

                    case "`else":
                    {
                        consumed = 0;
                        if (_conditionals.Count == 0)
                        {
                            _diagnostics.Error(token.Location, DiagnosticCodes.UnbalancedConditional, "`else without an `ifdef before it.");
                            break;
                        }

                        Conditional top = _conditionals.Pop();
                        _conditionals.Push(new Conditional(top.Directive, top.Start, top.Parent && !top.Taken, true, top.Parent));
                        break;
                    }

                    case "`endif":
                        consumed = 0;
                        if (_conditionals.Count == 0)
                            _diagnostics.Error(token.Location, DiagnosticCodes.UnbalancedConditional, "`endif without an `ifdef before it.");
                        else
                            _conditionals.Pop();
                        break;

                    default:
                        if (!Active)
                        {
                            // Inside a dropped stretch, a directive's own arguments are dropped with it --
                            // but only the line-bound ones; a macro use is a single token.
                            consumed = IsLineDirective(token.Text) ? rest.Count : 0;
                            break;
                        }

                        consumed = IsLineDirective(token.Text) || IsBareDirective(token.Text)
                            ? Directive(token, rest, macroDepth, depth)
                            : UseMacro(token, tokens, i, macroDepth, depth);
                        break;
                }

                i += consumed;
            }
        }

        private static bool IsBareDirective(string name) =>
            name == "`resetall" || name == "`celldefine" || name == "`endcelldefine" || name == "`nounconnected_drive" ||
            name == "`end_keywords" || name == "`unconnected_drive";

        private static bool IsLineDirective(string name) =>
            name == "`include" || name == "`define" || name == "`undef" || name == "`timescale" ||
            name == "`default_nettype" || name == "`line" || name == "`pragma" || name == "`begin_keywords";

        /// <summary>Works one directive; returns how many of the following tokens it used.</summary>
        private int Directive(Token token, List<Token> rest, int macroDepth, int depth)
        {
            switch (token.Text)
            {
                case "`include":
                    Include(token, rest, depth);
                    return rest.Count;

                case "`define":
                    Define(token, rest);
                    return rest.Count;

                case "`undef":
                    if (rest.Count > 0)
                        _macros.Remove(rest[0].Text);
                    return rest.Count;

                case "`timescale":
                {
                    // Noted for the tree, and passed on for the parser to give the modules after it (Part L7).
                    string text = string.Join("", rest.Select(t => t.Text));
                    Timescale = text;
                    _output.Add(new Token(TokenKind.Directive, "`timescale " + text, token.Location, token.LogicalLine));
                    return rest.Count;
                }

                case "`default_nettype":
                    return rest.Count;

                case "`resetall":
                case "`celldefine":
                case "`endcelldefine":
                case "`nounconnected_drive":
                    return 0;

                case "`line":
                case "`pragma":
                case "`begin_keywords":
                case "`end_keywords":
                case "`unconnected_drive":
                    _diagnostics.Warning(token.Location, DiagnosticCodes.UnsupportedDirective, $"{token.Text} is ignored here.");
                    return token.Text == "`end_keywords" ? 0 : rest.Count;

                default:
                    return 0;
            }
        }

        private void Include(Token token, List<Token> rest, int depth)
        {
            if (rest.Count == 0 || rest[0].Kind != TokenKind.String)
            {
                _diagnostics.Error(token.Location, DiagnosticCodes.Expected, "`include needs a file name in quotes: `include \"cells.sv\".");
                return;
            }

            string name = rest[0].Text;

            if (depth >= MaxDepth)
            {
                _diagnostics.Error(token.Location, DiagnosticCodes.IncludeTooDeep, $"Includes nest more than {MaxDepth} deep here; is a file including itself?");
                return;
            }

            if (_files != null)
            {
                foreach (string candidate in SourcePaths.Candidates(token.Location.File, name))
                {
                    if (!_files.TryRead(candidate, out string text))
                        continue;

                    if (_includeStack.Contains(candidate))
                    {
                        _diagnostics.Error(token.Location, DiagnosticCodes.IncludeCycle,
                            $"\"{name}\" is already being included ({string.Join(" -> ", _includeStack)} -> {candidate}).");
                        return;
                    }

                    ProcessFile(candidate, text, token);
                    return;
                }
            }

            _diagnostics.Error(token.Location, DiagnosticCodes.IncludeNotFound, $"The file \"{name}\" to include was not found.");
        }

        private void Define(Token token, List<Token> rest)
        {
            if (rest.Count == 0 || (rest[0].Kind != TokenKind.Identifier && rest[0].Kind != TokenKind.Keyword))
            {
                _diagnostics.Error(token.Location, DiagnosticCodes.Expected, "`define needs a name.");
                return;
            }

            Token name = rest[0];

            // A '(' touching the name makes it a macro with arguments.
            if (rest.Count > 1 && rest[1].Is("(") && rest[1].Location.Line == name.Location.Line &&
                rest[1].Location.Column == name.Location.Column + name.Text.Length)
            {
                var macro = new Macro { Parameters = new List<string>(), Defaults = new List<List<Token>>() };
                int at = 2;
                while (at < rest.Count && !rest[at].Is(")"))
                {
                    if (rest[at].Kind != TokenKind.Identifier)
                    {
                        _diagnostics.Error(rest[at].Location, DiagnosticCodes.MacroArguments,
                            $"`define {name.Text}(...) names its arguments, as `define {name.Text}(a, b); {Describe(rest[at])} is not a name.");
                        return;
                    }

                    macro.Parameters.Add(rest[at++].Text);
                    List<Token> fallback = null;
                    if (at < rest.Count && rest[at].Is("="))
                    {
                        at++;
                        fallback = new List<Token>();
                        for (int nest = 0; at < rest.Count && !(nest == 0 && (rest[at].Is(",") || rest[at].Is(")"))); at++)
                        {
                            nest += Opens(rest[at]) ? 1 : Closes(rest[at]) ? -1 : 0;
                            fallback.Add(rest[at]);
                        }
                    }

                    macro.Defaults.Add(fallback);
                    if (at < rest.Count && rest[at].Is(","))
                        at++;
                }

                if (at >= rest.Count)
                {
                    _diagnostics.Error(token.Location, DiagnosticCodes.MacroArguments, $"`define {name.Text}( lists its arguments, and needs a ) to close them.");
                    return;
                }

                macro.Body = rest.Skip(at + 1).ToList();
                _macros[name.Text] = macro;
                return;
            }

            _macros[name.Text] = new Macro { Body = rest.Skip(1).ToList() };
        }

        private static bool Opens(Token t) => t.Is("(") || t.Is("[") || t.Is("{") || t.Is("'{");
        private static bool Closes(Token t) => t.Is(")") || t.Is("]") || t.Is("}");

        private static string Describe(Token t) => t.Kind == TokenKind.String ? $"\"{t.Text}\"" : $"'{t.Text}'";

        /// <summary>
        /// A use of a macro: its body, with what the use gives for each argument put in, expanded again.
        /// Returns how many tokens after <paramref name="index"/> the use took -- its arguments, which may
        /// run over several lines.
        /// </summary>
        private int UseMacro(Token token, List<Token> tokens, int index, int macroDepth, int depth)
        {
            string name = token.Text.Substring(1);

            if (name == "`" || name.StartsWith("\"", System.StringComparison.Ordinal))
            {
                _diagnostics.Error(token.Location, DiagnosticCodes.Expected, $"{(name == "`" ? "``" : "`\"")} belongs in the body of a `define.");
                return 0;
            }

            if (!_macros.TryGetValue(name, out Macro macro))
            {
                _diagnostics.Error(token.Location, DiagnosticCodes.UndefinedMacro,
                    $"{token.Text} is not defined here. Is the `define above it, or in a file that is `included first?");
                return 0;
            }

            if (macroDepth >= MaxDepth)
            {
                _diagnostics.Error(token.Location, DiagnosticCodes.MacroTooDeep, $"{token.Text} expands into itself, over and over.");
                return 0;
            }

            if (macroDepth == 0)
                _outermostUse = token;
            if (!Spend(1, null))
                return 0;

            List<Token> body = macro.Body;
            int consumed = 0;

            if (macro.Parameters != null)
            {
                if (!TryArguments(token, tokens, index, out List<List<Token>> arguments, out consumed))
                    return consumed;

                if (arguments.Count == 1 && arguments[0].Count == 0 && macro.Parameters.Count == 0)
                    arguments.Clear();

                if (arguments.Count > macro.Parameters.Count)
                {
                    _diagnostics.Error(token.Location, DiagnosticCodes.MacroArguments,
                        $"{token.Text} takes {macro.Parameters.Count} argument{(macro.Parameters.Count == 1 ? "" : "s")}, and this use gives {arguments.Count}.");
                    return consumed;
                }

                body = Substitute(token, macro, arguments);
                if (_budget.Runaway)
                    return consumed;
            }

            var withEnd = new List<Token>(body) { new Token(TokenKind.EndOfFile, string.Empty, token.Location, token.LogicalLine) };
            Process(withEnd, macroDepth + 1, depth + 1);
            return consumed;
        }

        /// <summary>
        /// <paramref name="units"/> more of what macros and includes make, and whether the import is still within
        /// <see cref="MaxExpansion"/>. Past it, the read says so once -- at <paramref name="at"/>, an include, or else the use
        /// of a macro in the file that began it -- and expands nothing more.
        /// </summary>
        private bool Spend(long units, Token at)
        {
            if (_budget.Runaway)
                return false;
            _budget.Spent += units;
            if (_budget.Spent <= MaxExpansion)
                return true;

            _budget.Runaway = true;
            _diagnostics.Error((at ?? _outermostUse).Location, DiagnosticCodes.MacroTooMuch,
                "The macros and includes used here expand to more than a million tokens, so BitSorter stopped expanding them. " +
                "Does a macro use another twice, or a file include another twice, and that one again, over and over?");
            return false;
        }

        /// <summary>A use's arguments, <c>(a, f(b, c), {d, e})</c>: split at the commas outside brackets.</summary>
        private bool TryArguments(Token use, List<Token> tokens, int index, out List<List<Token>> arguments, out int consumed)
        {
            arguments = new List<List<Token>>();
            consumed = 0;
            int at = index + 1;

            if (at >= tokens.Count || !tokens[at].Is("("))
            {
                _diagnostics.Error(use.Location, DiagnosticCodes.MacroArguments, $"{use.Text} takes arguments: {use.Text}(...).");
                return false;
            }

            var current = new List<Token>();
            int nest = 0;
            for (at++; at < tokens.Count && tokens[at].Kind != TokenKind.EndOfFile; at++)
            {
                Token t = tokens[at];
                if (nest == 0 && t.Is(")"))
                    break;

                if (nest == 0 && t.Is(","))
                {
                    arguments.Add(current);
                    current = new List<Token>();
                    continue;
                }

                nest += Opens(t) ? 1 : Closes(t) ? -1 : 0;
                current.Add(t);
            }

            if (at >= tokens.Count || !tokens[at].Is(")"))
            {
                _diagnostics.Error(use.Location, DiagnosticCodes.MacroArguments, $"{use.Text}( gives its arguments, and needs a ) to close them.");
                consumed = at - index - 1;
                return false;
            }

            arguments.Add(current);
            consumed = at - index;
            return true;
        }

        /// <summary>A macro's body with each argument put in, <c>``</c> joined, and <c>`"...`"</c> made a string.</summary>
        private List<Token> Substitute(Token use, Macro macro, List<List<Token>> arguments)
        {
            var actual = new List<Token>[macro.Parameters.Count];
            for (int i = 0; i < actual.Length; i++)
            {
                List<Token> given = i < arguments.Count ? arguments[i] : null;
                actual[i] = given != null && given.Count > 0 ? given : macro.Defaults[i] ?? given;
                if (actual[i] == null)
                {
                    _diagnostics.Error(use.Location, DiagnosticCodes.MacroArguments, $"{use.Text} needs a value for its argument {macro.Parameters[i]}, which has no default.");
                    actual[i] = new List<Token>();
                }
            }

            // Each argument put in is paid for before it is copied, and each string's spelling as it grows, so a use asking for
            // more than an import may make stops before it is made (MaxExpansion).
            var output = new List<Token>();
            bool joins = false;
            foreach (Token t in macro.Body)
            {
                if (t.Kind == TokenKind.Directive && t.Text.StartsWith("`\"", System.StringComparison.Ordinal))
                {
                    string spelled = Stringify(t.Text.Substring(2), macro.Parameters, actual);
                    if (spelled == null)
                        return output;
                    output.Add(new Token(TokenKind.String, spelled, t.Location, t.LogicalLine));
                    continue;
                }

                int parameter = t.Kind == TokenKind.Identifier ? macro.Parameters.IndexOf(t.Text) : -1;
                if (parameter >= 0)
                {
                    if (!Spend(actual[parameter].Count, null))
                        return output;
                    output.AddRange(actual[parameter]);
                }
                else
                {
                    joins |= t.Kind == TokenKind.Directive && t.Text == "``";
                    output.Add(t);
                }
            }

            return joins ? Joined(output) : output;
        }

        /// <summary>
        /// <c>``</c> joins what is either side of it into one token: a name made of two, a``_reg. In one pass, left to right,
        /// so a body of many joins costs its length (Part 13f review); a <c>``</c> at either end has nothing to join and stays.
        /// </summary>
        private static List<Token> Joined(List<Token> tokens)
        {
            var joined = new List<Token>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                Token t = tokens[i];
                if (t.Kind == TokenKind.Directive && t.Text == "``" && joined.Count > 0 && i + 1 < tokens.Count)
                {
                    Token left = joined[joined.Count - 1], right = tokens[++i];
                    string text = left.Text + right.Text;
                    TokenKind kind = Keywords.IsKeyword(text) ? TokenKind.Keyword : left.Kind == TokenKind.Number && right.Kind == TokenKind.Number ? TokenKind.Number : TokenKind.Identifier;
                    joined[joined.Count - 1] = new Token(kind, text, left.Location, left.LogicalLine);
                    continue;
                }

                joined.Add(t);
            }

            return joined;
        }

        /// <summary>
        /// The text of <c>`"...`"</c> with each argument's name, as a whole word, replaced by what the use gave; null when
        /// spelling it out would pass what an import may make.
        /// </summary>
        private string Stringify(string raw, List<string> parameters, List<Token>[] actual)
        {
            var text = new System.Text.StringBuilder();
            if (!Spend(raw.Length, null))
                return null;
            for (int i = 0; i < raw.Length;)
            {
                if (char.IsLetter(raw[i]) || raw[i] == '_')
                {
                    int start = i;
                    while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] == '_' || raw[i] == '$'))
                        i++;
                    string word = raw.Substring(start, i - start);
                    int parameter = parameters.IndexOf(word);
                    if (parameter >= 0)
                    {
                        string spelled = Spell(actual[parameter]);
                        if (!Spend(spelled.Length, null))
                            return null;
                        text.Append(spelled);
                    }
                    else
                    {
                        text.Append(word);
                    }

                    continue;
                }

                text.Append(raw[i++]);
            }

            return text.ToString();
        }

        /// <summary>Tokens as text, a space between two that had one between them where they were written.</summary>
        private static string Spell(List<Token> tokens)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < tokens.Count; i++)
            {
                Token t = tokens[i];
                if (i > 0)
                {
                    Token before = tokens[i - 1];
                    if (before.Location.Line != t.Location.Line || before.Location.Column + before.Text.Length < t.Location.Column)
                        text.Append(' ');
                }

                text.Append(t.Kind == TokenKind.String ? "\"" + t.Text + "\"" : t.Text);
            }

            return text.ToString();
        }

        /// <summary>The tokens after <paramref name="index"/> on the same logical line of the same file.</summary>
        private static List<Token> RestOfLine(List<Token> tokens, int index)
        {
            var rest = new List<Token>();
            Token directive = tokens[index];

            for (int i = index + 1; i < tokens.Count; i++)
            {
                Token t = tokens[i];
                if (t.Kind == TokenKind.EndOfFile || t.LogicalLine != directive.LogicalLine || t.Location.File != directive.Location.File)
                    break;
                rest.Add(t);
            }

            return rest;
        }

        private readonly struct Conditional
        {
            public readonly string Directive;
            public readonly SourceLocation Start;
            public readonly bool Active;
            public readonly bool Taken;
            public readonly bool Parent;

            public Conditional(string directive, SourceLocation start, bool active, bool taken, bool parent)
            {
                Directive = directive;
                Start = start;
                Active = active;
                Taken = taken;
                Parent = parent;
            }
        }
    }

    /// <summary>
    /// What the macros and includes of one import have made between them (<see cref="Preprocessor.MaxExpansion"/>, Part
    /// 13f): every file read together shares one, so a bomb split across two hundred files is stopped as one would be.
    /// </summary>
    public sealed class ExpansionBudget
    {
        internal long Spent;
        internal bool Runaway;
    }
}
