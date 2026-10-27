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
        /// or as the cell's module; no logic written with operators -- an assign or a process may only connect, and a process
        /// only as a register; no constant where the library takes none. Each fault is an error at its line, so the code
        /// editor marks it and the text does not apply, as a drawn gate off the library is refused.
        /// </summary>
        /// <remarks>
        /// The checks read what was built -- the elaborated expressions and delays, parameters and generate loops worked out
        /// -- so <c>NAND2 #(.Tpdlh(T), .Tpdhl(T))</c> inside a loop is held to what <c>T</c> is there, and a select by a
        /// genvar is a wire while a select by a signal is a multiplexer. A cell written without its delays takes the
        /// library's, since the cells are built with the library's delays as their defaults (<c>CellLibrary.Build</c>).
        /// </remarks>
        private sealed partial class Run
        {
            /// <summary>Whether what is built in <paramref name="scope"/> is held to the library: a module the rules name.</summary>
            private bool HeldToLibrary(Scope scope) => _rules != null && scope?.Module != null && _rules.Held.Contains(scope.Module.Name);

            /// <summary>A gate primitive written in a module held to the library: one of its cells, at its delays.</summary>
            private void HoldGateToLibrary(Scope scope, GateKind kind, int inputs, Delay delay, Expr[] inputExprs, string written, SourceLocation at)
            {
                if (!HeldToLibrary(scope))
                    return;

                GateLevelCell? found = _rules.CellFor(kind, inputs);
                if (found == null)
                {
                    Error(at, ElaborationCodes.OffLibrary,
                        $"A {written} of {inputs} input{(inputs == 1 ? "" : "s")} is no cell of the library, which has {_rules.Listed()}.");
                    return;
                }

                GateLevelCell cell = found.Value;
                if (delay.Rise != cell.Rise || delay.Fall != cell.Fall)
                    Error(at, ElaborationCodes.OffLibrary,
                        $"This {written} is the library's {cell.Name} at rise {delay.Rise} and fall {delay.Fall}, but the library's {cell.Name} is " +
                        $"rise {cell.Rise} and fall {cell.Fall}: write it {cell.Name} (...), which takes them, or give it #({cell.Rise}, {cell.Fall}).");
                else if (_rules.NoConstants)
                    foreach (Expr input in inputExprs)
                        if (input.HasConstant)
                        {
                            NoConstant(at);
                            break;
                        }
            }

            /// <summary>
            /// An instance made in a module held to the library: a cell's module the library has, at its delays as its
            /// parameters came out; and, where the library takes no constant, no constant given to any instance's port.
            /// </summary>
            private void HoldInstanceToLibrary(Scope scope, string module, Scope child, ModuleInstance instance)
            {
                if (!HeldToLibrary(scope))
                    return;

                if (_rules.NoConstants)
                    foreach (PortConnection connection in instance.Connections)
                        if (connection.Value != null && WritesAConstant(connection.Value))
                        {
                            NoConstant(connection.Location);
                            break;
                        }

                if (!_rules.CellModules.TryGetValue(module, out (GateKind Kind, int Inputs) gate))
                    return;

                GateLevelCell? found = _rules.CellFor(gate.Kind, gate.Inputs);
                if (found == null)
                {
                    Error(instance.Location, ElaborationCodes.OffLibrary,
                        $"{instance.Name} is a {module}, which the library has no cell for: it has {_rules.Listed()}.");
                    return;
                }

                GateLevelCell cell = found.Value;
                long rise = ParameterOf(child, _rules.RiseParameter), fall = ParameterOf(child, _rules.FallParameter);
                if (rise != cell.Rise || fall != cell.Fall)
                    Error(instance.Location, ElaborationCodes.OffLibrary,
                        $"{instance.Name} is a {module} at rise {rise} and fall {fall}, but the library's {module} is rise {cell.Rise} and fall " +
                        $"{cell.Fall}: leave its delays out, and it takes the library's.");
            }

            /// <summary>A continuous assignment in a module held to the library: it may only connect, and hold no constant where none is taken.</summary>
            private void HoldAssignToLibrary(Scope scope, Expr value, SourceLocation at)
            {
                if (!HeldToLibrary(scope))
                    return;
                if (!value.IsWiring)
                    Error(at, ElaborationCodes.OffLibrary,
                        "This works something out with an operator, a choice or a select by a signal, which is no cell of the library: " +
                        "build it of the library's gates. A plain connection -- assign y = a; -- is fine.");
                else if (_rules.NoConstants && value.HasConstant)
                    NoConstant(at);
            }

            /// <summary>
            /// A process in a module held to the library: only a register -- an <c>always_ff</c>, or an <c>always</c> on a
            /// clock's edge -- whose writes only connect and whose conditions are a signal or its inverse, as a reset is; its
            /// starting and reset values may be constants. An <c>initial</c> sets things up and works nothing out.
            /// </summary>
            private void HoldProcessToLibrary(Scope scope, ProceduralBlock block, List<ProcessInfo> compiled)
            {
                if (!HeldToLibrary(scope) || block.Kind == ProcessKind.Initial)
                    return;

                bool register = block.Kind == ProcessKind.AlwaysFf || (block.Kind == ProcessKind.Always && Clocked(block.Body));
                if (!register)
                {
                    Error(block.Location, ElaborationCodes.OffLibrary,
                        $"This {ProcessName(block.Kind)} works logic out in code, which is no cell of the library: build it of the library's " +
                        "gates. A register is an always_ff of plain writes, q <= d.");
                    return;
                }

                foreach (ProcessInfo info in compiled)
                    foreach (Instruction i in info.Code)
                    {
                        bool fine;
                        switch (i.Op)
                        {
                            case OpCode.Assign:
                            case OpCode.AssignLater:
                                fine = i.Value == null || i.Value.IsWiring;
                                break;
                            case OpCode.JumpIf:
                            case OpCode.JumpUnless:
                                fine = i.Value == null || i.Value.IsWiring || i.Value.IsInvertedWiring;
                                break;
                            case OpCode.JumpIfMatch:
                                fine = false;
                                break;
                            default:
                                fine = true;
                                break;
                        }

                        if (!fine)
                        {
                            Error(block.Location, ElaborationCodes.OffLibrary,
                                "This register works something out -- an operator, a choice or a case -- which is no cell of the library: " +
                                "work its D out with the library's gates, and write the register q <= d.");
                            return;
                        }
                    }
            }

            private void NoConstant(SourceLocation at) =>
                Error(at, ElaborationCodes.OffLibrary, "A constant, and the library takes none: give the design its 0s and 1s from an input.");

            /// <summary>Whether a process's body waits on a clock's edge: <c>always @(posedge clk)</c>.</summary>
            private static bool Clocked(Statement body)
            {
                if (!(body is TimedStatement timed) || !(timed.Control is EventControl events))
                    return false;
                foreach (EventExpression e in events.Events)
                    if (e.Edge == "posedge" || e.Edge == "negedge")
                        return true;
                return false;
            }

            /// <summary>Whether a port's connection is, or holds, a number written out.</summary>
            private static bool WritesAConstant(Expression e)
            {
                switch (e)
                {
                    case NumberLiteral _:
                        return true;
                    case ConcatenationExpression c:
                        return c.Items.Exists(WritesAConstant);
                    case ReplicationExpression r:
                        return WritesAConstant(r.Value);
                    default:
                        return false;
                }
            }

            /// <summary>A cell's delay parameter as its instance has it; -1 when it is not a known number.</summary>
            private static long ParameterOf(Scope scope, string name) =>
                scope?.Own(name) is ConstantEntry c && c.Value.TryGetInt64(out long value) ? value : -1;
        }
    }
}
