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
    /// - **A macro is a name for tokens, with no arguments.** That is what a course file and most
    ///   simple designs use; a macro with arguments is refused with a plain message rather than
    ///   half-expanded.
    /// - <c>`timescale</c> only labels time (Docs/architecture-5.md, section 6), so it is recorded and
    ///   the time unit stays the simulator's own.
    /// - It never throws: a missing file, a cycle or a runaway macro is a diagnostic.
    /// </remarks>
    public sealed class Preprocessor
    {
        /// <summary>How deep includes and macro uses may nest before the reader gives up on them.</summary>
        public const int MaxDepth = 32;

        private readonly ISourceFiles _files;
        private readonly DiagnosticBag _diagnostics;
        private readonly Dictionary<string, List<Token>> _macros = new Dictionary<string, List<Token>>();
        private readonly Stack<Conditional> _conditionals = new Stack<Conditional>();
        private readonly List<Token> _output = new List<Token>();
        private readonly List<string> _includeStack = new List<string>();

        private Preprocessor(ISourceFiles files, DiagnosticBag diagnostics)
        {
            _files = files;
            _diagnostics = diagnostics;
        }

        /// <summary>What a <c>`timescale</c> said, if anything did: <c>1ns/1ps</c>.</summary>
        public string Timescale { get; private set; }

        /// <summary>Every file read, the first included, in the order they were read.</summary>
        public List<string> FilesRead { get; } = new List<string>();

        /// <summary>The tokens of <paramref name="path"/>, with every directive worked and every include spliced in.</summary>
        public static List<Token> Run(string path, string text, ISourceFiles files, DiagnosticBag diagnostics, out Preprocessor done)
        {
            var preprocessor = new Preprocessor(files, diagnostics);
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

        private void ProcessFile(string path, string text)
        {
            FilesRead.Add(path);
            _includeStack.Add(path);
            List<Token> tokens = Lexer.Lex(text, path, _diagnostics);
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

                if (token.Kind != TokenKind.Directive)
                {
                    if (Active)
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

                        consumed = Directive(token, rest, macroDepth, depth);
                        break;
                }

                i += consumed;
            }
        }

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
                    Timescale = string.Join("", rest.Select(t => t.Text));
                    return rest.Count;

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
                    return UseMacro(token, macroDepth, depth);
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

                    ProcessFile(candidate, text);
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
                _diagnostics.Error(token.Location, DiagnosticCodes.MacroWithArguments,
                    $"`define {name.Text}(...) takes arguments, which BitSorter does not expand yet. A localparam or a function usually does the same job.");
                return;
            }

            _macros[name.Text] = rest.Skip(1).ToList();
        }

        private int UseMacro(Token token, int macroDepth, int depth)
        {
            string name = token.Text.Substring(1);

            if (!_macros.TryGetValue(name, out List<Token> body))
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

            var withEnd = new List<Token>(body) { new Token(TokenKind.EndOfFile, string.Empty, token.Location, token.LogicalLine) };
            Process(withEnd, macroDepth + 1, depth + 1);
            return 0;
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
}
