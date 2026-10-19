using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    public static partial class Elaborator
    {
        /// <summary>
        /// Functions and tasks (Part L2). Two ways in, as two kinds of call need:
        /// </summary>
        /// <remarks>
        /// - **A function called inside an expression** is compiled once into a <see cref="FunctionInfo"/>
        ///   and run by the expression where it is evaluated (<c>Expr.Call</c>): in a continuous assignment, a
        ///   condition, an <c>always_comb</c>. It may do what a function may (IEEE 1800, 13.4) -- compute, branch,
        ///   loop, call other functions, call itself if it is automatic -- and write only its own signals.
        /// - **A task, or a function called as a statement**, has its statements written into the calling
        ///   process where the call stands, between its arguments copied in and its outputs copied back
        ///   (13.3). So a task may wait -- its time is its caller's -- and print, and write the design's
        ///   signals, and a void function may print. A task that calls itself is refused: written in place,
        ///   it would never end.
        ///
        /// **Every call uses the same signals of its own** (<see cref="SubroutineEntry.Frame"/>), made the
        /// first time one is needed: static storage, as the standard's default is. An automatic one starts
        /// each call from its locals' starting values. Two processes running one task at the same moment
        /// would share them -- as two callers of a static task do in any simulator.
        /// </remarks>
        private sealed partial class Run
        {
            private readonly HashSet<SubroutineEntry> _inlining = new HashSet<SubroutineEntry>();

            private void DeclareSubroutine(Scope scope, SubroutineDeclaration s)
            {
                var entry = new SubroutineEntry { Declaration = s, Scope = scope, Declared = s.Location, Compile = CompileFunction };

                if (!s.IsTask && !s.IsVoid && s.ReturnType != null)
                {
                    if (TryShape(s.ReturnType, scope, out Shape shape))
                    {
                        entry.Width = shape.Width;
                        entry.Signed = shape.Signed;
                    }
                    else
                    {
                        entry.Failed = true;
                    }
                }

                if (!scope.TryDeclare(s.Name, entry))
                    Error(s.Location, ElaborationCodes.Duplicate, $"{s.Name} is declared twice here.");
            }

            /// <summary>
            /// The function's or task's own signals, made the first time a call needs them: its result,
            /// named as the function is; its arguments, in the header or in the body; its locals. False if
            /// they could not be made, having said why once.
            /// </summary>
            private bool EnsureFrame(SubroutineEntry r)
            {
                if (r.Failed)
                    return false;
                if (r.Frame != null)
                    return true;

                SubroutineDeclaration s = r.Declaration;
                var frame = new Scope(r.Scope.Path + "." + s.Name, r.Scope, r.Scope.Module);
                r.Frame = frame;
                r.Arguments = new List<(SignalEntry, PortDirection, Expression)>();
                r.Own = new List<SignalEntry>();
                bool ok = true;

                if (!r.IsVoid)
                {
                    Shape shape = new Shape(0, 0, false);
                    if (s.ReturnType != null && !TryShape(s.ReturnType, r.Scope, out shape))
                        ok = false;

                    SignalEntry result = DeclareSignal(frame, s.Name, s.Location, shape, SignalKind.Variable, null, null, local: true);
                    if (result == null)
                        ok = false;
                    else
                    {
                        r.Result = result;
                        r.Own.Add(result);
                    }
                }

                foreach (SubroutinePort port in s.Ports)
                {
                    if (!TryShape(port.Type, frame, out Shape shape))
                    {
                        ok = false;
                        continue;
                    }

                    SignalEntry argument = DeclareSignal(frame, port.Name, port.Location, shape, SignalKind.Variable, null, null, local: true);
                    if (argument == null)
                    {
                        ok = false;
                        continue;
                    }

                    r.Arguments.Add((argument, port.Direction, port.Default));
                    r.Own.Add(argument);
                }

                foreach (ModuleItem item in s.Declarations)
                {
                    switch (item)
                    {
                        case PortItemDeclaration ports:
                        {
                            // The old style: input [7:0] x; in the body.
                            if (!TryShape(ports.Type, frame, out Shape shape))
                            {
                                ok = false;
                                break;
                            }

                            foreach (Declarator declarator in ports.Declarators)
                            {
                                SignalEntry argument = DeclareSignal(frame, declarator.Name, declarator.Location, shape, SignalKind.Variable, null, null, local: true);
                                if (argument == null)
                                {
                                    ok = false;
                                    continue;
                                }

                                r.Arguments.Add((argument, ports.Direction, null));
                                r.Own.Add(argument);
                            }

                            break;
                        }

                        case DataDeclaration d:
                        {
                            if (d.NetType != null)
                            {
                                Error(d.Location, ElaborationCodes.BadLValue, $"A wire cannot be declared inside {s.Name}; declare a variable.");
                                ok = false;
                                break;
                            }

                            if (!TryShape(d.Type, frame, out Shape shape))
                            {
                                ok = false;
                                break;
                            }

                            foreach (Declarator declarator in d.Declarators)
                            {
                                if (declarator.Unpacked.Count > 0)
                                {
                                    NotYet(declarator.Location, $"{declarator.Name} is an array inside {s.Name}, which BitSorter does not support yet; declare it in the module.");
                                    ok = false;
                                    continue;
                                }

                                LogicVec? initial = null;
                                if (declarator.Initializer != null)
                                {
                                    if (!TryStartingValue(frame, declarator, shape, out LogicVec value))
                                    {
                                        ok = false;
                                        continue;
                                    }

                                    initial = value;
                                }

                                SignalEntry local = DeclareSignal(frame, declarator.Name, declarator.Location, shape, SignalKind.Variable, null, initial, local: true);
                                if (local == null)
                                    ok = false;
                                else
                                    r.Own.Add(local);
                            }

                            break;
                        }

                        case ParameterDeclaration parameter:
                            DeclareParameter(frame, parameter, null, null);
                            break;

                        case TypedefDeclaration typedef:
                            DeclareTypedef(frame, typedef);
                            break;

                        default:
                            NotYet(item.Location, $"Only arguments, variables, parameters and typedefs can be declared inside {s.Name} in BitSorter.");
                            ok = false;
                            break;
                    }
                }

                if (!ok)
                    r.Failed = true;
                return ok;
            }

            /// <summary>
            /// A function compiled to run inside an expression, the first time a call there needs it. A call
            /// of it met while it is compiled -- a function that calls itself -- gets the same
            /// <see cref="FunctionInfo"/>, filled in once the compiling is done.
            /// </summary>
            private FunctionInfo CompileFunction(SubroutineEntry r)
            {
                if (r.Compiled != null)
                    return r.Compiled;
                if (!EnsureFrame(r))
                    return null;

                SubroutineDeclaration s = r.Declaration;
                var info = new FunctionInfo(r.Frame.Path) { Automatic = s.IsAutomatic };
                r.Compiled = info;
                r.Compiling = true;

                var p = new Proc(r.Frame.Path);
                var frame = new Frame(r);
                p.Frames.Add(frame);
                foreach (Statement statement in s.Statements)
                    Statement(p, r.Frame, statement);
                foreach (int at in frame.Returns)
                    p.Patch(at, p.Here);
                p.Emit(OpCode.End);
                r.Compiling = false;

                bool ok = !p.Failed && FitsInAnExpression(r, p);
                if (!ok)
                {
                    r.Failed = true;
                    r.Compiled = null;
                    return null;
                }

                var own = new List<int>();
                var seen = new HashSet<int>();
                foreach (SignalEntry signal in r.Own)
                    if (seen.Add(signal.Signal.Index))
                        own.Add(signal.Signal.Index);
                foreach (int local in p.Locals)
                    if (seen.Add(local))
                        own.Add(local);

                var inputs = new int[r.Arguments.Count];
                for (int i = 0; i < inputs.Length; i++)
                    inputs[i] = r.Arguments[i].Signal.Signal.Index;

                var localInfo = new SignalInfo[own.Count];
                for (int i = 0; i < own.Count; i++)
                    localInfo[i] = _builder.InfoOf(new SignalId(own[i]));

                // What it reads, and what the functions it calls read in turn -- one it is calling itself
                // has none yet, and needs none: its own reads are these.
                var reads = new SortedSet<int>();
                CollectReads(p, 0, p.Here, reads);
                CollectFunctionReads(p, 0, p.Here, reads);
                reads.ExceptWith(seen);

                info.Code = p.Code.ToArray();
                info.Inputs = inputs;
                info.Result = r.Result != null ? r.Result.Signal.Index : -1;
                info.Locals = own.ToArray();
                info.LocalInfo = localInfo;
                info.BodyReads = new List<int>(reads).ToArray();
                return info;
            }

            /// <summary>Whether a function's statements are ones a call inside an expression can run, saying why not once.</summary>
            private bool FitsInAnExpression(SubroutineEntry r, Proc p)
            {
                string name = r.Declaration.Name;

                foreach (Instruction ins in p.Code)
                {
                    string why;
                    switch (ins.Op)
                    {
                        case OpCode.Assign:
                        case OpCode.Jump:
                        case OpCode.JumpUnless:
                        case OpCode.JumpIf:
                        case OpCode.JumpIfMatch:
                        case OpCode.End:
                            continue;

                        case OpCode.AssignLater:
                            why = $"{name} makes a nonblocking write (<=), which a function cannot: it gives its value at once. Write it with =.";
                            break;

                        case OpCode.Delay:
                        case OpCode.WaitEvent:
                        case OpCode.WaitUntil:
                            why = $"{name} waits, which a function cannot: it gives its value at once. A task can wait.";
                            break;

                        case OpCode.Print:
                            why = $"{name} prints, which BitSorter does not do from a function called inside an expression yet. Call it as a statement of its own to print, or print where it is called.";
                            break;

                        case OpCode.Fork:
                        case OpCode.WaitFork:
                        case OpCode.DisableFork:
                            why = $"{name} forks, which a function cannot: it gives its value at once. A task can fork.";
                            break;

                        case OpCode.Disable:
                            why = $"{name} disables a block or a task, which BitSorter does not do inside a function called in an expression yet. Leave its loop with break, or call it as a statement of its own.";
                            break;

                        case OpCode.Force:
                        case OpCode.Release:
                            why = $"{name} forces or releases a signal, which a function cannot. A task can.";
                            break;

                        default:
                            why = $"{name} calls a system task that cannot run inside an expression. Call it as a statement of its own, or make it a task.";
                            break;
                    }

                    Error(r.Declaration.Location, ElaborationCodes.BadCall, why);
                    return false;
                }

                var own = new HashSet<int>(p.Locals);
                foreach (SignalEntry signal in r.Own)
                    own.Add(signal.Signal.Index);

                foreach (int written in p.Writes)
                {
                    if (own.Contains(written))
                        continue;

                    Error(r.Declaration.Location, ElaborationCodes.BadCall,
                        $"{name} writes {_builder.InfoOf(new SignalId(written)).Name}, which is not its own: a function called inside an expression writes only its arguments, " +
                        "its result and its locals. Call it as a statement of its own, or make it a task.");
                    return false;
                }

                return true;
            }

            /// <summary>
            /// A task, or a function called as a statement: its arguments copied into its own signals, its
            /// statements written here in the calling process, and its outputs copied back.
            /// </summary>
            private void Inline(Proc p, Scope scope, CallStatement call, SubroutineEntry r)
            {
                if (!EnsureFrame(r))
                {
                    p.Failed = true;
                    return;
                }

                if (!_inlining.Add(r))
                {
                    Error(call.Location, ElaborationCodes.Recursion,
                        $"{call.Name} calls itself as a statement, which BitSorter cannot run: written in place it would never end. " +
                        "A function that calls itself inside an expression can.");
                    p.Failed = true;
                    return;
                }

                try
                {
                    InlineBody(p, scope, call, r);
                }
                finally
                {
                    _inlining.Remove(r);
                }
            }

            private void InlineBody(Proc p, Scope scope, CallStatement call, SubroutineEntry r)
            {
                SubroutineDeclaration s = r.Declaration;

                if (call.Arguments.Count > r.Arguments.Count)
                {
                    Error(call.Location, ElaborationCodes.BadCall,
                        $"{call.Name} takes {r.Arguments.Count} argument{(r.Arguments.Count == 1 ? "" : "s")}, and this call gives {call.Arguments.Count}.");
                    p.Failed = true;
                    return;
                }

                var arguments = new HashSet<SignalEntry>();
                foreach ((SignalEntry signal, PortDirection _, Expression _) in r.Arguments)
                    arguments.Add(signal);

                foreach (SignalEntry own in r.Own)
                {
                    p.Locals.Add(own.Signal.Index);
                    p.Writes.Add(own.Signal.Index);

                    // An automatic one starts each call afresh: every local, and the result, back to its start.
                    if (s.IsAutomatic && !arguments.Contains(own))
                    {
                        LogicVec start = FunctionRunner.Start(_builder.InfoOf(own.Signal));
                        p.Emit(new Instruction { Op = OpCode.Assign, Target = Whole(own), TargetWidth = own.Width, Value = Expr.Constant(start) });
                    }
                }

                var outputs = new List<(SignalEntry Formal, Expression Target)>();
                for (int i = 0; i < r.Arguments.Count; i++)
                {
                    (SignalEntry formal, PortDirection direction, Expression fallback) = r.Arguments[i];
                    Expression given = i < call.Arguments.Count ? call.Arguments[i] : null;

                    if (direction != PortDirection.Output)
                    {
                        Expression value = given ?? fallback;
                        if (value == null)
                        {
                            Error(call.Location, ElaborationCodes.BadCall, $"{call.Name} needs a value for {ExpressionBuilder.Short(formal)}, its argument {i + 1}.");
                            p.Failed = true;
                            continue;
                        }

                        var builder = Builder(p, given != null ? scope : r.Scope);
                        Expr built = Sized(builder, value, formal.Width, formal.Type);
                        if (builder.Failed)
                            p.Failed = true;
                        p.Emit(new Instruction { Op = OpCode.Assign, Target = Whole(formal), TargetWidth = formal.Width, Value = built });
                    }

                    if (direction != PortDirection.Input)
                    {
                        if (given == null)
                        {
                            Error(call.Location, ElaborationCodes.BadCall, $"{call.Name} gives a value back through its argument {i + 1}, so the call needs a variable there to take it.");
                            p.Failed = true;
                            continue;
                        }

                        outputs.Add((formal, given));
                    }
                }

                // The body: its own loops and returns, apart from the caller's.
                var callerLoops = new List<Loop>(p.Loops);
                p.Loops.Clear();
                var frame = new Frame(r);
                p.Frames.Add(frame);
                int body = p.Here;

                foreach (Statement statement in s.Statements)
                    Statement(p, r.Frame, statement);

                foreach (int at in frame.Returns)
                    p.Patch(at, p.Here);
                p.Frames.RemoveAt(p.Frames.Count - 1);
                p.Loops.Clear();
                p.Loops.AddRange(callerLoops);

                foreach ((SignalEntry formal, Expression target) in outputs)
                    CopyOut(p, scope, target, formal);

                // Where this call is, for a disable of the task: past its outputs, which the standard leaves open.
                var range = new BlockRange { Start = body, End = p.Here };
                r.Inlined.Add(range);
                p.Ranges.Add(range);
            }

            /// <summary>An output argument's value, given back to what the call names for it, as an assignment would.</summary>
            private void CopyOut(Proc p, Scope scope, Expression target, SignalEntry formal)
            {
                var builder = Builder(p, scope);
                var parts = new List<ExpressionBuilder.ProcPart>();
                if (!builder.TryProceduralTarget(target, parts))
                {
                    p.Failed = true;
                    return;
                }

                int width = 0;
                foreach (ExpressionBuilder.ProcPart part in parts)
                    width += part.Width;

                Expr value = _builder.Read(formal.Signal);
                value = width > formal.Width ? Expr.Resize(value, width, formal.Signed)
                    : width < formal.Width ? Expr.Slice(value, 0, width)
                    : value;

                p.Emit(new Instruction { Op = OpCode.Assign, Target = Pieces(p, parts), TargetWidth = width, Value = value });
            }

            /// <summary>A whole signal as an assignment's target.</summary>
            private static LPiece[] Whole(SignalEntry signal) => new[] { new LPiece(signal.Signal.Index, 0, signal.Width, null) };

            private void Return(Proc p, Scope scope, ReturnStatement r)
            {
                if (p.Frames.Count == 0)
                {
                    Error(r.Location, ElaborationCodes.Misplaced, "return belongs inside a function or a task.");
                    p.Failed = true;
                    return;
                }

                Frame frame = p.Frames[p.Frames.Count - 1];
                string name = frame.Routine.Declaration.Name;

                if (r.Value != null)
                {
                    if (frame.Routine.Result == null)
                    {
                        Error(r.Location, ElaborationCodes.BadCall, $"{name} gives no value, so its return takes none: write return;.");
                        p.Failed = true;
                        return;
                    }

                    SignalEntry result = frame.Routine.Result;
                    var builder = Builder(p, scope);
                    Expr value = Sized(builder, r.Value, result.Width, result.Type);
                    if (builder.Failed)
                        p.Failed = true;
                    p.Emit(new Instruction { Op = OpCode.Assign, Target = Whole(result), TargetWidth = result.Width, Value = value });
                    p.Writes.Add(result.Signal.Index);
                }
                else if (frame.Routine.Result != null)
                {
                    Error(r.Location, ElaborationCodes.BadCall, $"{name} gives a value, so its return needs one: return x;.");
                    p.Failed = true;
                    return;
                }

                frame.Returns.Add(p.Emit(OpCode.Jump));
            }

            /// <summary>A function's or task's body being compiled: what a return in it writes, and where it jumps.</summary>
            private sealed class Frame
            {
                public Frame(SubroutineEntry routine) => Routine = routine;

                public readonly SubroutineEntry Routine;
                public readonly List<int> Returns = new List<int>();
            }
        }
    }
}
