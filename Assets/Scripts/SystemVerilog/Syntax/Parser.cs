using System;
using System.Collections.Generic;
using System.Text;

namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>
    /// Reads SystemVerilog source into a <see cref="SyntaxTree"/>: the whole language the course's
    /// workshops use, and the common general forms beside it (Docs/architecture-5.md, section 7).
    /// </summary>
    /// <remarks>
    /// - **Reading is not understanding.** The parser accepts behavioural code that the simulator
    ///   only runs from Part 5 on; what a construct means, and whether BitSorter can simulate it, is
    ///   decided after it has been read. Constructs outside the language BitSorter reads at all --
    ///   classes, interfaces, assertions -- are reported where they stand and skipped, never crashed on.
    /// - **It never throws and never hangs.** Every error is a diagnostic; after one, it skips to a
    ///   place it can carry on from (a <c>;</c>, a keyword that starts something, <c>endmodule</c>),
    ///   and every loop is guarded to make progress. Nesting deeper than <see cref="MaxDepth"/> stops
    ///   the read rather than the stack, which in a browser is small.
    /// - **Messages are for students**: what was expected, in the course's words, at the place a student
    ///   will look -- a missing <c>;</c> is reported on the line it is missing from, not the next one.
    /// </remarks>
    public sealed partial class Parser
    {
        /// <summary>How deep expressions, statements and generate blocks may nest.</summary>
        public const int MaxDepth = 200;

        private readonly List<Token> _tokens;
        private readonly DiagnosticBag _diagnostics;
        private int _at;
        private int _depth;
        private bool _stopped;

        /// <summary>Names declared by <c>typedef</c> so far: a statement or item starting with one is a declaration.</summary>
        private readonly HashSet<string> _typeNames = new HashSet<string>(StringComparer.Ordinal);

        public Parser(List<Token> tokens, DiagnosticBag diagnostics)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            if (_tokens.Count == 0 || _tokens[_tokens.Count - 1].Kind != TokenKind.EndOfFile)
                _tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, default, 0));
        }

        // -----------------------------------------------------------------
        // Moving through the tokens
        // -----------------------------------------------------------------

        private Token Current => _stopped ? _tokens[_tokens.Count - 1] : _tokens[_at];
        private Token Peek(int ahead) => _stopped ? _tokens[_tokens.Count - 1] : _tokens[Math.Min(_at + ahead, _tokens.Count - 1)];
        private Token Previous => _tokens[Math.Max(0, Math.Min(_at, _tokens.Count) - 1)];
        private bool AtEnd => Current.Kind == TokenKind.EndOfFile;

        private Token Next()
        {
            Token token = Current;
            if (!AtEnd)
                _at++;
            return token;
        }

        private bool Accept(string symbol)
        {
            if (!Current.Is(symbol))
                return false;
            _at++;
            return true;
        }

        private bool AcceptKeyword(string keyword)
        {
            if (!Current.IsKeyword(keyword))
                return false;
            _at++;
            return true;
        }

        private bool Expect(string symbol, string context)
        {
            if (Accept(symbol))
                return true;

            if (symbol == ";" && _at > 0)
                Error(Previous.Location, DiagnosticCodes.Expected, $"Missing ';' {context}.");
            else
                Error(Current.Location, DiagnosticCodes.Expected, $"Expected '{symbol}' {context}, but found {Describe(Current)}.");
            return false;
        }

        private bool ExpectKeyword(string keyword, string context)
        {
            if (AcceptKeyword(keyword))
                return true;

            Error(Current.Location, DiagnosticCodes.Expected, $"Expected '{keyword}' {context}, but found {Describe(Current)}.");
            return false;
        }

        private string ExpectIdentifier(string what)
        {
            if (Current.Kind == TokenKind.Identifier)
                return Next().Text;

            if (Current.Kind == TokenKind.Keyword)
                Error(Current.Location, DiagnosticCodes.Expected, $"'{Current.Text}' is a reserved word in SystemVerilog and cannot be {what}; choose another name.");
            else
                Error(Current.Location, DiagnosticCodes.Expected, $"Expected {what}, but found {Describe(Current)}.");
            return null;
        }

        private static string Describe(Token token) =>
            token.Kind == TokenKind.EndOfFile ? "the end of the file"
            : token.Kind == TokenKind.String ? $"the string \"{token.Text}\""
            : $"'{token.Text}'";

        private void Error(SourceLocation at, string code, string message) => _diagnostics.Error(at, code, message);

        /// <summary>One level deeper; false, having stopped the read, if that is too deep.</summary>
        private bool Enter()
        {
            if (_stopped)
                return false;

            if (++_depth <= MaxDepth)
                return true;

            Error(Current.Location, DiagnosticCodes.TooDeep, $"This nests more than {MaxDepth} levels deep; BitSorter stopped reading here.");
            _stopped = true;
            return false;
        }

        private void Leave() => _depth--;

        /// <summary>Keywords that start a module item: where error recovery can safely pick up again.</summary>
        private static readonly HashSet<string> ItemStarts = new HashSet<string>(StringComparer.Ordinal)
        {
            "module", "endmodule", "assign", "always", "always_comb", "always_ff", "always_latch", "initial", "final",
            "parameter", "localparam", "logic", "wire", "reg", "bit", "int", "integer", "genvar", "generate",
            "endgenerate", "typedef", "function", "endfunction", "task", "endtask", "input", "output", "inout",
            "and", "or", "nand", "nor", "xor", "xnor", "not", "buf",
        };

        /// <summary>After an error in an item: past the next <c>;</c>, or up to a keyword that starts something.</summary>
        private void SkipToItemEnd()
        {
            while (!AtEnd)
            {
                if (Accept(";"))
                    return;

                if (Current.Kind == TokenKind.Keyword && ItemStarts.Contains(Current.Text) && Current.Text != "or" && Current.Text != "and")
                    return;

                Next();
            }
        }

        // -----------------------------------------------------------------
        // The file
        // -----------------------------------------------------------------

        /// <summary>Reads every module and file-level item into <paramref name="tree"/>.</summary>
        public void ParseInto(SyntaxTree tree)
        {
            while (!AtEnd)
            {
                int start = _at;
                Token t = Current;

                if (t.IsKeyword("module") || t.IsKeyword("macromodule"))
                {
                    tree.Modules.Add(ParseModule());
                }
                else if (t.IsKeyword("typedef") || t.IsKeyword("parameter") || t.IsKeyword("localparam") ||
                         t.IsKeyword("function") || t.IsKeyword("task"))
                {
                    ParseModuleItem(tree.UnitItems);
                }
                else if (t.IsKeyword("timeunit") || t.IsKeyword("timeprecision"))
                {
                    SkipToItemEnd();
                }
                else if (t.Kind == TokenKind.Keyword && UnsupportedBlocks.TryGetValue(t.Text, out string end))
                {
                    tree.UnitItems.Add(SkipUnsupported(Plural(t.Text) + " are not supported by BitSorter", end));
                }
                else if (t.IsKeyword("import") || t.IsKeyword("export"))
                {
                    tree.UnitItems.Add(SkipUnsupported("Packages are not supported by BitSorter, so there is nothing to import", null));
                }
                else if (t.Is(";"))
                {
                    Next();
                }
                else
                {
                    Error(t.Location, DiagnosticCodes.NotInModule,
                        $"Everything has to be inside a module, but {Describe(t)} is outside one.");
                    while (!AtEnd && !Current.IsKeyword("module") && !Current.IsKeyword("macromodule"))
                        Next();
                }

                if (_at == start && !AtEnd)
                    Next();
            }
        }

        private static string Plural(string keyword)
        {
            switch (keyword)
            {
                case "class": return "Classes";
                case "property": return "Properties";
                case "primitive": return "User-defined primitives (UDPs)";
                case "config": return "Configurations";
                case "specify": return "Specify blocks";
                case "clocking": return "Clocking blocks";
                default: return char.ToUpperInvariant(keyword[0]) + keyword.Substring(1) + "s";
            }
        }

        /// <summary>Constructs BitSorter does not read at all, and the keyword each ends with.</summary>
        private static readonly Dictionary<string, string> UnsupportedBlocks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["package"] = "endpackage", ["interface"] = "endinterface", ["program"] = "endprogram",
            ["class"] = "endclass", ["primitive"] = "endprimitive", ["config"] = "endconfig",
            ["checker"] = "endchecker", ["specify"] = "endspecify", ["property"] = "endproperty",
            ["sequence"] = "endsequence", ["clocking"] = "endclocking", ["covergroup"] = "endgroup",
        };

        /// <summary>
        /// Reports a construct BitSorter does not read, and skips it: to <paramref name="end"/> when it
        /// has an end keyword, or past its <c>;</c>. Its text is kept for printing.
        /// </summary>
        private UnsupportedItem SkipUnsupported(string message, string end)
        {
            Token first = Current;
            Error(first.Location, DiagnosticCodes.Unsupported, message + ".");
            var text = new StringBuilder();
            int nesting = 0;
            string opener = first.Text;

            while (!AtEnd)
            {
                Token t = Next();
                if (text.Length > 0) text.Append(' ');
                text.Append(t.Kind == TokenKind.String ? "\"" + t.Text + "\"" : t.Text);

                if (end == null)
                {
                    if (t.Is(";"))
                        break;
                }
                else if (t.IsKeyword(opener) && t != first)
                {
                    nesting++;
                }
                else if (t.IsKeyword(end))
                {
                    if (nesting == 0)
                    {
                        if (Accept(":"))
                            text.Append(" : ").Append(Next().Text);
                        break;
                    }

                    nesting--;
                }
                else if (t.IsKeyword("endmodule") && end != "endmodule")
                {
                    _at--;   // never eat the module's own end
                    text.Length = Math.Max(0, text.Length - t.Text.Length - 1);
                    break;
                }
            }

            return new UnsupportedItem { Location = first.Location, What = message, Text = text.ToString() };
        }

        // -----------------------------------------------------------------
        // Modules
        // -----------------------------------------------------------------

        private ModuleDeclaration ParseModule()
        {
            var module = new ModuleDeclaration { Location = Current.Location };
            Next();

            if (!AcceptKeyword("automatic"))
                AcceptKeyword("static");

            module.Name = ExpectIdentifier("the module's name") ?? "?";
            module.Comment = Previous.TrailingComment;

            if (Accept("#"))
            {
                module.HasParameterList = true;
                if (Expect("(", "to open the module's parameter list"))
                {
                    ParseHeaderParameters(module.HeaderParameters);
                    Expect(")", "to close the module's parameter list");
                }
            }

            if (Accept("("))
            {
                module.HasPortList = true;
                ParsePortList(module);
                Expect(")", "to close the module's port list");
            }

            if (!Expect(";", $"after the header of module {module.Name}"))
                SkipToItemEnd();
            else if (module.Comment == null && !module.HasPortList)
                module.Comment = Previous.TrailingComment;   // module tb; // what it is

            ParseItems(module.Items, "endmodule");

            if (!AcceptKeyword("endmodule"))
            {
                Error(Current.Location, DiagnosticCodes.Expected, $"Module {module.Name} never ends: 'endmodule' is missing.");
                return module;
            }

            if (Accept(":"))
            {
                string label = ExpectIdentifier("the module's name after 'endmodule :'");
                if (label != null && label != module.Name)
                    _diagnostics.Warning(Previous.Location, DiagnosticCodes.MismatchedLabel, $"'endmodule : {label}' closes module {module.Name}.");
            }

            return module;
        }

        private void ParseHeaderParameters(List<ParameterDeclaration> into)
        {
            if (Current.Is(")"))
                return;

            bool local = false;
            DataType type = null;

            do
            {
                if (Current.Is(")"))
                    break;

                if (AcceptKeyword("parameter"))
                {
                    local = false;
                    type = null;
                }
                else if (AcceptKeyword("localparam"))
                {
                    local = true;
                    type = null;
                }

                if (Current.IsKeyword("type"))
                {
                    Error(Current.Location, DiagnosticCodes.Unsupported, "Type parameters are not supported by BitSorter.");
                    while (!AtEnd && !Current.Is(",") && !Current.Is(")"))
                        Next();
                    continue;
                }

                var parameter = new ParameterDeclaration { Location = Current.Location, IsLocal = local };
                DataType written = TryParseParameterType();
                if (written != null)
                    type = written;

                parameter.Type = type ?? new DataType { Location = parameter.Location };
                parameter.Name = ExpectIdentifier("a parameter's name") ?? "?";
                SkipUnpacked();

                if (Accept("="))
                    parameter.Value = ParseExpression();

                parameter.Comment = Current.Is(",") ? Current.TrailingComment : Previous.TrailingComment;
                into.Add(parameter);
            }
            while (Accept(","));
        }

        /// <summary>A parameter's type, if one is written before its name.</summary>
        private DataType TryParseParameterType()
        {
            if (IsDataTypeStart(Current) || Current.Is("[") || Current.IsKeyword("signed") || Current.IsKeyword("unsigned"))
                return ParseDataType();

            if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier)
                return ParseDataType();

            return null;
        }

        private void SkipUnpacked()
        {
            while (Current.Is("["))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, "An array of parameters is not supported by BitSorter.");
                ParseDimension();
            }
        }

        private void ParsePortList(ModuleDeclaration module)
        {
            if (Current.Is(")"))
                return;

            // A non-ANSI header lists only names; their directions are declared in the body.
            if (Current.Kind == TokenKind.Identifier && (Peek(1).Is(",") || Peek(1).Is(")")) && !_typeNames.Contains(Current.Text))
            {
                do
                {
                    string name = ExpectIdentifier("a port's name");
                    if (name != null)
                        module.PortNames.Add(name);
                }
                while (Accept(","));

                return;
            }

            PortDirection direction = PortDirection.Input;
            bool haveDirection = false;
            string netType = null;
            DataType type = null;

            do
            {
                if (Current.Is(")"))
                    break;

                var port = new PortDeclaration { Location = Current.Location };
                bool fresh = false;

                if (TryDirection(out PortDirection written))
                {
                    direction = written;
                    haveDirection = true;
                    fresh = true;
                }
                else if (!haveDirection)
                {
                    Error(Current.Location, DiagnosticCodes.Expected,
                        $"Expected a direction for this port -- input, output or inout -- but found {Describe(Current)}.");
                    haveDirection = true;
                }

                string writtenNet = TryNetType();
                DataType writtenType = null;

                if (IsDataTypeStart(Current) || Current.Is("[") || Current.IsKeyword("signed") || Current.IsKeyword("unsigned") ||
                    (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier))
                {
                    writtenType = ParseDataType();
                }

                if (fresh || writtenNet != null || writtenType != null)
                {
                    netType = writtenNet;
                    type = writtenType ?? new DataType { Location = port.Location };
                }

                port.Direction = direction;
                port.NetType = netType;
                port.Type = type ?? new DataType { Location = port.Location };
                port.Name = ExpectIdentifier("a port's name") ?? "?";
                ParseUnpacked(port.Unpacked);

                if (Accept("="))
                {
                    Error(Previous.Location, DiagnosticCodes.Unsupported, "A port with a default value is not supported by BitSorter.");
                    ParseExpression();
                }

                port.Comment = Current.Is(",") && Current.TrailingComment != null ? Current.TrailingComment : Previous.TrailingComment;
                module.Ports.Add(port);

                if (!Current.Is(",") && !Current.Is(")"))
                {
                    Error(Current.Location, DiagnosticCodes.Expected, $"Expected ',' or ')' after port {port.Name}, but found {Describe(Current)}.");
                    while (!AtEnd && !Current.Is(",") && !Current.Is(")") && !Current.Is(";"))
                        Next();
                }
            }
            while (Accept(","));
        }

        private bool TryDirection(out PortDirection direction)
        {
            direction = PortDirection.Input;

            if (AcceptKeyword("input")) direction = PortDirection.Input;
            else if (AcceptKeyword("output")) direction = PortDirection.Output;
            else if (AcceptKeyword("inout")) direction = PortDirection.Inout;
            else return false;

            return true;
        }

        private static readonly HashSet<string> NetTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "wire", "tri", "tri0", "tri1", "triand", "trior", "trireg", "wand", "wor", "uwire", "supply0", "supply1",
        };

        private static readonly HashSet<string> DataTypeKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "logic", "bit", "reg", "int", "integer", "byte", "shortint", "longint", "time", "real", "realtime",
            "shortreal", "string", "event", "chandle", "enum", "struct", "union", "var",
        };

        private string TryNetType()
        {
            if (Current.Kind != TokenKind.Keyword || !NetTypes.Contains(Current.Text))
                return null;

            string net = Next().Text;
            if (!AcceptKeyword("vectored"))
                AcceptKeyword("scalared");
            return net;
        }

        private static bool IsDataTypeStart(Token token) =>
            token.Kind == TokenKind.Keyword && DataTypeKeywords.Contains(token.Text);

        /// <summary>A data type: a keyword or a typedef's name, its signedness and its packed dimensions.</summary>
        private DataType ParseDataType()
        {
            var type = new DataType { Location = Current.Location };
            AcceptKeyword("var");

            if (Current.IsKeyword("enum"))
            {
                type.Enum = ParseEnum();
                return type;
            }

            if (Current.IsKeyword("struct") || Current.IsKeyword("union"))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, $"A {Current.Text} is not supported by BitSorter yet; use separate signals.");
                type.Keyword = Next().Text;
                AcceptKeyword("packed");
                SkipBalanced("{", "}");
            }
            else if (IsDataTypeStart(Current))
            {
                type.Keyword = Next().Text;
            }
            else if (Current.Kind == TokenKind.Identifier && !Current.Is("["))
            {
                type.Name = Next().Text;
                if (Current.Is("::"))
                {
                    Error(Current.Location, DiagnosticCodes.Unsupported, "Packages are not supported by BitSorter, so 'package::type' cannot be used.");
                    Next();
                    if (Current.Kind == TokenKind.Identifier)
                        type.Name = Next().Text;
                }
            }

            if (AcceptKeyword("signed"))
                type.Signed = true;
            else if (AcceptKeyword("unsigned"))
                type.Unsigned = true;

            while (Current.Is("["))
                type.Packed.Add(ParseDimension());

            return type;
        }

        private EnumType ParseEnum()
        {
            var e = new EnumType { Location = Current.Location };
            Next();

            if (!Current.Is("{"))
                e.BaseType = ParseDataType();

            if (!Expect("{", "to open the enum's names"))
                return e;

            if (!Current.Is("}"))
            {
                do
                {
                    if (Current.Is("}"))
                        break;

                    var member = new EnumMember { Location = Current.Location, Name = ExpectIdentifier("a name in the enum") ?? "?" };
                    if (Current.Is("["))
                    {
                        Error(Current.Location, DiagnosticCodes.Unsupported, "A range of enum names (name[N]) is not supported by BitSorter.");
                        ParseDimension();
                    }

                    if (Accept("="))
                        member.Value = ParseExpression();

                    e.Members.Add(member);
                }
                while (Accept(","));
            }

            Expect("}", "to close the enum's names");
            return e;
        }

        private void SkipBalanced(string open, string close)
        {
            if (!Current.Is(open))
                return;

            int depth = 0;
            while (!AtEnd)
            {
                Token t = Next();
                if (t.Is(open)) depth++;
                else if (t.Is(close) && --depth == 0) return;
            }
        }

        private Dimension ParseDimension()
        {
            var dimension = new Dimension { Location = Current.Location };
            Next();

            if (Current.Is("]"))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, "A dynamic array [] is not supported by BitSorter.");
                Next();
                dimension.Left = new ErrorExpression { Location = dimension.Location };
                return dimension;
            }

            dimension.Left = ParseExpression();
            if (Accept(":"))
                dimension.Right = ParseExpression();

            Expect("]", "to close the range");
            return dimension;
        }

        private void ParseUnpacked(List<Dimension> into)
        {
            while (Current.Is("["))
                into.Add(ParseDimension());
        }

        // -----------------------------------------------------------------
        // Module items
        // -----------------------------------------------------------------

        /// <summary>Reads items until <paramref name="end"/> (not consumed), <c>endmodule</c> or the end of the file.</summary>
        private void ParseItems(List<ModuleItem> into, string end)
        {
            while (!AtEnd && !Current.IsKeyword(end) && !(end != "endmodule" && Current.IsKeyword("endmodule")))
            {
                int start = _at;
                ParseModuleItem(into);

                if (_at == start && !AtEnd)
                {
                    Error(Current.Location, DiagnosticCodes.Expected, $"{Describe(Current)} does not start anything a module can hold.");
                    Next();
                }
            }
        }

        private static readonly HashSet<string> Primitives = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "or", "nand", "nor", "xor", "xnor", "not", "buf", "bufif0", "bufif1", "notif0", "notif1",
        };

        private static readonly HashSet<string> SwitchPrimitives = new HashSet<string>(StringComparer.Ordinal)
        {
            "cmos", "rcmos", "nmos", "pmos", "rnmos", "rpmos", "tran", "tranif0", "tranif1", "rtran", "rtranif0",
            "rtranif1", "pullup", "pulldown",
        };

        private void ParseModuleItem(List<ModuleItem> into)
        {
            Token t = Current;

            if (t.Is(";"))
            {
                Next();
                return;
            }

            if (t.Kind == TokenKind.Identifier)
            {
                ModuleItem item = ParseIdentifierItem();
                if (item != null)
                    into.Add(item);
                return;
            }

            if (t.Kind == TokenKind.SystemName)
            {
                into.Add(SkipUnsupported($"{t.Text} outside a process is not supported by BitSorter", null));
                return;
            }

            if (t.Kind != TokenKind.Keyword)
            {
                Error(t.Location, DiagnosticCodes.Expected, $"{Describe(t)} does not start anything a module can hold.");
                Next();
                SkipToItemEnd();
                return;
            }

            switch (t.Text)
            {
                case "parameter":
                case "localparam":
                    ParseParameterItems(into);
                    return;

                case "input":
                case "output":
                case "inout":
                    into.Add(ParsePortItem());
                    return;

                case "typedef":
                    into.Add(ParseTypedef());
                    return;

                case "genvar":
                    into.Add(ParseGenvar());
                    return;

                case "assign":
                    into.Add(ParseContinuousAssign());
                    return;

                case "generate":
                {
                    var region = new GenerateRegion { Location = t.Location };
                    Next();
                    ParseItems(region.Items, "endgenerate");
                    ExpectKeyword("endgenerate", "to close 'generate'");
                    into.Add(region);
                    return;
                }

                case "for":
                    into.Add(ParseLoopGenerate());
                    return;

                case "if":
                    into.Add(ParseIfGenerate());
                    return;

                case "begin":
                    into.Add(ParseGenerateBlock());
                    return;

                case "case":
                    into.Add(SkipUnsupported("A case in a module (a case-generate) is not supported by BitSorter; use if-generate", "endcase"));
                    return;

                case "initial":
                case "final":
                case "always":
                case "always_comb":
                case "always_ff":
                case "always_latch":
                    into.Add(ParseProcess());
                    return;

                case "function":
                case "task":
                    into.Add(ParseSubroutine());
                    return;

                case "defparam":
                    into.Add(SkipUnsupported("defparam is not supported by BitSorter; give the parameter at the instance with #(.NAME(value))", null));
                    return;

                case "timeunit":
                case "timeprecision":
                    SkipToItemEnd();
                    return;

                case "import":
                case "export":
                    into.Add(SkipUnsupported("Packages are not supported by BitSorter, so there is nothing to import", null));
                    return;

                case "assert":
                case "assume":
                case "cover":
                case "restrict":
                    into.Add(SkipUnsupported("Assertions are not supported by BitSorter", null));
                    return;

                case "alias":
                    into.Add(SkipUnsupported("alias is not supported by BitSorter", null));
                    return;

                case "endmodule":
                    return;
            }

            if (Primitives.Contains(t.Text))
            {
                into.Add(ParsePrimitive());
                return;
            }

            if (SwitchPrimitives.Contains(t.Text))
            {
                into.Add(SkipUnsupported($"The switch-level primitive '{t.Text}' is not supported by BitSorter", null));
                return;
            }

            if (UnsupportedBlocks.TryGetValue(t.Text, out string end))
            {
                into.Add(SkipUnsupported(Plural(t.Text) + " are not supported by BitSorter", end));
                return;
            }

            if (NetTypes.Contains(t.Text) || IsDataTypeStart(t) || t.IsKeyword("const") || t.IsKeyword("signed") || t.IsKeyword("unsigned"))
            {
                into.Add(ParseDataDeclaration());
                return;
            }

            Error(t.Location, DiagnosticCodes.Expected, $"'{t.Text}' does not start anything a module can hold.");
            Next();
            SkipToItemEnd();
        }

        private void ParseParameterItems(List<ModuleItem> into)
        {
            var first = Current;
            bool local = Next().Text == "localparam";

            if (Current.IsKeyword("type"))
            {
                into.Add(SkipUnsupported("Type parameters are not supported by BitSorter", null));
                return;
            }

            DataType type = TryParseParameterType() ?? new DataType { Location = first.Location };
            var made = new List<ParameterDeclaration>();

            do
            {
                var parameter = new ParameterDeclaration { Location = made.Count == 0 ? first.Location : Current.Location, IsLocal = local, Type = type };
                parameter.Name = ExpectIdentifier("a parameter's name") ?? "?";
                SkipUnpacked();

                if (Expect("=", $"and a value for {(local ? "localparam" : "parameter")} {parameter.Name}"))
                    parameter.Value = ParseExpression();

                made.Add(parameter);
            }
            while (Accept(","));

            if (!Expect(";", $"after {(local ? "localparam" : "parameter")} {made[made.Count - 1].Name}"))
                SkipToItemEnd();

            made[made.Count - 1].Comment = Previous.TrailingComment;
            into.AddRange(made);
        }

        private PortItemDeclaration ParsePortItem()
        {
            var item = new PortItemDeclaration { Location = Current.Location };
            TryDirection(out PortDirection direction);
            item.Direction = direction;
            item.NetType = TryNetType();

            if (IsDataTypeStart(Current) || Current.Is("[") || Current.IsKeyword("signed") || Current.IsKeyword("unsigned") ||
                (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier))
                item.Type = ParseDataType();
            else
                item.Type = new DataType { Location = item.Location };

            ParseDeclarators(item.Declarators, allowInitializer: false);
            if (!Expect(";", "after the port declaration"))
                SkipToItemEnd();
            item.Comment = Previous.TrailingComment;
            return item;
        }

        private DataDeclaration ParseDataDeclaration()
        {
            var declaration = new DataDeclaration { Location = Current.Location };

            if (AcceptKeyword("const"))
                declaration.IsConst = true;

            declaration.NetType = TryNetType();

            if (declaration.NetType != null)
            {
                if (Current.Is("("))
                {
                    Error(Current.Location, DiagnosticCodes.Unsupported, "Drive strengths are not supported by BitSorter.");
                    SkipBalanced("(", ")");
                }

                if (Current.Is("#"))
                    declaration.Delay = ParseDelay();

                bool typed = IsDataTypeStart(Current) || Current.Is("[") || Current.IsKeyword("signed") || Current.IsKeyword("unsigned") ||
                             (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier);
                declaration.Type = typed ? ParseDataType() : new DataType { Location = declaration.Location };
            }
            else
            {
                declaration.Type = ParseDataType();
            }

            ParseDeclarators(declaration.Declarators, allowInitializer: true);

            if (!Expect(";", "after the declaration"))
                SkipToItemEnd();

            declaration.Comment = Previous.TrailingComment;
            return declaration;
        }

        private void ParseDeclarators(List<Declarator> into, bool allowInitializer)
        {
            do
            {
                var declarator = new Declarator { Location = Current.Location, Name = ExpectIdentifier("a name to declare") };
                if (declarator.Name == null)
                    return;

                ParseUnpacked(declarator.Unpacked);

                if (Current.Is("="))
                {
                    Next();
                    declarator.Initializer = ParseExpression();
                    if (!allowInitializer)
                        Error(declarator.Location, DiagnosticCodes.Unsupported, $"A port declaration cannot give {declarator.Name} a value.");
                }

                into.Add(declarator);
            }
            while (Accept(","));
        }

        private TypedefDeclaration ParseTypedef()
        {
            var typedef = new TypedefDeclaration { Location = Current.Location };
            Next();

            if (Current.IsKeyword("enum") || IsDataTypeStart(Current) || Current.Kind == TokenKind.Identifier)
                typedef.Type = ParseDataType();
            else
            {
                Error(Current.Location, DiagnosticCodes.Expected, $"Expected a type after 'typedef', but found {Describe(Current)}.");
                typedef.Type = new DataType { Location = Current.Location };
            }

            typedef.Name = ExpectIdentifier("the new type's name") ?? "?";
            _typeNames.Add(typedef.Name);
            ParseUnpacked(new List<Dimension>());

            if (!Expect(";", $"after typedef {typedef.Name}"))
                SkipToItemEnd();

            typedef.Comment = Previous.TrailingComment;
            return typedef;
        }

        private GenvarDeclaration ParseGenvar()
        {
            var genvar = new GenvarDeclaration { Location = Current.Location };
            Next();

            do
            {
                string name = ExpectIdentifier("a genvar's name");
                if (name != null)
                    genvar.Names.Add(name);
            }
            while (Accept(","));

            if (!Expect(";", "after the genvar"))
                SkipToItemEnd();
            genvar.Comment = Previous.TrailingComment;
            return genvar;
        }

        private ContinuousAssign ParseContinuousAssign()
        {
            var assign = new ContinuousAssign { Location = Current.Location };
            Next();

            if (Current.Is("("))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, "Drive strengths are not supported by BitSorter.");
                SkipBalanced("(", ")");
            }

            if (Current.Is("#"))
                assign.Delay = ParseDelay();

            do
            {
                var assignment = new Assignment { Location = Current.Location, Target = ParseLValue() };
                if (Expect("=", "in the assign"))
                    assignment.Value = ParseExpression();
                else
                    assignment.Value = new ErrorExpression { Location = Current.Location };
                assign.Assignments.Add(assignment);
            }
            while (Accept(","));

            if (!Expect(";", "after the assign"))
                SkipToItemEnd();

            assign.Comment = Previous.TrailingComment;
            return assign;
        }

        private PrimitiveInstantiation ParsePrimitive()
        {
            var primitive = new PrimitiveInstantiation { Location = Current.Location, Primitive = Next().Text };

            if (Current.Is("(") && Peek(1).Kind == TokenKind.Keyword && (Peek(1).Text.StartsWith("strong", StringComparison.Ordinal) ||
                Peek(1).Text.StartsWith("weak", StringComparison.Ordinal) || Peek(1).Text.StartsWith("supply", StringComparison.Ordinal) ||
                Peek(1).Text.StartsWith("pull", StringComparison.Ordinal) || Peek(1).Text.StartsWith("highz", StringComparison.Ordinal)))
            {
                Error(Current.Location, DiagnosticCodes.Unsupported, "Drive strengths are not supported by BitSorter.");
                SkipBalanced("(", ")");
            }

            if (Current.Is("#"))
                primitive.Delay = ParseDelay();

            do
            {
                var instance = new PrimitiveInstance { Location = Current.Location };

                if (Current.Kind == TokenKind.Identifier)
                {
                    instance.Name = Next().Text;
                    if (Current.Is("["))
                        instance.Array = ParseDimension();
                }

                if (Expect("(", $"to list the {primitive.Primitive} gate's output and inputs"))
                {
                    do
                    {
                        instance.Terminals.Add(ParseExpression());
                    }
                    while (Accept(","));

                    Expect(")", $"to close the {primitive.Primitive} gate's connections");
                }

                primitive.Instances.Add(instance);
            }
            while (Accept(","));

            if (!Expect(";", $"after the {primitive.Primitive} gate"))
                SkipToItemEnd();

            primitive.Comment = Previous.TrailingComment;
            return primitive;
        }

        /// <summary>
        /// An item starting with a name: an instance of a module (<c>adder a1 (...)</c>), a
        /// declaration of a typedef's type (<c>state_t state;</c>), or a mistake.
        /// </summary>
        private ModuleItem ParseIdentifierItem()
        {
            Token first = Current;
            Token second = Peek(1);

            if (_typeNames.Contains(first.Text))
                return ParseDataDeclaration();

            if (second.Is("#"))
                return ParseModuleInstantiation();

            if (second.Kind == TokenKind.Identifier)
            {
                Token third = Peek(2);
                if (third.Is("("))
                    return ParseModuleInstantiation();

                if (third.Is("["))
                {
                    // An instance array, a[3:0] (...), or an unpacked array of a named type.
                    int close = MatchingBracket(_at + 2);
                    if (close > 0 && close + 1 < _tokens.Count && _tokens[close + 1].Is("("))
                        return ParseModuleInstantiation();
                }

                return ParseDataDeclaration();
            }

            if (second.Is("::"))
                return SkipUnsupported("Packages are not supported by BitSorter, so 'package::name' cannot be used", null);

            if (second.Is("=") || second.Is("<=") || second.Is("[") || second.Is("."))
            {
                Error(first.Location, DiagnosticCodes.AssignmentOutsideProcess,
                    $"'{first.Text} = ...' outside always or initial: in a module, write 'assign {first.Text} = ...;'.");
                SkipToItemEnd();
                return null;
            }

            Error(second.Location, DiagnosticCodes.Expected,
                $"After '{first.Text}', expected an instance name -- '{first.Text} u1 (...);' -- but found {Describe(second)}.");
            Next();
            SkipToItemEnd();
            return null;
        }

        private int MatchingBracket(int open)
        {
            int depth = 0;
            for (int i = open; i < _tokens.Count; i++)
            {
                if (_tokens[i].Is("[")) depth++;
                else if (_tokens[i].Is("]") && --depth == 0) return i;
                else if (_tokens[i].Is(";") || _tokens[i].Kind == TokenKind.EndOfFile) return -1;
            }

            return -1;
        }

        private ModuleInstantiation ParseModuleInstantiation()
        {
            var instantiation = new ModuleInstantiation { Location = Current.Location, Module = Next().Text };

            if (Accept("#"))
            {
                instantiation.HasParameterList = true;

                if (Accept("("))
                {
                    if (!Current.Is(")"))
                    {
                        do
                        {
                            var value = new ParameterValue { Location = Current.Location };

                            if (Accept("."))
                            {
                                value.Name = ExpectIdentifier("the parameter's name") ?? "?";
                                if (Expect("(", $"after .{value.Name}"))
                                {
                                    if (!Current.Is(")"))
                                        value.Value = ParseExpression();
                                    Expect(")", $"to close .{value.Name}(...)");
                                }
                            }
                            else
                            {
                                value.Value = ParseExpression();
                            }

                            instantiation.Parameters.Add(value);
                        }
                        while (Accept(","));
                    }

                    Expect(")", "to close the parameter values");
                }
                else
                {
                    // Verilog's #5 with no parentheses: one parameter by position.
                    instantiation.Parameters.Add(new ParameterValue { Location = Current.Location, Value = ParsePrimary() });
                }
            }

            do
            {
                var instance = new ModuleInstance { Location = Current.Location };
                instance.Name = ExpectIdentifier($"a name for this {instantiation.Module}") ?? "?";

                if (Current.Is("["))
                    instance.Array = ParseDimension();

                if (Expect("(", $"to list the connections of {instance.Name}"))
                {
                    if (!Current.Is(")"))
                    {
                        do
                        {
                            instance.Connections.Add(ParseConnection());
                        }
                        while (Accept(","));
                    }

                    Expect(")", $"to close the connections of {instance.Name}");
                }

                instantiation.Instances.Add(instance);
            }
            while (Accept(","));

            if (!Expect(";", $"after the instance of {instantiation.Module}"))
                SkipToItemEnd();

            instantiation.Comment = Previous.TrailingComment;
            return instantiation;
        }

        private PortConnection ParseConnection()
        {
            var connection = new PortConnection { Location = Current.Location };

            if (Accept(".*"))
            {
                connection.IsWildcard = true;
                return connection;
            }

            if (Accept("."))
            {
                connection.Name = ExpectIdentifier("a port's name after '.'") ?? "?";

                if (Accept("("))
                {
                    if (!Current.Is(")"))
                        connection.Value = ParseExpression();
                    Expect(")", $"to close .{connection.Name}(...)");
                }
                else
                {
                    connection.IsShorthand = true;
                }

                return connection;
            }

            if (Current.Is(",") || Current.Is(")"))
                return connection;   // left open, by position

            connection.Value = ParseExpression();
            return connection;
        }

        private LoopGenerate ParseLoopGenerate()
        {
            var loop = new LoopGenerate { Location = Current.Location };
            Next();

            if (!Expect("(", "after 'for'"))
            {
                SkipToItemEnd();
                return loop;
            }

            if (AcceptKeyword("genvar"))
                loop.DeclaresGenvar = true;

            loop.Variable = ExpectIdentifier("the loop's genvar") ?? "?";
            if (Expect("=", $"to give {loop.Variable} its first value"))
                loop.Start = ParseExpression();
            Expect(";", "after the loop's start");

            loop.Condition = ParseExpression();
            Expect(";", "after the loop's condition");

            if (Accept("++")) { loop.StepOperator = "++"; ExpectIdentifier("the genvar"); }
            else if (Accept("--")) { loop.StepOperator = "--"; ExpectIdentifier("the genvar"); }
            else
            {
                ExpectIdentifier("the genvar in the loop's step");
                if (Accept("++")) loop.StepOperator = "++";
                else if (Accept("--")) loop.StepOperator = "--";
                else if (Current.Is("=") || Current.Is("+=") || Current.Is("-=") || Current.Is("*="))
                {
                    loop.StepOperator = Next().Text;
                    loop.StepValue = ParseExpression();
                }
                else
                {
                    Error(Current.Location, DiagnosticCodes.Expected, $"Expected the loop's step -- {loop.Variable}++ or {loop.Variable} = {loop.Variable} + 1 -- but found {Describe(Current)}.");
                }
            }

            Expect(")", "to close the loop's header");
            loop.Body = ParseGenerateBlock();
            return loop;
        }

        private IfGenerate ParseIfGenerate()
        {
            var item = new IfGenerate { Location = Current.Location };
            Next();

            if (Expect("(", "after 'if'"))
            {
                item.Condition = ParseExpression();
                Expect(")", "to close the condition");
            }

            item.Then = ParseGenerateBlock();

            if (AcceptKeyword("else"))
                item.Else = ParseGenerateBlock();

            return item;
        }

        /// <summary>A generate construct's body: <c>begin [: label] items end</c>, or one item on its own.</summary>
        private GenerateBlock ParseGenerateBlock()
        {
            var block = new GenerateBlock { Location = Current.Location };

            if (!Enter())
                return block;

            try
            {
                if (!Current.IsKeyword("begin"))
                {
                    ParseModuleItem(block.Items);
                    return block;
                }

                block.HasBeginEnd = true;
                Next();

                if (Accept(":"))
                    block.Label = ExpectIdentifier("the block's name");

                ParseItems(block.Items, "end");

                if (ExpectKeyword("end", block.Label != null ? $"to close block {block.Label}" : "to close the 'begin'") && Accept(":"))
                {
                    string label = ExpectIdentifier("the block's name after 'end :'");
                    if (label != null && label != block.Label)
                        _diagnostics.Warning(Previous.Location, DiagnosticCodes.MismatchedLabel, $"'end : {label}' closes block {block.Label ?? "(unnamed)"}.");
                }

                return block;
            }
            finally
            {
                Leave();
            }
        }

        private ProceduralBlock ParseProcess()
        {
            var process = new ProceduralBlock { Location = Current.Location };

            switch (Next().Text)
            {
                case "initial": process.Kind = ProcessKind.Initial; break;
                case "final": process.Kind = ProcessKind.Final; break;
                case "always_comb": process.Kind = ProcessKind.AlwaysComb; break;
                case "always_ff": process.Kind = ProcessKind.AlwaysFf; break;
                case "always_latch": process.Kind = ProcessKind.AlwaysLatch; break;
                default: process.Kind = ProcessKind.Always; break;
            }

            process.Body = ParseStatement();
            return process;
        }

        private SubroutineDeclaration ParseSubroutine()
        {
            var routine = new SubroutineDeclaration { Location = Current.Location, IsTask = Next().Text == "task" };

            if (AcceptKeyword("automatic"))
                routine.IsAutomatic = true;
            else
                AcceptKeyword("static");

            if (!routine.IsTask)
            {
                if (AcceptKeyword("void"))
                    routine.IsVoid = true;
                else if (IsDataTypeStart(Current) || Current.Is("[") || Current.IsKeyword("signed") || Current.IsKeyword("unsigned") ||
                         (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier))
                    routine.ReturnType = ParseDataType();
            }

            routine.Name = ExpectIdentifier(routine.IsTask ? "the task's name" : "the function's name") ?? "?";

            if (Accept("("))
            {
                routine.HasPortList = true;
                ParseSubroutinePorts(routine.Ports);
                Expect(")", $"to close the ports of {routine.Name}");
            }

            if (!Expect(";", $"after the header of {routine.Name}"))
                SkipToItemEnd();

            string end = routine.IsTask ? "endtask" : "endfunction";

            while (!AtEnd && !Current.IsKeyword(end) && !Current.IsKeyword("endmodule"))
            {
                int start = _at;

                if (Current.IsKeyword("input") || Current.IsKeyword("output") || Current.IsKeyword("inout"))
                    routine.Declarations.Add(ParsePortItem());
                else if (IsDeclarationStart())
                    ParseLocalDeclaration(routine.Declarations);
                else
                    routine.Statements.Add(ParseStatement());

                if (_at == start && !AtEnd)
                    Next();
            }

            if (ExpectKeyword(end, $"to close {routine.Name}") && Accept(":"))
                ExpectIdentifier($"the name of {routine.Name} after '{end} :'");

            return routine;
        }

        private void ParseSubroutinePorts(List<SubroutinePort> into)
        {
            if (Current.Is(")"))
                return;

            PortDirection direction = PortDirection.Input;
            DataType type = null;

            do
            {
                var port = new SubroutinePort { Location = Current.Location };

                if (TryDirection(out PortDirection written))
                {
                    direction = written;
                    type = null;
                }
                else if (AcceptKeyword("ref"))
                {
                    Error(Previous.Location, DiagnosticCodes.Unsupported, "'ref' arguments are not supported by BitSorter.");
                }

                if (IsDataTypeStart(Current) || Current.Is("[") || Current.IsKeyword("signed") || Current.IsKeyword("unsigned") ||
                    (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier))
                    type = ParseDataType();

                port.Direction = direction;
                port.Type = type ?? new DataType { Location = port.Location };
                port.Name = ExpectIdentifier("an argument's name") ?? "?";
                ParseUnpacked(new List<Dimension>());

                if (Accept("="))
                    port.Default = ParseExpression();

                into.Add(port);
            }
            while (Accept(","));
        }

        /// <summary>Whether a declaration starts here, inside a block or a subroutine.</summary>
        private bool IsDeclarationStart()
        {
            Token t = Current;

            if (IsDataTypeStart(t) || t.IsKeyword("const") || t.IsKeyword("typedef") || t.IsKeyword("parameter") || t.IsKeyword("localparam") ||
                t.IsKeyword("automatic") || t.IsKeyword("static"))
                return true;

            if (t.Kind != TokenKind.Identifier)
                return false;

            if (_typeNames.Contains(t.Text))
                return !Peek(1).Is("::") && !Peek(1).Is("=") && !Peek(1).Is("<=");

            return Peek(1).Kind == TokenKind.Identifier;
        }

        private void ParseLocalDeclaration(List<ModuleItem> into)
        {
            if (Current.IsKeyword("automatic") || Current.IsKeyword("static"))
                Next();

            if (Current.IsKeyword("typedef"))
                into.Add(ParseTypedef());
            else if (Current.IsKeyword("parameter") || Current.IsKeyword("localparam"))
                ParseParameterItems(into);
            else
                into.Add(ParseDataDeclaration());
        }

        // -----------------------------------------------------------------
        // Delays
        // -----------------------------------------------------------------

        private static readonly HashSet<string> TimeUnits = new HashSet<string>(StringComparer.Ordinal) { "s", "ms", "us", "ns", "ps", "fs" };

        /// <summary><c>#5</c>, <c>#DELAY</c>, <c>#(2, 3)</c> or <c>#(Tpdlh, Tpdhl)</c>.</summary>
        private DelayControl ParseDelay()
        {
            var delay = new DelayControl { Location = Current.Location };
            Next();

            if (Accept("("))
            {
                delay.Parenthesized = true;
                do
                {
                    Expression value = ParseExpression();

                    if (Accept(":"))
                    {
                        // min:typ:max: keep the typical value.
                        _diagnostics.Warning(Previous.Location, DiagnosticCodes.Unsupported, "A min:typ:max delay uses its typical (middle) value in BitSorter.");
                        value = ParseExpression();
                        if (Accept(":"))
                            ParseExpression();
                    }

                    delay.Values.Add(value);
                }
                while (Accept(","));

                Expect(")", "to close the delay");
                return delay;
            }

            if (Current.Kind == TokenKind.Number || Current.Kind == TokenKind.RealNumber || Current.Kind == TokenKind.Identifier)
            {
                Token value = Current;
                delay.Values.Add(ParsePrimary());

                // A time unit written straight after the number: 10ns.
                if (Current.Kind == TokenKind.Identifier && TimeUnits.Contains(Current.Text) &&
                    Current.Location.Line == value.Location.Line && Current.Location.Column == value.Location.Column + value.Text.Length)
                {
                    _diagnostics.Warning(Current.Location, DiagnosticCodes.Unsupported,
                        $"The unit in #{value.Text}{Current.Text} is ignored: delays count in the simulator's time unit (ns).");
                    Next();
                }

                return delay;
            }

            Error(Current.Location, DiagnosticCodes.Expected, $"Expected a delay after '#' -- #5, #DELAY or #(2, 3) -- but found {Describe(Current)}.");
            delay.Values.Add(new ErrorExpression { Location = Current.Location });
            return delay;
        }
    }
}
