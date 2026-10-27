using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    public static partial class Elaborator
    {
        /// <summary>
        /// Written code held to a gate library (Part 14g, Ahmad's choice: gates only), checked as each module the rules name
        /// is built (<see cref="GateLevelRules"/>): every gate one of the library's cells at its delays, written as a primitive
        /// or as the cell's module; everything else only connects -- an assign, a gate's input, a port's connection -- with no
        /// delay of its own; a process only as a register of a set shape, or an initial giving starting values; and no
        /// constant where the library takes none. Each fault is an error at its line, said once however many copies a generate
        /// loop makes, so the code editor marks it and the text does not apply, as a drawn gate off the library is refused.
        /// </summary>
        /// <remarks>
        /// The checks read what was built where they can -- the elaborated expressions and delays, parameters and generate
        /// loops worked out -- so <c>NAND2 #(.Tpdlh(T), .Tpdhl(T))</c> inside a loop is held to what <c>T</c> is there, and a
        /// select by a genvar is a wire while a select by a signal is a multiplexer. A register's shape is read from what was
        /// written, since its branches are what would compute: <c>q &lt;= d</c>, or <c>if (reset) q &lt;= constant; else
        /// q &lt;= d</c>, its reset a one-bit signal, its inverse, or one compared with a constant -- the drawn register's
        /// shape, so the two sides agree (the 14g review found ifs nested to any function passing). A cell written without its
        /// delays takes the library's, since the cells are built with the library's delays as their defaults
        /// (<c>CellLibrary.Build</c>).
        /// </remarks>
        private sealed partial class Run
        {
            /// <summary>The faults said, so a generate loop's every copy of one is said once.</summary>
            private readonly HashSet<string> _libraryFaults = new HashSet<string>();

            /// <summary>Whether what is built in <paramref name="scope"/> is held to the library: a module the rules name.</summary>
            private bool HeldToLibrary(Scope scope) => _rules != null && scope?.Module != null && _rules.Held.Contains(scope.Module.Name);

            private void OffLibrary(SourceLocation at, string message)
            {
                if (_libraryFaults.Add(at.File + "\n" + at.Line + "\n" + at.Column + "\n" + message))
                    Error(at, ElaborationCodes.OffLibrary, message);
            }

            private void NoConstant(SourceLocation at) =>
                OffLibrary(at, "A constant, and the library takes none: give the design its 0s and 1s from an input.");

            /// <summary>What only connects, held to: no logic in it, and no constant where none is taken.</summary>
            private void Connects(Expr value, SourceLocation at, string what)
            {
                if (!value.IsWiring)
                    OffLibrary(at, $"This {what} works something out -- an operator, a choice or a select by a signal -- which is no cell " +
                        "of the library: build it of the library's gates. A plain connection is fine.");
                else if (_rules.NoConstants && value.HasConstant)
                    NoConstant(at);
            }

            /// <summary>A gate primitive written in a module held to the library: one of its cells, at its delays, its inputs only connecting.</summary>
            private void HoldGateToLibrary(Scope scope, GateKind kind, int inputs, Delay delay, Expr[] inputExprs, string written, SourceLocation at)
            {
                if (!HeldToLibrary(scope))
                    return;

                GateLevelCell? found = _rules.CellFor(kind, inputs);
                if (found == null)
                {
                    OffLibrary(at, $"A {written} of {inputs} input{(inputs == 1 ? "" : "s")} is no cell of the library, which has {_rules.Listed()}.");
                    return;
                }

                GateLevelCell cell = found.Value;
                if (delay.Rise != cell.Rise || delay.Fall != cell.Fall)
                    OffLibrary(at, $"This {written} is the library's {cell.Name} at rise {delay.Rise} and fall {delay.Fall}, but the library's " +
                        $"{cell.Name} is rise {cell.Rise} and fall {cell.Fall}: write it {cell.Name} (...), which takes them, or give it " +
                        $"#({cell.Rise}, {cell.Fall}).");

                // Its inputs only connect: an AND written into a NAND's input is a gate the library and the count never see.
                foreach (Expr input in inputExprs)
                    Connects(input, at, "gate's input");
            }

            /// <summary>An instance made in a module held to the library: a cell's module the library has, at its delays as its parameters came out.</summary>
            private void HoldInstanceToLibrary(Scope scope, string module, Scope child, ModuleInstance instance)
            {
                if (!HeldToLibrary(scope) || !_rules.CellModules.TryGetValue(module, out (GateKind Kind, int Inputs) gate))
                    return;

                GateLevelCell? found = _rules.CellFor(gate.Kind, gate.Inputs);
                if (found == null)
                {
                    OffLibrary(instance.Location, $"{instance.Name} is a {module}, which the library has no cell for: it has {_rules.Listed()}.");
                    return;
                }

                GateLevelCell cell = found.Value;
                long rise = ParameterOf(child, _rules.RiseParameter), fall = ParameterOf(child, _rules.FallParameter);
                if (rise != cell.Rise || fall != cell.Fall)
                    OffLibrary(instance.Location, $"{instance.Name} is a {module} at rise {rise} and fall {fall}, but the library's {module} is rise " +
                        $"{cell.Rise} and fall {cell.Fall}: leave its delays out, and it takes the library's.");
            }

            /// <summary>What a port of an instance is given in a module held to the library: it only connects (the 14g review).</summary>
            private void HoldConnectionToLibrary(Scope scope, Expr value, SourceLocation at)
            {
                if (HeldToLibrary(scope))
                    Connects(value, at, "connection");
            }

            /// <summary>A continuous assignment in a module held to the library: it only connects, and with no delay of its own.</summary>
            private void HoldAssignToLibrary(Scope scope, Expr value, Delay delay, SourceLocation at)
            {
                if (!HeldToLibrary(scope))
                    return;
                if (!delay.Equals(Delay.Zero))
                    OffLibrary(at, "A delay on a connection is no cell of the library: a delay is a gate's, as its cell gives it.");
                Connects(value, at, "assignment");
            }

            /// <summary>A supply net in a module held to a library that takes no constant: a constant by another name.</summary>
            private void HoldSupplyToLibrary(Scope scope, SourceLocation at)
            {
                if (HeldToLibrary(scope) && _rules.NoConstants)
                    NoConstant(at);
            }

            /// <summary>
            /// A process in a module held to the library: a register of the drawn register's shape, clocked by a plain signal,
            /// whose writes only connect (<see cref="Register"/>); or an <c>initial</c> that gives starting values and nothing
            /// else. Its starting and reset values may be constants; a constant loaded on every clock may not, where none is
            /// taken.
            /// </summary>
            private void HoldProcessToLibrary(Scope scope, ProceduralBlock block, List<ProcessInfo> compiled)
            {
                if (!HeldToLibrary(scope))
                    return;

                if (block.Kind == ProcessKind.Initial)
                {
                    if (!StartingValues(scope, block.Body))
                        OffLibrary(block.Location, "An initial in a design held to the library only gives starting values -- q = 1'b0; -- " +
                            "and works nothing out.");
                    return;
                }

                bool clocked = block.Kind == ProcessKind.AlwaysFf || block.Kind == ProcessKind.Always;
                if (!clocked || !(block.Body is TimedStatement timed) || !ClockedByASignal(timed.Control))
                {
                    OffLibrary(block.Location, $"This {ProcessName(block.Kind)} works logic out in code, which is no cell of the library: build " +
                        "it of the library's gates. A register is an always_ff @(posedge clk) of plain writes, q <= d.");
                    return;
                }

                if (!Register(scope, timed.Body, out SourceLocation at, out string why))
                {
                    OffLibrary(at, why);
                    return;
                }

                // What its writes are, as built: they only connect.
                foreach (ProcessInfo info in compiled)
                    foreach (Instruction i in info.Code)
                        if ((i.Op == OpCode.Assign || i.Op == OpCode.AssignLater) && i.Value != null && !i.Value.IsWiring)
                        {
                            OffLibrary(block.Location, "This register works something out -- an operator, a choice or a select by a signal -- " +
                                "which is no cell of the library: work its D out with the library's gates, and write it q <= d.");
                            return;
                        }
            }

            /// <summary>
            /// Whether a register's body has the drawn register's shape: plain writes, or one if on a reset whose then writes
            /// constants and whose else writes plain; false with where and why not. Plain writes take no constant where none
            /// is taken, since a constant loaded every clock is a tie, not a reset.
            /// </summary>
            private bool Register(Scope scope, Statement body, out SourceLocation at, out string why)
            {
                at = body?.Location ?? default;
                why = null;
                if (Writes(scope, body, constantsOnly: false, out at, out why))
                    return true;

                Statement single = body is BlockStatement b && b.Declarations.Count == 0 && b.Statements.Count == 1 ? b.Statements[0] : body;
                if (!(single is IfStatement reset))
                    return false;   // what Writes found is the reason

                at = reset.Location;
                if (!ResetCondition(scope, reset.Condition))
                {
                    why = "A register's if is its reset, a one-bit signal, its inverse, or one compared with a constant -- if (!rst_n) -- " +
                        "and any other choice is logic: build it of the library's gates, and give the register its D.";
                    return false;
                }

                if (reset.Else == null)
                {
                    why = "A register with a reset loads its D otherwise: write else q <= d. One that keeps its value is a multiplexer in " +
                        "front of it, built of the library's gates.";
                    return false;
                }

                return Writes(scope, reset.Then, constantsOnly: true, out at, out why) && Writes(scope, reset.Else, constantsOnly: false, out at, out why);
            }

            /// <summary>
            /// Whether <paramref name="s"/> is writes and nothing else -- an assignment, or a block of them -- each of a constant
            /// when <paramref name="constantsOnly"/> (a reset's), and of no constant where none is taken otherwise.
            /// </summary>
            private bool Writes(Scope scope, Statement s, bool constantsOnly, out SourceLocation at, out string why)
            {
                at = s?.Location ?? default;
                why = "A register's body is its writes, q <= d -- or an if on its reset, its then the reset values and its else the " +
                    "writes. Any other statement works something out: build it of the library's gates.";

                if (s is AssignmentStatement a)
                {
                    if (a.Operator != "=" && a.Operator != "<=")
                        return false;
                    bool constant = Constant(scope, a.Value);
                    if (constantsOnly && !constant)
                    {
                        why = "A reset loads constants -- q <= 1'b0 -- and the register's D is in its else.";
                        return false;
                    }

                    if (!constantsOnly && constant && _rules.NoConstants)
                    {
                        why = "A constant loaded on every clock, and the library takes none: give the design its 0s and 1s from an input.";
                        return false;
                    }

                    return true;
                }

                if (s is BlockStatement b && b.Declarations.Count == 0 && b.Statements.Count > 0)
                {
                    foreach (Statement inner in b.Statements)
                        if (!Writes(scope, inner, constantsOnly, out at, out why))
                            return false;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Whether a reset's condition is a one-bit signal, its inverse, or one compared with a constant: <c>rst</c>,
            /// <c>!rst_n</c>, <c>~rst_n</c>, <c>rst == 1'b1</c>.
            /// </summary>
            private static bool ResetCondition(Scope scope, Expression condition)
            {
                Expression c = Bare(condition);
                if (c is UnaryExpression u && (u.Operator == "!" || u.Operator == "~"))
                    return OneBitSignal(scope, u.Operand);
                if (c is BinaryExpression b && (b.Operator == "==" || b.Operator == "!=" || b.Operator == "===" || b.Operator == "!=="))
                    return OneBitSignal(scope, b.Left) && Bare(b.Right) is NumberLiteral || OneBitSignal(scope, b.Right) && Bare(b.Left) is NumberLiteral;
                return OneBitSignal(scope, c);
            }

            private static bool OneBitSignal(Scope scope, Expression e) =>
                Bare(e) is NameExpression n && n.Path.Count == 1 && scope.Lookup(n.Name) is SignalEntry s && s.Width == 1;

            /// <summary>Whether an initial gives starting values and nothing else: writes of constants, with no delay.</summary>
            private static bool StartingValues(Scope scope, Statement s)
            {
                if (s is AssignmentStatement a)
                    return a.IntraAssignment == null && a.Operator == "=" && Constant(scope, a.Value);
                if (s is BlockStatement b && b.Declarations.Count == 0)
                {
                    foreach (Statement inner in b.Statements)
                        if (!StartingValues(scope, inner))
                            return false;
                    return true;
                }

                return false;
            }

            /// <summary>Whether an expression is a constant as written: a number, a parameter, or a concatenation or replication of them -- '0, {4{1'b0}}, WIDTH'(0) aside.</summary>
            private static bool Constant(Scope scope, Expression e)
            {
                switch (Bare(e))
                {
                    case NumberLiteral _:
                        return true;
                    case NameExpression n:
                        return n.Path.Count == 1 && scope.Lookup(n.Name) is ConstantEntry;
                    case ConcatenationExpression c:
                        return c.Items.Count > 0 && c.Items.TrueForAll(item => Constant(scope, item));
                    case ReplicationExpression r:
                        return r.Value != null && Constant(scope, r.Value);
                    default:
                        return false;
                }
            }

            /// <summary>Whether an event control is a clock's edge on a plain signal: <c>@(posedge clk)</c>, with a reset's edge beside it.</summary>
            private static bool ClockedByASignal(TimingControl control)
            {
                if (!(control is EventControl events) || events.Events.Count == 0)
                    return false;
                bool edge = false;
                foreach (EventExpression e in events.Events)
                {
                    if (!(Bare(e.Value) is NameExpression))
                        return false;   // @(posedge (a & b)) is a clock of logic
                    edge |= e.Edge == "posedge" || e.Edge == "negedge";
                }

                return edge;
            }

            /// <summary>A cell's delay parameter as its instance has it; -1 when it is not a known number.</summary>
            private static long ParameterOf(Scope scope, string name) =>
                scope?.Own(name) is ConstantEntry c && c.Value.TryGetInt64(out long value) ? value : -1;
        }
    }
}
