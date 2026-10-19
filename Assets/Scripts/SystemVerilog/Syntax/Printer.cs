using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BitSorter.SystemVerilog.Syntax
{
    /// <summary>
    /// Writes a syntax tree back out as SystemVerilog, in the course's style: four-space indents, one
    /// port per line with its comment, <c>begin</c> on the line of what it belongs to.
    /// </summary>
    /// <remarks>
    /// **Printing is a fixed point** (Docs/architecture-5.md, invariant 4): print a tree, read what was
    /// printed, print that, and the two texts are the same. Parentheses the author wrote are kept;
    /// where a tree built in code needs them for its meaning, the printer adds them by precedence.
    /// What a read could not understand is printed as it was written, so nothing is lost.
    /// </remarks>
    public sealed class Printer
    {
        private const string Indent = "    ";

        /// <summary>A module instance's connections go one per line beyond this many.</summary>
        public const int InlineConnections = 3;

        private readonly StringBuilder _out = new StringBuilder();
        private int _level;

        public static string Print(SyntaxTree tree)
        {
            var printer = new Printer();

            if (tree.Timescale != null)
                printer.Line("`timescale " + tree.Timescale);

            foreach (ModuleItem item in tree.UnitItems)
                printer.Item(item);

            for (int i = 0; i < tree.Modules.Count; i++)
            {
                if (i > 0 || tree.UnitItems.Count > 0 || tree.Timescale != null)
                    printer._out.Append('\n');
                printer.Module(tree.Modules[i]);
            }

            return printer._out.ToString();
        }

        public static string Print(ModuleDeclaration module)
        {
            var printer = new Printer();
            printer.Module(module);
            return printer._out.ToString();
        }

        public static string Print(Expression expression) => ExpressionText(expression);

        public static string Print(Statement statement)
        {
            var printer = new Printer();
            printer.Statement(statement);
            return printer._out.ToString();
        }

        // -----------------------------------------------------------------

        private void Line(string text, string comment = null)
        {
            for (int i = 0; i < _level; i++)
                _out.Append(Indent);
            _out.Append(text);
            if (!string.IsNullOrEmpty(comment))
                _out.Append(" // ").Append(comment);
            _out.Append('\n');
        }

        private void Module(ModuleDeclaration module)
        {
            string name = "module " + module.Name;
            bool parameters = module.HasParameterList && module.HeaderParameters.Count > 0;

            if (module.HasParameterList && !parameters)
                name += " #()";

            if (parameters)
            {
                Line(name + " #(", module.Comment);
                _level++;
                for (int i = 0; i < module.HeaderParameters.Count; i++)
                {
                    ParameterDeclaration p = module.HeaderParameters[i];
                    Line((p.IsLocal ? "localparam " : "parameter ") + TypePrefix(p.Type) + p.Name +
                         (p.Value != null ? " = " + ExpressionText(p.Value) : "") +
                         (i < module.HeaderParameters.Count - 1 ? "," : ""), p.Comment);
                }
                _level--;
                PortList(module, ")", null);
            }
            else
            {
                PortList(module, name, module.Comment);
            }

            _level++;
            foreach (ModuleItem item in module.Items)
                Item(item);
            _level--;

            Line("endmodule");
        }

        /// <summary>The port list after <paramref name="head"/>, closing the header with its ';'.</summary>
        private void PortList(ModuleDeclaration module, string head, string comment)
        {
            if (!module.HasPortList)
            {
                Line(head + ";", comment);
                return;
            }

            if (module.Ports.Count == 0)
            {
                Line(head + " (" + string.Join(", ", module.PortNames) + ");", comment);
                return;
            }

            Line(head + " (", comment);
            _level++;
            for (int i = 0; i < module.Ports.Count; i++)
            {
                PortDeclaration port = module.Ports[i];
                Line(PortText(port) + (i < module.Ports.Count - 1 ? "," : ""), port.Comment);
            }
            _level--;
            Line(");");
        }

        private static string PortText(PortDeclaration port)
        {
            var text = new StringBuilder(DirectionText(port.Direction)).Append(' ');
            if (port.NetType != null)
                text.Append(port.NetType).Append(' ');
            text.Append(TypePrefix(port.Type)).Append(port.Name);
            foreach (Dimension d in port.Unpacked)
                text.Append(' ').Append(DimensionText(d));
            return text.ToString();
        }

        private static string DirectionText(PortDirection direction) =>
            direction == PortDirection.Input ? "input" : direction == PortDirection.Output ? "output" : "inout";

        /// <summary>A type as written before a name, with its trailing space; empty for an implicit type with no ranges.</summary>
        private static string TypePrefix(DataType type)
        {
            if (type == null)
                return string.Empty;

            var text = new StringBuilder();

            if (type.Enum != null)
                text.Append(EnumText(type.Enum));
            else if (type.Struct != null)
                text.Append(StructText(type.Struct));
            else if (type.Keyword != null)
                text.Append(type.Keyword);
            else if (type.Name != null)
                text.Append(type.Name);

            if (type.Signed)
                text.Append(text.Length > 0 ? " signed" : "signed");
            else if (type.Unsigned)
                text.Append(text.Length > 0 ? " unsigned" : "unsigned");

            foreach (Dimension d in type.Packed)
                text.Append(text.Length > 0 ? " " : "").Append(DimensionText(d));

            return text.Length > 0 ? text.Append(' ').ToString() : string.Empty;
        }

        private static string EnumText(EnumType e)
        {
            var text = new StringBuilder("enum ");
            if (e.BaseType != null)
                text.Append(TypePrefix(e.BaseType));
            text.Append("{");
            for (int i = 0; i < e.Members.Count; i++)
            {
                text.Append(i == 0 ? "" : ", ").Append(e.Members[i].Name);
                if (e.Members[i].Value != null)
                    text.Append(" = ").Append(ExpressionText(e.Members[i].Value));
            }
            return text.Append("}").ToString();
        }

        /// <summary>A struct or union in place, its members on one line: <c>struct packed {logic [3:0] op; logic [7:0] imm;}</c>.</summary>
        private static string StructText(StructType s)
        {
            var text = new StringBuilder(s.IsUnion ? "union" : "struct");
            if (s.Packed)
                text.Append(" packed");
            if (s.Signed)
                text.Append(" signed");
            text.Append(" {");
            foreach (StructMember member in s.Members)
                text.Append(TypePrefix(member.Type)).Append(member.Name).Append("; ");
            if (s.Members.Count > 0)
                text.Length -= 1;
            return text.Append("}").ToString();
        }

        private static string DimensionText(Dimension d) =>
            "[" + ExpressionText(d.Left, compact: true) + (d.Right != null ? ":" + ExpressionText(d.Right, compact: true) : "") + "]";

        // -----------------------------------------------------------------
        // Items
        // -----------------------------------------------------------------

        private void Item(ModuleItem item)
        {
            switch (item)
            {
                case ParameterDeclaration p:
                    Line((p.IsLocal ? "localparam " : "parameter ") + TypePrefix(p.Type) + p.Name +
                         (p.Value != null ? " = " + ExpressionText(p.Value) : "") + ";", p.Comment);
                    break;

                case DataDeclaration d:
                {
                    var text = new StringBuilder();
                    if (d.IsConst) text.Append("const ");
                    if (d.NetType != null) text.Append(d.NetType).Append(' ');
                    if (d.Delay != null) text.Append(DelayText(d.Delay)).Append(' ');
                    text.Append(TypePrefix(d.Type));
                    text.Append(DeclaratorsText(d.Declarators)).Append(';');
                    Line(text.ToString(), d.Comment);
                    break;
                }

                case PortItemDeclaration p:
                {
                    var text = new StringBuilder(DirectionText(p.Direction)).Append(' ');
                    if (p.NetType != null) text.Append(p.NetType).Append(' ');
                    text.Append(TypePrefix(p.Type)).Append(DeclaratorsText(p.Declarators)).Append(';');
                    Line(text.ToString(), p.Comment);
                    break;
                }

                case TypedefDeclaration t:
                    Line("typedef " + TypePrefix(t.Type) + t.Name + ";", t.Comment);
                    break;

                case GenvarDeclaration g:
                    Line("genvar " + string.Join(", ", g.Names) + ";", g.Comment);
                    break;

                case ContinuousAssign a:
                {
                    var text = new StringBuilder("assign ");
                    if (a.Delay != null) text.Append(DelayText(a.Delay)).Append(' ');
                    for (int i = 0; i < a.Assignments.Count; i++)
                    {
                        if (i > 0) text.Append(", ");
                        text.Append(ExpressionText(a.Assignments[i].Target)).Append(" = ").Append(ExpressionText(a.Assignments[i].Value));
                    }
                    Line(text.Append(';').ToString(), a.Comment);
                    break;
                }

                case PrimitiveInstantiation p:
                {
                    var text = new StringBuilder(p.Primitive);
                    if (p.Delay != null) text.Append(' ').Append(DelayText(p.Delay));
                    for (int i = 0; i < p.Instances.Count; i++)
                    {
                        PrimitiveInstance instance = p.Instances[i];
                        text.Append(i == 0 ? "" : ",");
                        if (instance.Name != null)
                        {
                            text.Append(' ').Append(instance.Name);
                            if (instance.Array != null) text.Append(' ').Append(DimensionText(instance.Array));
                            text.Append(' ');
                        }
                        else
                        {
                            text.Append(p.Delay != null || i > 0 ? " " : "");
                        }

                        text.Append('(').Append(ExpressionsText(instance.Terminals)).Append(')');
                    }
                    Line(text.Append(';').ToString(), p.Comment);
                    break;
                }

                case ModuleInstantiation m:
                    Instantiation(m);
                    break;

                case GenerateRegion r:
                    Line("generate");
                    _level++;
                    foreach (ModuleItem inner in r.Items) Item(inner);
                    _level--;
                    Line("endgenerate");
                    break;

                case GenerateBlock b:
                    Block(b, string.Empty);
                    break;

                case LoopGenerate l:
                {
                    string step = l.StepOperator == "++" || l.StepOperator == "--"
                        ? l.Variable + l.StepOperator
                        : l.Variable + " " + (l.StepOperator ?? "=") + " " + ExpressionText(l.StepValue);
                    string head = "for (" + (l.DeclaresGenvar ? "genvar " : "") + l.Variable + " = " + ExpressionText(l.Start) + "; " +
                                  ExpressionText(l.Condition) + "; " + step + ")";
                    Block(l.Body, head);
                    break;
                }

                case IfGenerate i:
                    IfGenerateText(i, "if (" + ExpressionText(i.Condition) + ")");
                    break;

                case ProceduralBlock p:
                    Process(p);
                    break;

                case SubroutineDeclaration s:
                    Subroutine(s);
                    break;

                case UnsupportedItem u:
                    Line(u.Text);
                    break;
            }
        }

        private static string DeclaratorsText(List<Declarator> declarators)
        {
            var text = new StringBuilder();
            for (int i = 0; i < declarators.Count; i++)
            {
                Declarator d = declarators[i];
                text.Append(i == 0 ? "" : ", ").Append(d.Name);
                foreach (Dimension dimension in d.Unpacked)
                    text.Append(' ').Append(DimensionText(dimension));
                if (d.Initializer != null)
                    text.Append(" = ").Append(ExpressionText(d.Initializer));
            }
            return text.ToString();
        }

        private void Instantiation(ModuleInstantiation m)
        {
            var head = new StringBuilder(m.Module);

            if (m.HasParameterList)
            {
                head.Append(" #(");
                for (int i = 0; i < m.Parameters.Count; i++)
                {
                    ParameterValue p = m.Parameters[i];
                    head.Append(i == 0 ? "" : ", ");
                    head.Append(p.Name != null ? "." + p.Name + "(" + (p.Value != null ? ExpressionText(p.Value) : "") + ")" : ExpressionText(p.Value));
                }
                head.Append(')');
            }

            for (int i = 0; i < m.Instances.Count; i++)
            {
                ModuleInstance instance = m.Instances[i];
                string prefix = i == 0 ? head + " " : string.Empty;
                string name = instance.Name + (instance.Array != null ? " " + DimensionText(instance.Array) : "");
                bool last = i == m.Instances.Count - 1;
                string end = last ? ");" : "),";

                if (instance.Connections.Count <= InlineConnections)
                {
                    var connections = new List<string>();
                    foreach (PortConnection c in instance.Connections) connections.Add(ConnectionText(c));
                    Line(prefix + name + " (" + string.Join(", ", connections) + end, last ? m.Comment : null);
                }
                else
                {
                    Line(prefix + name + " (");
                    _level++;
                    for (int c = 0; c < instance.Connections.Count; c++)
                        Line(ConnectionText(instance.Connections[c]) + (c < instance.Connections.Count - 1 ? "," : ""));
                    _level--;
                    Line(end, last ? m.Comment : null);
                }
            }
        }

        private static string ConnectionText(PortConnection c)
        {
            if (c.IsWildcard) return ".*";
            if (c.Name == null) return c.Value != null ? ExpressionText(c.Value) : "";
            if (c.IsShorthand) return "." + c.Name;
            return "." + c.Name + "(" + (c.Value != null ? ExpressionText(c.Value) : "") + ")";
        }

        /// <summary>A generate block after <paramref name="head"/>: <c>head begin : label</c> ... <c>end</c>, or a bare item.</summary>
        private void Block(GenerateBlock block, string head)
        {
            if (block == null)
            {
                Line(head + ";");
                return;
            }

            if (!block.HasBeginEnd && block.Items.Count == 1 && block.Label == null)
            {
                if (head.Length > 0)
                {
                    Line(head);
                    _level++;
                    Item(block.Items[0]);
                    _level--;
                }
                else
                {
                    Item(block.Items[0]);
                }

                return;
            }

            Line((head.Length > 0 ? head + " " : "") + "begin" + (block.Label != null ? " : " + block.Label : ""));
            _level++;
            foreach (ModuleItem item in block.Items) Item(item);
            _level--;
            Line("end");
        }

        private void IfGenerateText(IfGenerate item, string head)
        {
            Block(item.Then, head);

            if (item.Else == null)
                return;

            if (!item.Else.HasBeginEnd && item.Else.Items.Count == 1 && item.Else.Items[0] is IfGenerate nested)
            {
                IfGenerateText(nested, "else if (" + ExpressionText(nested.Condition) + ")");
                return;
            }

            Block(item.Else, "else");
        }

        private void Process(ProceduralBlock p)
        {
            string keyword;
            switch (p.Kind)
            {
                case ProcessKind.Initial: keyword = "initial"; break;
                case ProcessKind.Final: keyword = "final"; break;
                case ProcessKind.AlwaysComb: keyword = "always_comb"; break;
                case ProcessKind.AlwaysFf: keyword = "always_ff"; break;
                case ProcessKind.AlwaysLatch: keyword = "always_latch"; break;
                default: keyword = "always"; break;
            }

            StatementAfter(keyword, p.Body);
        }

        private void Subroutine(SubroutineDeclaration s)
        {
            var head = new StringBuilder(s.IsTask ? "task " : "function ");
            if (s.IsAutomatic) head.Append("automatic ");
            if (s.IsVoid) head.Append("void ");
            else if (s.ReturnType != null) head.Append(TypePrefix(s.ReturnType));
            head.Append(s.Name);

            if (s.HasPortList)
            {
                head.Append('(');
                for (int i = 0; i < s.Ports.Count; i++)
                {
                    SubroutinePort p = s.Ports[i];
                    head.Append(i == 0 ? "" : ", ").Append(DirectionText(p.Direction)).Append(' ').Append(TypePrefix(p.Type)).Append(p.Name);
                    if (p.Default != null) head.Append(" = ").Append(ExpressionText(p.Default));
                }
                head.Append(')');
            }

            Line(head.Append(';').ToString(), s.Comment);
            _level++;
            foreach (ModuleItem d in s.Declarations) Item(d);
            foreach (Statement statement in s.Statements) Statement(statement);
            _level--;
            Line(s.IsTask ? "endtask" : "endfunction");
        }

        // -----------------------------------------------------------------
        // Statements
        // -----------------------------------------------------------------

        private void Statement(Statement statement) => StatementAfter(string.Empty, statement);

        /// <summary>
        /// A statement after <paramref name="head"/> on the same line -- <c>always_ff @(posedge clk) begin</c>,
        /// <c>if (a) begin</c> -- or on its own line indented under the head when it is not a block.
        /// </summary>
        private void StatementAfter(string head, Statement statement)
        {
            string lead = head.Length > 0 ? head + " " : string.Empty;

            switch (statement)
            {
                case null:
                    Line(lead.TrimEnd() + ";");
                    return;

                case BlockStatement b:
                    Line(lead + "begin" + (b.Label != null ? " : " + b.Label : ""), b.Comment);
                    _level++;
                    foreach (ModuleItem d in b.Declarations) Item(d);
                    foreach (Statement s in b.Statements) Statement(s);
                    _level--;
                    Line("end" + (b.Label != null ? " : " + b.Label : ""));
                    return;

                case TimedStatement t:
                {
                    string control = TimingText(t.Control);
                    if (t.Body is NullStatement)
                    {
                        Line(lead + control + ";", t.Body.Comment ?? t.Comment);
                        return;
                    }

                    StatementAfter(lead + control, t.Body);
                    return;
                }

                case IfStatement i when lead.Length > 0:
                    // On a line of its own under the head, so its else lines up with it:
                    // always_ff @(posedge clk, posedge rst)
                    //     if (rst) ...
                    //     else ...
                    Line(head);
                    _level++;
                    IfText(i, string.Empty);
                    _level--;
                    return;

                case IfStatement i:
                    IfText(i, lead);
                    return;

                case CaseStatement c:
                {
                    Line(lead + (c.Qualifier != null ? c.Qualifier + " " : "") + c.Keyword + " (" + ExpressionText(c.Subject) + ")" + (c.Inside ? " inside" : ""), c.Comment);
                    _level++;
                    foreach (CaseItem item in c.Items)
                    {
                        string label = item.IsDefault ? "default:" : ExpressionsText(item.Labels) + ":";
                        StatementAfter(label, item.Body);
                    }
                    _level--;
                    Line("endcase");
                    return;
                }

                case ForStatement f:
                {
                    var inits = new List<string>();
                    foreach (ForInitializer i in f.Initializers)
                        inits.Add((i.Type != null ? TypePrefix(i.Type) : "") + ExpressionText(i.Target) + " = " + ExpressionText(i.Value));
                    var steps = new List<string>();
                    foreach (Statement s in f.Steps) steps.Add(InlineText(s));
                    StatementAfter(lead + "for (" + string.Join(", ", inits) + "; " + (f.Condition != null ? ExpressionText(f.Condition) : "") +
                                   "; " + string.Join(", ", steps) + ")", f.Body);
                    return;
                }

                case RepeatStatement r:
                    StatementAfter(lead + "repeat (" + ExpressionText(r.Count) + ")", r.Body);
                    return;

                case ForeachStatement f:
                    StatementAfter(lead + "foreach (" + ExpressionText(f.Array) + "[" + string.Join(", ", f.Variables.ConvertAll(v => v ?? "")) + "])", f.Body);
                    return;

                case WhileStatement w when !w.IsDoWhile:
                    StatementAfter(lead + "while (" + ExpressionText(w.Condition) + ")", w.Body);
                    return;

                case WhileStatement w:
                    StatementAfter(lead + "do", w.Body);
                    Line("while (" + ExpressionText(w.Condition) + ");");
                    return;

                case ForeverStatement f:
                    StatementAfter(lead + "forever", f.Body);
                    return;

                case WaitStatement w:
                    if (w.Body is NullStatement)
                        Line(lead + "wait (" + ExpressionText(w.Condition) + ");");
                    else
                        StatementAfter(lead + "wait (" + ExpressionText(w.Condition) + ")", w.Body);
                    return;

                case NullStatement n:
                    Line(lead.TrimEnd() + ";", n.Comment);
                    return;

                case UnsupportedStatement u:
                    Line(lead + u.Text);
                    return;
            }

            // A simple statement: on the head's line if it is short, else indented under it.
            string text = InlineText(statement) + ";";
            if (lead.Length == 0)
            {
                Line(text, statement.Comment);
            }
            else if (head.EndsWith(":", System.StringComparison.Ordinal) || head.StartsWith("#", System.StringComparison.Ordinal) ||
                     head.StartsWith("@", System.StringComparison.Ordinal))
            {
                Line(lead + text, statement.Comment);
            }
            else
            {
                Line(head);
                _level++;
                Line(text, statement.Comment);
                _level--;
            }
        }

        private void IfText(IfStatement i, string lead)
        {
            string head = lead + (i.Qualifier != null ? i.Qualifier + " " : "") + "if (" + ExpressionText(i.Condition) + ")";

            if (i.Else == null)
            {
                StatementAfter(head, i.Then);
                return;
            }

            StatementAfter(head, i.Then);

            if (i.Else is IfStatement chained)
            {
                IfText(chained, "else ");
                return;
            }

            StatementAfter("else", i.Else);
        }

        /// <summary>A simple statement's text, with no <c>;</c>: for a loop's step, or before one.</summary>
        private static string InlineText(Statement statement)
        {
            switch (statement)
            {
                case AssignmentStatement a:
                    return ExpressionText(a.Target) + " " + a.Operator + " " +
                           (a.IntraAssignment != null ? TimingText(a.IntraAssignment) + " " : "") + ExpressionText(a.Value);

                case IncrementStatement i:
                    return i.Prefix ? i.Operator + ExpressionText(i.Target) : ExpressionText(i.Target) + i.Operator;

                case CallStatement c:
                    return c.Name + (c.HasParentheses ? "(" + ArgumentsText(c.Arguments) + ")" : "");

                case ReturnStatement r:
                    return r.Value != null ? "return " + ExpressionText(r.Value) : "return";

                case JumpStatement j:
                    return j.Target != null ? j.Keyword + " " + j.Target : j.Keyword;

                default:
                    return string.Empty;
            }
        }

        private static string TimingText(TimingControl control)
        {
            switch (control)
            {
                case DelayControl d:
                    return DelayText(d);

                case EventControl e:
                {
                    if (e.Implicit) return "@(*)";
                    var events = new List<string>();
                    foreach (EventExpression ev in e.Events)
                        events.Add((ev.Edge != null ? ev.Edge + " " : "") + ExpressionText(ev.Value));
                    if (e.Bare && events.Count == 1) return "@" + events[0];
                    return "@(" + string.Join(e.UsesOr ? " or " : ", ", events) + ")";
                }

                default:
                    return string.Empty;
            }
        }

        private static string DelayText(DelayControl d)
        {
            if (!d.Parenthesized && d.Values.Count == 1)
            {
                Expression only = d.Values[0];
                bool simple = only is NumberLiteral || only is RealLiteral || only is NameExpression;
                return simple ? "#" + ExpressionText(only) : "#(" + ExpressionText(only) + ")";
            }

            return "#(" + ExpressionsText(d.Values) + ")";
        }

        // -----------------------------------------------------------------
        // Expressions
        // -----------------------------------------------------------------

        private static string ExpressionsText(List<Expression> expressions)
        {
            var parts = new List<string>(expressions.Count);
            foreach (Expression e in expressions) parts.Add(ExpressionText(e));
            return string.Join(", ", parts);
        }

        private static string ArgumentsText(List<Expression> arguments)
        {
            var parts = new List<string>(arguments.Count);
            foreach (Expression e in arguments) parts.Add(e == null ? "" : ExpressionText(e));
            return string.Join(", ", parts);
        }

        /// <summary>
        /// An expression's text. <paramref name="compact"/> drops the spaces around arithmetic, as a
        /// range is written: <c>[N-1:0]</c>.
        /// </summary>
        public static string ExpressionText(Expression expression, bool compact = false)
        {
            var text = new StringBuilder();
            Write(text, expression, compact);
            return text.ToString();
        }

        private const int Primary = 100;
        private const int UnaryPrecedence = 12;
        private const int ConditionalPrecedence = 0;

        private static int Precedence(Expression e)
        {
            switch (e)
            {
                case BinaryExpression b: return BinaryPrecedence(b.Operator);
                case InsideExpression _: return 7;   // as the relational operators bind
                case UnaryExpression _: return UnaryPrecedence;
                case ConditionalExpression _: return ConditionalPrecedence;
                default: return Primary;
            }
        }

        private static int BinaryPrecedence(string op)
        {
            switch (op)
            {
                case "**": return 11;
                case "*": case "/": case "%": return 10;
                case "+": case "-": return 9;
                case "<<": case ">>": case "<<<": case ">>>": return 8;
                case "<": case "<=": case ">": case ">=": return 7;
                case "==": case "!=": case "===": case "!==": case "==?": case "!=?": return 6;
                case "&": case "~&": return 5;
                case "^": case "~^": case "^~": return 4;
                case "|": case "~|": return 3;
                case "&&": return 2;
                case "||": return 1;
                default: return 1;
            }
        }

        private static void Write(StringBuilder text, Expression expression, bool compact)
        {
            switch (expression)
            {
                case null:
                case ErrorExpression _:
                    text.Append("/*?*/");
                    return;

                case NumberLiteral n:
                    text.Append(n.Text);
                    return;

                case RealLiteral r:
                    text.Append(r.Text);
                    return;

                case StringLiteral s:
                    text.Append('"').Append(Escape(s.Value)).Append('"');
                    return;

                case NameExpression n:
                    for (int i = 0; i < n.Path.Count; i++)
                        text.Append(i == 0 ? "" : ".").Append(NameText(n.Path[i]));
                    return;

                case IndexExpression i:
                    WriteOperand(text, i.Target, Primary, compact);
                    text.Append('[');
                    Write(text, i.Index, true);
                    text.Append(']');
                    return;

                case RangeSelectExpression r:
                    WriteOperand(text, r.Target, Primary, compact);
                    text.Append('[');
                    Write(text, r.Left, true);
                    text.Append(r.Kind == RangeKind.Constant ? ":" : r.Kind == RangeKind.IndexedUp ? " +: " : " -: ");
                    Write(text, r.Right, true);
                    text.Append(']');
                    return;

                case MemberExpression m:
                    WriteOperand(text, m.Target, Primary, compact);
                    text.Append('.').Append(NameText(m.Member));
                    return;

                case ConcatenationExpression c:
                    text.Append('{');
                    for (int i = 0; i < c.Items.Count; i++)
                    {
                        if (i > 0) text.Append(", ");
                        Write(text, c.Items[i], compact);
                    }
                    text.Append('}');
                    return;

                case ReplicationExpression r:
                    text.Append('{');
                    Write(text, r.Count, compact);
                    Write(text, r.Value, compact);
                    text.Append('}');
                    return;

                case UnaryExpression u:
                    text.Append(u.Operator);
                    // Never let two operators run together into another one: - -a, & &a.
                    if (u.Operand is UnaryExpression)
                    {
                        text.Append('(');
                        Write(text, u.Operand, compact);
                        text.Append(')');
                    }
                    else
                    {
                        WriteOperand(text, u.Operand, UnaryPrecedence, compact);
                    }
                    return;

                case BinaryExpression b:
                {
                    int precedence = BinaryPrecedence(b.Operator);
                    WriteOperand(text, b.Left, precedence, compact);
                    bool tight = compact && (b.Operator == "+" || b.Operator == "-" || b.Operator == "*" || b.Operator == "/") &&
                                 !(b.Right is UnaryExpression);
                    text.Append(tight ? b.Operator : " " + b.Operator + " ");
                    // Left associative: a right operand of the same precedence needs parentheses.
                    WriteOperand(text, b.Right, precedence + 1, compact);
                    return;
                }

                case ConditionalExpression c:
                    WriteOperand(text, c.Condition, ConditionalPrecedence + 1, compact);
                    text.Append(" ? ");
                    Write(text, c.WhenTrue, compact);
                    text.Append(" : ");
                    Write(text, c.WhenFalse, compact);
                    return;

                case CallExpression c:
                    text.Append(c.Name);
                    if (c.HasParentheses)
                        text.Append('(').Append(ArgumentsText(c.Arguments)).Append(')');
                    return;

                case MethodCallExpression m:
                    WriteOperand(text, m.Target, Primary, compact);
                    text.Append('.').Append(m.Method);
                    if (m.HasParentheses)
                        text.Append('(').Append(ArgumentsText(m.Arguments)).Append(')');
                    return;

                case InsideExpression inside:
                    WriteOperand(text, inside.Value, 8, compact);
                    text.Append(" inside {");
                    for (int i = 0; i < inside.Set.Count; i++)
                    {
                        if (i > 0) text.Append(", ");
                        Write(text, inside.Set[i], compact);
                    }
                    text.Append('}');
                    return;

                case ValueRange range:
                    text.Append('[');
                    Write(text, range.Low, compact);
                    text.Append(':');
                    Write(text, range.High, compact);
                    text.Append(']');
                    return;

                case CastExpression c:
                    text.Append(c.TargetText).Append("'(");
                    Write(text, c.Value, compact);
                    text.Append(')');
                    return;

                case AssignmentPatternExpression p:
                {
                    text.Append("'{");
                    var parts = new List<string>();
                    foreach (Expression item in p.Items) parts.Add(ExpressionText(item, compact));
                    if (p.Default != null) parts.Add("default: " + ExpressionText(p.Default, compact));
                    text.Append(string.Join(", ", parts)).Append('}');
                    return;
                }

                case ParenthesizedExpression p:
                    text.Append('(');
                    Write(text, p.Inner, compact);
                    text.Append(')');
                    return;
            }
        }

        private static void WriteOperand(StringBuilder text, Expression operand, int needed, bool compact)
        {
            if (Precedence(operand) < needed)
            {
                text.Append('(');
                Write(text, operand, compact);
                text.Append(')');
            }
            else
            {
                Write(text, operand, compact);
            }
        }

        private static string NameText(string name)
        {
            if (name.Length == 0)
                return name;

            bool plain = Lexer.IsIdentifierStart(name[0]) && !Keywords.IsKeyword(name);
            for (int i = 1; i < name.Length && plain; i++)
                plain = Lexer.IsIdentifierPart(name[i]);

            return plain ? name : "\\" + name + " ";
        }

        private static string Escape(string value)
        {
            var text = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\n': text.Append("\\n"); break;
                    case '\t': text.Append("\\t"); break;
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    default: text.Append(c); break;
                }
            }
            return text.ToString();
        }
    }
}
