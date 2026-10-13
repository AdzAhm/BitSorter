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
                        into.Add(n.Name);
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
