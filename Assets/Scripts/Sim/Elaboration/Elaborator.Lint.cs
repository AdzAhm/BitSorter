using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    public static partial class Elaborator
    {
        /// <summary>
        /// The first lint check: logic meant to be combinational that keeps a value -- a latch.
        /// </summary>
        /// <remarks>
        /// An <c>always_comb</c> (or <c>always @*</c>) that assigns a variable on some paths and not on
        /// others makes it hold its old value on those: a latch, whatever the keyword says. The check
        /// follows every path through <c>if</c> and <c>case</c> and warns about each variable assigned
        /// somewhere but not everywhere, with the way out -- a value at the top of the block, as the
        /// course's "defaults first" style gives one.
        ///
        /// It errs towards silence. A variable counts as assigned when any bit of it is; a
        /// <c>case</c> is complete with a <c>default</c>, as <c>unique</c> or <c>priority</c>, or when its
        /// labels name every value its subject can take; a <c>for</c> loop whose condition holds at its
        /// first value, a <c>repeat</c> of a constant count above zero, and a <c>do</c> loop run their
        /// body. Only <c>always_latch</c> is left alone: there a latch is what was asked for.
        /// </remarks>
        private sealed partial class Run
        {
            /// <summary>Every signal declared, in order: what the design's lint looks at (Part 14e).</summary>
            private readonly List<SignalEntry> _declared = new List<SignalEntry>();

            /// <summary>What lint has said, so a module placed many times says each thing once.</summary>
            private readonly HashSet<string> _linted = new HashSet<string>(StringComparer.Ordinal);

            private void Lint(SourceLocation at, string code, string message)
            {
                if (_linted.Add(at.File + ":" + at.Line + ":" + at.Column + ":" + code + ":" + message))
                    Warning(at, code, message);
            }

            /// <summary>
            /// The design's lint, once it is built (Part 14e), each at the signal's declaration: a signal read that nothing
            /// drives or sets (SV0402), one set that nothing reads (SV0403), and a net driven on the same bits by more than one
            /// driver that cannot all let go (SV0404). A port is left alone -- what is outside drives or reads it, and an input
            /// of an instance left unconnected is said where it is placed (SV0313) -- and so is a process's own working variable.
            /// **What the top sets is watched, not read**: a testbench's signals are on the waves, so SV0403 is only said inside
            /// an instance -- a block whose logic is worked out and thrown away.
            /// </summary>
            /// <remarks>
            /// What vlog warns of. Icarus warns of none of these with -Wall (Docs/oracle-notes.md, "Lint"); asked with
            /// -Wfloating-nets, it warns of every net with no driver, the top's own inputs included, where this says only
            /// a net or a variable that something reads.
            /// </remarks>
            private void LintDesign(Design design)
            {
                var reads = new HashSet<int>();
                var written = new HashSet<int>();
                var drivers = new Dictionary<int, List<DriverInfo>>();
                foreach (DriverInfo driver in design.Drivers)
                {
                    driver.CollectReads(reads);
                    written.Add(driver.Target.Index);
                    if (!drivers.TryGetValue(driver.Target.Index, out List<DriverInfo> on))
                        drivers[driver.Target.Index] = on = new List<DriverInfo>();
                    on.Add(driver);
                }
                foreach (ProcessInfo process in design.Processes)
                {
                    process.CollectAllReads(reads);
                    foreach (int w in process.Writes)
                        written.Add(w);
                }

                foreach (SignalEntry s in _declared)
                {
                    int id = s.Signal.Index;
                    if (id < 0 || id >= design.Signals.Count || s.IsEvent || s.Port.HasValue)
                        continue;
                    SignalInfo info = design.Signals[id];
                    if (info.Local || info.Memory != null)
                        continue;

                    string name = info.Name.Substring(info.Name.LastIndexOf('.') + 1);
                    bool read = reads.Contains(id), set = written.Contains(id) || info.Initial.HasValue;
                    if (read && !set)
                        Lint(s.Declared, ElaborationCodes.Undriven,
                            $"{name} is read, but nothing drives or sets it, so it reads {(s.Kind == SignalKind.Net ? "z" : "x")}.");
                    else if (set && !read && InsideAnInstance(design, info.Scope))
                        Lint(s.Declared, ElaborationCodes.NeverRead, $"{name} is set, but nothing reads it.");

                    if (drivers.TryGetValue(id, out List<DriverInfo> on) && on.Count > 1 && Overlapping(on, out int count) && !AllLetGo(on))
                        Lint(s.Declared, ElaborationCodes.SeveralDrivers,
                            $"{name} has {count} drivers on the same bits, and not all of them can let go, so where they disagree it reads x. " +
                            "A bus's drivers are tri-state: bufif1, or a value that is z when it is not driving.");
                }
            }

            /// <summary>
            /// Whether <paramref name="scope"/> is inside an instance of a module -- not the top, a block of the top's own, nor
            /// an interface, whose signals are there for what is outside it to use, as a port's are.
            /// </summary>
            private bool InsideAnInstance(Design design, int scope)
            {
                while (scope >= 0 && scope < design.Scopes.Count)
                {
                    ScopeInfo here = design.Scopes[scope];
                    if (here.Kind == ScopeKind.Instance)
                        return !_interfaces.ContainsKey(here.Module);
                    if (here.Kind == ScopeKind.Top)
                        return false;
                    scope = here.Parent;
                }
                return false;
            }

            /// <summary>Whether two of <paramref name="on"/> drive a bit in common; how many drive the most-driven bit.</summary>
            private static bool Overlapping(List<DriverInfo> on, out int count)
            {
                count = 0;
                for (int i = 0; i < on.Count; i++)
                {
                    int here = 1;
                    for (int j = 0; j < on.Count; j++)
                        if (i != j && on[i].TargetLowest < on[j].TargetLowest + on[j].TargetWidth && on[j].TargetLowest < on[i].TargetLowest + on[i].TargetWidth)
                            here++;
                    count = Math.Max(count, here);
                }
                return count > 1;
            }

            /// <summary>Whether every one of <paramref name="on"/> can let go: a tri-state gate, or an assignment of a value that can be z.</summary>
            private static bool AllLetGo(List<DriverInfo> on)
            {
                foreach (DriverInfo driver in on)
                {
                    bool floats = driver is GateDriver gate
                        ? gate.Kind == GateKind.Bufif0 || gate.Kind == GateKind.Bufif1 || gate.Kind == GateKind.Notif0 || gate.Kind == GateKind.Notif1
                        : driver is AssignDriver assign && assign.Value.CanFloat;
                    if (!floats)
                        return false;
                }
                return true;
            }

            /// <summary>
            /// A plain value -- a name, a select of one, a concatenation of such -- given to something narrower (SV0405): its top
            /// bits are dropped, which is seldom meant. Anything with an operator is sized by the rules of its context, and a
            /// number fits or is cut as written, so neither is said. Nor is an int or a real: a number given to a few bits --
            /// <c>n = i</c> in a loop -- is how a testbench walks its values, and a real is converted, not cut.
            /// </summary>
            private void LintTruncation(ExpressionBuilder.TypeInfo type, Expression target, Expression value, int width, SourceLocation at)
            {
                int from = type.Width;
                if (from <= width || !IsPlain(value) || type.Real || type.Signed && from == 32)
                    return;
                int lost = from - width;
                Lint(at, ElaborationCodes.Truncated,
                    $"{Printer.Print(target)} is {width} bit{(width == 1 ? "" : "s")}, and {Printer.Print(value)} is {from}: its top {lost} bit{(lost == 1 ? " is" : "s are")} dropped.");
            }

            /// <summary>Whether <paramref name="value"/> is only names and selects of them, concatenated or not.</summary>
            private static bool IsPlain(Expression value)
            {
                switch (value)
                {
                    case ParenthesizedExpression p: return IsPlain(p.Inner);
                    case NameExpression _: return true;
                    case IndexExpression index: return IsPlain(index.Target);
                    case RangeSelectExpression range: return IsPlain(range.Target);
                    case MemberExpression member: return IsPlain(member.Target);
                    case ConcatenationExpression concat:
                        foreach (Expression item in concat.Items)
                            if (!IsPlain(item))
                                return false;
                        return concat.Items.Count > 0;
                    default: return false;
                }
            }

            private readonly struct Paths
            {
                public Paths(HashSet<string> always, HashSet<string> some)
                {
                    Always = always;
                    Some = some;
                }

                /// <summary>Assigned on every path.</summary>
                public readonly HashSet<string> Always;

                /// <summary>Assigned on at least one.</summary>
                public readonly HashSet<string> Some;

                public static Paths None => new Paths(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
            }

            private void LintLatches(Scope scope, ProceduralBlock block)
            {
                Statement body = block.Body;

                if (block.Kind == ProcessKind.Always)
                {
                    // always @* is combinational by intent; an always with a list may be anything.
                    if (!(body is TimedStatement timed && timed.Control is EventControl events && events.Implicit))
                        return;
                    body = timed.Body;
                }
                else if (block.Kind != ProcessKind.AlwaysComb)
                {
                    return;
                }

                Paths paths = Walk(body, scope);
                var declared = new HashSet<string>(StringComparer.Ordinal);
                Declared(body, declared);

                var latches = new List<string>();
                foreach (string name in paths.Some)
                    if (!paths.Always.Contains(name) && !declared.Contains(name))
                        latches.Add(name);

                if (latches.Count == 0)
                    return;

                latches.Sort(StringComparer.Ordinal);
                string names = latches.Count == 1 ? latches[0] : string.Join(", ", latches.GetRange(0, latches.Count - 1)) + " and " + latches[latches.Count - 1];
                string kind = block.Kind == ProcessKind.AlwaysComb ? "always_comb" : "always @*";

                Warning(block.Location, ElaborationCodes.LatchProne,
                    $"This {kind} assigns {names} on some paths and not on others, so {(latches.Count == 1 ? "it keeps its" : "they keep their")} " +
                    "old value on those: a latch, not combinational logic. Give " + (latches.Count == 1 ? "it" : "each") +
                    " a value at the top of the block, before the if or case.");
            }

            private Paths Walk(Statement statement, Scope scope)
            {
                switch (statement)
                {
                    case BlockStatement b:
                    {
                        Paths all = Paths.None;
                        foreach (Statement s in b.Statements)
                        {
                            Paths p = Walk(s, scope);
                            all.Always.UnionWith(p.Always);
                            all.Some.UnionWith(p.Some);
                        }

                        return all;
                    }

                    case AssignmentStatement a:
                        return Assigns(a.Target);

                    case IncrementStatement i:
                        return Assigns(i.Target);

                    case IfStatement i:
                    {
                        Paths then = Walk(i.Then, scope);
                        Paths otherwise = i.Else != null ? Walk(i.Else, scope) : Paths.None;
                        then.Always.IntersectWith(otherwise.Always);
                        then.Some.UnionWith(otherwise.Some);
                        return then;
                    }

                    case CaseStatement c:
                    {
                        Paths result = Paths.None;
                        HashSet<string> everywhere = null;

                        foreach (CaseItem item in c.Items)
                        {
                            Paths p = Walk(item.Body, scope);
                            result.Some.UnionWith(p.Some);
                            if (everywhere == null)
                                everywhere = new HashSet<string>(p.Always, StringComparer.Ordinal);
                            else
                                everywhere.IntersectWith(p.Always);
                        }

                        if (everywhere != null && IsComplete(c, scope))
                            result.Always.UnionWith(everywhere);

                        return result;
                    }

                    case ForStatement f:
                    {
                        Paths body = Walk(f.Body, scope);
                        return RunsAtLeastOnce(f, scope) ? body : new Paths(new HashSet<string>(StringComparer.Ordinal), body.Some);
                    }

                    case ForeachStatement f:
                        return Walk(f.Body, scope);   // every dimension has at least one index, so the body runs

                    case AssertionStatement a:
                    {
                        // As an if: the pass statement on one path, the else on the other.
                        Paths pass = Walk(a.Pass, scope);
                        Paths fail = a.Fail != null ? Walk(a.Fail, scope) : Paths.None;
                        pass.Always.IntersectWith(fail.Always);
                        pass.Some.UnionWith(fail.Some);
                        return pass;
                    }

                    case RepeatStatement r:
                    {
                        Paths body = Walk(r.Body, scope);
                        var quiet = new DiagnosticBag();
                        bool runs = ExpressionBuilder.TryInteger(r.Count, scope, quiet, "a count", out long count) && count > 0;
                        return runs ? body : new Paths(new HashSet<string>(StringComparer.Ordinal), body.Some);
                    }

                    case WhileStatement w:
                    {
                        Paths body = Walk(w.Body, scope);
                        return w.IsDoWhile ? body : new Paths(new HashSet<string>(StringComparer.Ordinal), body.Some);
                    }

                    case TimedStatement t:
                        return Walk(t.Body, scope);

                    default:
                        return Paths.None;
                }
            }

            private static Paths Assigns(Expression target)
            {
                Paths p = Paths.None;
                TargetNames(target, p.Some);
                p.Always.UnionWith(p.Some);
                return p;
            }

            private static void TargetNames(Expression target, HashSet<string> into)
            {
                switch (target)
                {
                    case NameExpression n:
                        // A member of a struct, pkt.kind, by its whole name: each member is assigned or not.
                        into.Add(n.Path.Count == 1 ? n.Name : string.Join(".", n.Path));
                        break;
                    case MemberExpression m:
                        TargetNames(m.Target, into);
                        break;
                    case IndexExpression i:
                        TargetNames(i.Target, into);
                        break;
                    case RangeSelectExpression r:
                        TargetNames(r.Target, into);
                        break;
                    case ConcatenationExpression c:
                        foreach (Expression item in c.Items)
                            TargetNames(item, into);
                        break;
                    case ParenthesizedExpression p:
                        TargetNames(p.Inner, into);
                        break;
                }
            }

            /// <summary>The names declared inside the block, which belong to it and are not latches of the module's.</summary>
            private static void Declared(Statement statement, HashSet<string> into)
            {
                switch (statement)
                {
                    case BlockStatement b:
                        foreach (ModuleItem item in b.Declarations)
                            if (item is DataDeclaration d)
                                foreach (Declarator declarator in d.Declarators)
                                    into.Add(declarator.Name);
                        foreach (Statement s in b.Statements)
                            Declared(s, into);
                        break;
                    case IfStatement i:
                        Declared(i.Then, into);
                        Declared(i.Else, into);
                        break;
                    case CaseStatement c:
                        foreach (CaseItem item in c.Items)
                            Declared(item.Body, into);
                        break;
                    case ForStatement f:
                        foreach (ForInitializer init in f.Initializers)
                            if (init.Type != null && init.Target is NameExpression n)
                                into.Add(n.Name);
                        Declared(f.Body, into);
                        break;
                    case ForeachStatement f:
                        foreach (string name in f.Variables)
                            if (name != null)
                                into.Add(name);
                        Declared(f.Body, into);
                        break;
                    case RepeatStatement r:
                        Declared(r.Body, into);
                        break;
                    case WhileStatement w:
                        Declared(w.Body, into);
                        break;
                    case TimedStatement t:
                        Declared(t.Body, into);
                        break;
                }
            }

            /// <summary>
            /// Whether a <c>case</c> always takes one of its items: it has a <c>default</c>, it is
            /// <c>unique</c> or <c>priority</c> (the writer says it is complete), or its labels are
            /// known numbers that name every value of its subject.
            /// </summary>
            private static bool IsComplete(CaseStatement c, Scope scope)
            {
                if (c.Qualifier == "unique" || c.Qualifier == "priority")
                    return true;

                var labels = new List<Expression>();
                foreach (CaseItem item in c.Items)
                {
                    if (item.IsDefault)
                        return true;
                    labels.AddRange(item.Labels);
                }

                var quiet = new DiagnosticBag();
                int width = new ExpressionBuilder(scope, quiet, constantOnly: false).TypeOf(c.Subject).Width;
                if (width > 16)
                    return false;

                var values = new HashSet<ulong>();
                foreach (Expression label in labels)
                {
                    if (!ExpressionBuilder.TryConstant(label, scope, quiet, out LogicVec value) || !value.IsKnown)
                        return false;

                    if (value.Width > width && LogicVec.Truth(value.Slice(width, value.Width - width)) != Logic4.Zero)
                        continue;   // wider than the subject can be: it never matches

                    value.Slice(0, Math.Min(width, value.Width)).TryGetUInt64(out ulong bits);
                    values.Add(bits);
                }

                return values.Count == 1 << width;
            }

            /// <summary>Whether a <c>for</c> loop's condition holds at its first value, so its body runs at least once.</summary>
            private static bool RunsAtLeastOnce(ForStatement f, Scope scope)
            {
                if (f.Condition == null)
                    return true;

                var first = new Scope(scope.Path, scope, scope.Module);
                var quiet = new DiagnosticBag();

                foreach (ForInitializer init in f.Initializers)
                {
                    if (!(init.Target is NameExpression name) || !ExpressionBuilder.TryConstant(init.Value, scope, quiet, out LogicVec value))
                        return false;
                    first.Set(name.Name, new ConstantEntry { Value = value });
                }

                return ExpressionBuilder.TryConstant(f.Condition, first, quiet, out LogicVec holds) && LogicVec.Truth(holds) == Logic4.One;
            }
        }
    }
}
