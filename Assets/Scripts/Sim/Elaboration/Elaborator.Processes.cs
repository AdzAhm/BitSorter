using System;
using System.Collections.Generic;
using System.Text;
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
        /// - <c>$display</c>, <c>$write</c>, <c>$strobe</c>, <c>$monitor</c> (with their <c>b</c>, <c>h</c>
        ///   and <c>o</c> forms), <c>$info</c>, <c>$warning</c>, <c>$error</c> and <c>$fatal</c> print into
        ///   the transcript; their formats are read here, once. <c>$readmemh</c>, <c>$readmemb</c>,
        ///   <c>$writememh</c> and <c>$writememb</c> load and save memories through the run's files.
        ///   <c>$dumpfile</c> and <c>$dumpvars</c> are skipped silently, since every signal is recorded
        ///   anyway. <c>$finish</c> and <c>$fatal</c> end the run; <c>$stop</c> suspends it.
        /// </remarks>
        private sealed partial class Run
        {
            private readonly HashSet<string> _ignoredTasks = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>The print tasks: when each prints, how an argument with no format is written, and how its line is marked.</summary>
            private static readonly Dictionary<string, (PrintKind Kind, char Radix, Severity Severity)> PrintTasks =
                new Dictionary<string, (PrintKind, char, Severity)>(StringComparer.Ordinal)
                {
                    ["$display"] = (PrintKind.Display, 'd', Severity.Info),
                    ["$displayb"] = (PrintKind.Display, 'b', Severity.Info),
                    ["$displayh"] = (PrintKind.Display, 'h', Severity.Info),
                    ["$displayo"] = (PrintKind.Display, 'o', Severity.Info),
                    ["$write"] = (PrintKind.Write, 'd', Severity.Info),
                    ["$writeb"] = (PrintKind.Write, 'b', Severity.Info),
                    ["$writeh"] = (PrintKind.Write, 'h', Severity.Info),
                    ["$writeo"] = (PrintKind.Write, 'o', Severity.Info),
                    ["$strobe"] = (PrintKind.Strobe, 'd', Severity.Info),
                    ["$strobeb"] = (PrintKind.Strobe, 'b', Severity.Info),
                    ["$strobeh"] = (PrintKind.Strobe, 'h', Severity.Info),
                    ["$strobeo"] = (PrintKind.Strobe, 'o', Severity.Info),
                    ["$monitor"] = (PrintKind.Monitor, 'd', Severity.Info),
                    ["$monitorb"] = (PrintKind.Monitor, 'b', Severity.Info),
                    ["$monitorh"] = (PrintKind.Monitor, 'h', Severity.Info),
                    ["$monitoro"] = (PrintKind.Monitor, 'o', Severity.Info),
                    ["$monitoron"] = (PrintKind.MonitorOn, 'd', Severity.Info),
                    ["$monitoroff"] = (PrintKind.MonitorOff, 'd', Severity.Info),
                    ["$info"] = (PrintKind.Display, 'd', Severity.Info),
                    ["$warning"] = (PrintKind.Display, 'd', Severity.Warning),
                    ["$error"] = (PrintKind.Display, 'd', Severity.Error),
                };

            /// <summary>Tasks that only change how time is printed, which BitSorter does not yet: skipped with a warning.</summary>
            private static readonly HashSet<string> SkippedTasks = new HashSet<string>(StringComparer.Ordinal)
            {
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

                /// <summary>The functions and tasks whose statements are being written in, innermost last: where a return goes.</summary>
                public readonly List<Frame> Frames = new List<Frame>();

                /// <summary>Whether anything in it waits: a delay, an event control, a <c>wait</c>.</summary>
                public bool Waits;
                public bool Failed;

                /// <summary>Whether it forks (Part L5): its forks' branches, then those and every branch they fork in turn.</summary>
                public bool Forks;

                /// <inheritdoc cref="Forks"/>
                public readonly List<ProcessInfo> Branches = new List<ProcessInfo>();

                /// <inheritdoc cref="Forks"/>
                public readonly List<ProcessInfo> Spawned = new List<ProcessInfo>();

                /// <summary>Its named blocks and forks and the tasks written into it, for <c>disable</c>: given the process once it is made.</summary>
                public readonly List<BlockRange> Ranges = new List<BlockRange>();

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

            /// <summary>What each <c>disable</c> names, resolved once every process is compiled (<see cref="ResolveDisables"/>).</summary>
            private readonly List<(DisableSpec Spec, Scope Scope, JumpStatement Statement)> _disables = new List<(DisableSpec, Scope, JumpStatement)>();

            private List<ProcessInfo> CompileProcess(Scope scope, ProceduralBlock block)
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
                        if (p.Forks)
                        {
                            Error(block.Location, ElaborationCodes.NeverWaits,
                                $"An {kind} may not fork: it runs all at once whenever what it reads changes. Fork in an initial or an always.");
                            return null;
                        }

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

                LintLatches(scope, block);
                return Made(p, kind);
            }

            /// <summary>
            /// A compiled process, followed by its forks' branches, each before the branches of its own forks
            /// (Part L5): every one a process of the design, in the order written.
            /// </summary>
            private static List<ProcessInfo> Made(Proc p, string kind)
            {
                var writes = new List<int>(p.Writes);
                writes.Sort();
                var info = new ProcessInfo(p.Name, kind, p.Code.ToArray(), writes.ToArray());

                foreach (ProcessInfo branch in p.Branches)
                    branch.Parent = info;
                foreach (BlockRange range in p.Ranges)
                    range.Process = info;

                var all = new List<ProcessInfo> { info };
                all.AddRange(p.Spawned);
                return all;
            }

            /// <summary>
            /// What an <c>always_comb</c> or an <c>@*</c> waits on: every signal read in the
            /// instructions from <paramref name="from"/> to <paramref name="to"/>, except in wait and event
            /// expressions, except the process's own locals -- and for <c>always_comb</c>, except what it writes.
            /// </summary>
            private WaitSpec Sensitivity(Proc p, int from, int to, bool excludeWritten)
            {
                var reads = new SortedSet<int>();
                CollectReads(p, from, to, reads);

                // always_comb also waits on what the functions it calls read; @* only on their arguments
                // (IEEE 1800, 9.2.2.2.1).
                if (excludeWritten)
                    CollectFunctionReads(p, from, to, reads);

                reads.ExceptWith(p.Locals);
                if (excludeWritten)
                    reads.ExceptWith(p.Writes);

                var items = new List<EventItem>();
                foreach (int s in reads)
                    items.Add(new EventItem(EdgeKind.Any, _builder.Read(new SignalId(s))));

                return new WaitSpec(items.ToArray());
            }

            /// <summary>What the functions the instructions call read in their statements: their <see cref="FunctionInfo.BodyReads"/>.</summary>
            private static void CollectFunctionReads(Proc p, int from, int to, ISet<int> reads)
            {
                var calls = new HashSet<FunctionInfo>();
                for (int i = from; i < to; i++)
                {
                    Instruction ins = p.Code[i];
                    if (ins.Op == OpCode.WaitEvent || ins.Op == OpCode.WaitUntil)
                        continue;

                    ins.Value?.CollectCalls(calls);
                    ins.Other?.CollectCalls(calls);
                    if (ins.Target != null)
                    {
                        foreach (LPiece piece in ins.Target)
                        {
                            piece.Offset?.CollectCalls(calls);
                            piece.Word?.CollectCalls(calls);
                        }
                    }

                    if (ins.Print != null)
                        foreach (PrintSegment segment in ins.Print.Segments)
                            segment.Argument?.CollectCalls(calls);
                }

                foreach (FunctionInfo call in calls)
                    if (call.BodyReads != null)
                        foreach (int s in call.BodyReads)
                            reads.Add(s);
            }

            /// <summary>Every signal the instructions from <paramref name="from"/> to <paramref name="to"/> read, wait and event expressions left out.</summary>
            private static void CollectReads(Proc p, int from, int to, ISet<int> reads)
            {
                for (int i = from; i < to; i++)
                {
                    Instruction ins = p.Code[i];
                    if (ins.Op == OpCode.WaitEvent || ins.Op == OpCode.WaitUntil)
                        continue;

                    ins.Value?.CollectReads(reads);
                    ins.Other?.CollectReads(reads);
                    if (ins.Target != null)
                    {
                        foreach (LPiece piece in ins.Target)
                        {
                            piece.Offset?.CollectReads(reads);
                            piece.Word?.CollectReads(reads);
                        }
                    }

                    if (ins.Memory != null)
                    {
                        ins.Memory.Start?.CollectReads(reads);
                        ins.Memory.End?.CollectReads(reads);
                    }

                    if (ins.Print != null)
                        foreach (PrintSegment segment in ins.Print.Segments)
                            segment.Argument?.CollectReads(reads);
                }
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

                    case ForeachStatement f:
                        Foreach(p, scope, f);
                        return;

                    case ForkStatement f:
                        Fork(p, scope, f);
                        return;

                    case WaitForkStatement _:
                        p.Emit(OpCode.WaitFork);
                        p.Waits = true;
                        return;

                    case EventTriggerStatement t:
                        Trigger(p, scope, t);
                        return;

                    case AssertionStatement a:
                        Assertion(p, scope, a);
                        return;

                    case ForceStatement f:
                        Force(p, scope, f);
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
                        Call(p, scope, call);
                        return;

                    case JumpStatement j:
                        Jump(p, scope, j);
                        return;

                    case ReturnStatement r:
                        Return(p, scope, r);
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

            /// <summary>
            /// The blocks and forks with a name or declarations already compiled, by statement and by where:
            /// a task's are written into a caller again on every call, and take the name, the scope and the
            /// variables the first call made -- the task's statics -- rather than declaring them twice.
            /// </summary>
            private readonly Dictionary<(Statement, string), (InstanceEntry Entry, Scope Inner, List<int> Locals)> _blocks =
                new Dictionary<(Statement, string), (InstanceEntry, Scope, List<int>)>();

            /// <summary>A block or fork compiled before at the same place, made ready again; false the first time.</summary>
            private bool Again(Proc p, Scope scope, Statement statement, string label, out InstanceEntry entry, out Scope inner)
            {
                entry = null;
                inner = scope;
                if (!_blocks.TryGetValue((statement, scope.Path), out (InstanceEntry Entry, Scope Inner, List<int> Locals) again))
                    return false;

                entry = again.Entry;
                inner = again.Inner;
                p.Locals.UnionWith(again.Locals);
                if (entry != null && scope.Own(label) == null)
                    scope.TryDeclare(label, entry);
                return true;
            }

            /// <summary>Declares a block's or fork's own, the first time it is compiled at a place, and remembers them for the next.</summary>
            private void DeclareOwn(Proc p, Scope scope, Statement statement, InstanceEntry entry, Scope inner, List<ModuleItem> declarations, bool recorded)
            {
                var before = new HashSet<int>(p.Locals);
                foreach (ModuleItem declaration in declarations)
                    DeclareInBlock(p, inner, declaration, recorded);

                var locals = new List<int>();
                foreach (int local in p.Locals)
                    if (!before.Contains(local))
                        locals.Add(local);
                _blocks[(statement, scope.Path)] = (entry, inner, locals);
            }

            private void Block(Proc p, Scope scope, BlockStatement b)
            {
                Scope inner = scope;

                InstanceEntry entry = null;
                if ((b.Label != null || b.Declarations.Count > 0) && !Again(p, scope, b, b.Label, out entry, out inner))
                {
                    bool named = b.Label != null;
                    if (named)
                        entry = new InstanceEntry { Declared = b.Location, Ranges = new List<BlockRange>() };
                    if (named && !scope.TryDeclare(b.Label, entry))
                    {
                        Error(b.Location, ElaborationCodes.Duplicate, $"{b.Label} is the name of something else here already.");
                        p.Failed = true;
                        named = false;
                        entry = null;
                    }

                    inner = new Scope(b.Label != null ? scope.Path + "." + b.Label : scope.Path, scope, scope.Module);

                    // A named block is a scope of the hierarchy: what it declares is recorded under its name.
                    if (named)
                        inner.Index = _builder.Scope(inner.Path, null, ScopeKind.Block, scope.Recorded);

                    DeclareOwn(p, scope, b, entry, inner, b.Declarations, recorded: b.Label != null);
                }

                int start = p.Here;
                foreach (Statement statement in b.Statements)
                    Statement(p, inner, statement);

                // Where it is, for a disable of it.
                if (entry != null)
                {
                    var range = new BlockRange { Start = start, End = p.Here };
                    entry.Ranges.Add(range);
                    p.Ranges.Add(range);
                }
            }

            private void DeclareInBlock(Proc p, Scope scope, ModuleItem item, bool recorded)
            {
                switch (item)
                {
                    case DataDeclaration d when d.Type != null && d.Type.Keyword == "event":
                        NotYet(d.Location, "An event declared inside a process is not supported by BitSorter yet; declare it in the module.");
                        p.Failed = true;
                        return;

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
                                NotYet(declarator.Location, $"{declarator.Name} is a memory declared inside a process, which BitSorter does not support; declare it in the module.");
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
                if (!ExpressionBuilder.TryConstantFor(declarator.Initializer, shape.Type, scope, _diagnostics, shape.Width, out LogicVec given))
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
                var builder = Builder(p, scope);
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

                Expr built = Sized(builder, value, width, parts.Count == 1 ? builder.TypeAt(a.Target) : null);
                if (builder.Failed)
                {
                    p.Failed = true;
                    return;
                }

                LPiece[] target = Pieces(p, parts);

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

            /// <summary>What an assignment writes, piece by piece, each signal it may write counted as the process's.</summary>
            private static LPiece[] Pieces(Proc p, List<ExpressionBuilder.ProcPart> parts)
            {
                var target = new LPiece[parts.Count];
                for (int i = 0; i < parts.Count; i++)
                {
                    ExpressionBuilder.ProcPart part = parts[i];
                    int depth = part.Word != null ? part.Memory.Depth : 0;
                    target[i] = new LPiece(part.Signal.Signal.Index, part.Lowest, part.Width, part.Offset, part.Word, depth);

                    // A word picked as the process runs may be any of them.
                    for (int w = 0; w < Math.Max(1, depth); w++)
                        p.Writes.Add(part.Signal.Signal.Index + w);
                }

                return target;
            }

            /// <summary>
            /// An expression at a target's width: widened as its own sizing says, or cut to the low bits -- or a
            /// <c>'{...}</c> laid out on the target's <paramref name="type"/> (Part L4).
            /// </summary>
            private static Expr Sized(ExpressionBuilder builder, Expression value, int width, PackedType type = null) =>
                builder.BuildAssigned(value, type, width);

            /// <summary>An expression builder for a statement of this process: one that can call <c>$random</c> and its kin.</summary>
            private ExpressionBuilder Builder(Proc p, Scope scope) =>
                new ExpressionBuilder(scope, _diagnostics, constantOnly: false) { Random = call => RandomCall(p, scope, call), Opens = call => OpenCall(p, scope, call) };

            /// <summary>
            /// <c>$fopen(name [, mode])</c> (Part L6): the file opened just before the statement that asks, its
            /// descriptor written into a working variable the call then reads -- as <c>$random</c>'s number is.
            /// </summary>
            private Expr OpenCall(Proc p, Scope scope, CallExpression call)
            {
                if (call.Arguments.Count < 1 || call.Arguments.Count > 2 || call.Arguments.Exists(a => a == null))
                {
                    Error(call.Location, ElaborationCodes.BadCall, "$fopen takes the file's name, and then how to open it: \"w\" to write it afresh, \"a\" to add to it.");
                    p.Failed = true;
                    return Expr.Constant(LogicVec.AllX(32));
                }

                var builder = new ExpressionBuilder(scope, _diagnostics, constantOnly: false);
                Expr name = builder.Build(call.Arguments[0], 0);
                Expr mode = call.Arguments.Count == 2 ? builder.Build(call.Arguments[1], 0) : null;
                if (builder.Failed)
                {
                    p.Failed = true;
                    return Expr.Constant(LogicVec.AllX(32));
                }

                SignalId descriptor = _builder.Local($"{scope.Path}.$fopen#{++_unnamed}", 32, signed: true);
                p.Locals.Add(descriptor.Index);
                p.Writes.Add(descriptor.Index);
                p.Emit(new Instruction
                {
                    Op = OpCode.FileOpen,
                    Target = new[] { new LPiece(descriptor.Index, 0, 32, null) },
                    TargetWidth = 32,
                    Value = name,
                    Other = mode,
                });

                return _builder.Read(descriptor);
            }

            /// <summary>The seeds <c>$random</c> and <c>$urandom</c> use when they are given none: the design's own, from 0.</summary>
            private SignalId? _randomSeed, _urandomSeed;

            /// <summary>
            /// <c>$random(seed)</c>, <c>$urandom</c>, <c>$urandom_range(max, min)</c> (Part L3): the number into a
            /// working variable and the seed moved on, both worked out from the seed as it was and written just
            /// before the statement that asks -- so the call has no side effect of its own, and the seed's new
            /// value is recorded like any write. The call reads the working variable.
            /// </summary>
            private Expr RandomCall(Proc p, Scope scope, CallExpression call)
            {
                RandomKind kind = call.Name == "$random" ? RandomKind.Random
                    : call.Name == "$urandom" ? RandomKind.URandom
                    : RandomKind.URandomRange;
                var builder = Builder(p, scope);
                Expr low = null, high = null;
                Expression seedArgument = null;

                if (kind == RandomKind.URandomRange)
                {
                    if (call.Arguments.Count < 1 || call.Arguments.Count > 2 || call.Arguments.Exists(a => a == null))
                    {
                        Error(call.Location, ElaborationCodes.BadCall, "$urandom_range takes the largest value, and then the smallest if it is not 0.");
                        p.Failed = true;
                        return Expr.Constant(LogicVec.AllX(32));
                    }

                    high = builder.Build(call.Arguments[0], 32);
                    low = call.Arguments.Count == 2 ? builder.Build(call.Arguments[1], 32) : null;
                }
                else if (call.Arguments.Count == 1 && call.Arguments[0] != null)
                {
                    seedArgument = call.Arguments[0];
                }
                else if (call.Arguments.Count > 1)
                {
                    Error(call.Location, ElaborationCodes.BadCall, $"{call.Name} takes at most one argument, the seed variable.");
                    p.Failed = true;
                    return Expr.Constant(LogicVec.AllX(32));
                }

                LPiece[] seedTarget;
                Expr seed;
                if (seedArgument != null)
                {
                    // The seed must be a variable: the call writes it back.
                    var parts = new List<ExpressionBuilder.ProcPart>();
                    if (!builder.TryProceduralTarget(seedArgument, parts))
                    {
                        p.Failed = true;
                        return Expr.Constant(LogicVec.AllX(32));
                    }

                    seedTarget = Pieces(p, parts);
                    seed = builder.Build(seedArgument, 32);
                }
                else
                {
                    ref SignalId? own = ref kind == RandomKind.Random ? ref _randomSeed : ref _urandomSeed;
                    if (own == null)
                        own = _builder.Local(kind == RandomKind.Random ? "$random seed" : "$urandom seed", 32, signed: true, LogicVec.FromInt64(0, 32, signed: true));

                    seedTarget = new[] { new LPiece(own.Value.Index, 0, 32, null) };
                    seed = _builder.Read(own.Value);
                    p.Locals.Add(own.Value.Index);
                    p.Writes.Add(own.Value.Index);
                }

                if (builder.Failed)
                    p.Failed = true;

                SignalId number = _builder.Local($"{scope.Path}.{call.Name}#{++_unnamed}", 32, signed: kind == RandomKind.Random);
                p.Locals.Add(number.Index);
                p.Writes.Add(number.Index);

                int seedWidth = 0;
                foreach (LPiece piece in seedTarget)
                    seedWidth += piece.Width;

                p.Emit(new Instruction { Op = OpCode.Assign, Target = new[] { new LPiece(number.Index, 0, 32, null) }, TargetWidth = 32, Value = Expr.Random(seed, kind, low, high, nextSeed: false) });
                p.Emit(new Instruction { Op = OpCode.Assign, Target = seedTarget, TargetWidth = seedWidth, Value = Expr.Resize(Expr.Random(seed, kind, low, high, nextSeed: true), seedWidth, true) });

                return Expr.WithSignedness(_builder.Read(number), kind == RandomKind.Random);
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
                var builder = Builder(p, scope);
                Expr built = builder.BuildCondition(condition);
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
                var builder = Builder(p, scope);

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

                Expr subject = c.Inside ? null : builder.BuildAs(c.Subject, width, signed);
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
                        if (c.Inside)
                        {
                            // case inside: each label a member of an inside set, a range included (IEEE 1800, 12.5.4).
                            var inside = new InsideExpression { Location = label.Location, Value = c.Subject };
                            inside.Set.Add(label);
                            matches.Add((p.Emit(new Instruction { Op = OpCode.JumpIf, Value = builder.Build(inside, 0) }), item));
                            continue;
                        }

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

            /// <summary>
            /// <c>foreach (grid[r, c])</c> (Part L4): a loop for each dimension named, the first the outermost,
            /// each from its left bound to its right as declared -- <c>[7:0]</c> counts down -- with its own
            /// <c>int</c>. A place left empty, <c>grid[, c]</c>, walks no loop for its dimension.
            /// </summary>
            private void Foreach(Proc p, Scope scope, ForeachStatement f)
            {
                var builder = Builder(p, scope);
                if (!builder.TryLoopDimensions(f.Array, out List<(int Left, int Right)> dims))
                {
                    p.Failed = true;
                    return;
                }

                if (f.Variables.Count > dims.Count)
                {
                    Error(f.Location, ElaborationCodes.BadSelect,
                        $"This foreach names {f.Variables.Count} dimensions, and {Printer.Print(f.Array)} has {dims.Count}.");
                    p.Failed = true;
                    return;
                }

                var inner = new Scope(scope.Path, scope, scope.Module);
                if (!DeclareLoopVariables(p, inner, f))
                    return;

                Loops(p, inner, f, dims, 0);
            }

            private bool DeclareLoopVariables(Proc p, Scope inner, ForeachStatement f)
            {
                var integer = new Shape(31, 0, signed: true, null, twoState: true);
                foreach (string name in f.Variables)
                {
                    if (name == null)
                        continue;

                    SignalEntry variable = DeclareSignal(inner, name, f.Location, integer, SignalKind.Variable, null, local: true);
                    if (variable == null)
                    {
                        p.Failed = true;
                        return false;
                    }

                    p.Locals.Add(variable.Signal.Index);
                }

                return true;
            }

            /// <summary>The loops from dimension <paramref name="k"/> in, and the body inside the innermost.</summary>
            private void Loops(Proc p, Scope scope, ForeachStatement f, List<(int Left, int Right)> dims, int k)
            {
                while (k < f.Variables.Count && f.Variables[k] == null)
                    k++;

                if (k >= f.Variables.Count)
                {
                    Statement(p, scope, f.Body);
                    return;
                }

                (int left, int right) = dims[k];
                bool up = left <= right;
                var variable = new NameExpression { Location = f.Location };
                variable.Path.Add(f.Variables[k]);

                Assignment(p, scope, new AssignmentStatement { Location = f.Location, Target = variable, Operator = "=", Value = Number(left, f.Location) });

                int top = p.Here;
                var condition = new BinaryExpression { Location = f.Location, Operator = up ? "<=" : ">=", Left = variable, Right = Number(right, f.Location) };
                int exit = p.Emit(OpCode.JumpUnless, Condition(p, scope, condition));

                // break and continue belong to the innermost loop, as the loops a foreach stands for would take them.
                bool innermost = true;
                for (int j = k + 1; j < f.Variables.Count; j++)
                    innermost &= f.Variables[j] == null;

                var loop = new Loop();
                if (innermost)
                    p.Loops.Add(loop);
                Loops(p, scope, f, dims, k + 1);

                int next = p.Here;
                Assignment(p, scope, new AssignmentStatement { Location = f.Location, Target = variable, Operator = up ? "+=" : "-=", Value = Number(1, f.Location) });
                p.Emit(OpCode.Jump, jump: top);

                p.Patch(exit, p.Here);
                if (innermost)
                    CloseLoop(p, loop, next, p.Here);
            }

            private static NumberLiteral Number(long value, SourceLocation at) =>
                new NumberLiteral { Location = at, Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Value = LogicVec.FromInt64(value, 32, true) };

            /// <summary>
            /// <c>fork ... join</c> (IEEE 1800, 9.3.2; Part L5): each statement a process of its own, compiled
            /// here and started by the fork, which then waits for every one of them, for the first, or for none.
            /// What it declares is shared by its branches.
            /// </summary>
            private void Fork(Proc p, Scope scope, ForkStatement f)
            {
                p.Forks = true;
                Scope inner = scope;
                InstanceEntry entry = null;

                if ((f.Label != null || f.Declarations.Count > 0) && !Again(p, scope, f, f.Label, out entry, out inner))
                {
                    if (f.Label != null)
                    {
                        entry = new InstanceEntry { Declared = f.Location, Ranges = new List<BlockRange>() };
                        if (!scope.TryDeclare(f.Label, entry))
                        {
                            Error(f.Location, ElaborationCodes.Duplicate, $"{f.Label} is the name of something else here already.");
                            p.Failed = true;
                            entry = null;
                        }
                    }

                    inner = new Scope(f.Label != null ? scope.Path + "." + f.Label : scope.Path, scope, scope.Module);
                    if (entry != null)
                        inner.Index = _builder.Scope(inner.Path, null, ScopeKind.Block, scope.Recorded);

                    DeclareOwn(p, scope, f, entry, inner, f.Declarations, recorded: f.Label != null);
                }

                var branches = new ProcessInfo[f.Branches.Count];
                for (int i = 0; i < f.Branches.Count; i++)
                {
                    var branch = new Proc($"{p.Name}.fork@{f.Location.Line}[{i + 1}]");
                    Statement(branch, inner, f.Branches[i]);
                    branch.Emit(OpCode.End);
                    if (branch.Failed)
                    {
                        p.Failed = true;
                        continue;
                    }

                    List<ProcessInfo> made = Made(branch, "fork");
                    made[0].Dormant = true;
                    branches[i] = made[0];
                    p.Branches.Add(made[0]);
                    p.Spawned.AddRange(made);
                }

                if (p.Failed)
                    return;

                JoinKind join = f.Join == "join_any" ? JoinKind.Any : f.Join == "join_none" ? JoinKind.None : JoinKind.All;
                int at = p.Emit(new Instruction { Op = OpCode.Fork, Fork = new ForkSpec(branches, join) });
                if (join != JoinKind.None)
                    p.Waits = true;

                if (entry != null)
                {
                    var range = new BlockRange { Start = at, End = at + 1, Fork = true };
                    entry.Ranges.Add(range);
                    p.Ranges.Add(range);
                }
            }

            /// <summary>
            /// <c>-&gt; e</c> and <c>-&gt;&gt; e</c> (IEEE 1800, 15.5.1): the event's bit turned over, which wakes
            /// every <c>@(e)</c>, and the time noted for <c>e.triggered</c> -- at once, or in the NBA region.
            /// </summary>
            private void Trigger(Proc p, Scope scope, EventTriggerStatement t)
            {
                var builder = Builder(p, scope);
                var parts = new List<ExpressionBuilder.ProcPart>();
                if (!builder.TryProceduralTarget(t.Target, parts))
                {
                    p.Failed = true;
                    return;
                }

                if (parts.Count != 1 || !parts[0].Signal.IsEvent || parts[0].Offset != null || parts[0].Word != null)
                {
                    Error(t.Location, ElaborationCodes.BadLValue, $"{Printer.Print(t.Target)} is not an event: {(t.Nonblocking ? "->>" : "->")} triggers one, declared event e;.");
                    p.Failed = true;
                    return;
                }

                SignalEntry e = parts[0].Signal;
                OpCode op = t.Nonblocking ? OpCode.AssignLater : OpCode.Assign;
                p.Writes.Add(e.Signal.Index);
                p.Writes.Add(e.Triggered.Index);

                p.Emit(new Instruction
                {
                    Op = op,
                    Target = new[] { new LPiece(e.Signal.Index, 0, 1, null) },
                    TargetWidth = 1,
                    Value = Expr.Apply(UnaryOp.Not, _builder.Read(e.Signal)),
                });

                p.Emit(new Instruction
                {
                    Op = op,
                    Target = new[] { new LPiece(e.Triggered.Index, 0, 64, null) },
                    TargetWidth = 64,
                    Value = Expr.Apply(BinaryOp.Add, scope.Root.TimeSource(), Expr.Constant(LogicVec.FromUInt64(1, 64))),
                });
            }

            /// <summary>
            /// An immediate assertion (IEEE 1800, 16.3): its pass statement when the condition is 1, its else
            /// when it is 0, x or z -- an <c>$error</c> saying which, when it has none. A <c>cover</c> has no else.
            /// </summary>
            private void Assertion(Proc p, Scope scope, AssertionStatement a)
            {
                Expr condition = Condition(p, scope, a.Condition);
                int toFail = p.Emit(OpCode.JumpUnless, condition);
                Statement(p, scope, a.Pass);

                if (a.Keyword == "cover")
                {
                    p.Patch(toFail, p.Here);
                    return;
                }

                int toEnd = p.Emit(OpCode.Jump);
                p.Patch(toFail, p.Here);

                if (a.Fail != null)
                {
                    Statement(p, scope, a.Fail);
                }
                else
                {
                    string text = $"{(a.Label != null ? a.Label + ": " : "")}{a.Keyword} ({Printer.Print(a.Condition)}) failed ({a.Location.File}:{a.Location.Line})";
                    p.Emit(new Instruction
                    {
                        Op = OpCode.Print,
                        Print = new PrintSpec { Kind = PrintKind.Display, Severity = Severity.Error, Segments = new[] { new PrintSegment { Text = text } } },
                    });
                }

                p.Patch(toEnd, p.Here);
            }

            /// <summary>
            /// <c>force</c> and <c>release</c> (IEEE 1800, 10.6.2): a whole variable, or a wire or bits of one,
            /// given a value that overrides every driver and every write until it is released.
            /// </summary>
            private void Force(Proc p, Scope scope, ForceStatement f)
            {
                var builder = Builder(p, scope);
                var parts = new List<ExpressionBuilder.Part>();
                if (!builder.TryTarget(f.Target, parts, null))
                {
                    p.Failed = true;
                    return;
                }

                int width = 0;
                var pieces = new LPiece[parts.Count];
                for (int i = 0; i < parts.Count; i++)
                {
                    ExpressionBuilder.Part part = parts[i];
                    if (part.Signal.IsEvent || part.Signal.Kind == SignalKind.Variable && (part.Lowest != 0 || part.Width != part.Signal.Width))
                    {
                        Error(f.Target.Location, ElaborationCodes.BadLValue,
                            $"{f.Keyword} takes a whole variable, or a wire or bits of one, and {Printer.Print(f.Target)} is {(part.Signal.IsEvent ? "an event" : "bits of a variable")}.");
                        p.Failed = true;
                        return;
                    }

                    pieces[i] = new LPiece(part.Signal.Signal.Index, part.Lowest, part.Width, null);
                    width += part.Width;
                }

                Expr value = null;
                if (f.Value != null)
                {
                    value = Sized(builder, f.Value, width, parts.Count == 1 ? builder.TypeAt(f.Target) : null);
                    if (builder.Failed)
                    {
                        p.Failed = true;
                        return;
                    }
                }

                p.Emit(new Instruction { Op = f.Keyword == "force" ? OpCode.Force : OpCode.Release, Target = pieces, TargetWidth = width, Value = value });
            }

            /// <summary>
            /// What each <c>disable</c> names, once every process is compiled: a named block or fork -- here or
            /// in another process -- or a task, wherever it is written in. Every place of it goes in.
            /// </summary>
            private void ResolveDisables()
            {
                foreach ((DisableSpec spec, Scope scope, JumpStatement j) in _disables)
                {
                    switch (scope.Lookup(j.Target))
                    {
                        case InstanceEntry block when block.Ranges != null:
                            spec.Ranges = block.Ranges;
                            continue;

                        case InstanceEntry _:
                            Error(j.Location, ElaborationCodes.BadSelect, $"{j.Target} is an instance or a generate block; disable ends a named block, a fork or a task.");
                            continue;
                    }

                    SubroutineEntry routine = scope.LookupSubroutine(j.Target);
                    if (routine != null && routine.IsTask)
                    {
                        spec.Ranges = routine.Inlined;
                        continue;
                    }

                    Error(j.Location, ElaborationCodes.UnknownName, routine != null
                        ? $"{j.Target} is a function; disable ends a named block, a fork or a task."
                        : $"There is no named block, fork or task called {j.Target} here to disable.");
                }
            }

            private void Repeat(Proc p, Scope scope, RepeatStatement r)
            {
                var builder = Builder(p, scope);
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
                var builder = Builder(p, scope);
                builder.AllowEvents = true;   // @(e) waits on an event's bit
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

                var builder = Builder(p, scope);
                Expr ticks = builder.BuildDelay(delay.Values[0]);
                if (builder.Failed)
                    p.Failed = true;
                return ticks;
            }

            private void Call(Proc p, Scope scope, CallStatement call)
            {
                string where = $"{call.Location.File}:{call.Location.Line}";

                if (call.Target != null)
                {
                    MethodStatement(p, scope, call);
                    return;
                }

                switch (call.Name)
                {
                    case "$finish":
                        p.Emit(new Instruction { Op = OpCode.Finish, Text = $"{call.Name} ({where})" });
                        return;

                    case "$stop":
                        p.Emit(new Instruction { Op = OpCode.Stop, Text = $"{call.Name} ({where})" });
                        return;

                    case "$fatal":
                    {
                        // $fatal(finish_number, "message", ...): the number is how much to report, which BitSorter does not vary.
                        int first = call.Arguments.Count > 0 && !(call.Arguments[0] is StringLiteral) ? 1 : 0;
                        PrintSegment[] segments = PrintSegments(p, scope, call, first, 'd');
                        p.Emit(new Instruction
                        {
                            Op = OpCode.Print,
                            Print = new PrintSpec { Kind = PrintKind.Display, Severity = Severity.Error, Segments = Labelled(segments, call.Name) },
                        });
                        p.Emit(new Instruction { Op = OpCode.Finish, Text = $"{call.Name} ({where})" });
                        return;
                    }

                    case "$readmemh":
                    case "$readmemb":
                    case "$writememh":
                    case "$writememb":
                        MemoryCall(p, scope, call, where);
                        return;

                    case "$sformat":
                    case "$swrite":
                        FormatInto(p, scope, call);
                        return;

                    case "$fclose":
                    case "$fflush":
                    {
                        if (call.Name == "$fclose" ? call.Arguments.Count != 1 || call.Arguments[0] == null : call.Arguments.Count > 1)
                        {
                            Error(call.Location, ElaborationCodes.BadCall, call.Name == "$fclose" ? "$fclose takes the descriptor $fopen gave." : "$fflush takes a descriptor, or nothing for every file.");
                            p.Failed = true;
                            return;
                        }

                        var builder = Builder(p, scope);
                        Expr descriptor = call.Arguments.Count == 1 && call.Arguments[0] != null ? builder.Build(call.Arguments[0], 0) : null;
                        if (builder.Failed)
                            p.Failed = true;
                        p.Emit(new Instruction { Op = call.Name == "$fclose" ? OpCode.FileClose : OpCode.FileFlush, Value = descriptor });
                        return;
                    }

                    case "$fmonitor":
                    case "$fmonitorb":
                    case "$fmonitorh":
                    case "$fmonitoro":
                        NotYet(call.Location, $"{call.Name} is not supported by BitSorter yet; $fstrobe at each change, or $monitor to the transcript, does the same work.");
                        p.Failed = true;
                        return;
                }

                // $fdisplay(fd, ...), $fwrite and $fstrobe: a print task with the descriptor of where it goes first (Part L6).
                if (call.Name.StartsWith("$f", StringComparison.Ordinal) && PrintTasks.TryGetValue("$" + call.Name.Substring(2), out (PrintKind Kind, char Radix, Severity Severity) filed) &&
                    filed.Severity == Severity.Info && filed.Kind != PrintKind.Monitor && filed.Kind != PrintKind.MonitorOn && filed.Kind != PrintKind.MonitorOff)
                {
                    if (call.Arguments.Count < 1 || call.Arguments[0] == null)
                    {
                        Error(call.Location, ElaborationCodes.BadCall, $"{call.Name} takes the descriptor $fopen gave first, then what to write.");
                        p.Failed = true;
                        return;
                    }

                    var builder = Builder(p, scope);
                    Expr destination = builder.Build(call.Arguments[0], 0);
                    PrintSegment[] segments = builder.Segments(call.Arguments, 1, filed.Radix);
                    if (builder.Failed)
                        p.Failed = true;

                    p.Emit(new Instruction
                    {
                        Op = OpCode.Print,
                        Print = new PrintSpec { Kind = filed.Kind, Severity = Severity.Info, Segments = segments, Destination = destination },
                    });
                    return;
                }

                if (DumpTasks.Contains(call.Name))
                    return;

                if (PrintTasks.TryGetValue(call.Name, out (PrintKind Kind, char Radix, Severity Severity) task))
                {
                    PrintSegment[] segments = PrintSegments(p, scope, call, 0, task.Radix);
                    if (task.Kind == PrintKind.Display && task.Severity != Severity.Info || call.Name == "$info")
                        segments = Labelled(segments, call.Name);

                    p.Emit(new Instruction
                    {
                        Op = OpCode.Print,
                        Print = new PrintSpec { Kind = task.Kind, Severity = task.Severity, Segments = segments },
                    });
                    return;
                }

                if (SkippedTasks.Contains(call.Name))
                {
                    if (_ignoredTasks.Add(call.Name))
                        Warning(call.Location, ElaborationCodes.Ignored, $"{call.Name} changes how time is printed, which BitSorter does not do yet; it is skipped.");
                    return;
                }

                if (!call.IsSystem)
                {
                    SubroutineEntry routine = scope.LookupSubroutine(call.Package, call.Name);
                    if (routine != null)
                    {
                        Inline(p, scope, call, routine);
                        return;
                    }

                    Error(call.Location, ElaborationCodes.UnknownName,
                        $"There is no task or function called {call.Name} here. Is it declared in this module, and spelt the same?");
                    p.Failed = true;
                    return;
                }

                NotYet(call.Location, $"{call.Name} is not supported by BitSorter yet.");
                p.Failed = true;
            }

            /// <summary>An <c>$error</c> with no message still says something: the task's own name.</summary>
            private static PrintSegment[] Labelled(PrintSegment[] segments, string task) =>
                segments.Length > 0 ? segments : new[] { new PrintSegment { Text = task } };

            /// <summary>
            /// A print task's arguments, from <paramref name="first"/>, as the line's pieces: a string
            /// is a format for the arguments after it (IEEE 1800, 21.2.1); any other argument is written
            /// in <paramref name="radix"/> at its default width.
            /// </summary>
            private PrintSegment[] PrintSegments(Proc p, Scope scope, CallStatement call, int first, char radix)
            {
                var builder = Builder(p, scope);
                PrintSegment[] segments = builder.Segments(call.Arguments, first, radix);
                if (builder.Failed)
                    p.Failed = true;
                return segments;
            }

            /// <summary>
            /// A string's methods that change it, called as statements (IEEE 1800, 6.16; Part L6):
            /// <c>s.putc(i, c)</c>, and <c>s.itoa(n)</c>, <c>s.hextoa(n)</c>, <c>s.octtoa(n)</c> and <c>s.bintoa(n)</c>,
            /// which write a number into it in decimal, hex, octal or binary.
            /// </summary>
            private void MethodStatement(Proc p, Scope scope, CallStatement call)
            {
                var builder = Builder(p, scope);
                string written = $"{Printer.Print(call.Target)}.{call.Name}()";

                // A task or function of an instance (Part L8): bus.send(v), through an interface port, or dut.reset().
                SubroutineEntry routine = builder.InstanceSubroutine(call.Target, call.Name, call.Location, out bool onInstance);
                if (onInstance)
                {
                    if (routine == null)
                        p.Failed = true;
                    else
                        Inline(p, scope, call, routine);
                    return;
                }

                if (!builder.IsStringValued(call.Target))
                {
                    Error(call.Location, ElaborationCodes.BadCall, $"{written} gives a value, which a statement of its own would drop: assign it, or call it where the value is used.");
                    p.Failed = true;
                    return;
                }

                char radix = call.Name == "itoa" ? 'd' : call.Name == "hextoa" ? 'h' : call.Name == "octtoa" ? 'o' : call.Name == "bintoa" ? 'b' : '\0';
                int arguments = call.Name == "putc" ? 2 : radix != '\0' ? 1 : -1;
                if (arguments < 0)
                {
                    Error(call.Location, ElaborationCodes.BadCall, $"{written} gives a value, which a statement of its own would drop; the string methods that change a string are putc, itoa, hextoa, octtoa and bintoa.");
                    p.Failed = true;
                    return;
                }

                if (call.Arguments.Count != arguments || call.Arguments.Exists(a => a == null))
                {
                    Error(call.Location, ElaborationCodes.BadCall, $".{call.Name}() takes {arguments} argument{(arguments == 1 ? "" : "s")}.");
                    p.Failed = true;
                    return;
                }

                var parts = new List<ExpressionBuilder.ProcPart>();
                if (!builder.TryProceduralTarget(call.Target, parts))
                {
                    p.Failed = true;
                    return;
                }

                int width = 0;
                foreach (ExpressionBuilder.ProcPart part in parts)
                    width += part.Width;

                Expr value = call.Name == "putc"
                    ? Expr.StringCall(StringMethod.Putc, builder.Build(call.Target, 0), new[] { builder.Build(call.Arguments[0], 0), builder.Build(call.Arguments[1], 0) }, width)
                    : Expr.Format(new[] { new PrintSegment { Conversion = radix, Width = 0, Argument = builder.Build(call.Arguments[0], 0) } }, width);

                if (builder.Failed)
                {
                    p.Failed = true;
                    return;
                }

                p.Emit(new Instruction { Op = OpCode.Assign, Target = Pieces(p, parts), TargetWidth = width, Value = value });
            }

            /// <summary>
            /// <c>$sformat(s, "format", values...)</c> and <c>$swrite(s, values...)</c> (Part L6): the line
            /// <c>$display</c> would print, written into a variable -- a string, or bits wide enough -- instead.
            /// </summary>
            private void FormatInto(Proc p, Scope scope, CallStatement call)
            {
                if (call.Arguments.Count < 1 || call.Arguments[0] == null || call.Name == "$sformat" && (call.Arguments.Count < 2 || !(call.Arguments[1] is StringLiteral)))
                {
                    Error(call.Location, ElaborationCodes.BadCall, call.Name == "$sformat"
                        ? "$sformat takes the variable to write into, then a format in quotes and the values it writes."
                        : "$swrite takes the variable to write into, then what to write.");
                    p.Failed = true;
                    return;
                }

                var builder = Builder(p, scope);
                var parts = new List<ExpressionBuilder.ProcPart>();
                if (!builder.TryProceduralTarget(call.Arguments[0], parts))
                {
                    p.Failed = true;
                    return;
                }

                int width = 0;
                foreach (ExpressionBuilder.ProcPart part in parts)
                    width += part.Width;

                PrintSegment[] segments = builder.Segments(call.Arguments, 1, 'd');
                if (builder.Failed)
                {
                    p.Failed = true;
                    return;
                }

                p.Emit(new Instruction { Op = OpCode.Assign, Target = Pieces(p, parts), TargetWidth = width, Value = Expr.Format(segments, width) });
            }

            /// <summary><c>$readmemh("file", mem [, start [, end]])</c> and the others: the file and the memory are read here, the bounds as the design runs.</summary>
            private void MemoryCall(Proc p, Scope scope, CallStatement call, string where)
            {
                bool read = call.Name.StartsWith("$readmem", StringComparison.Ordinal);
                var builder = Builder(p, scope);
                List<Expression> args = call.Arguments;

                // The file is named in quotes, or by a parameter holding the name: #(.IMAGE("prog.hex")).
                string file = null;
                if (args.Count >= 2 && args.Count <= 4 && args[0] != null)
                {
                    var quiet = new DiagnosticBag();
                    if (args[0] is StringLiteral literal)
                        file = literal.Value;
                    else if (ExpressionBuilder.TryConstant(args[0], scope, quiet, out LogicVec named) && ExpressionBuilder.TryString(named, out string spelled) && spelled.Length > 0)
                        file = spelled;
                }

                if (file == null)
                {
                    Error(call.Location, ElaborationCodes.BadFormat,
                        $"{call.Name} takes a file name -- in quotes, or a parameter holding one -- and a memory, and then a first and a last word if you want them: {call.Name}(\"image.hex\", mem).");
                    p.Failed = true;
                    return;
                }

                MemoryEntry memory = builder.MemoryNamed(args[1]);
                Expr start = args.Count > 2 && args[2] != null ? builder.Build(args[2], 0) : null;
                Expr end = args.Count > 3 && args[3] != null ? builder.Build(args[3], 0) : null;

                if (memory == null || builder.Failed)
                {
                    p.Failed = true;
                    return;
                }

                if (memory.Dims.Length > 1)
                {
                    NotYet(args[1].Location, $"{call.Name} into a memory of more than one dimension is not supported by BitSorter yet; give it one.");
                    p.Failed = true;
                    return;
                }

                if (read)
                    for (int w = 0; w < memory.Memory.Depth; w++)
                        p.Writes.Add(memory.Memory.First.Index + w);

                p.Emit(new Instruction
                {
                    Op = read ? OpCode.ReadMemory : OpCode.WriteMemory,
                    Memory = new MemoryTask
                    {
                        Task = call.Name,
                        Memory = memory.Memory,
                        Hex = call.Name.EndsWith("h", StringComparison.Ordinal),
                        File = file,
                        Start = start,
                        End = end,
                        Where = where,
                    },
                });
            }

            private void Jump(Proc p, Scope scope, JumpStatement j)
            {
                if (j.Keyword == "disable")
                {
                    if (j.Target == "fork")
                    {
                        p.Emit(OpCode.DisableFork);
                        return;
                    }

                    // A block or a fork may be in a process compiled later, and a task is written into every
                    // process that calls it: what the name reaches is filled in once every process is compiled.
                    var spec = new DisableSpec { Name = j.Target };
                    p.Emit(new Instruction { Op = OpCode.Disable, Disable = spec });
                    _disables.Add((spec, scope, j));
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
