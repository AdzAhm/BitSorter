using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    public static partial class Elaborator
    {
        /// <summary>
        /// Processes: an <c>initial</c> or an <c>always</c> compiled to a flat array of instructions
        /// (ADR-5). Statements lower to the few things the engine runs -- a blocking or nonblocking
        /// write, a jump, a wait, the end -- so <c>if</c>, <c>case</c> and every loop are jumps, and a
        /// delay or an event control is where the process hands back to the scheduler.
        /// </summary>
        /// <remarks>
        /// - An <c>initial</c> runs its statement once and ends.
        /// - An <c>always</c> (and <c>always_ff</c>) runs its statement over and over; one with no delay or
        ///   event control anywhere is an error, because it would never let time move.
        /// - An <c>always_comb</c> (and <c>always_latch</c>) runs once at time 0 and then whenever anything
        ///   it reads changes -- everything it reads except what it writes itself (IEEE 1800, 9.2.2.2). It
        ///   may not wait.
        /// - <c>@*</c> waits on everything its statement reads, written or not (IEEE 1800, 9.4.2.2).
        /// - A variable declared in a named block is a signal of that block's scope, recorded like any
        ///   other; one declared in an unnamed block, a <c>for</c> loop's <c>int i</c>, and the counters
        ///   the compiler needs are the process's own (<see cref="SignalInfo.Local"/>).
        /// - <c>$display</c> and its kind print, which arrives in Part 6, and are skipped with a warning;
        ///   <c>$dumpfile</c> and <c>$dumpvars</c> are skipped silently, since every signal is recorded
        ///   anyway. <c>$finish</c>, <c>$stop</c> and <c>$fatal</c> end the run.
        /// </remarks>
        private sealed partial class Run
        {
            private readonly HashSet<string> _ignoredTasks = new HashSet<string>(StringComparer.Ordinal);

            private static readonly HashSet<string> PrintTasks = new HashSet<string>(StringComparer.Ordinal)
            {
                "$display", "$displayb", "$displayh", "$displayo", "$write", "$writeb", "$writeh", "$writeo",
                "$strobe", "$monitor", "$monitoron", "$monitoroff", "$info", "$warning", "$error",
                "$timeformat", "$printtimescale",
            };

            private static readonly HashSet<string> DumpTasks = new HashSet<string>(StringComparer.Ordinal)
            {
                "$dumpfile", "$dumpvars", "$dumpon", "$dumpoff", "$dumpall", "$dumplimit", "$dumpflush",
            };

            /// <summary>One process as it is compiled.</summary>
            private sealed class Proc
            {
                public Proc(string name) => Name = name;

                public readonly string Name;
                public readonly List<Instruction> Code = new List<Instruction>();
                public readonly HashSet<int> Writes = new HashSet<int>();
                public readonly HashSet<int> Locals = new HashSet<int>();
                public readonly List<Loop> Loops = new List<Loop>();

                /// <summary>Whether anything in it waits: a delay, an event control, a <c>wait</c>.</summary>
                public bool Waits;
                public bool Failed;

                public int Here => Code.Count;

                public int Emit(Instruction instruction)
                {
                    Code.Add(instruction);
                    return Code.Count - 1;
                }

                public int Emit(OpCode op, Expr value = null, int jump = 0) => Emit(new Instruction { Op = op, Value = value, Jump = jump });

                public void Patch(int at, int target)
                {
                    Instruction i = Code[at];
                    i.Jump = target;
                    Code[at] = i;
                }
            }

            /// <summary>The jumps a <c>break</c> and a <c>continue</c> leave, to be pointed once the loop's ends are known.</summary>
            private sealed class Loop
            {
                public readonly List<int> Breaks = new List<int>();
                public readonly List<int> Continues = new List<int>();
            }

            private ProcessInfo CompileProcess(Scope scope, ProceduralBlock block)
            {
                string kind = ProcessName(block.Kind);

                if (block.Kind == ProcessKind.Final)
                {
                    Warning(block.Location, ElaborationCodes.Ignored,
                        "A final block runs as the simulation ends, which BitSorter does not do yet; it is skipped.");
                    return null;
                }

                var p = new Proc($"{scope.Path}.{kind}@{block.Location.Line}");

                switch (block.Kind)
                {
                    case ProcessKind.Initial:
                        Statement(p, scope, block.Body);
                        p.Emit(OpCode.End);
                        break;

                    case ProcessKind.AlwaysComb:
                    case ProcessKind.AlwaysLatch:
                    {
                        Statement(p, scope, block.Body);
                        if (p.Waits)
                        {
                            Error(block.Location, ElaborationCodes.NeverWaits,
                                $"An {kind} may not wait: it runs whenever what it reads changes. Take the delay or the event control out.");
                            return null;
                        }

                        WaitSpec sensitivity = Sensitivity(p, 0, p.Here, excludeWritten: true);
                        p.Emit(new Instruction { Op = OpCode.WaitEvent, Wait = sensitivity });
                        p.Emit(OpCode.Jump, jump: 0);
                        break;
                    }

                    default:
                        Statement(p, scope, block.Body);
                        p.Emit(OpCode.Jump, jump: 0);

                        if (!p.Waits && !p.Failed)
                        {
                            Error(block.Location, ElaborationCodes.NeverWaits,
                                $"This {kind} has no delay and no event control, so it would run forever without letting time move. " +
                                "Start it with what it waits for, as always_ff @(posedge clk) does.");
                            return null;
                        }

                        break;
                }

                if (p.Failed)
                    return null;

                var writes = new List<int>(p.Writes);
                writes.Sort();
                return new ProcessInfo(p.Name, kind, p.Code.ToArray(), writes.ToArray());
            }

            /// <summary>
            /// What an <c>always_comb</c> or an <c>@*</c> waits on: every signal read in the
            /// instructions from <paramref name="from"/> to <paramref name="to"/>, except in wait and event
            /// expressions, except the process's own locals -- and for <c>always_comb</c>, except what it writes.
            /// </summary>
            private WaitSpec Sensitivity(Proc p, int from, int to, bool excludeWritten)
            {
                var reads = new SortedSet<int>();

                for (int i = from; i < to; i++)
                {
                    Instruction ins = p.Code[i];
                    if (ins.Op == OpCode.WaitEvent || ins.Op == OpCode.WaitUntil)
                        continue;

                    ins.Value?.CollectReads(reads);
                    ins.Other?.CollectReads(reads);
                    if (ins.Target != null)
                        foreach (LPiece piece in ins.Target)
                            piece.Offset?.CollectReads(reads);
                }

                reads.ExceptWith(p.Locals);
                if (excludeWritten)
                    reads.ExceptWith(p.Writes);

                var items = new List<EventItem>();
                foreach (int s in reads)
                    items.Add(new EventItem(EdgeKind.Any, _builder.Read(new SignalId(s))));

                return new WaitSpec(items.ToArray());
            }

            // -----------------------------------------------------------------
            // Statements
            // -----------------------------------------------------------------

            private void Statement(Proc p, Scope scope, Statement statement)
            {
                switch (statement)
                {
                    case null:
                    case NullStatement _:
                        return;

                    case BlockStatement b:
                        Block(p, scope, b);
                        return;

                    case AssignmentStatement a:
                        Assignment(p, scope, a);
                        return;

                    case IncrementStatement inc:
                        Assignment(p, scope, new AssignmentStatement
                        {
                            Location = inc.Location,
                            Target = inc.Target,
                            Operator = inc.Operator == "++" ? "+=" : "-=",
                            Value = new NumberLiteral { Location = inc.Location, Text = "1", Value = LogicVec.FromInt64(1, 32, true) },
                        });
                        return;

                    case IfStatement i:
                        If(p, scope, i);
                        return;

                    case CaseStatement c:
                        Case(p, scope, c);
                        return;

                    case ForStatement f:
                        For(p, scope, f);
                        return;

                    case RepeatStatement r:
                        Repeat(p, scope, r);
                        return;

                    case WhileStatement w:
                        While(p, scope, w);
                        return;

                    case ForeverStatement forever:
                    {
                        int top = p.Here;
                        var loop = new Loop();
                        p.Loops.Add(loop);
                        Statement(p, scope, forever.Body);
                        p.Emit(OpCode.Jump, jump: top);
                        CloseLoop(p, loop, top, p.Here);
                        return;
                    }

                    case TimedStatement t:
                        Timed(p, scope, t);
                        return;

                    case WaitStatement w:
                    {
                        Expr condition = Condition(p, scope, w.Condition);
                        p.Emit(new Instruction
                        {
                            Op = OpCode.WaitUntil,
                            Value = condition,
                            Wait = new WaitSpec(new[] { new EventItem(EdgeKind.Any, condition) }),
                        });
                        p.Waits = true;
                        Statement(p, scope, w.Body);
                        return;
                    }

                    case CallStatement call:
                        Call(p, call);
                        return;

                    case JumpStatement j:
                        Jump(p, j);
                        return;

                    case ReturnStatement r:
                        NotYet(r.Location, "return belongs to functions and tasks, which BitSorter does not support.");
                        p.Failed = true;
                        return;

                    case UnsupportedStatement _:
                        p.Failed = true;   // the read has said why
                        return;

                    default:
                        NotYet(statement.Location, "BitSorter cannot run this statement yet.");
                        p.Failed = true;
                        return;
                }
            }

            private void Block(Proc p, Scope scope, BlockStatement b)
            {
                Scope inner = scope;

                if (b.Label != null || b.Declarations.Count > 0)
                {
                    if (b.Label != null && !scope.TryDeclare(b.Label, new InstanceEntry { Declared = b.Location }))
                    {
                        Error(b.Location, ElaborationCodes.Duplicate, $"{b.Label} is the name of something else here already.");
                        p.Failed = true;
                    }

                    inner = new Scope(b.Label != null ? scope.Path + "." + b.Label : scope.Path, scope, scope.Module);

                    foreach (ModuleItem declaration in b.Declarations)
                        DeclareInBlock(p, inner, declaration, recorded: b.Label != null);
                }

                foreach (Statement statement in b.Statements)
                    Statement(p, inner, statement);
            }

            private void DeclareInBlock(Proc p, Scope scope, ModuleItem item, bool recorded)
            {
                switch (item)
                {
                    case DataDeclaration d:
                    {
                        if (d.NetType != null)
                        {
                            Error(d.Location, ElaborationCodes.BadLValue, "A wire cannot be declared inside a process; declare it in the module.");
                            p.Failed = true;
                            return;
                        }

                        if (!TryShape(d.Type, scope, out Shape shape))
                        {
                            p.Failed = true;
                            return;
                        }

                        foreach (Declarator declarator in d.Declarators)
                        {
                            if (declarator.Unpacked.Count > 0)
                            {
                                NotYet(declarator.Location, $"{declarator.Name} is an array -- a memory -- which arrives in Part 6.");
                                p.Failed = true;
                                continue;
                            }

                            LogicVec? initial = null;
                            if (declarator.Initializer != null)
                            {
                                if (!TryStartingValue(scope, declarator, shape, out LogicVec value))
                                {
                                    p.Failed = true;
                                    continue;
                                }

                                initial = value;
                            }

                            SignalEntry entry = DeclareSignal(scope, declarator.Name, declarator.Location, shape, SignalKind.Variable, null, initial, local: !recorded);
                            if (entry == null)
                                p.Failed = true;
                            else if (!recorded)
                                p.Locals.Add(entry.Signal.Index);
                        }

                        return;
                    }

                    case ParameterDeclaration parameter:
                        DeclareParameter(scope, parameter, null, null);
                        return;

                    case TypedefDeclaration typedef:
                        DeclareTypedef(scope, typedef);
                        return;

                    default:
                        NotYet(item.Location, "Only variables, localparams and typedefs can be declared inside a process in BitSorter.");
                        p.Failed = true;
                        return;
                }
            }

            /// <summary>A variable's declared starting value, sized to it; a constant, worked out before the run.</summary>
            private bool TryStartingValue(Scope scope, Declarator declarator, Shape shape, out LogicVec value)
            {
                if (!ExpressionBuilder.TryConstant(declarator.Initializer, scope, _diagnostics, out LogicVec given, shape.Width))
                {
                    value = default;
                    return false;
                }

                value = given.Extend(shape.Width, given.IsSigned);
                value = shape.Signed ? value.AsSigned() : value.AsUnsigned();
                return true;
            }

            private void Assignment(Proc p, Scope scope, AssignmentStatement a)
            {
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                var parts = new List<ExpressionBuilder.ProcPart>();
                if (!builder.TryProceduralTarget(a.Target, parts))
                {
                    p.Failed = true;
                    return;
                }

                int width = 0;
                foreach (ExpressionBuilder.ProcPart part in parts)
                    width += part.Width;

                bool later = a.Operator == "<=";
                Expression value = a.Value;
                if (a.Operator != "=" && !later)
                {
                    // x += 1 is x = x + 1, sized as that is.
                    value = new BinaryExpression { Location = a.Location, Operator = a.Operator.Substring(0, a.Operator.Length - 1), Left = a.Target, Right = a.Value };
                }

                Expr built = Sized(builder, value, width);
                if (builder.Failed)
                {
                    p.Failed = true;
                    return;
                }

                var target = new LPiece[parts.Count];
                for (int i = 0; i < parts.Count; i++)
                {
                    target[i] = new LPiece(parts[i].Signal.Signal.Index, parts[i].Lowest, parts[i].Width, parts[i].Offset);
                    p.Writes.Add(parts[i].Signal.Signal.Index);
                }

                switch (a.IntraAssignment)
                {
                    case null:
                        p.Emit(new Instruction { Op = later ? OpCode.AssignLater : OpCode.Assign, Target = target, TargetWidth = width, Value = built });
                        return;

                    case DelayControl delay when later:
                        // q <= #5 d: the value now, the write five units on; the process goes straight on.
                        p.Emit(new Instruction { Op = OpCode.AssignLater, Target = target, TargetWidth = width, Value = built, Other = DelayAmount(p, scope, delay) });
                        return;

                    case DelayControl delay:
                    {
                        // a = #5 b: b now, into a held value; wait; then write it.
                        LPiece[] held = Temporary(p, scope, width);
                        p.Emit(new Instruction { Op = OpCode.Assign, Target = held, TargetWidth = width, Value = built });
                        p.Emit(OpCode.Delay, DelayAmount(p, scope, delay));
                        p.Waits = true;
                        p.Emit(new Instruction { Op = OpCode.Assign, Target = target, TargetWidth = width, Value = _builder.Read(new SignalId(held[0].Signal)) });
                        return;
                    }

                    case EventControl events when !later:
                    {
                        LPiece[] held = Temporary(p, scope, width);
                        p.Emit(new Instruction { Op = OpCode.Assign, Target = held, TargetWidth = width, Value = built });
                        p.Emit(new Instruction { Op = OpCode.WaitEvent, Wait = Events(p, scope, events) });
                        p.Waits = true;
                        p.Emit(new Instruction { Op = OpCode.Assign, Target = target, TargetWidth = width, Value = _builder.Read(new SignalId(held[0].Signal)) });
                        return;
                    }

                    default:
                        NotYet(a.Location, "An event control inside a nonblocking assignment is not supported by BitSorter yet.");
                        p.Failed = true;
                        return;
                }
            }

            /// <summary>An expression at a target's width: widened as its own sizing says, or cut to the low bits.</summary>
            private static Expr Sized(ExpressionBuilder builder, Expression value, int width)
            {
                Expr built = builder.Build(value, width);
                return built.Width > width ? Expr.Slice(built, 0, width) : built;
            }

            /// <summary>A working variable of the process's own, to hold a value across a wait.</summary>
            private LPiece[] Temporary(Proc p, Scope scope, int width)
            {
                SignalId held = _builder.Local($"{scope.Path}.$held#{++_unnamed}", width);
                p.Locals.Add(held.Index);
                p.Writes.Add(held.Index);
                return new[] { new LPiece(held.Index, 0, width, null) };
            }

            private Expr Condition(Proc p, Scope scope, Expression condition)
            {
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                Expr built = builder.Build(condition, 0);
                if (builder.Failed)
                    p.Failed = true;
                return built;
            }

            private void If(Proc p, Scope scope, IfStatement i)
            {
                int toElse = p.Emit(OpCode.JumpUnless, Condition(p, scope, i.Condition));
                Statement(p, scope, i.Then);

                if (i.Else == null)
                {
                    p.Patch(toElse, p.Here);
                    return;
                }

                int toEnd = p.Emit(OpCode.Jump);
                p.Patch(toElse, p.Here);
                Statement(p, scope, i.Else);
                p.Patch(toEnd, p.Here);
            }

            /// <summary>
            /// A <c>case</c>: the subject and every label sized together, at the widest and signed only if
            /// all are (IEEE 1800, 12.5); each label a jump to its item, the first that matches winning.
            /// </summary>
            private void Case(Proc p, Scope scope, CaseStatement c)
            {
                CaseKind kind = c.Keyword == "casez" ? CaseKind.Z : c.Keyword == "casex" ? CaseKind.X : CaseKind.Exact;
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);

                ExpressionBuilder.TypeInfo subjectType = builder.TypeOf(c.Subject);
                int width = subjectType.Width;
                bool signed = subjectType.Signed;
                foreach (CaseItem item in c.Items)
                {
                    foreach (Expression label in item.Labels)
                    {
                        ExpressionBuilder.TypeInfo t = builder.TypeOf(label);
                        width = Math.Max(width, t.Width);
                        signed &= t.Signed;
                    }
                }

                Expr subject = builder.BuildAs(c.Subject, width, signed);
                var matches = new List<(int At, CaseItem Item)>();
                CaseItem fallback = null;

                foreach (CaseItem item in c.Items)
                {
                    if (item.IsDefault)
                    {
                        fallback = item;
                        continue;
                    }

                    foreach (Expression label in item.Labels)
                    {
                        Expr built = builder.BuildAs(label, width, signed);
                        matches.Add((p.Emit(new Instruction { Op = OpCode.JumpIfMatch, Value = subject, Other = built, Case = kind }), item));
                    }
                }

                if (builder.Failed)
                    p.Failed = true;

                int toFallback = p.Emit(OpCode.Jump);
                var ends = new List<int>();

                foreach (CaseItem item in c.Items)
                {
                    if (item.IsDefault)
                        continue;

                    int start = p.Here;
                    foreach ((int at, CaseItem owner) in matches)
                        if (owner == item)
                            p.Patch(at, start);

                    Statement(p, scope, item.Body);
                    ends.Add(p.Emit(OpCode.Jump));
                }

                p.Patch(toFallback, p.Here);
                if (fallback != null)
                    Statement(p, scope, fallback.Body);

                foreach (int end in ends)
                    p.Patch(end, p.Here);
            }

            private void For(Proc p, Scope scope, ForStatement f)
            {
                var inner = new Scope(scope.Path, scope, scope.Module);

                foreach (ForInitializer init in f.Initializers)
                {
                    if (init.Type != null)
                    {
                        if (!(init.Target is NameExpression name) || name.Path.Count != 1 || !TryShape(init.Type, inner, out Shape shape))
                        {
                            p.Failed = true;
                            continue;
                        }

                        SignalEntry variable = DeclareSignal(inner, name.Name, name.Location, shape, SignalKind.Variable, null, local: true);
                        if (variable == null)
                        {
                            p.Failed = true;
                            continue;
                        }

                        p.Locals.Add(variable.Signal.Index);
                    }

                    Assignment(p, inner, new AssignmentStatement { Location = init.Location, Target = init.Target, Operator = "=", Value = init.Value });
                }

                int top = p.Here;
                int exit = f.Condition != null ? p.Emit(OpCode.JumpUnless, Condition(p, inner, f.Condition)) : -1;

                var loop = new Loop();
                p.Loops.Add(loop);
                Statement(p, inner, f.Body);

                int next = p.Here;
                foreach (Statement step in f.Steps)
                    Statement(p, inner, step);
                p.Emit(OpCode.Jump, jump: top);

                if (exit >= 0)
                    p.Patch(exit, p.Here);
                CloseLoop(p, loop, next, p.Here);
            }

            private void Repeat(Proc p, Scope scope, RepeatStatement r)
            {
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                Expr count = builder.Build(r.Count, 0);
                bool signed = builder.TypeOf(r.Count).Signed;
                if (builder.Failed)
                {
                    p.Failed = true;
                    return;
                }

                // The count is worked out once, into a counter of the process's own; x or z repeats nothing.
                SignalId counter = _builder.Local($"{scope.Path}.$repeat#{++_unnamed}", 64, signed: true);
                p.Locals.Add(counter.Index);
                p.Writes.Add(counter.Index);
                var target = new[] { new LPiece(counter.Index, 0, 64, null) };
                Expr read = _builder.Read(counter);

                Expr start = count.Width > 64 ? Expr.Slice(count, 0, 64) : Expr.Resize(count, 64, signed);
                p.Emit(new Instruction { Op = OpCode.Assign, Target = target, TargetWidth = 64, Value = start });

                int top = p.Here;
                int exit = p.Emit(OpCode.JumpUnless, Expr.Apply(BinaryOp.Greater, read, Expr.Constant(LogicVec.FromInt64(0, 64, true))));

                var loop = new Loop();
                p.Loops.Add(loop);
                Statement(p, scope, r.Body);

                int next = p.Here;
                p.Emit(new Instruction
                {
                    Op = OpCode.Assign,
                    Target = target,
                    TargetWidth = 64,
                    Value = Expr.Apply(BinaryOp.Subtract, read, Expr.Constant(LogicVec.FromInt64(1, 64, true))),
                });
                p.Emit(OpCode.Jump, jump: top);

                p.Patch(exit, p.Here);
                CloseLoop(p, loop, next, p.Here);
            }

            private void While(Proc p, Scope scope, WhileStatement w)
            {
                var loop = new Loop();
                int top = p.Here;

                if (w.IsDoWhile)
                {
                    p.Loops.Add(loop);
                    Statement(p, scope, w.Body);
                    int next = p.Here;
                    p.Emit(OpCode.JumpIf, Condition(p, scope, w.Condition), top);
                    CloseLoop(p, loop, next, p.Here);
                    return;
                }

                int exit = p.Emit(OpCode.JumpUnless, Condition(p, scope, w.Condition));
                p.Loops.Add(loop);
                Statement(p, scope, w.Body);
                p.Emit(OpCode.Jump, jump: top);
                p.Patch(exit, p.Here);
                CloseLoop(p, loop, top, p.Here);
            }

            /// <summary>Points the loop's <c>continue</c>s at <paramref name="next"/> and its <c>break</c>s at <paramref name="exit"/>.</summary>
            private static void CloseLoop(Proc p, Loop loop, int next, int exit)
            {
                foreach (int at in loop.Continues)
                    p.Patch(at, next);
                foreach (int at in loop.Breaks)
                    p.Patch(at, exit);
                p.Loops.Remove(loop);
            }

            private void Timed(Proc p, Scope scope, TimedStatement t)
            {
                switch (t.Control)
                {
                    case DelayControl delay:
                        p.Emit(OpCode.Delay, DelayAmount(p, scope, delay));
                        p.Waits = true;
                        Statement(p, scope, t.Body);
                        return;

                    case EventControl events when events.Implicit:
                    {
                        // @*: waits on what the statement after it reads, so that is compiled first.
                        int wait = p.Emit(new Instruction { Op = OpCode.WaitEvent, Wait = new WaitSpec(Array.Empty<EventItem>()) });
                        p.Waits = true;
                        int start = p.Here;
                        Statement(p, scope, t.Body);

                        Instruction i = p.Code[wait];
                        i.Wait = Sensitivity(p, start, p.Here, excludeWritten: false);
                        p.Code[wait] = i;
                        return;
                    }

                    case EventControl events:
                        p.Emit(new Instruction { Op = OpCode.WaitEvent, Wait = Events(p, scope, events) });
                        p.Waits = true;
                        Statement(p, scope, t.Body);
                        return;
                }
            }

            private WaitSpec Events(Proc p, Scope scope, EventControl control)
            {
                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                var items = new List<EventItem>();

                foreach (EventExpression e in control.Events)
                {
                    EdgeKind edge = e.Edge == "posedge" ? EdgeKind.Posedge
                        : e.Edge == "negedge" ? EdgeKind.Negedge
                        : e.Edge == "edge" ? EdgeKind.Edge
                        : EdgeKind.Any;
                    items.Add(new EventItem(edge, builder.Build(e.Value, 0)));
                }

                if (builder.Failed)
                    p.Failed = true;

                return new WaitSpec(items.ToArray());
            }

            private Expr DelayAmount(Proc p, Scope scope, DelayControl delay)
            {
                if (delay.Values.Count != 1)
                {
                    Error(delay.Location, ElaborationCodes.BadDelay, "A delay in a process has one value, as #5 or #(PERIOD / 2); rise and fall belong to gates.");
                    p.Failed = true;
                    return Expr.Constant(LogicVec.FromInt64(0, 32));
                }

                return Condition(p, scope, delay.Values[0]);
            }

            private void Call(Proc p, CallStatement call)
            {
                switch (call.Name)
                {
                    case "$finish":
                    case "$stop":
                    case "$fatal":
                        p.Emit(new Instruction { Op = OpCode.Finish, Text = $"{call.Name} ({call.Location.File}:{call.Location.Line})" });
                        return;
                }

                if (DumpTasks.Contains(call.Name))
                    return;

                if (PrintTasks.Contains(call.Name))
                {
                    if (_ignoredTasks.Add(call.Name))
                        Warning(call.Location, ElaborationCodes.Ignored,
                            $"{call.Name} prints, which arrives in Part 6; until then BitSorter skips it.");
                    return;
                }

                NotYet(call.Location, call.IsSystem
                    ? $"{call.Name} is not supported by BitSorter yet."
                    : $"Calling the task {call.Name} is not supported by BitSorter.");
                p.Failed = true;
            }

            private void Jump(Proc p, JumpStatement j)
            {
                if (j.Keyword == "disable")
                {
                    NotYet(j.Location, "disable is not supported by BitSorter yet; use a flag the loop tests.");
                    p.Failed = true;
                    return;
                }

                if (p.Loops.Count == 0)
                {
                    Error(j.Location, ElaborationCodes.Misplaced, $"{j.Keyword} belongs inside a loop.");
                    p.Failed = true;
                    return;
                }

                Loop loop = p.Loops[p.Loops.Count - 1];
                (j.Keyword == "break" ? loop.Breaks : loop.Continues).Add(p.Emit(OpCode.Jump));
            }
        }
    }
}
