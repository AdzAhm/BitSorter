using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Sim
{
    /// <summary>
    /// Turns a syntax tree's expression into an engine <see cref="Expr"/>, sized and signed as the
    /// standard sizes it (IEEE 1800, 11.6 and 11.8), or into a constant when only constants may appear.
    /// </summary>
    /// <remarks>
    /// **Sizing works in two passes**, as the standard describes it. First every node's own width and
    /// signedness, bottom up (<see cref="TypeOf"/>). Then the width the context needs is pushed down
    /// into every operand the operator's width depends on -- arithmetic, bitwise, the left of a shift,
    /// both choices of <c>?:</c> -- which are extended to it before the operator runs, sign-extended only
    /// when the whole expression is signed. Everything else is self-determined: a comparison's operands
    /// (sized only against each other), a shift amount, a condition, the items of a concatenation, and
    /// the operand of a reduction or <c>!</c>. That is what makes <c>{carry, sum} = a + b</c> keep its
    /// carry: the addition is done at two bits, because its context is two bits wide.
    ///
    /// **A constant is the same expression, evaluated.** In constant mode a signal is refused, and the
    /// built tree is evaluated with no signal values at all -- so a parameter's arithmetic follows exactly
    /// the rules a circuit's does.
    /// </remarks>
    internal sealed class ExpressionBuilder
    {
        private readonly Scope _scope;
        private readonly DiagnosticBag _diagnostics;
        private readonly bool _constant;

        public ExpressionBuilder(Scope scope, DiagnosticBag diagnostics, bool constantOnly)
        {
            _scope = scope;
            _diagnostics = diagnostics;
            _constant = constantOnly;
        }

        /// <summary>Whether building reported an error.</summary>
        public bool Failed { get; private set; }

        /// <summary>
        /// How a <c>$random</c>, <c>$urandom</c> or <c>$urandom_range</c> is called: set by a process, which
        /// writes the number and the seed's next value just before the statement that asks for it (Part L3).
        /// Null everywhere else, where such a call is refused.
        /// </summary>
        public Func<CallExpression, Expr> Random { get; set; }

        /// <summary>Whether a named event may be read: in an event control, <c>@(e)</c>, which waits on its bit (Part L5).</summary>
        public bool AllowEvents { get; set; }

        public readonly struct TypeInfo
        {
            public readonly int Width;
            public readonly bool Signed;

            public TypeInfo(int width, bool signed)
            {
                Width = Math.Max(1, width);
                Signed = signed;
            }
        }

        private void Error(SourceLocation at, string code, string message)
        {
            Failed = true;
            _diagnostics.Error(at, code, message);
        }

        // -----------------------------------------------------------------
        // Entry points
        // -----------------------------------------------------------------

        /// <summary>The expression, at least <paramref name="contextWidth"/> wide.</summary>
        public Expr Build(Expression expression, int contextWidth)
        {
            TypeInfo type = TypeOf(expression);
            return BuildAt(expression, Math.Max(type.Width, contextWidth), type.Signed);
        }

        /// <summary>
        /// The expression as an operand of something <paramref name="width"/> wide and of signedness
        /// <paramref name="signed"/>: how a <c>case</c> sizes its subject and labels together.
        /// </summary>
        public Expr BuildAs(Expression expression, int width, bool signed) =>
            BuildAt(expression, Math.Max(width, TypeOf(expression).Width), signed);

        /// <summary>A constant expression's value, or false having reported why it is not one.</summary>
        public static bool TryConstant(Expression expression, Scope scope, DiagnosticBag diagnostics, out LogicVec value, int contextWidth = 0)
        {
            var builder = new ExpressionBuilder(scope, diagnostics, constantOnly: true);
            Expr built = builder.Build(expression, contextWidth);

            if (builder.Failed)
            {
                value = default;
                return false;
            }

            value = built.Evaluate(Array.Empty<LogicVec>());
            return true;
        }

        /// <summary>A constant that must be a known whole number: a delay, a replication count, a range bound.</summary>
        public static bool TryInteger(Expression expression, Scope scope, DiagnosticBag diagnostics, string what, out long result)
        {
            result = 0;
            if (!TryConstant(expression, scope, diagnostics, out LogicVec value))
                return false;

            if (!value.IsKnown)
            {
                diagnostics.Error(expression.Location, ElaborationCodes.NotConstant, $"{what} is {Printer.Print(expression)}, which has x or z bits; it must be a known number.");
                return false;
            }

            if (value.IsSigned ? value.TryGetInt64(out result)
                : value.TryGetUInt64(out ulong unsigned) && unsigned <= long.MaxValue && (result = (long)unsigned) >= 0)
                return true;

            diagnostics.Error(expression.Location, ElaborationCodes.TooLarge, $"{what} is {Printer.Print(expression)}, which is too large.");
            return false;
        }

        // -----------------------------------------------------------------
        // Pass 1: each node's own width and signedness
        // -----------------------------------------------------------------

        public TypeInfo TypeOf(Expression e)
        {
            switch (e)
            {
                case NumberLiteral n:
                    return n.FillsContext ? new TypeInfo(1, false) : new TypeInfo(n.Value.Width, n.Value.IsSigned);

                case NameExpression _:
                case MemberExpression _:
                    switch (Resolve(e, report: false))
                    {
                        case SignalEntry s: return new TypeInfo(s.Width, s.Signed);
                        case ConstantEntry c: return new TypeInfo(c.Value.Width, c.Value.IsSigned);
                        case GenvarEntry _: return new TypeInfo(32, true);
                        case MemoryEntry m: return new TypeInfo(m.Memory.Width, m.Memory.Signed);
                        case null when TypeAt(e) is PackedType member: return new TypeInfo(member.Width, member.Signed);
                        default: return new TypeInfo(1, false);
                    }

                case IndexExpression index:
                    return TypeAt(index) is PackedType element ? new TypeInfo(element.Width, element.Signed) : new TypeInfo(1, false);

                case RangeSelectExpression r:
                    return new TypeInfo((int)Math.Min(LogicVec.MaxWidth, (long)SelectWidth(r) * (TypeAt(r.Target)?.ElementWidth ?? 1)), false);

                case ConcatenationExpression c:
                {
                    int width = 0;
                    foreach (Expression item in c.Items)
                        width += TypeOf(item).Width;
                    return new TypeInfo(width, false);
                }

                case ReplicationExpression r:
                {
                    long count = ConstantCount(r, report: false);
                    int inner = TypeOf(r.Value).Width;
                    return new TypeInfo((int)Math.Max(1, Math.Min(LogicVec.MaxWidth, count * inner)), false);
                }

                case UnaryExpression u:
                    return u.Operator == "+" || u.Operator == "-" || u.Operator == "~" ? TypeOf(u.Operand) : new TypeInfo(1, false);

                case BinaryExpression b:
                {
                    TypeInfo l = TypeOf(b.Left), r = TypeOf(b.Right);
                    switch (b.Operator)
                    {
                        case "+": case "-": case "*": case "/": case "%":
                        case "&": case "|": case "^": case "~^": case "^~": case "~&": case "~|":
                            return new TypeInfo(Math.Max(l.Width, r.Width), l.Signed && r.Signed);
                        case "<<": case ">>": case "<<<": case ">>>": case "**":
                            return new TypeInfo(l.Width, l.Signed);
                        default:
                            return new TypeInfo(1, false);
                    }
                }

                case ConditionalExpression c:
                {
                    TypeInfo t = TypeOf(c.WhenTrue), f = TypeOf(c.WhenFalse);
                    return new TypeInfo(Math.Max(t.Width, f.Width), t.Signed && f.Signed);
                }

                case ParenthesizedExpression p:
                    return TypeOf(p.Inner);

                case CallExpression call:
                    switch (call.Name)
                    {
                        case "$signed": return call.Arguments.Count == 1 && call.Arguments[0] != null ? new TypeInfo(TypeOf(call.Arguments[0]).Width, true) : new TypeInfo(1, true);
                        case "$unsigned": return call.Arguments.Count == 1 && call.Arguments[0] != null ? new TypeInfo(TypeOf(call.Arguments[0]).Width, false) : new TypeInfo(1, false);
                        case "$clog2": case "$bits": return new TypeInfo(32, true);
                        case "$time": case "$realtime": return new TypeInfo(64, false);
                        case "$stime": return new TypeInfo(32, false);
                        case "$countones": case "$random": return new TypeInfo(32, true);
                        case "$onehot": case "$onehot0": case "$isunknown": return new TypeInfo(1, false);
                        case "$urandom": case "$urandom_range": return new TypeInfo(32, false);
                        default:
                            // A function written in the design: its return type.
                            return !call.Name.StartsWith("$", StringComparison.Ordinal) && _scope.LookupSubroutine(call.Name) is SubroutineEntry routine && !routine.IsVoid
                                ? new TypeInfo(routine.Width, routine.Signed)
                                : new TypeInfo(1, false);
                    }

                case CastExpression cast:
                {
                    TypeInfo inner = TypeOf(cast.Value);
                    if (cast.Width != null)
                        return new TypeInfo((int)Math.Max(1, Math.Min(LogicVec.MaxWidth, ConstantOf(cast.Width, report: false))), inner.Signed);
                    if (cast.TargetText == "signed") return new TypeInfo(inner.Width, true);
                    if (cast.TargetText == "unsigned") return new TypeInfo(inner.Width, false);
                    if (KeywordType(cast.TargetText, out int width, out bool signed, out bool _)) return new TypeInfo(width, signed);
                    if (_scope.Lookup(cast.TargetText) is TypeEntry type) return new TypeInfo(type.Width, type.Signed);
                    return inner;
                }

                case InsideExpression _:
                    return new TypeInfo(1, false);

                case MethodCallExpression m:
                    if (TryEnumOf(m.Target, out EnumInfo @enum, out bool _))
                    {
                        switch (m.Method)
                        {
                            case "name": return new TypeInfo(NameWidth(@enum), false);
                            case "num": return new TypeInfo(32, true);
                            default: return new TypeInfo(@enum.Width, TypeOf(m.Target).Signed);
                        }
                    }

                    return new TypeInfo(1, false);

                case StringLiteral s:
                    return new TypeInfo(Math.Max(8, s.Value.Length * 8), false);

                default:
                    return new TypeInfo(1, false);
            }
        }

        private int SelectWidth(RangeSelectExpression r)
        {
            if (r.Kind == RangeKind.Constant)
            {
                long left = ConstantOf(r.Left, report: false), right = ConstantOf(r.Right, report: false);
                return (int)Math.Max(1, Math.Min(LogicVec.MaxWidth, Math.Abs(left - right) + 1));
            }

            return (int)Math.Max(1, Math.Min(LogicVec.MaxWidth, ConstantOf(r.Right, report: false)));
        }

        private long ConstantOf(Expression e, bool report)
        {
            var quiet = new DiagnosticBag();
            return TryInteger(e, _scope, report ? _diagnostics : quiet, "this value", out long v) ? v : 0;
        }

        private long ConstantCount(ReplicationExpression r, bool report)
        {
            var quiet = new DiagnosticBag();
            if (!TryInteger(r.Count, _scope, report ? _diagnostics : quiet, "a replication count", out long count))
            {
                if (report) Failed = true;
                return 1;
            }

            return count;
        }

        // -----------------------------------------------------------------
        // Pass 2: building at a width
        // -----------------------------------------------------------------

        /// <summary>
        /// <paramref name="e"/> built as a context-determined operand: <paramref name="width"/> wide,
        /// extended with the sign when <paramref name="signed"/> says the whole expression is signed.
        /// </summary>
        private Expr BuildAt(Expression e, int width, bool signed)
        {
            switch (e)
            {
                case ParenthesizedExpression p:
                    return BuildAt(p.Inner, width, signed);

                case NumberLiteral n when n.FillsContext:
                    return Expr.Constant(LogicVec.Fill(n.Value[0], width));

                case UnaryExpression u when u.Operator == "+" || u.Operator == "-" || u.Operator == "~":
                {
                    Expr operand = BuildAt(u.Operand, width, signed);
                    if (u.Operator == "+") return operand;
                    return Expr.Apply(u.Operator == "-" ? UnaryOp.Negate : UnaryOp.Not, operand);
                }

                case BinaryExpression b:
                    return BuildBinary(b, width, signed);

                case ConditionalExpression c:
                {
                    Expr condition = BuildSelf(c.Condition);
                    return Expr.Conditional(condition, BuildAt(c.WhenTrue, width, signed), BuildAt(c.WhenFalse, width, signed));
                }

                default:
                {
                    // An operand of an unsigned expression is read as unsigned, even a signed one:
                    // (s1 / s2) < u divides without the sign (IEEE 1800, 11.8.1).
                    Expr self = BuildSelf(e);
                    if (!signed && TypeOf(e).Signed)
                        self = Expr.WithSignedness(self, false);
                    return Extend(self, width, signed);
                }
            }
        }

        /// <summary>A self-determined operand: at its own width and signedness.</summary>
        private Expr BuildSelf(Expression e)
        {
            switch (e)
            {
                case NumberLiteral n:
                    return Expr.Constant(n.FillsContext ? LogicVec.Fill(n.Value[0], 1) : n.Value);

                case NameExpression name:
                    return BuildNamed(name);

                case IndexExpression index:
                    return BuildIndex(index);

                case RangeSelectExpression range:
                    return BuildRange(range);

                case ConcatenationExpression c:
                    return BuildConcatenation(c);

                case ReplicationExpression r:
                {
                    long count = ConstantCount(r, report: true);
                    if (count < 1)
                    {
                        Error(r.Location, ElaborationCodes.BadSelect, $"A replication count must be at least 1, and this one is {count}.");
                        return Expr.Constant(LogicVec.AllX(1));
                    }

                    Expr inner = BuildConcatenation(r.Value);
                    if (count * inner.Width > LogicVec.MaxWidth)
                    {
                        Error(r.Location, ElaborationCodes.TooLarge, $"This replication is {count * inner.Width} bits wide, more than BitSorter allows ({LogicVec.MaxWidth}).");
                        return Expr.Constant(LogicVec.AllX(1));
                    }

                    return Expr.Replicate(inner, (int)count);
                }

                case UnaryExpression u:
                {
                    switch (u.Operator)
                    {
                        case "!": return Expr.Apply(UnaryOp.LogicalNot, BuildSelf(u.Operand));
                        case "&": return Expr.Apply(UnaryOp.ReduceAnd, BuildSelf(u.Operand));
                        case "|": return Expr.Apply(UnaryOp.ReduceOr, BuildSelf(u.Operand));
                        case "^": return Expr.Apply(UnaryOp.ReduceXor, BuildSelf(u.Operand));
                        case "~&": return Expr.Apply(UnaryOp.ReduceNand, BuildSelf(u.Operand));
                        case "~|": return Expr.Apply(UnaryOp.ReduceNor, BuildSelf(u.Operand));
                        case "~^": case "^~": return Expr.Apply(UnaryOp.ReduceXnor, BuildSelf(u.Operand));
                        default:
                        {
                            TypeInfo t = TypeOf(u);
                            return BuildAt(u, t.Width, t.Signed);
                        }
                    }
                }

                case BinaryExpression b:
                {
                    TypeInfo t = TypeOf(b);
                    return BuildBinary(b, t.Width, t.Signed);
                }

                case ConditionalExpression c:
                {
                    TypeInfo t = TypeOf(c);
                    return BuildAt(c, t.Width, t.Signed);
                }

                case ParenthesizedExpression p:
                    return BuildSelf(p.Inner);

                case CallExpression call:
                    return BuildCall(call);

                case CastExpression cast:
                    return BuildCast(cast);

                case InsideExpression inside:
                    return BuildInside(inside);

                case MethodCallExpression method:
                    return BuildMethod(method);

                case ValueRange range:
                    Error(range.Location, ElaborationCodes.BadSelect, "A range [low:high] belongs in an inside set or a case inside label.");
                    return Expr.Constant(LogicVec.AllX(1));

                case StringLiteral s:
                    return Expr.Constant(StringBits(s.Value));

                case RealLiteral r:
                    Error(r.Location, ElaborationCodes.NotYet, $"{r.Text} is a real number; BitSorter simulates whole numbers only.");
                    return Expr.Constant(LogicVec.AllX(1));

                case MemberExpression m:
                    return BuildMember(m);

                case AssignmentPatternExpression p:
                    Error(p.Location, ElaborationCodes.BadSelect,
                        "A '{...} fills a struct or a packed array, so it goes straight on the right of an assignment to one, or in its declaration.");
                    return Expr.Constant(LogicVec.AllX(1));

                case ErrorExpression err:
                    Failed = true;
                    return Expr.Constant(LogicVec.AllX(1));

                default:
                    Error(e.Location, ElaborationCodes.NotYet, "BitSorter cannot simulate this expression yet.");
                    return Expr.Constant(LogicVec.AllX(1));
            }
        }

        /// <summary>
        /// A string literal as a value: its characters eight bits each, the first the most significant
        /// (IEEE 1800, 5.9) -- so <c>parameter IMAGE = "prog.hex"</c> holds the name. "" is one zero byte.
        /// </summary>
        public static LogicVec StringBits(string text)
        {
            if (string.IsNullOrEmpty(text))
                return LogicVec.FromUInt64(0, 8);

            var bits = new System.Text.StringBuilder(text.Length * 8);
            foreach (char c in text)
                for (int b = 7; b >= 0; b--)
                    bits.Append(((c & 0xFF) >> b & 1) == 1 ? '1' : '0');
            return LogicVec.FromBinary(bits.ToString());
        }

        /// <summary>The string a value's bits spell, eight at a time, leading zero bytes dropped; false if any bit is unknown.</summary>
        public static bool TryString(LogicVec value, out string text)
        {
            text = null;
            if (!value.IsKnown)
                return false;

            // A string sits in the lowest bits, a character a byte from bit 0 up; a width that is not
            // a whole number of bytes leaves its odd bits in the top one.
            var chars = new System.Text.StringBuilder();
            for (int lowest = (value.Width - 1) / 8 * 8; lowest >= 0; lowest -= 8)
            {
                value.Slice(lowest, Math.Min(8, value.Width - lowest)).TryGetUInt64(out ulong code);
                if (code != 0 || chars.Length > 0)
                    chars.Append((char)code);
            }

            text = chars.ToString();
            return true;
        }

        private static Expr Extend(Expr e, int width, bool signed)
        {
            if (e.Width == width) return e;
            if (e.Width < width) return Expr.Resize(e, width, signed);
            return Expr.Slice(e, 0, width);
        }

        /// <summary>A name used as a value: a signal, a parameter, a genvar -- here, or through the hierarchy.</summary>
        private Expr BuildNamed(Expression e)
        {
            string text = Printer.Print(e);
            bool simple = e is NameExpression one && one.Path.Count == 1;

            switch (Resolve(e, report: false))
            {
                case SignalEntry s when s.IsEvent && !AllowEvents:
                    Error(e.Location, ElaborationCodes.BadSelect, $"{text} is an event, which has no value: wait for it with @({text}), or test {text}.triggered.");
                    return Expr.Constant(LogicVec.AllX(1));

                case SignalEntry s:
                    if (_constant)
                    {
                        Error(e.Location, ElaborationCodes.NotConstant,
                            $"{text} is a signal, but a value known before the run is needed here -- a number or a parameter.");
                        return Expr.Constant(LogicVec.AllX(s.Width));
                    }

                    return Expr.Read(s.Signal, s.Width);

                case ConstantEntry c:
                    return Expr.Constant(c.Value);

                case GenvarEntry g:
                    if (g.Value.HasValue)
                        return Expr.Constant(g.Value.Value);
                    Error(e.Location, ElaborationCodes.NotConstant, $"Genvar {text} only has a value inside its generate loop.");
                    return Expr.Constant(LogicVec.AllX(32));

                case MemoryEntry m:
                    Error(e.Location, ElaborationCodes.BadSelect, $"{text} is a memory, read a word at a time: {text}[i].");
                    return Expr.Constant(LogicVec.AllX(m.Memory.Width));

                case TypeEntry _:
                    Error(e.Location, ElaborationCodes.UnknownName, $"{text} is a type, not a value.");
                    return Expr.Constant(LogicVec.AllX(1));

                case InstanceEntry _:
                    Error(e.Location, ElaborationCodes.UnknownName, $"{text} is an instance or a generate block, not a signal.");
                    return Expr.Constant(LogicVec.AllX(1));

                case null when e is NameExpression trigger && TryEventOf(trigger, out SignalEntry triggered):
                    return Triggered(trigger, triggered);

                case null when e is NameExpression path && path.Path.Count > 1 && SignalPrefix(path, out _) != null:
                    return BuildMembers(path);

                default:
                    if (simple)
                        Error(e.Location, ElaborationCodes.UnknownName,
                            $"There is no signal or parameter called {text} here. Is it declared above, and spelled the same?");
                    else
                        Resolve(e, report: true);
                    Failed = true;
                    return Expr.Constant(LogicVec.AllX(1));
            }
        }

        /// <summary><c>mem[i]</c>: the word, or x outside the memory; a word picked as the design runs when <c>i</c> is not a constant.</summary>
        private Expr BuildWord(MemoryEntry memory, IndexExpression index)
        {
            MemoryInfo info = memory.Memory;
            if (_constant)
            {
                Error(index.Location, ElaborationCodes.NotConstant, $"{Printer.Print(index)} is a word of a memory, known only as the design runs.");
                return Expr.Constant(LogicVec.AllX(info.Width));
            }

            if (memory.Dims.Length > 1)
            {
                long word = WordIndex(memory, index, out Expr picked);
                if (picked != null)
                    return Expr.WordOf(info.First, info.Depth, info.Width, picked);

                return word < 0
                    ? Expr.Constant(LogicVec.AllX(info.Width, info.Signed))
                    : Expr.Read(new SignalId(info.First.Index + (int)word), info.Width);
            }

            var quiet = new DiagnosticBag();
            if (TryInteger(index.Index, _scope, quiet, "an index", out long k))
                return k < info.Lowest || k > info.Highest
                    ? Expr.Constant(LogicVec.AllX(info.Width, info.Signed))
                    : Expr.Read(info.Word((int)k), info.Width);

            return Expr.WordOf(info.First, info.Depth, info.Width, WordOffset(BuildSelf(index.Index), info));
        }

        /// <summary>Which word an index picks, counted from the memory's lowest.</summary>
        private static Expr WordOffset(Expr index, MemoryInfo info)
        {
            Expr wide = Expr.Resize(index, Math.Max(index.Width, 32), false);
            return info.Lowest == 0 ? wide : Expr.Apply(BinaryOp.Subtract, wide, Expr.Constant(LogicVec.FromInt64(info.Lowest, wide.Width)));
        }

        // -----------------------------------------------------------------
        // Names through the hierarchy
        // -----------------------------------------------------------------

        /// <summary>
        /// What a name means: a simple one looked up from here outwards; a hierarchical one --
        /// <c>dut.ctl.state</c>, <c>slice[3].s.sum</c> -- followed down through instances and generate
        /// blocks, starting from one visible here or from the top module. Null when it means nothing,
        /// having said why if <paramref name="report"/>.
        /// </summary>
        private Entry Resolve(Expression e, bool report)
        {
            switch (e)
            {
                case NameExpression n when n.Path.Count == 1:
                    return _scope.Lookup(n.Name);

                case NameExpression n:
                    return ResolvePath(n.Path, n.Location, report);

                case MemberExpression m:
                {
                    var parts = new List<string>();
                    if (TryPathOf(m, parts))
                        return ResolvePath(parts, m.Location, report);
                    if (report)
                        Error(m.Location, ElaborationCodes.BadSelect,
                            $"{Printer.Print(m)} reaches through a generate block by an index known only as the design runs; give it a constant.");
                    return null;
                }

                default:
                    return null;
            }
        }

        /// <summary>A name's parts, a generate block's index written into its part as Icarus names it: <c>slice[3]</c>.</summary>
        private bool TryPathOf(Expression e, List<string> parts)
        {
            switch (e)
            {
                case NameExpression n:
                    parts.AddRange(n.Path);
                    return true;

                case MemberExpression m:
                    if (!TryPathOf(m.Target, parts))
                        return false;
                    parts.Add(m.Member);
                    return true;

                case IndexExpression i:
                {
                    if (!TryPathOf(i.Target, parts))
                        return false;
                    var quiet = new DiagnosticBag();
                    if (!TryInteger(i.Index, _scope, quiet, "an index", out long k))
                        return false;
                    parts[parts.Count - 1] += $"[{k}]";
                    return true;
                }

                default:
                    return false;
            }
        }

        private Entry ResolvePath(List<string> parts, SourceLocation at, bool report)
        {
            string whole = string.Join(".", parts);
            Scope scope;

            Entry first = _scope.Lookup(parts[0]);
            if (first is InstanceEntry instance && instance.Scope != null)
            {
                scope = instance.Scope;
            }
            else if (_scope.Root.Top != null && _scope.Root.Top.Path == parts[0])
            {
                scope = _scope.Root.Top;
            }
            else
            {
                if (report)
                    Error(at, ElaborationCodes.UnknownName, first is InstanceEntry
                        ? $"{whole} reaches into {parts[0]}, which is not built at this point."
                        : $"There is no instance called {parts[0]} here, so {whole} leads nowhere.");
                return null;
            }

            for (int i = 1; i < parts.Count - 1; i++)
            {
                if (!(scope.Own(parts[i]) is InstanceEntry next) || next.Scope == null)
                {
                    if (report)
                        Error(at, ElaborationCodes.UnknownName,
                            $"{string.Join(".", parts.GetRange(0, i))} has no instance or generate block called {parts[i]}, so {whole} leads nowhere.");
                    return null;
                }

                scope = next.Scope;
            }

            Entry last = scope.Own(parts[parts.Count - 1]);
            if (last == null && report)
                Error(at, ElaborationCodes.UnknownName, $"{string.Join(".", parts.GetRange(0, parts.Count - 1))} has nothing called {parts[parts.Count - 1]}.");
            return last;
        }

        /// <summary>
        /// The dimensions <c>foreach</c> walks (Part L4), the first the outermost, as declared: a memory's
        /// unpacked ones and then its words' packed ones; a vector's or a packed array's packed ones. False,
        /// having said why, when <paramref name="array"/> is not something with dimensions.
        /// </summary>
        public bool TryLoopDimensions(NameExpression array, out List<(int Left, int Right)> dims)
        {
            dims = new List<(int Left, int Right)>();
            PackedType type;

            switch (Resolve(array, report: false))
            {
                case MemoryEntry memory:
                    dims.AddRange(memory.Dims);
                    type = memory.Type;
                    break;

                case SignalEntry signal:
                    type = PackedOf(signal);
                    break;

                case null when array.Path.Count > 1 && SignalPrefix(array, out _) != null:
                    if (!TryMembers(array, report: true, out SignalEntry _, out int _, out type))
                        return false;
                    break;

                case null when array.Path.Count == 1:
                    Error(array.Location, ElaborationCodes.UnknownName, $"There is no array called {array.Name} here for foreach to walk.");
                    return false;

                case null:
                    Resolve(array, report: true);
                    Failed = true;
                    return false;

                default:
                    Error(array.Location, ElaborationCodes.BadSelect, $"{Printer.Print(array)} is not an array, so foreach has nothing to walk.");
                    return false;
            }

            // A struct's bits are members, not a dimension.
            for (PackedType t = type; t != null && t.Struct == null; t = t.Element)
                dims.Add((t.Msb, t.Lsb));
            return true;
        }

        /// <summary>The memory a system task's argument names, or null having said it names none.</summary>
        public MemoryEntry MemoryNamed(Expression e)
        {
            if (Resolve(e, report: false) is MemoryEntry memory)
                return memory;

            Error(e?.Location ?? default, ElaborationCodes.BadSelect, $"{(e == null ? "Nothing" : Printer.Print(e))} is not a memory, which this task needs.");
            return null;
        }

        /// <summary>The memory <paramref name="target"/> names, if it names one.</summary>
        private MemoryEntry MemoryOf(Expression target) =>
            target is NameExpression || target is MemberExpression ? Resolve(target, report: false) as MemoryEntry : null;

        // -----------------------------------------------------------------
        // Selects, through packed arrays and structs (Part L4)
        // -----------------------------------------------------------------

        /// <summary>What a signal's bits are: its declared type, or its range when nothing else says.</summary>
        private static PackedType PackedOf(SignalEntry s) => s.Type ?? PackedType.Bits(s.Msb, s.Lsb, s.Signed, s.TwoState, s.Enum);

        /// <summary>
        /// What the bits of <paramref name="e"/> are, when it names a place: a signal, a word of a memory, a
        /// member of a struct, an element or a slice of a packed array. Null for anything else -- a number,
        /// an operator, a row of a memory -- whose bits are only bits, counted from 0.
        /// </summary>
        public PackedType TypeAt(Expression e)
        {
            switch (e)
            {
                case NameExpression _:
                case MemberExpression _:
                    switch (Resolve(e, report: false))
                    {
                        case SignalEntry s:
                            return PackedOf(s);
                        case null when e is NameExpression path && path.Path.Count > 1:
                            return TryMembers(path, report: false, out SignalEntry _, out int _, out PackedType member) ? member : null;
                        case null when e is MemberExpression m:
                            return TypeAt(m.Target)?.Struct?.Find(m.Member)?.Type;
                        default:
                            return null;
                    }

                case IndexExpression index:
                {
                    int depth = WordDepth(index, out MemoryEntry memory);
                    if (memory != null && depth <= memory.Dims.Length)
                        return depth == memory.Dims.Length ? memory.Type : null;

                    PackedType target = TypeAt(index.Target);
                    return target == null ? null : target.Element ?? PackedType.Bit;
                }

                case RangeSelectExpression range:
                {
                    PackedType target = TypeAt(range.Target);
                    if (target == null)
                        return null;

                    // A slice of a packed array is a packed array of its elements, unsigned (IEEE 1800, 7.4.3).
                    if (range.Kind == RangeKind.Constant)
                        return new PackedType((int)ConstantOf(range.Left, report: false), (int)ConstantOf(range.Right, report: false), false, target.Element, null, null, target.TwoState);
                    return new PackedType(SelectWidth(range) - 1, 0, false, target.Element, null, null, target.TwoState);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// The signal the first parts of a dotted name lead to, here or through the hierarchy, when its last
        /// parts are then members of a struct: <c>pkt.kind</c>, <c>dut.pkt.kind</c>. Null when none does.
        /// </summary>
        private SignalEntry SignalPrefix(NameExpression n, out int parts)
        {
            for (int k = n.Path.Count - 1; k >= 1; k--)
            {
                Entry entry = k == 1 ? _scope.Lookup(n.Path[0]) : ResolvePath(n.Path.GetRange(0, k), n.Location, report: false);
                if (entry is SignalEntry s)
                {
                    parts = k;
                    return s;
                }
            }

            parts = 0;
            return null;
        }

        /// <summary>
        /// <c>pkt.head.kind</c> as members, one inside the other, of the struct a signal is: where the last of
        /// them starts in the signal, and its type. False when no first part is a signal, or when a member is
        /// not there -- having said so, if <paramref name="report"/>.
        /// </summary>
        private bool TryMembers(NameExpression n, bool report, out SignalEntry signal, out int lowest, out PackedType type)
        {
            lowest = 0;
            type = null;
            signal = SignalPrefix(n, out int parts);
            if (signal == null)
                return false;

            type = PackedOf(signal);
            for (int k = parts; k < n.Path.Count; k++)
            {
                StructField field = type.Struct?.Find(n.Path[k]);
                if (field == null)
                {
                    if (report)
                        NoMember(n.Location, string.Join(".", n.Path.GetRange(0, k)), type, n.Path[k]);
                    type = null;
                    return false;
                }

                lowest += field.Lowest;
                type = field.Type;
            }

            return true;
        }

        private void NoMember(SourceLocation at, string target, PackedType type, string member)
        {
            if (type?.Struct == null)
            {
                Error(at, ElaborationCodes.UnknownName, $"{target} is not a struct, so it has no member called {member}.");
                return;
            }

            var names = new List<string>();
            foreach (StructField field in type.Struct.Fields)
                names.Add(field.Name);
            string list = names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
            Error(at, ElaborationCodes.UnknownName, $"{target} has no member called {member}; its members are {list}.");
        }

        /// <summary><c>e.triggered</c>: the event its first parts name, when its last is <c>triggered</c>.</summary>
        private bool TryEventOf(NameExpression n, out SignalEntry e)
        {
            e = null;
            if (n.Path.Count < 2 || n.Name != "triggered" || !(SignalPrefix(n, out int parts) is SignalEntry signal) || !signal.IsEvent || parts != n.Path.Count - 1)
                return false;

            e = signal;
            return true;
        }

        /// <summary><c>e.triggered</c> (IEEE 1800, 15.5.3): 1 from a trigger of the event until its time step ends.</summary>
        private Expr Triggered(Expression at, SignalEntry e)
        {
            Func<Expr> time = _scope.Root.TimeSource;
            if (_constant || time == null)
            {
                Error(at.Location, ElaborationCodes.NotConstant, $"{Printer.Print(at)} is known only as the design runs.");
                return Expr.Constant(LogicVec.AllX(1));
            }

            Expr now = Expr.Apply(BinaryOp.Add, time(), Expr.Constant(LogicVec.FromUInt64(1, 64)));
            return Expr.Apply(BinaryOp.Equal, Expr.Read(e.Triggered, 64), now);
        }

        /// <summary>A struct's member read: <c>pkt.kind</c>, the bits of the signal the member is.</summary>
        private Expr BuildMembers(NameExpression n)
        {
            if (!TryMembers(n, report: true, out SignalEntry s, out int lowest, out PackedType type))
                return Expr.Constant(LogicVec.AllX(1));

            if (_constant)
            {
                Error(n.Location, ElaborationCodes.NotConstant,
                    $"{Printer.Print(n)} is part of a signal, but a value known before the run is needed here -- a number or a parameter.");
                return Expr.Constant(LogicVec.AllX(type.Width));
            }

            return AsSigned(Expr.Slice(Expr.Read(s.Signal, s.Width), lowest, type.Width), type.Signed);
        }

        /// <summary><c>a[i].b</c>: a name through a generate block, or a member of an element that is a struct.</summary>
        private Expr BuildMember(MemberExpression m)
        {
            if (Resolve(m, report: false) != null)
                return BuildNamed(m);

            PackedType target = TypeAt(m.Target);
            if (target == null)
                return BuildNamed(m);   // which says why the name leads nowhere

            StructField field = target.Struct?.Find(m.Member);
            if (field == null)
            {
                NoMember(m.Location, Printer.Print(m.Target), target, m.Member);
                return Expr.Constant(LogicVec.AllX(1));
            }

            return AsSigned(Expr.Slice(BuildSelf(m.Target), field.Lowest, field.Type.Width), field.Type.Signed);
        }

        /// <summary>
        /// How many indices <paramref name="e"/> puts on a memory -- <c>grid[i][j]</c> two, the memory's name
        /// alone 0 -- or -1, with no memory, when it does not start at one.
        /// </summary>
        private int WordDepth(Expression e, out MemoryEntry memory)
        {
            switch (e)
            {
                case IndexExpression index:
                {
                    int inner = WordDepth(index.Target, out memory);
                    return inner < 0 ? -1 : inner + 1;
                }

                case NameExpression _:
                case MemberExpression _:
                    memory = MemoryOf(e);
                    return memory != null ? 0 : -1;

                default:
                    memory = null;
                    return -1;
            }
        }

        /// <summary>
        /// Which word of a memory of more than one dimension the indices ending at <paramref name="word"/>
        /// pick, counted from its first: a number when every index is a constant, -1 when one is outside its
        /// dimension. When an index is known only as the design runs, <paramref name="picked"/> works it out
        /// then -- x when an index is outside, so nothing is read or written rather than another word.
        /// </summary>
        private long WordIndex(MemoryEntry memory, IndexExpression word, out Expr picked)
        {
            picked = null;
            var indices = new Expression[memory.Dims.Length];
            Expression at = word;
            for (int k = indices.Length - 1; k >= 0; k--)
            {
                var index = (IndexExpression)at;
                indices[k] = index.Index;
                at = index.Target;
            }

            var quiet = new DiagnosticBag();
            long constant = 0, stride = 1;
            bool outside = false;
            Expr sum = null, inside = null;

            // The last dimension moves fastest: row by row, as the words were declared.
            for (int k = indices.Length - 1; k >= 0; k--)
            {
                (int left, int right) = memory.Dims[k];
                int lowest = Math.Min(left, right);
                long count = Math.Abs((long)left - right) + 1;

                if (TryInteger(indices[k], _scope, quiet, "an index", out long c))
                {
                    if (c < lowest || c >= lowest + count)
                        outside = true;
                    else
                        constant += (c - lowest) * stride;
                }
                else
                {
                    Expr position = Linear(BuildSelf(indices[k]), TypeOf(indices[k]).Signed, +1, -lowest);
                    Expr term = stride == 1 ? position : Expr.Apply(BinaryOp.Multiply, position, Int64(stride));
                    sum = sum == null ? term : Expr.Apply(BinaryOp.Add, sum, term);
                    inside = inside == null ? Within(position, count) : Expr.Apply(BinaryOp.LogicalAnd, inside, Within(position, count));
                }

                stride *= count;
            }

            if (outside)
                return -1;

            if (sum != null)
            {
                if (constant != 0)
                    sum = Expr.Apply(BinaryOp.Add, sum, Int64(constant));
                picked = Expr.Conditional(inside, sum, Expr.Constant(LogicVec.AllX(64, true)));
            }

            return constant;
        }

        /// <summary>What saying <c>name[i]</c> of a memory of several dimensions, short of a word, gets told.</summary>
        private static string RowMessage(IndexExpression index, MemoryEntry memory)
        {
            string name = memory.FullName.Substring(memory.FullName.LastIndexOf('.') + 1);
            var word = new System.Text.StringBuilder(name);
            for (int k = 0; k < memory.Dims.Length; k++)
                word.Append('[').Append("ijklmn"[Math.Min(k, 5)]).Append(']');
            return $"{Printer.Print(index)} is a row of {name}, which has {memory.Dims.Length} dimensions; BitSorter reads and writes it a word at a time, {word}.";
        }

        /// <summary>A whole number as a signed 64-bit constant, which a select's place is counted in.</summary>
        private static Expr Int64(long value) => Expr.Constant(LogicVec.FromInt64(value, 64, true));

        /// <summary><c>sign * b + constant</c>, signed and 64 bits wide: a select's place, which may be below 0.</summary>
        private static Expr Linear(Expr b, bool signed, int sign, long constant)
        {
            Expr wide = Expr.WithSignedness(b.Width > 64 ? Expr.Slice(b, 0, 64) : Expr.Resize(b, 64, signed), true);
            if (sign < 0)
                return Expr.Apply(BinaryOp.Subtract, Int64(constant), wide);
            return constant == 0 ? wide : Expr.Apply(BinaryOp.Add, wide, Int64(constant));
        }

        /// <summary>Whether a place, counted from 0, is one of <paramref name="count"/>.</summary>
        private static Expr Within(Expr position, long count) =>
            Expr.Apply(BinaryOp.LogicalAnd,
                Expr.Apply(BinaryOp.GreaterOrEqual, position, Int64(0)),
                Expr.Apply(BinaryOp.Less, position, Int64(count)));

        /// <summary>A value read as signed, when the type it is of is.</summary>
        private static Expr AsSigned(Expr e, bool signed) => signed ? Expr.WithSignedness(e, true) : e;

        private Expr BuildIndex(IndexExpression index)
        {
            int depth = WordDepth(index, out MemoryEntry memory);
            if (memory != null && depth <= memory.Dims.Length)
            {
                if (depth < memory.Dims.Length)
                {
                    Error(index.Location, ElaborationCodes.BadSelect, RowMessage(index, memory));
                    return Expr.Constant(LogicVec.AllX(memory.Memory.Width));
                }

                return BuildWord(memory, index);
            }

            Expr target = BuildSelf(index.Target);
            PackedType type = TypeAt(index.Target) ?? PackedType.Bits(target.Width - 1, 0);
            int width = type.ElementWidth;
            bool signed = type.Element?.Signed ?? false;

            var quiet = new DiagnosticBag();
            if (TryInteger(index.Index, _scope, quiet, "an index", out long constant))
            {
                long position = type.Position(constant);
                if (position < 0 || position + width > target.Width)
                    return Expr.Constant(LogicVec.AllX(width, signed));   // reading outside the vector gives x, as the standard says
                return AsSigned(Expr.Slice(target, (int)position, width), signed);
            }

            if (_constant)
            {
                TryInteger(index.Index, _scope, _diagnostics, "an index", out _);
                Failed = true;
                return Expr.Constant(LogicVec.AllX(width));
            }

            // A variable index: shift it to count from bit 0, then the engine's own bit-select -- or, for
            // an element of a packed array, a part-select an element wide.
            Expr offset = OffsetFrom(BuildSelf(index.Index), type.Msb, type.Lsb);
            return type.Element == null
                ? Expr.Bit(target, offset)
                : AsSigned(Expr.Part(target, offset, width, width), signed);
        }

        private static Expr OffsetFrom(Expr index, int msb, int lsb)
        {
            Expr wide = Expr.Resize(index, Math.Max(index.Width, 32), false);
            if (msb >= lsb)
                return lsb == 0 ? wide : Expr.Apply(BinaryOp.Subtract, wide, Expr.Constant(LogicVec.FromInt64(lsb, wide.Width)));
            return Expr.Apply(BinaryOp.Subtract, Expr.Constant(LogicVec.FromInt64(lsb, wide.Width)), wide);
        }

        private Expr BuildRange(RangeSelectExpression range)
        {
            int depth = WordDepth(range.Target, out MemoryEntry memory);
            if (memory != null && depth < memory.Dims.Length)
            {
                Error(range.Location, ElaborationCodes.NotYet, $"{Printer.Print(range)} is a slice of a memory, which BitSorter does not support yet; read it a word at a time.");
                return Expr.Constant(LogicVec.AllX(1));
            }

            Expr target = BuildSelf(range.Target);
            PackedType type = TypeAt(range.Target) ?? PackedType.Bits(target.Width - 1, 0);
            int msb = type.Msb, lsb = type.Lsb, step = type.ElementWidth;

            if (range.Kind == RangeKind.Constant)
            {
                if (!TryInteger(range.Left, _scope, _diagnostics, "a part-select's bound", out long left) ||
                    !TryInteger(range.Right, _scope, _diagnostics, "a part-select's bound", out long right))
                {
                    Failed = true;
                    return Expr.Constant(LogicVec.AllX(1));
                }

                bool descending = msb >= lsb;
                if (left != right && (left > right) != descending)
                {
                    Error(range.Location, ElaborationCodes.BadSelect,
                        $"{Printer.Print(range)} runs the other way from how {Printer.Print(range.Target)} was declared ([{msb}:{lsb}]).");
                    return Expr.Constant(LogicVec.AllX(1));
                }

                long low = Math.Min(type.Position(left), type.Position(right));
                long width = (Math.Abs(left - right) + 1) * step;

                if (low < 0 || low + width > target.Width)
                {
                    Error(range.Location, ElaborationCodes.BadSelect,
                        $"{Printer.Print(range)} reaches outside {Printer.Print(range.Target)}, which is [{msb}:{lsb}].");
                    return Expr.Constant(LogicVec.AllX((int)Math.Min(width, LogicVec.MaxWidth)));
                }

                return Expr.Slice(target, (int)low, (int)width);
            }

            if (!TryInteger(range.Right, _scope, _diagnostics, "an indexed part-select's width", out long w) || w < 1)
            {
                Failed = true;
                return Expr.Constant(LogicVec.AllX(1));
            }

            if (w * step > LogicVec.MaxWidth)
            {
                Error(range.Location, ElaborationCodes.TooLarge, $"{Printer.Print(range)} is wider than BitSorter allows ({LogicVec.MaxWidth} bits).");
                return Expr.Constant(LogicVec.AllX(1));
            }

            int selectWidth = (int)(w * step);
            bool up = range.Kind == RangeKind.IndexedUp;
            var quiet = new DiagnosticBag();

            if (TryInteger(range.Left, _scope, quiet, "a part-select's base", out long b))
            {
                long top = up ? b + w - 1 : b;
                long bottom = up ? b : b - w + 1;
                long low = Math.Min(type.Position(top), type.Position(bottom));

                if (low >= target.Width || low + selectWidth <= 0)
                    return Expr.Constant(LogicVec.AllX(selectWidth));

                return Expr.Slice(target, (int)low, selectWidth);   // a bit past either end reads x
            }

            // A variable base: the select's lowest element, counted from element 0, as the design runs.
            Expr baseExpr = BuildSelf(range.Left);
            bool signed = TypeOf(range.Left).Signed;
            Expr lowest = msb >= lsb
                ? Linear(baseExpr, signed, +1, up ? -lsb : -lsb - (w - 1))
                : Linear(baseExpr, signed, -1, up ? lsb - (w - 1) : lsb);
            return Expr.Part(target, lowest, step, selectWidth);
        }

        private Expr BuildConcatenation(ConcatenationExpression c)
        {
            Expr result = null;

            foreach (Expression item in c.Items)
            {
                Expression bare = item;
                while (bare is ParenthesizedExpression p)
                    bare = p.Inner;

                if (bare is NumberLiteral n && (!n.Text.Contains("'") || n.Text.StartsWith("'", StringComparison.Ordinal)))
                {
                    Error(item.Location, ElaborationCodes.UnsizedInConcatenation, n.Text.Contains("'")
                        ? $"{n.Text} has no size, so it cannot go in a {{ }}: give it one, as 1{n.Text}."
                        : $"{n.Text} has no size, so it cannot go in a {{ }}: give it one, as 1'b0 or 4'd{n.Text}.");
                    continue;
                }

                Expr built = BuildSelf(item);
                result = result == null ? built : Expr.Concat(result, built);
            }

            if (result != null && result.Width > LogicVec.MaxWidth)
            {
                Error(c.Location, ElaborationCodes.TooLarge, $"This concatenation is wider than BitSorter allows ({LogicVec.MaxWidth}).");
                return Expr.Constant(LogicVec.AllX(1));
            }

            return result ?? Expr.Constant(LogicVec.AllX(1));
        }

        private Expr BuildBinary(BinaryExpression b, int width, bool signed)
        {
            switch (b.Operator)
            {
                case "+": return Arithmetic(BinaryOp.Add, b, width, signed);
                case "-": return Arithmetic(BinaryOp.Subtract, b, width, signed);
                case "*": return Arithmetic(BinaryOp.Multiply, b, width, signed);
                case "/": return Arithmetic(BinaryOp.Divide, b, width, signed);
                case "%": return Arithmetic(BinaryOp.Modulo, b, width, signed);
                case "&": return Arithmetic(BinaryOp.And, b, width, signed);
                case "|": return Arithmetic(BinaryOp.Or, b, width, signed);
                case "^": return Arithmetic(BinaryOp.Xor, b, width, signed);
                case "~^": case "^~": return Arithmetic(BinaryOp.Xnor, b, width, signed);

                // Not the standard's, but the course's first workshop writes them (the parser warns).
                case "~&": return Expr.Apply(UnaryOp.Not, Arithmetic(BinaryOp.And, b, width, signed));
                case "~|": return Expr.Apply(UnaryOp.Not, Arithmetic(BinaryOp.Or, b, width, signed));

                case "<<": return Shift(BinaryOp.ShiftLeft, b, width, signed);
                case ">>": return Shift(BinaryOp.ShiftRight, b, width, signed);
                case "<<<": return Shift(BinaryOp.ArithmeticShiftLeft, b, width, signed);
                case ">>>": return Shift(BinaryOp.ArithmeticShiftRight, b, width, signed);

                case "**": return Power(b, width, signed);

                case "==?": return Extend(Compare(BinaryOp.WildcardEqual, b), width, false);
                case "!=?": return Extend(Compare(BinaryOp.WildcardNotEqual, b), width, false);

                case "<": return Extend(Compare(BinaryOp.Less, b), width, false);
                case "<=": return Extend(Compare(BinaryOp.LessOrEqual, b), width, false);
                case ">": return Extend(Compare(BinaryOp.Greater, b), width, false);
                case ">=": return Extend(Compare(BinaryOp.GreaterOrEqual, b), width, false);
                case "==": return Extend(Compare(BinaryOp.Equal, b), width, false);
                case "!=": return Extend(Compare(BinaryOp.NotEqual, b), width, false);
                case "===": return Extend(Compare(BinaryOp.CaseEqual, b), width, false);
                case "!==": return Extend(Compare(BinaryOp.CaseNotEqual, b), width, false);

                case "&&": return Extend(Expr.Apply(BinaryOp.LogicalAnd, BuildSelf(b.Left), BuildSelf(b.Right)), width, false);
                case "||": return Extend(Expr.Apply(BinaryOp.LogicalOr, BuildSelf(b.Left), BuildSelf(b.Right)), width, false);

                default:
                    Error(b.Location, ElaborationCodes.NotYet, $"The operator {b.Operator} is not supported by BitSorter yet.");
                    return Expr.Constant(LogicVec.AllX(width));
            }
        }

        private Expr Arithmetic(BinaryOp op, BinaryExpression b, int width, bool signed) =>
            Expr.Apply(op, BuildAt(b.Left, width, signed), BuildAt(b.Right, width, signed));

        private Expr Shift(BinaryOp op, BinaryExpression b, int width, bool signed) =>
            Expr.Apply(op, BuildAt(b.Left, width, signed), BuildSelf(b.Right));

        /// <summary>A comparison: its operands sized against each other, never against the context.</summary>
        private Expr Compare(BinaryOp op, BinaryExpression b)
        {
            TypeInfo l = TypeOf(b.Left), r = TypeOf(b.Right);
            int width = Math.Max(l.Width, r.Width);
            bool signed = l.Signed && r.Signed;
            return Expr.Apply(op, BuildAt(b.Left, width, signed), BuildAt(b.Right, width, signed));
        }

        /// <summary><c>a ** b</c>: worked out before the run when both are constants, which is how designs use it.</summary>
        private Expr Power(BinaryExpression b, int width, bool signed)
        {
            var quiet = new DiagnosticBag();
            if (!TryConstant(b.Left, _scope, quiet, out LogicVec baseValue) || !TryInteger(b.Right, _scope, quiet, "an exponent", out long exponent))
            {
                Error(b.Location, ElaborationCodes.NotYet, "** is supported between constants only, such as 2 ** N.");
                return Expr.Constant(LogicVec.AllX(width));
            }

            LogicVec result = LogicVec.FromUInt64(1, width, signed);
            LogicVec factor = baseValue.Extend(width, signed && baseValue.IsSigned);

            if (exponent < 0)
                return Expr.Constant(LogicVec.AllX(width));

            for (long i = 0; i < Math.Min(exponent, 4096); i++)
                result = LogicVec.Multiply(result, factor);

            return Expr.Constant(result);
        }

        private Expr BuildCall(CallExpression call)
        {
            switch (call.Name)
            {
                case "$signed":
                case "$unsigned":
                    if (call.Arguments.Count != 1 || call.Arguments[0] == null)
                    {
                        Error(call.Location, ElaborationCodes.NotYet, $"{call.Name} takes one value.");
                        return Expr.Constant(LogicVec.AllX(1));
                    }

                    return Expr.WithSignedness(BuildSelf(call.Arguments[0]), call.Name == "$signed");

                case "$clog2":
                {
                    if (call.Arguments.Count != 1 || call.Arguments[0] == null ||
                        !TryConstant(call.Arguments[0], _scope, _diagnostics, out LogicVec value) || !value.IsKnown)
                    {
                        Failed = true;
                        return Expr.Constant(LogicVec.AllX(32, true));
                    }

                    int bits = 0;
                    LogicVec one = LogicVec.FromUInt64(1, value.Width);
                    LogicVec n = LogicVec.Subtract(value.AsUnsigned(), one);
                    if (LogicVec.Truth(value) == Logic4.Zero)
                        return Expr.Constant(LogicVec.FromInt64(0, 32, true));
                    while (LogicVec.Truth(n) == Logic4.One)
                    {
                        n = LogicVec.ShiftRight(n, LogicVec.FromUInt64(1, 8));
                        bits++;
                    }

                    return Expr.Constant(LogicVec.FromInt64(bits, 32, true));
                }

                case "$time":
                case "$stime":
                case "$realtime":
                {
                    if (_constant)
                    {
                        Error(call.Location, ElaborationCodes.NotConstant, $"{call.Name} is known only as the design runs.");
                        return Expr.Constant(LogicVec.AllX(64));
                    }

                    Func<Expr> source = _scope.Root.TimeSource;
                    if (source == null)
                    {
                        Error(call.Location, ElaborationCodes.NotYet, $"{call.Name} cannot be read here.");
                        return Expr.Constant(LogicVec.AllX(64));
                    }

                    // $realtime is $time: time here is a whole number of units.
                    Expr time = source();
                    return call.Name == "$stime" ? Expr.Slice(time, 0, 32) : time;
                }

                case "$bits":
                    if (call.Arguments.Count == 1 && call.Arguments[0] != null)
                    {
                        Entry named = Resolve(call.Arguments[0], report: false);
                        long bits = named is TypeEntry type ? type.Width
                            : named is MemoryEntry memory ? (long)memory.Memory.Width * memory.Memory.Depth
                            : TypeOf(call.Arguments[0]).Width;
                        return Expr.Constant(LogicVec.FromInt64(bits, 32, true));
                    }
                    break;

                case "$countones":
                case "$onehot":
                case "$onehot0":
                case "$isunknown":
                {
                    if (call.Arguments.Count != 1 || call.Arguments[0] == null)
                    {
                        Error(call.Location, ElaborationCodes.BadCall, $"{call.Name} takes one value.");
                        return Expr.Constant(LogicVec.AllX(1));
                    }

                    UnaryOp op = call.Name == "$countones" ? UnaryOp.CountOnes
                        : call.Name == "$onehot" ? UnaryOp.OneHot
                        : call.Name == "$onehot0" ? UnaryOp.OneHot0
                        : UnaryOp.IsUnknown;
                    return Expr.Apply(op, BuildSelf(call.Arguments[0]));
                }

                case "$random":
                case "$urandom":
                case "$urandom_range":
                    if (_constant || Random == null)
                    {
                        Error(call.Location, ElaborationCodes.NotYet,
                            $"{call.Name} moves its seed on each call, so BitSorter calls it in a process statement only -- an initial or an always -- not in a continuous assignment or a constant.");
                        return Expr.Constant(LogicVec.AllX(32));
                    }

                    return Random(call);
            }

            if (!call.Name.StartsWith("$", StringComparison.Ordinal))
            {
                // Parameters are worked out before a module's functions are declared, so say why here.
                if (_constant)
                {
                    Error(call.Location, ElaborationCodes.NotConstant,
                        $"{call.Name}(...) calls a function, which BitSorter does not work out before the run yet; a constant here is made of numbers and parameters.");
                    return Expr.Constant(LogicVec.AllX(1));
                }

                SubroutineEntry routine = _scope.LookupSubroutine(call.Name);
                if (routine != null)
                    return BuildFunctionCall(call, routine);

                Error(call.Location, ElaborationCodes.UnknownName, $"There is no function called {call.Name} here. Is it declared in this module, and spelt the same?");
                return Expr.Constant(LogicVec.AllX(1));
            }

            Error(call.Location, ElaborationCodes.NotYet, $"{call.Name} is not supported here by BitSorter yet.");
            return Expr.Constant(LogicVec.AllX(1));
        }

        /// <summary>
        /// A function written in the design, called in an expression (Part L2): its arguments, each
        /// sized as an assignment to its argument is, and a node that runs the function where the
        /// expression is evaluated.
        /// </summary>
        private Expr BuildFunctionCall(CallExpression call, SubroutineEntry routine)
        {
            Expr unknown = Expr.Constant(LogicVec.AllX(routine.Width, routine.Signed));

            if (routine.IsTask)
            {
                Error(call.Location, ElaborationCodes.BadCall,
                    $"{call.Name} is a task, which is called as a statement of its own, not inside an expression.");
                return unknown;
            }

            if (routine.IsVoid)
            {
                Error(call.Location, ElaborationCodes.BadCall,
                    $"{call.Name} is a void function: it gives no value, so it is called as a statement of its own.");
                return unknown;
            }

            if (_constant)
            {
                Error(call.Location, ElaborationCodes.NotConstant,
                    $"{call.Name}(...) calls a function, which BitSorter does not work out before the run yet; a constant here is made of numbers and parameters.");
                return unknown;
            }

            FunctionInfo function = routine.Compile(routine);
            if (function == null)
            {
                Failed = true;
                return unknown;
            }

            var arguments = new List<Expr>();
            for (int i = 0; i < routine.Arguments.Count; i++)
            {
                (SignalEntry formal, PortDirection direction, Expression fallback) = routine.Arguments[i];
                Expression given = i < call.Arguments.Count ? call.Arguments[i] : null;

                if (direction != PortDirection.Input)
                {
                    Error(call.Location, ElaborationCodes.BadCall,
                        $"{call.Name} has an {direction.ToString().ToLowerInvariant()} argument, {Short(formal)}, which a call inside an expression cannot take; call it as a statement of its own.");
                    return unknown;
                }

                ExpressionBuilder builder = this;
                if (given == null)
                {
                    if (fallback == null)
                    {
                        Error(call.Location, ElaborationCodes.BadCall, $"{call.Name} needs a value for {Short(formal)}, its argument {i + 1}.");
                        return unknown;
                    }

                    // An argument left out takes its default, worked out where the function is declared.
                    given = fallback;
                    builder = new ExpressionBuilder(routine.Scope, _diagnostics, constantOnly: false);
                }

                Expr built = builder.Build(given, formal.Width);
                if (builder.Failed)
                    Failed = true;
                arguments.Add(built.Width > formal.Width ? Expr.Slice(built, 0, formal.Width) : built);
            }

            if (call.Arguments.Count > routine.Arguments.Count)
            {
                Error(call.Location, ElaborationCodes.BadCall,
                    $"{call.Name} takes {routine.Arguments.Count} argument{(routine.Arguments.Count == 1 ? "" : "s")}, and this call gives {call.Arguments.Count}.");
                return unknown;
            }

            return Expr.Call(function, arguments.ToArray(), routine.Width, routine.Signed);
        }

        /// <summary>A signal's own name, without its scope's path or a working variable's number: <c>x</c> for <c>alu.double.x#12</c>.</summary>
        internal static string Short(SignalEntry signal)
        {
            string name = signal.FullName;
            int hash = name.IndexOf('#');
            if (hash >= 0)
                name = name.Substring(0, hash);
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private Expr BuildCast(CastExpression cast)
        {
            Expr value = BuildSelf(cast.Value);
            TypeInfo inner = TypeOf(cast.Value);

            if (cast.Width != null)
            {
                if (!TryInteger(cast.Width, _scope, _diagnostics, "a cast's width", out long width) || width < 1 || width > LogicVec.MaxWidth)
                {
                    Failed = true;
                    return Expr.Constant(LogicVec.AllX(1));
                }

                return Extend(value, (int)width, inner.Signed);
            }

            if (cast.TargetText == "signed") return Expr.WithSignedness(value, true);
            if (cast.TargetText == "unsigned") return Expr.WithSignedness(value, false);

            // int'(x), byte'(x), bit'(x): the value at the type's width and signedness, and a 2-state type
            // holds x and z as 0 -- int'(4'b1x01) is 9, as Icarus gives it.
            if (KeywordType(cast.TargetText, out int keywordWidth, out bool keywordSigned, out bool twoState))
            {
                Expr typed = Expr.WithSignedness(Extend(value, keywordWidth, inner.Signed), keywordSigned);
                return twoState ? Expr.AsTwoState(typed) : typed;
            }

            // state_t'(n): the value at the type's width, then read with its signedness.
            if (_scope.Lookup(cast.TargetText) is TypeEntry type)
            {
                Expr typed = Expr.WithSignedness(Extend(value, type.Width, inner.Signed), type.Signed);
                return type.TwoState ? Expr.AsTwoState(typed) : typed;
            }

            Error(cast.Location, ElaborationCodes.NotYet, $"A cast to {cast.TargetText} is not supported by BitSorter yet.");
            return value;
        }

        /// <summary>A built-in type a cast may name: its width, signedness, and whether it holds only 0 and 1 (IEEE 1800, 6.11).</summary>
        private static bool KeywordType(string keyword, out int width, out bool signed, out bool twoState)
        {
            switch (keyword)
            {
                case "int": width = 32; signed = true; twoState = true; return true;
                case "shortint": width = 16; signed = true; twoState = true; return true;
                case "longint": width = 64; signed = true; twoState = true; return true;
                case "byte": width = 8; signed = true; twoState = true; return true;
                case "integer": width = 32; signed = true; twoState = false; return true;
                case "time": width = 64; signed = false; twoState = false; return true;
                case "bit": width = 1; signed = false; twoState = true; return true;
                case "logic": case "reg": width = 1; signed = false; twoState = false; return true;
                default: width = 0; signed = false; twoState = false; return false;
            }
        }

        // -----------------------------------------------------------------
        // inside, and an enum's methods (Part L3)
        // -----------------------------------------------------------------

        /// <summary>
        /// <c>v inside {1, [6:9]}</c> (IEEE 1800, 11.4.13): the value and every member sized together, as an
        /// equality's operands are; a value matched by <c>==?</c>, a range by <c>&gt;=</c> and <c>&lt;=</c>; 1 if any
        /// matches, x if none does and one might, else 0.
        /// </summary>
        private Expr BuildInside(InsideExpression inside)
        {
            TypeInfo valueType = TypeOf(inside.Value);
            int width = valueType.Width;
            bool signed = valueType.Signed;

            foreach (Expression member in inside.Set)
            {
                foreach (Expression end in member is ValueRange r ? new[] { r.Low, r.High } : new[] { member })
                {
                    TypeInfo t = TypeOf(end);
                    width = Math.Max(width, t.Width);
                    signed &= t.Signed;
                }
            }

            Expr value = BuildAt(inside.Value, width, signed);
            Expr result = null;

            foreach (Expression member in inside.Set)
            {
                Expr match = member is ValueRange range
                    ? Expr.Apply(BinaryOp.LogicalAnd,
                        Expr.Apply(BinaryOp.GreaterOrEqual, value, BuildAt(range.Low, width, signed)),
                        Expr.Apply(BinaryOp.LessOrEqual, value, BuildAt(range.High, width, signed)))
                    : Expr.Apply(BinaryOp.WildcardEqual, value, BuildAt(member, width, signed));

                result = result == null ? match : Expr.Apply(BinaryOp.LogicalOr, result, match);
            }

            return result ?? Expr.Constant(LogicVec.FromUInt64(0, 1));
        }

        /// <summary>
        /// The enum a value has, and whether its type is 2-state: a signal declared of an enum type, a cast to
        /// one, or another enum method that gives one (<c>s.next().name()</c>).
        /// </summary>
        private bool TryEnumOf(Expression e, out EnumInfo @enum, out bool twoState)
        {
            @enum = null;
            twoState = false;

            switch (e)
            {
                case ParenthesizedExpression p:
                    return TryEnumOf(p.Inner, out @enum, out twoState);

                case NameExpression _:
                case MemberExpression _:
                case IndexExpression _:
                    // A signal of an enum type, or a member, an element or a word that is one.
                    if (TypeAt(e) is PackedType type && type.Enum != null)
                    {
                        @enum = type.Enum;
                        twoState = type.TwoState;
                        return true;
                    }

                    return false;

                case CastExpression c when _scope.Lookup(c.TargetText) is TypeEntry t && t.Enum != null:
                    @enum = t.Enum;
                    twoState = t.TwoState;
                    return true;

                case MethodCallExpression m when m.Method == "next" || m.Method == "prev" || m.Method == "first" || m.Method == "last":
                    return TryEnumOf(m.Target, out @enum, out twoState);

                default:
                    return false;
            }
        }

        /// <summary>How wide <c>.name()</c>'s string is: a byte for each character of the longest name.</summary>
        private static int NameWidth(EnumInfo @enum)
        {
            int longest = 1;
            foreach (EnumMemberInfo member in @enum.Members)
                longest = Math.Max(longest, member.Name.Length);
            return 8 * longest;
        }

        /// <summary>Whether an argument is a string -- <c>.name()</c>'s, or a literal -- which <c>%s</c> writes exactly.</summary>
        public bool IsString(Expression e) =>
            e is StringLiteral || e is MethodCallExpression m && m.Method == "name" && TryEnumOf(m.Target, out EnumInfo _, out bool _);

        /// <summary>An enum's methods (IEEE 1800, 6.19.5): <c>first()</c>, <c>last()</c>, <c>next(n)</c>, <c>prev(n)</c>, <c>num()</c>, <c>name()</c>.</summary>
        private Expr BuildMethod(MethodCallExpression m)
        {
            if (m.Method == "triggered" && m.Target is NameExpression named && Resolve(named, report: false) is SignalEntry ev && ev.IsEvent)
                return Triggered(m, ev);

            if (!TryEnumOf(m.Target, out EnumInfo @enum, out bool twoState))
            {
                Error(m.Location, ElaborationCodes.NotYet,
                    $".{m.Method}() is a method BitSorter knows on an enum only, and {Printer.Print(m.Target)} is not one.");
                return Expr.Constant(LogicVec.AllX(1));
            }

            int takes = m.Method == "next" || m.Method == "prev" ? 1 : 0;
            if (m.Arguments.Count > takes)
            {
                Error(m.Location, ElaborationCodes.BadCall, $".{m.Method}() takes {(takes == 0 ? "no argument" : "at most one argument, how many steps")}.");
                return Expr.Constant(LogicVec.AllX(@enum.Width));
            }

            IReadOnlyList<EnumMemberInfo> members = @enum.Members;
            Expr value = BuildSelf(m.Target);
            bool signed = TypeOf(m.Target).Signed;

            switch (m.Method)
            {
                case "name":
                    return Expr.EnumName(value, @enum);

                case "num":
                    return Expr.Constant(LogicVec.FromInt64(members.Count, 32, signed: true));

                case "first":
                case "last":
                    return members.Count == 0
                        ? Expr.Constant(LogicVec.AllX(@enum.Width))
                        : Expr.Constant(members[m.Method == "first" ? 0 : members.Count - 1].Value);

                case "next":
                case "prev":
                {
                    Expr steps = m.Arguments.Count == 1 && m.Arguments[0] != null ? BuildSelf(m.Arguments[0]) : null;

                    // A value that is no member gives the type's default: x, or 0 for a 2-state enum.
                    LogicVec fallback = twoState ? LogicVec.FromUInt64(0, @enum.Width) : LogicVec.AllX(@enum.Width);
                    return Expr.WithSignedness(Expr.EnumStep(value, @enum, steps, m.Method == "next" ? 1 : -1, fallback), signed);
                }

                default:
                    Error(m.Location, ElaborationCodes.UnknownName,
                        $".{m.Method}() is not a method of an enum: an enum has first(), last(), next(), prev(), num() and name().");
                    return Expr.Constant(LogicVec.AllX(1));
            }
        }

        // -----------------------------------------------------------------
        // Assignment patterns (Part L4)
        // -----------------------------------------------------------------

        /// <summary>
        /// The value an assignment gives a place of type <paramref name="type"/>, <paramref name="width"/>
        /// bits wide: <c>'{...}</c> laid out on the type, or any other expression sized as its context says
        /// and cut to the width.
        /// </summary>
        public Expr BuildAssigned(Expression value, PackedType type, int width)
        {
            Expression bare = value;
            while (bare is ParenthesizedExpression p)
                bare = p.Inner;

            Expr built;
            if (bare is AssignmentPatternExpression pattern)
            {
                if (type == null)
                {
                    Error(pattern.Location, ElaborationCodes.BadSelect,
                        "A '{...} fills a struct or a packed array, and what it is assigned to here is neither; write a value, or { } to join bits.");
                    return Expr.Constant(LogicVec.AllX(width));
                }

                built = Extend(BuildPattern(pattern, type), width, type.Signed);
            }
            else
            {
                built = Build(value, width);
            }

            return built.Width > width ? Expr.Slice(built, 0, width) : built;
        }

        /// <summary>A constant's value given to a type: a parameter's or a variable's, which may be a <c>'{...}</c>.</summary>
        public static bool TryConstantFor(Expression value, PackedType type, Scope scope, DiagnosticBag diagnostics, int width, out LogicVec result)
        {
            Expression bare = value;
            while (bare is ParenthesizedExpression p)
                bare = p.Inner;

            if (!(bare is AssignmentPatternExpression) || type == null)
                return TryConstant(value, scope, diagnostics, out result, width);

            var builder = new ExpressionBuilder(scope, diagnostics, constantOnly: true);
            Expr built = builder.BuildAssigned(value, type, width);
            result = builder.Failed ? default : built.Evaluate(Array.Empty<LogicVec>());
            return !builder.Failed;
        }

        /// <summary>
        /// <c>'{...}</c> laid out on <paramref name="type"/> (IEEE 1800, 10.9.2): a struct's members or a packed
        /// array's elements, either all in order or by name and by index, and <c>default:</c> for every one not
        /// given. Each value is sized as an assignment to its member is, and may be a <c>'{...}</c> itself.
        /// </summary>
        private Expr BuildPattern(AssignmentPatternExpression pattern, PackedType type)
        {
            StructLayout layout = type.Struct;
            if (layout != null && layout.IsUnion)
            {
                Error(pattern.Location, ElaborationCodes.NotYet, "A '{...} for a union is not supported by BitSorter yet; assign one member.");
                return Expr.Constant(LogicVec.AllX(type.Width));
            }

            int count = layout != null ? layout.Fields.Count : type.Count;
            string what = layout != null ? "member" : "element";
            var given = new Expression[count];
            bool positional = false, keyed = false;

            for (int i = 0; i < pattern.Items.Count; i++)
            {
                Expression key = i < pattern.Keys.Count ? pattern.Keys[i] : null;
                if (key == null)
                {
                    positional = true;
                    if (i < count)
                        given[i] = pattern.Items[i];
                    continue;
                }

                keyed = true;
                int slot = SlotOf(key, type);
                if (slot < 0)
                    continue;

                if (given[slot] != null)
                {
                    Error(key.Location, ElaborationCodes.Duplicate, $"This '{{...}} gives {Printer.Print(key)} twice.");
                    continue;
                }

                given[slot] = pattern.Items[i];
            }

            if (positional && keyed)
            {
                Error(pattern.Location, ElaborationCodes.BadSelect, $"A '{{...}} either lists every {what} in order or names them; this one does both.");
                return Expr.Constant(LogicVec.AllX(type.Width));
            }

            if (positional && pattern.Items.Count != count)
            {
                Error(pattern.Location, ElaborationCodes.WidthMismatch,
                    $"This '{{...}} lists {pattern.Items.Count} values in order, for {count} {what}s{(pattern.Default != null ? ", and a default is for one that names them" : "")}.");
                return Expr.Constant(LogicVec.AllX(type.Width));
            }

            Expr result = null;
            for (int slot = 0; slot < count; slot++)
            {
                PackedType part = layout != null ? layout.Fields[slot].Type : type.Element ?? PackedType.Bit;
                Expression value = given[slot] ?? pattern.Default;
                Expr built;

                if (value == null)
                {
                    string name = layout != null ? layout.Fields[slot].Name : $"element {(type.Msb >= type.Lsb ? type.Msb - slot : type.Msb + slot)}";
                    Error(pattern.Location, ElaborationCodes.BadSelect, $"This '{{...}} gives {name} no value: give it one, or add default: and a value for every one not named.");
                    built = Expr.Constant(LogicVec.AllX(part.Width));
                }
                else
                {
                    built = BuildAssigned(value, part, part.Width);
                }

                result = result == null ? built : Expr.Concat(result, built);
            }

            return AsSigned(result ?? Expr.Constant(LogicVec.AllX(type.Width)), type.Signed);
        }

        /// <summary>Which member or element a key in a <c>'{...}</c> names, counted from the first; -1 having said why it names none.</summary>
        private int SlotOf(Expression key, PackedType type)
        {
            if (type.Struct != null)
            {
                if (key is NameExpression name && name.Path.Count == 1)
                {
                    for (int i = 0; i < type.Struct.Fields.Count; i++)
                        if (type.Struct.Fields[i].Name == name.Name)
                            return i;
                }

                NoMember(key.Location, "The struct", type, Printer.Print(key));
                return -1;
            }

            if (!TryInteger(key, _scope, _diagnostics, "a key in a '{...}", out long index))
            {
                Failed = true;
                return -1;
            }

            long slot = type.Msb >= type.Lsb ? type.Msb - index : index - type.Msb;
            if (slot < 0 || slot >= type.Count)
            {
                Error(key.Location, ElaborationCodes.BadSelect, $"{index} is not an index of [{type.Msb}:{type.Lsb}].");
                return -1;
            }

            return (int)slot;
        }

        // -----------------------------------------------------------------
        // Targets of an assignment
        // -----------------------------------------------------------------

        /// <summary>
        /// One piece of what a process writes: bits of a variable, from <see cref="Lowest"/> or, when the
        /// select is only known as the process runs (<c>y[i] = a</c>), from where <see cref="Offset"/> says.
        /// In a memory, <see cref="Word"/> picks the word as the process runs (<c>mem[addr] &lt;= d</c>);
        /// <see cref="Signal"/> is then the lowest word.
        /// </summary>
        public readonly struct ProcPart
        {
            public readonly SignalEntry Signal;
            public readonly int Lowest;
            public readonly int Width;
            public readonly Expr Offset;
            public readonly Expr Word;
            public readonly MemoryInfo Memory;

            public ProcPart(SignalEntry signal, int lowest, int width, Expr offset, Expr word = null, MemoryInfo memory = null)
            {
                Signal = signal;
                Lowest = lowest;
                Width = width;
                Offset = offset;
                Word = word;
                Memory = memory;
            }
        }

        /// <summary>One piece of a target: bits of a signal.</summary>
        public readonly struct Part
        {
            public readonly SignalEntry Signal;
            public readonly int Lowest;
            public readonly int Width;

            public Part(SignalEntry signal, int lowest, int width)
            {
                Signal = signal;
                Lowest = lowest;
                Width = width;
            }
        }

        /// <summary>
        /// What a procedural assignment writes, most significant piece first: a variable, a bit or part
        /// of one -- at a place known now or only as the process runs -- a word of a memory, or a
        /// concatenation of those; here or through the hierarchy.
        /// </summary>
        public bool TryProceduralTarget(Expression target, List<ProcPart> into)
        {
            switch (target)
            {
                case ParenthesizedExpression p:
                    return TryProceduralTarget(p.Inner, into);

                case ConcatenationExpression c:
                {
                    bool ok = true;
                    foreach (Expression item in c.Items)
                        ok &= TryProceduralTarget(item, into);
                    return ok;
                }
            }

            if (!TryLocate(target, procedural: true, null, out ProcPart at))
                return false;

            into.Add(at);
            return true;
        }

        /// <summary>
        /// What an assignment's left side drives, most significant piece first: a signal, a bit or a
        /// constant part of one, a word of a memory, or a concatenation of those. False, having reported
        /// why, otherwise.
        /// </summary>
        public bool TryTarget(Expression target, List<Part> into, Func<NameExpression, SignalEntry> implicitNet)
        {
            switch (target)
            {
                case ParenthesizedExpression p:
                    return TryTarget(p.Inner, into, implicitNet);

                case ConcatenationExpression c:
                {
                    bool ok = true;
                    foreach (Expression item in c.Items)
                        ok &= TryTarget(item, into, implicitNet);
                    return ok;
                }
            }

            if (!TryLocate(target, procedural: false, implicitNet, out ProcPart at))
                return false;

            into.Add(new Part(at.Signal, at.Lowest, at.Width));
            return true;
        }

        /// <summary>
        /// Where one piece of a target is, as its chain of selects finds it: a signal or a word of a memory,
        /// and which of its bits -- from <see cref="Lowest"/>, or from where <see cref="Offset"/> says as the
        /// process runs -- with <see cref="Type"/> saying what those bits are, for the next select (Part L4).
        /// </summary>
        private struct Place
        {
            public SignalEntry Signal;
            public Expr Word;
            public MemoryInfo Memory;
            public int Lowest;
            public Expr Offset;
            public int Width;
            public PackedType Type;

            /// <summary>Whether it is the whole of its signal or word, where an index outside simply misses it.</summary>
            public bool Whole => Offset == null && Lowest == 0 && Width == Signal.Width;

            /// <summary>Its bits from <paramref name="position"/> on, counted from its own lowest.</summary>
            public Place Narrowed(long position, int width, PackedType type)
            {
                Place p = this;
                if (Offset == null)
                    p.Lowest = Lowest + (int)position;
                else if (position != 0)
                    p.Offset = Expr.Apply(BinaryOp.Add, Offset, Int64(position));
                p.Width = width;
                p.Type = type;
                return p;
            }

            /// <summary>Its bits from <paramref name="bits"/> on, a place worked out as the process runs.</summary>
            public Place Moved(Expr bits, int width, PackedType type)
            {
                Place p = this;
                p.Offset = Offset == null && Lowest == 0 ? bits : Expr.Apply(BinaryOp.Add, Offset ?? Int64(Lowest), bits);
                p.Lowest = 0;
                p.Width = width;
                p.Type = type;
                return p;
            }
        }

        /// <summary>
        /// The signal -- or word of a memory -- one piece of a target writes, and which of its bits. A
        /// select only known as the design runs is allowed in a process alone.
        /// </summary>
        private bool TryLocate(Expression target, bool procedural, Func<NameExpression, SignalEntry> implicitNet, out ProcPart at)
        {
            at = default;
            if (!TryPlace(target, procedural, implicitNet, out Place place))
                return false;

            at = new ProcPart(place.Signal, place.Offset == null ? place.Lowest : 0, place.Width, place.Offset, place.Word, place.Memory);
            return true;
        }

        /// <summary>
        /// A target's place, select by select from the signal or the word it starts at: an element of a
        /// packed array, a part, a member of a struct. Only the whole target may be a net declared on the
        /// spot, through <paramref name="implicitNet"/>.
        /// </summary>
        private bool TryPlace(Expression e, bool procedural, Func<NameExpression, SignalEntry> implicitNet, out Place place)
        {
            place = default;
            switch (e)
            {
                case IndexExpression index:
                {
                    int depth = WordDepth(index, out MemoryEntry memory);
                    if (memory != null && depth <= memory.Dims.Length)
                        return TryWordPlace(memory, index, depth, procedural, out place);

                    return TryPlace(index.Target, procedural, null, out Place inner) && TryElementPlace(inner, index, procedural, out place);
                }

                case RangeSelectExpression range:
                    return TryPlace(range.Target, procedural, null, out Place whole) && TryRangePlace(whole, range, procedural, out place);

                case MemberExpression m when Resolve(m, report: false) == null && TypeAt(m.Target) != null:
                {
                    if (!TryPlace(m.Target, procedural, null, out Place outer))
                        return false;

                    StructField field = outer.Type.Struct?.Find(m.Member);
                    if (field == null)
                    {
                        NoMember(m.Location, Printer.Print(m.Target), outer.Type, m.Member);
                        return false;
                    }

                    place = outer.Narrowed(field.Lowest, field.Type.Width, field.Type);
                    return true;
                }

                default:
                    return TryNamedPlace(e, procedural, implicitNet, out place);
            }
        }

        /// <summary>A target that names what it writes: a signal, here or through the hierarchy, or a member of a struct one is.</summary>
        private bool TryNamedPlace(Expression e, bool procedural, Func<NameExpression, SignalEntry> implicitNet, out Place place)
        {
            place = default;
            Entry entry = Resolve(e, report: false);
            if (entry == null && implicitNet != null && e is NameExpression bare && bare.Path.Count == 1)
                entry = implicitNet(bare);

            int lowest = 0;
            PackedType member = null;
            if (entry == null && e is NameExpression path && path.Path.Count > 1 && SignalPrefix(path, out _) != null)
            {
                if (!TryMembers(path, report: true, out SignalEntry whole, out lowest, out member))
                    return false;
                entry = whole;
            }

            string name = Printer.Print(e);
            switch (entry)
            {
                case SignalEntry s when !procedural || s.Kind == SignalKind.Variable:
                    place = new Place { Signal = s, Lowest = lowest, Width = member?.Width ?? s.Width, Type = member ?? PackedOf(s) };
                    return true;

                case SignalEntry s:
                    Error(e.Location, ElaborationCodes.BadLValue, $"{(member != null ? s.FullName.Substring(s.FullName.LastIndexOf('.') + 1) : name)} is a wire, and a process can only write a variable. Declare it logic.");
                    return false;

                case MemoryEntry _:
                    Error(e.Location, ElaborationCodes.BadLValue, $"{name} is a memory: write it a word at a time, as {name}[i].");
                    return false;

                case null when e is NameExpression one && one.Path.Count == 1:
                    Error(e.Location, ElaborationCodes.UnknownName,
                        $"There is no {(procedural ? "variable" : "signal")} called {name} here to assign.");
                    return false;

                case null when e is NameExpression || e is MemberExpression:
                    Resolve(e, report: true);
                    Failed = true;
                    return false;

                case null:
                    Error(e.Location, ElaborationCodes.BadLValue,
                        $"{name} cannot be assigned: the left side must be a {(procedural ? "variable" : "signal")}, a bit or a part of one, or a {{ }} of those.");
                    return false;

                default:
                    Error(e.Location, ElaborationCodes.BadLValue,
                        $"{name} is not a {(procedural ? "variable" : "signal")}, so nothing can be assigned to it.");
                    return false;
            }
        }

        /// <summary>A word of a memory a target writes: <c>mem[a]</c>, <c>grid[r][c]</c>.</summary>
        private bool TryWordPlace(MemoryEntry m, IndexExpression index, int depth, bool procedural, out Place place)
        {
            place = default;
            MemoryInfo memory = m.Memory;

            if (depth < m.Dims.Length)
            {
                Error(index.Location, ElaborationCodes.BadLValue, RowMessage(index, m));
                return false;
            }

            SignalEntry signal;
            Expr word = null;
            MemoryInfo picked = null;
            string runs = $"{Printer.Print(index)} picks its word as the design runs, which only a process may write; an assign needs a constant index.";

            if (m.Dims.Length == 1)
            {
                var quiet = new DiagnosticBag();
                if (TryInteger(index.Index, _scope, quiet, "an index", out long k))
                {
                    if (k < memory.Lowest || k > memory.Highest)
                    {
                        Error(index.Location, ElaborationCodes.BadSelect,
                            $"{Printer.Print(index)} is outside {Printer.Print(index.Target)}, which has words {memory.Lowest} to {memory.Highest}.");
                        return false;
                    }

                    signal = WordEntry(m, memory.Word((int)k));
                }
                else if (!procedural)
                {
                    Error(index.Location, ElaborationCodes.BadSelect, runs);
                    return false;
                }
                else
                {
                    Expr i = BuildSelf(index.Index);
                    if (Failed)
                        return false;
                    word = WordOffset(i, memory);
                    signal = WordEntry(m, memory.First);
                    picked = memory;
                }
            }
            else
            {
                long k = WordIndex(m, index, out Expr variable);
                if (Failed)
                    return false;

                if (k < 0)
                {
                    var dims = new System.Text.StringBuilder();
                    foreach ((int left, int right) in m.Dims)
                        dims.Append('[').Append(left).Append(':').Append(right).Append(']');
                    Error(index.Location, ElaborationCodes.BadSelect, $"{Printer.Print(index)} is outside the memory, which is {dims}.");
                    return false;
                }

                if (variable == null)
                {
                    signal = WordEntry(m, new SignalId(memory.First.Index + (int)k));
                }
                else if (!procedural)
                {
                    Error(index.Location, ElaborationCodes.BadSelect, runs);
                    return false;
                }
                else
                {
                    word = variable;
                    signal = WordEntry(m, memory.First);
                    picked = memory;
                }
            }

            place = new Place { Signal = signal, Word = word, Memory = picked, Width = signal.Width, Type = m.Type ?? PackedOf(signal) };
            return true;
        }

        /// <summary><c>v[i]</c> in a target: a bit of a vector, or an element of a packed array.</summary>
        private bool TryElementPlace(Place inner, IndexExpression index, bool procedural, out Place place)
        {
            place = default;
            PackedType type = inner.Type;
            PackedType element = type.Element ?? PackedType.Bit;
            int width = type.ElementWidth;
            string name = Printer.Print(index.Target);
            var quiet = new DiagnosticBag();

            if (TryInteger(index.Index, _scope, quiet, "an index", out long constant))
            {
                long position = type.Position(constant);
                if (position < 0 || position + width > type.Width)
                {
                    Error(index.Location, ElaborationCodes.BadSelect, $"{Printer.Print(index)} is outside {name}, which is [{type.Msb}:{type.Lsb}].");
                    return false;
                }

                place = inner.Narrowed(position, width, element);
                return true;
            }

            if (!procedural)
            {
                TryInteger(index.Index, _scope, _diagnostics, $"the index into {name}", out _);
                Failed = true;
                return false;
            }

            Expr i = BuildSelf(index.Index);
            if (Failed)
                return false;

            if (inner.Whole && type.Element == null)
            {
                // A bit of a whole vector: an index outside it writes nothing, which the engine sees to.
                place = inner.Moved(OffsetFrom(i, type.Msb, type.Lsb), 1, element);
                return true;
            }

            // An element, or a bit inside a part: x when the index is outside, so that nothing is written
            // rather than the bits of the element next to it.
            bool descending = type.Msb >= type.Lsb;
            Expr at = Linear(i, TypeOf(index.Index).Signed, descending ? +1 : -1, descending ? -type.Lsb : type.Lsb);
            Expr bits = width == 1 ? at : Expr.Apply(BinaryOp.Multiply, at, Int64(width));
            place = inner.Moved(Expr.Conditional(Within(at, type.Count), bits, Expr.Constant(LogicVec.AllX(64, true))), width, element);
            return true;
        }

        /// <summary><c>v[7:4]</c>, <c>v[i +: 4]</c> in a target: a part of a vector, a slice of a packed array.</summary>
        private bool TryRangePlace(Place inner, RangeSelectExpression range, bool procedural, out Place place)
        {
            place = default;
            PackedType type = inner.Type;
            int step = type.ElementWidth;
            bool descending = type.Msb >= type.Lsb;
            string name = Printer.Print(range.Target);
            var quiet = new DiagnosticBag();
            long left, right;

            if (range.Kind == RangeKind.Constant)
            {
                if (!TryInteger(range.Left, _scope, _diagnostics, "a part-select's bound", out left) ||
                    !TryInteger(range.Right, _scope, _diagnostics, "a part-select's bound", out right))
                {
                    Failed = true;
                    return false;
                }
            }
            else
            {
                if (!TryInteger(range.Right, _scope, _diagnostics, "an indexed part-select's width", out long w) || w < 1)
                {
                    Failed = true;
                    return false;
                }

                if (w * step > LogicVec.MaxWidth)
                {
                    Error(range.Location, ElaborationCodes.TooLarge, $"{Printer.Print(range)} is wider than BitSorter allows ({LogicVec.MaxWidth} bits).");
                    return false;
                }

                bool up = range.Kind == RangeKind.IndexedUp;
                if (!TryInteger(range.Left, _scope, quiet, "a part-select's base", out long b))
                {
                    if (!procedural)
                    {
                        TryInteger(range.Left, _scope, _diagnostics, "a part-select's base", out _);
                        Failed = true;
                        return false;
                    }

                    Expr baseExpr = BuildSelf(range.Left);
                    if (Failed)
                        return false;

                    var slice = new PackedType((int)w - 1, 0, false, type.Element, null, null, type.TwoState);
                    if (inner.Whole && type.Element == null && descending)
                    {
                        // Part of a whole vector: bits past its ends are not written, which the engine sees to.
                        Expr offset = OffsetFrom(baseExpr, type.Msb, type.Lsb);
                        if (!up)
                            offset = Expr.Apply(BinaryOp.Subtract, offset, Expr.Constant(LogicVec.FromInt64(w - 1, offset.Width)));
                        place = inner.Moved(offset, (int)w, slice);
                        return true;
                    }

                    bool signed = TypeOf(range.Left).Signed;
                    Expr lowest = descending
                        ? Linear(baseExpr, signed, +1, up ? -type.Lsb : -type.Lsb - (w - 1))
                        : Linear(baseExpr, signed, -1, up ? type.Lsb - (w - 1) : type.Lsb);
                    Expr bits = step == 1 ? lowest : Expr.Apply(BinaryOp.Multiply, lowest, Int64(step));

                    // Inside a part only a slice wholly inside it is written: the bits past its ends are another's.
                    if (!inner.Whole)
                    {
                        Expr fits = Expr.Apply(BinaryOp.LogicalAnd,
                            Expr.Apply(BinaryOp.GreaterOrEqual, lowest, Int64(0)),
                            Expr.Apply(BinaryOp.LessOrEqual, lowest, Int64(type.Count - w)));
                        bits = Expr.Conditional(fits, bits, Expr.Constant(LogicVec.AllX(64, true)));
                    }

                    place = inner.Moved(bits, (int)(w * step), slice);
                    return true;
                }

                left = up ? b + w - 1 : b;
                right = up ? b : b - w + 1;
                if (!descending)
                    (left, right) = (right, left);
            }

            long low = Math.Min(type.Position(left), type.Position(right));
            long width = (Math.Abs(left - right) + 1) * step;

            if (low < 0 || low + width > type.Width)
            {
                Error(range.Location, ElaborationCodes.BadSelect, $"{Printer.Print(range)} reaches outside {name}, which is [{type.Msb}:{type.Lsb}].");
                return false;
            }

            place = inner.Narrowed(low, (int)width, new PackedType((int)left, (int)right, false, type.Element, null, null, type.TwoState));
            return true;
        }

        /// <summary>A word of a memory, as a signal a select can count against.</summary>
        private static SignalEntry WordEntry(MemoryEntry memory, SignalId word) => new SignalEntry
        {
            Signal = word,
            FullName = memory.FullName,
            Width = memory.Memory.Width,
            Msb = memory.Msb,
            Lsb = memory.Lsb,
            Signed = memory.Memory.Signed,
            Kind = SignalKind.Variable,
            Enum = memory.Type?.Enum,
            TwoState = memory.Type?.TwoState ?? false,
            Type = memory.Type,
            Declared = memory.Declared,
        };
    }
}
