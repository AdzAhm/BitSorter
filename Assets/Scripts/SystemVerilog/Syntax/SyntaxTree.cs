using System.Collections.Generic;

namespace BitSorter.SystemVerilog.Syntax
{
    // =====================================================================================
    // The syntax tree: what the source says, before anything is worked out about what it means.
    // Every node knows where it came from. Names are kept as written; widths, parameter values
    // and which module an instance means are the elaborator's business (Part 4b on).
    // =====================================================================================

    /// <summary>Anything in the tree: it knows its file, line and column.</summary>
    public abstract class SyntaxNode
    {
        public SourceLocation Location { get; set; }
    }

    /// <summary>A whole read: the modules, anything declared outside them, and what was said along the way.</summary>
    public sealed class SyntaxTree
    {
        public List<ModuleDeclaration> Modules { get; } = new List<ModuleDeclaration>();

        /// <summary>Typedefs, parameters and functions written outside every module, which every module can see.</summary>
        public List<ModuleItem> UnitItems { get; } = new List<ModuleItem>();

        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

        /// <summary>What a <c>`timescale</c> said, or null.</summary>
        public string Timescale { get; set; }

        /// <summary>Every file read, the first one first.</summary>
        public List<string> Files { get; } = new List<string>();

        public bool HasErrors => Diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error);
    }

    // -----------------------------------------------------------------
    // Modules and ports
    // -----------------------------------------------------------------

    public sealed class ModuleDeclaration : SyntaxNode
    {
        public string Name { get; set; }

        /// <summary>The <c>#( ... )</c> list after the name; empty when there is none.</summary>
        public List<ParameterDeclaration> HeaderParameters { get; } = new List<ParameterDeclaration>();

        /// <summary>Whether the header had a <c>#( )</c> list at all, even an empty one.</summary>
        public bool HasParameterList { get; set; }

        /// <summary>ANSI ports, declared in the header with their direction and type.</summary>
        public List<PortDeclaration> Ports { get; } = new List<PortDeclaration>();

        /// <summary>
        /// A non-ANSI header -- <c>module m(a, b);</c> with the directions declared inside -- lists
        /// names only, here.
        /// </summary>
        public List<string> PortNames { get; } = new List<string>();

        /// <summary>Whether the header had a port list in parentheses, even an empty one.</summary>
        public bool HasPortList { get; set; }

        public List<ModuleItem> Items { get; } = new List<ModuleItem>();

        /// <summary>The comment on the line of <c>module</c>, if any.</summary>
        public string Comment { get; set; }
    }

    public enum PortDirection { Input, Output, Inout }

    /// <summary>One port in an ANSI header: <c>input logic [7:0] a, // the first operand</c>.</summary>
    public sealed class PortDeclaration : SyntaxNode
    {
        public PortDirection Direction { get; set; }

        /// <summary><c>wire</c>, <c>tri</c> and the like when written; null when the port is simply a <c>logic</c> or has no type.</summary>
        public string NetType { get; set; }

        public DataType Type { get; set; }
        public string Name { get; set; }
        public List<Dimension> Unpacked { get; } = new List<Dimension>();
        public string Comment { get; set; }
    }

    // -----------------------------------------------------------------
    // Types
    // -----------------------------------------------------------------

    /// <summary>
    /// A data type as written: a keyword (<c>logic</c>, <c>wire</c>, <c>int</c>), a typedef's name,
    /// or an enum, with its signedness and packed ranges. Implicit is a port or parameter written
    /// with no type at all (<c>input [3:0] a</c>, <c>parameter N = 4</c>).
    /// </summary>
    public sealed class DataType : SyntaxNode
    {
        /// <summary>The keyword, or null for a typedef's name or an implicit type.</summary>
        public string Keyword { get; set; }

        /// <summary>A typedef's name, when the type is one.</summary>
        public string Name { get; set; }

        public bool Signed { get; set; }

        /// <summary>Whether <c>unsigned</c> was written out.</summary>
        public bool Unsigned { get; set; }

        public List<Dimension> Packed { get; } = new List<Dimension>();

        /// <summary>The enum, when the type is one.</summary>
        public EnumType Enum { get; set; }

        public bool IsImplicit => Keyword == null && Name == null && Enum == null;
    }

    public sealed class EnumType : SyntaxNode
    {
        /// <summary>The type the names stand for values of: <c>logic [1:0]</c>; null for the default, <c>int</c>.</summary>
        public DataType BaseType { get; set; }

        public List<EnumMember> Members { get; } = new List<EnumMember>();
    }

    public sealed class EnumMember : SyntaxNode
    {
        public string Name { get; set; }

        /// <summary>The value written for it, or null for the next one along.</summary>
        public Expression Value { get; set; }
    }

    /// <summary>
    /// A dimension: <c>[7:0]</c>, or the C-style size of an unpacked array, <c>[16]</c>, which has no
    /// right side. Not called a range because <c>System.Range</c> already is one.
    /// </summary>
    public sealed class Dimension : SyntaxNode
    {
        public Expression Left { get; set; }
        public Expression Right { get; set; }
    }

    // -----------------------------------------------------------------
    // Module items
    // -----------------------------------------------------------------

    public abstract class ModuleItem : SyntaxNode
    {
        /// <summary>A comment on the item's last line.</summary>
        public string Comment { get; set; }
    }

    /// <summary><c>parameter N = 8;</c> or <c>localparam WIDTH = N + 1;</c>, one name each.</summary>
    public sealed class ParameterDeclaration : ModuleItem
    {
        public bool IsLocal { get; set; }

        /// <summary>The type written, or an implicit one.</summary>
        public DataType Type { get; set; }

        public string Name { get; set; }
        public Expression Value { get; set; }
    }

    /// <summary><c>logic [3:0] a, b;</c>, <c>wire w = x &amp; y;</c>, <c>state_t state;</c>.</summary>
    public sealed class DataDeclaration : ModuleItem
    {
        /// <summary><c>wire</c>, <c>tri</c> and the like when a net type is written.</summary>
        public string NetType { get; set; }

        public DataType Type { get; set; }
        public List<Declarator> Declarators { get; } = new List<Declarator>();
        public bool IsConst { get; set; }

        /// <summary>A net's delay: <c>wire #2 w = a &amp; b;</c>.</summary>
        public DelayControl Delay { get; set; }
    }

    public sealed class Declarator : SyntaxNode
    {
        public string Name { get; set; }
        public List<Dimension> Unpacked { get; } = new List<Dimension>();
        public Expression Initializer { get; set; }
    }

    /// <summary>A non-ANSI port's declaration in the body: <c>input [3:0] a, b;</c>.</summary>
    public sealed class PortItemDeclaration : ModuleItem
    {
        public PortDirection Direction { get; set; }
        public string NetType { get; set; }
        public DataType Type { get; set; }
        public List<Declarator> Declarators { get; } = new List<Declarator>();
    }

    public sealed class TypedefDeclaration : ModuleItem
    {
        public DataType Type { get; set; }
        public string Name { get; set; }
    }

    public sealed class GenvarDeclaration : ModuleItem
    {
        public List<string> Names { get; } = new List<string>();
    }

    /// <summary><c>assign #2 y = a &amp; b, z = ~y;</c>.</summary>
    public sealed class ContinuousAssign : ModuleItem
    {
        public DelayControl Delay { get; set; }
        public List<Assignment> Assignments { get; } = new List<Assignment>();
    }

    public sealed class Assignment : SyntaxNode
    {
        public Expression Target { get; set; }
        public Expression Value { get; set; }
    }

    /// <summary>A built-in gate: <c>and #(2, 3) g1 (y, a, b);</c> or <c>not (y, a);</c>.</summary>
    public sealed class PrimitiveInstantiation : ModuleItem
    {
        /// <summary><c>and</c>, <c>or</c>, <c>nand</c>, <c>nor</c>, <c>xor</c>, <c>xnor</c>, <c>not</c>, <c>buf</c>, and the tri-state ones.</summary>
        public string Primitive { get; set; }

        public DelayControl Delay { get; set; }
        public List<PrimitiveInstance> Instances { get; } = new List<PrimitiveInstance>();
    }

    public sealed class PrimitiveInstance : SyntaxNode
    {
        /// <summary>The instance's name, or null for an unnamed gate.</summary>
        public string Name { get; set; }

        public Dimension Array { get; set; }

        /// <summary>The output first, then the inputs.</summary>
        public List<Expression> Terminals { get; } = new List<Expression>();
    }

    /// <summary><c>full_adder #(.N(4)) fa0 (.a(a), .b(b), .s(s)), fa1 (...);</c>.</summary>
    public sealed class ModuleInstantiation : ModuleItem
    {
        public string Module { get; set; }
        public bool HasParameterList { get; set; }
        public List<ParameterValue> Parameters { get; } = new List<ParameterValue>();
        public List<ModuleInstance> Instances { get; } = new List<ModuleInstance>();
    }

    /// <summary>One parameter's value at an instance: <c>.N(4)</c>, or <c>4</c> by position.</summary>
    public sealed class ParameterValue : SyntaxNode
    {
        /// <summary>The parameter's name, or null when given by position.</summary>
        public string Name { get; set; }

        /// <summary>The value; null for <c>.N()</c>, which keeps the default.</summary>
        public Expression Value { get; set; }
    }

    public sealed class ModuleInstance : SyntaxNode
    {
        public string Name { get; set; }
        public Dimension Array { get; set; }
        public List<PortConnection> Connections { get; } = new List<PortConnection>();
    }

    /// <summary><c>.a(x)</c>, <c>.a()</c>, <c>.a</c>, <c>.*</c>, or <c>x</c> by position.</summary>
    public sealed class PortConnection : SyntaxNode
    {
        /// <summary>The port's name, or null when connected by position.</summary>
        public string Name { get; set; }

        /// <summary>What it connects to; null for <c>.a()</c> (left open) and for <c>.a</c> and <c>.*</c>.</summary>
        public Expression Value { get; set; }

        /// <summary><c>.a</c>: connect to the signal of the same name.</summary>
        public bool IsShorthand { get; set; }

        /// <summary><c>.*</c>: connect every port to the signal of its name.</summary>
        public bool IsWildcard { get; set; }
    }

    /// <summary><c>generate ... endgenerate</c>: only a grouping; its items are module items.</summary>
    public sealed class GenerateRegion : ModuleItem
    {
        public List<ModuleItem> Items { get; } = new List<ModuleItem>();
    }

    /// <summary>A <c>begin [: label] ... end</c> holding module items, inside a generate construct.</summary>
    public sealed class GenerateBlock : ModuleItem
    {
        public string Label { get; set; }
        public List<ModuleItem> Items { get; } = new List<ModuleItem>();

        /// <summary>
        /// Whether it was written with <c>begin</c> and <c>end</c>; a generate construct may also hold
        /// a single item bare.
        /// </summary>
        public bool HasBeginEnd { get; set; }
    }

    /// <summary><c>for (genvar i = 0; i &lt; N; i++) begin : stage ... end</c>.</summary>
    public sealed class LoopGenerate : ModuleItem
    {
        public bool DeclaresGenvar { get; set; }
        public string Variable { get; set; }
        public Expression Start { get; set; }
        public Expression Condition { get; set; }

        /// <summary>The step: <c>i++</c>, <c>i--</c>, <c>i += 2</c> or <c>i = i + 1</c>; its operator and value.</summary>
        public string StepOperator { get; set; }

        /// <inheritdoc cref="StepOperator"/>
        public Expression StepValue { get; set; }

        public GenerateBlock Body { get; set; }
    }

    /// <summary><c>if (N > 1) begin ... end else begin ... end</c> in a module.</summary>
    public sealed class IfGenerate : ModuleItem
    {
        public Expression Condition { get; set; }
        public GenerateBlock Then { get; set; }
        public GenerateBlock Else { get; set; }
    }

    public enum ProcessKind { Initial, Final, Always, AlwaysComb, AlwaysFf, AlwaysLatch }

    /// <summary><c>always_ff @(posedge clk) begin ... end</c>, <c>initial ...</c>: a process and its statement.</summary>
    public sealed class ProceduralBlock : ModuleItem
    {
        public ProcessKind Kind { get; set; }
        public Statement Body { get; set; }
    }

    /// <summary>A <c>function</c> or a <c>task</c>, with its ports, locals and statements.</summary>
    public sealed class SubroutineDeclaration : ModuleItem
    {
        public bool IsTask { get; set; }
        public bool IsAutomatic { get; set; }

        /// <summary>The return type; null for a task and for <c>void</c> (see <see cref="IsVoid"/>).</summary>
        public DataType ReturnType { get; set; }

        public bool IsVoid { get; set; }
        public string Name { get; set; }

        /// <summary>Whether the ports were declared in parentheses after the name.</summary>
        public bool HasPortList { get; set; }

        public List<SubroutinePort> Ports { get; } = new List<SubroutinePort>();

        /// <summary>Local declarations, and the old style of port declared in the body.</summary>
        public List<ModuleItem> Declarations { get; } = new List<ModuleItem>();

        public List<Statement> Statements { get; } = new List<Statement>();
    }

    public sealed class SubroutinePort : SyntaxNode
    {
        public PortDirection Direction { get; set; }
        public DataType Type { get; set; }
        public string Name { get; set; }
        public Expression Default { get; set; }
    }

    /// <summary>
    /// Something read but not understood: a construct BitSorter does not support. It was reported
    /// where it stands, and its text is kept so printing the module loses nothing.
    /// </summary>
    public sealed class UnsupportedItem : ModuleItem
    {
        public string What { get; set; }
        public string Text { get; set; }
    }

    // -----------------------------------------------------------------
    // Timing controls
    // -----------------------------------------------------------------

    public abstract class TimingControl : SyntaxNode { }

    /// <summary><c>#5</c>, <c>#DELAY</c>, <c>#(2, 3)</c>: a delay, with up to three values for a gate.</summary>
    public sealed class DelayControl : TimingControl
    {
        public List<Expression> Values { get; } = new List<Expression>();

        /// <summary>Whether the values were written in parentheses: <c>#(5)</c> as against <c>#5</c>.</summary>
        public bool Parenthesized { get; set; }
    }

    /// <summary><c>@(posedge clk, posedge rst)</c>, <c>@(a or b)</c>, <c>@*</c>, <c>@(*)</c>, <c>@clk</c>.</summary>
    public sealed class EventControl : TimingControl
    {
        /// <summary><c>@*</c> or <c>@(*)</c>: whatever the statement reads.</summary>
        public bool Implicit { get; set; }

        public List<EventExpression> Events { get; } = new List<EventExpression>();

        /// <summary>Whether the events were separated by <c>or</c> rather than commas.</summary>
        public bool UsesOr { get; set; }

        /// <summary><c>@clk</c>, written with no parentheses.</summary>
        public bool Bare { get; set; }
    }

    public sealed class EventExpression : SyntaxNode
    {
        /// <summary><c>posedge</c>, <c>negedge</c> or <c>edge</c>; null for any change.</summary>
        public string Edge { get; set; }

        public Expression Value { get; set; }
    }

    // -----------------------------------------------------------------
    // Statements
    // -----------------------------------------------------------------

    public abstract class Statement : SyntaxNode
    {
        /// <summary>A comment on the statement's line.</summary>
        public string Comment { get; set; }
    }

    /// <summary><c>begin [: label] declarations statements end</c>.</summary>
    public sealed class BlockStatement : Statement
    {
        public string Label { get; set; }
        public List<ModuleItem> Declarations { get; } = new List<ModuleItem>();
        public List<Statement> Statements { get; } = new List<Statement>();
    }

    /// <summary><c>a = b;</c>, <c>q &lt;= d;</c>, <c>x += 1;</c>, <c>a = #5 b;</c>.</summary>
    public sealed class AssignmentStatement : Statement
    {
        public Expression Target { get; set; }

        /// <summary><c>=</c>, <c>&lt;=</c>, or a compound one such as <c>+=</c>.</summary>
        public string Operator { get; set; }

        public Expression Value { get; set; }

        /// <summary>A delay or event between the operator and the value, if one was written.</summary>
        public TimingControl IntraAssignment { get; set; }

        public bool IsNonBlocking => Operator == "<=";
    }

    /// <summary><c>i++;</c> or <c>--count;</c>.</summary>
    public sealed class IncrementStatement : Statement
    {
        public Expression Target { get; set; }
        public string Operator { get; set; }
        public bool Prefix { get; set; }
    }

    public sealed class IfStatement : Statement
    {
        /// <summary><c>unique</c>, <c>unique0</c> or <c>priority</c>, or null.</summary>
        public string Qualifier { get; set; }

        public Expression Condition { get; set; }
        public Statement Then { get; set; }
        public Statement Else { get; set; }
    }

    public sealed class CaseStatement : Statement
    {
        public string Qualifier { get; set; }

        /// <summary><c>case</c>, <c>casez</c> or <c>casex</c>.</summary>
        public string Keyword { get; set; }

        public Expression Subject { get; set; }
        public List<CaseItem> Items { get; } = new List<CaseItem>();
    }

    public sealed class CaseItem : SyntaxNode
    {
        /// <summary>The values that choose it; empty for <c>default</c>.</summary>
        public List<Expression> Labels { get; } = new List<Expression>();

        public bool IsDefault => Labels.Count == 0;
        public Statement Body { get; set; }
    }

    /// <summary><c>for (int i = 0; i &lt; 8; i++) ...</c>.</summary>
    public sealed class ForStatement : Statement
    {
        public List<ForInitializer> Initializers { get; } = new List<ForInitializer>();
        public Expression Condition { get; set; }
        public List<Statement> Steps { get; } = new List<Statement>();
        public Statement Body { get; set; }
    }

    /// <summary><c>int i = 0</c> (declaring the variable) or <c>i = 0</c> (an existing one).</summary>
    public sealed class ForInitializer : SyntaxNode
    {
        public DataType Type { get; set; }
        public Expression Target { get; set; }
        public Expression Value { get; set; }
    }

    public sealed class RepeatStatement : Statement
    {
        public Expression Count { get; set; }
        public Statement Body { get; set; }
    }

    public sealed class WhileStatement : Statement
    {
        public Expression Condition { get; set; }
        public Statement Body { get; set; }

        /// <summary><c>do ... while (c);</c> rather than <c>while (c) ...</c>.</summary>
        public bool IsDoWhile { get; set; }
    }

    public sealed class ForeverStatement : Statement
    {
        public Statement Body { get; set; }
    }

    /// <summary>A statement after a delay or an event: <c>#10 clk = ~clk;</c>, <c>@(posedge clk);</c>.</summary>
    public sealed class TimedStatement : Statement
    {
        public TimingControl Control { get; set; }

        /// <summary>What follows the control; a <see cref="NullStatement"/> for <c>#5;</c>.</summary>
        public Statement Body { get; set; }
    }

    public sealed class WaitStatement : Statement
    {
        public Expression Condition { get; set; }
        public Statement Body { get; set; }
    }

    /// <summary><c>$display("%d", x);</c>, <c>$finish;</c>, or a task: <c>send(byte);</c>.</summary>
    public sealed class CallStatement : Statement
    {
        public string Name { get; set; }
        public bool HasParentheses { get; set; }
        public List<Expression> Arguments { get; } = new List<Expression>();
        public bool IsSystem => Name.StartsWith("$", System.StringComparison.Ordinal);
    }

    public sealed class ReturnStatement : Statement
    {
        public Expression Value { get; set; }
    }

    /// <summary><c>break;</c>, <c>continue;</c>, or <c>disable name;</c>.</summary>
    public sealed class JumpStatement : Statement
    {
        public string Keyword { get; set; }
        public string Target { get; set; }
    }

    /// <summary>A lone <c>;</c>, or nothing after a timing control.</summary>
    public sealed class NullStatement : Statement { }

    /// <summary>A statement BitSorter does not support, kept as its text.</summary>
    public sealed class UnsupportedStatement : Statement
    {
        public string What { get; set; }
        public string Text { get; set; }
    }

    // -----------------------------------------------------------------
    // Expressions
    // -----------------------------------------------------------------

    public abstract class Expression : SyntaxNode { }

    /// <summary>An integer literal, its text as written (spacing removed) and the value it reads as.</summary>
    public sealed class NumberLiteral : Expression
    {
        public string Text { get; set; }
        public LogicVec Value { get; set; }

        /// <summary><c>'0</c>, <c>'1</c>, <c>'x</c>, <c>'z</c>: fills whatever width its context gives it.</summary>
        public bool FillsContext { get; set; }
    }

    public sealed class RealLiteral : Expression
    {
        public string Text { get; set; }
    }

    public sealed class StringLiteral : Expression
    {
        public string Value { get; set; }
    }

    /// <summary>A name, possibly through the hierarchy: <c>a</c>, <c>dut.ctl.state</c>.</summary>
    public sealed class NameExpression : Expression
    {
        public List<string> Path { get; } = new List<string>();
        public string Name => Path[Path.Count - 1];
    }

    /// <summary><c>a[3]</c>.</summary>
    public sealed class IndexExpression : Expression
    {
        public Expression Target { get; set; }
        public Expression Index { get; set; }
    }

    public enum RangeKind { Constant, IndexedUp, IndexedDown }

    /// <summary><c>a[7:4]</c>, <c>a[i +: 4]</c>, <c>a[i -: 4]</c>.</summary>
    public sealed class RangeSelectExpression : Expression
    {
        public Expression Target { get; set; }
        public Expression Left { get; set; }
        public Expression Right { get; set; }
        public RangeKind Kind { get; set; }
    }

    /// <summary><c>gen[0].sum</c>: a name reached through a generate block or an instance array.</summary>
    public sealed class MemberExpression : Expression
    {
        public Expression Target { get; set; }
        public string Member { get; set; }
    }

    /// <summary><c>{a, b, c}</c>.</summary>
    public sealed class ConcatenationExpression : Expression
    {
        public List<Expression> Items { get; } = new List<Expression>();
    }

    /// <summary><c>{4{a}}</c>: a count and the concatenation it repeats.</summary>
    public sealed class ReplicationExpression : Expression
    {
        public Expression Count { get; set; }
        public ConcatenationExpression Value { get; set; }
    }

    public sealed class UnaryExpression : Expression
    {
        public string Operator { get; set; }
        public Expression Operand { get; set; }
    }

    public sealed class BinaryExpression : Expression
    {
        public string Operator { get; set; }
        public Expression Left { get; set; }
        public Expression Right { get; set; }
    }

    public sealed class ConditionalExpression : Expression
    {
        public Expression Condition { get; set; }
        public Expression WhenTrue { get; set; }
        public Expression WhenFalse { get; set; }
    }

    /// <summary><c>$clog2(N)</c>, <c>$time</c>, <c>max(a, b)</c>.</summary>
    public sealed class CallExpression : Expression
    {
        public string Name { get; set; }
        public bool HasParentheses { get; set; }
        public List<Expression> Arguments { get; } = new List<Expression>();
    }

    /// <summary><c>signed'(x)</c>, <c>8'(x)</c>, <c>state_t'(n)</c>.</summary>
    public sealed class CastExpression : Expression
    {
        /// <summary>What it casts to, as written: a type, <c>signed</c>, or a width.</summary>
        public string TargetText { get; set; }

        /// <summary>The width, when the cast is to a size: <c>8'(x)</c>.</summary>
        public Expression Width { get; set; }

        public Expression Value { get; set; }
    }

    /// <summary><c>'{a, b}</c> or <c>'{default: 0}</c>.</summary>
    public sealed class AssignmentPatternExpression : Expression
    {
        public List<Expression> Items { get; } = new List<Expression>();
        public Expression Default { get; set; }
    }

    /// <summary><c>( ... )</c> as written, kept so printing gives back the author's grouping.</summary>
    public sealed class ParenthesizedExpression : Expression
    {
        public Expression Inner { get; set; }
    }

    /// <summary>Where an expression was expected and could not be read; already reported.</summary>
    public sealed class ErrorExpression : Expression { }
}
