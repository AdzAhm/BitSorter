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
                        default: return new TypeInfo(1, false);
                    }

                case IndexExpression index:
                    return MemoryOf(index.Target) is MemoryEntry memory
                        ? new TypeInfo(memory.Memory.Width, memory.Memory.Signed)
                        : new TypeInfo(1, false);

                case RangeSelectExpression r:
                    return new TypeInfo(SelectWidth(r), false);

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
                        case "&": case "|": case "^": case "~^": case "^~":
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
                        default: return new TypeInfo(1, false);
                    }

                case CastExpression cast:
                {
                    TypeInfo inner = TypeOf(cast.Value);
                    if (cast.Width != null)
                        return new TypeInfo((int)Math.Max(1, Math.Min(LogicVec.MaxWidth, ConstantOf(cast.Width, report: false))), inner.Signed);
                    if (cast.TargetText == "signed") return new TypeInfo(inner.Width, true);
                    if (cast.TargetText == "unsigned") return new TypeInfo(inner.Width, false);
                    if (_scope.Lookup(cast.TargetText) is TypeEntry type) return new TypeInfo(type.Width, type.Signed);
                    return inner;
                }

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

                case StringLiteral s:
                    return Expr.Constant(StringBits(s.Value));

                case RealLiteral r:
                    Error(r.Location, ElaborationCodes.NotYet, $"{r.Text} is a real number; BitSorter simulates whole numbers only.");
                    return Expr.Constant(LogicVec.AllX(1));

                case MemberExpression m:
                    return BuildNamed(m);

                case AssignmentPatternExpression p:
                    Error(p.Location, ElaborationCodes.NotYet, "'{...} is not supported by BitSorter yet.");
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

        /// <summary>The declared range a select on <paramref name="target"/> counts against.</summary>
        private void RangeOf(Expression target, Expr built, out int msb, out int lsb)
        {
            if (Resolve(target, report: false) is SignalEntry s)
            {
                msb = s.Msb;
                lsb = s.Lsb;
                return;
            }

            if (target is IndexExpression word && MemoryOf(word.Target) is MemoryEntry memory)
            {
                msb = memory.Msb;
                lsb = memory.Lsb;
                return;
            }

            msb = built.Width - 1;
            lsb = 0;
        }

        /// <summary>The bit position, from 0, of index <paramref name="index"/> in a vector declared [msb:lsb].</summary>
        private static long Position(long index, int msb, int lsb) => msb >= lsb ? index - lsb : lsb - index;

        private Expr BuildIndex(IndexExpression index)
        {
            if (MemoryOf(index.Target) is MemoryEntry memory)
                return BuildWord(memory, index);

            Expr target = BuildSelf(index.Target);
            RangeOf(index.Target, target, out int msb, out int lsb);

            var quiet = new DiagnosticBag();
            if (TryInteger(index.Index, _scope, quiet, "an index", out long constant))
            {
                long position = Position(constant, msb, lsb);
                if (position < 0 || position >= target.Width)
                    return Expr.Constant(LogicVec.AllX(1));   // reading outside the vector gives x, as the standard says
                return Expr.Slice(target, (int)position, 1);
            }

            if (_constant)
            {
                TryInteger(index.Index, _scope, _diagnostics, "an index", out _);
                Failed = true;
                return Expr.Constant(LogicVec.AllX(1));
            }

            // A variable index: shift it to count from bit 0, then the engine's own bit-select.
            Expr i = BuildSelf(index.Index);
            Expr offset = OffsetFrom(i, msb, lsb);
            return Expr.Bit(target, offset);
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
            Expr target = BuildSelf(range.Target);
            RangeOf(range.Target, target, out int msb, out int lsb);

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

                long low = Math.Min(Position(left, msb, lsb), Position(right, msb, lsb));
                int width = (int)(Math.Abs(left - right) + 1);

                if (low < 0 || low + width > target.Width)
                {
                    Error(range.Location, ElaborationCodes.BadSelect,
                        $"{Printer.Print(range)} reaches outside {Printer.Print(range.Target)}, which is [{msb}:{lsb}].");
                    return Expr.Constant(LogicVec.AllX(width));
                }

                return Expr.Slice(target, (int)low, width);
            }

            if (!TryInteger(range.Right, _scope, _diagnostics, "an indexed part-select's width", out long w) || w < 1)
            {
                Failed = true;
                return Expr.Constant(LogicVec.AllX(1));
            }

            int selectWidth = (int)w;
            var quiet = new DiagnosticBag();

            if (TryInteger(range.Left, _scope, quiet, "a part-select's base", out long b))
            {
                long top = range.Kind == RangeKind.IndexedUp ? b + selectWidth - 1 : b;
                long bottom = range.Kind == RangeKind.IndexedUp ? b : b - selectWidth + 1;
                long low = Math.Min(Position(top, msb, lsb), Position(bottom, msb, lsb));

                if (low < 0 || low + selectWidth > target.Width)
                    return Expr.Constant(LogicVec.AllX(selectWidth));

                return Expr.Slice(target, (int)low, selectWidth);
            }

            if (msb < lsb)
            {
                Error(range.Location, ElaborationCodes.NotYet, "A variable part-select of a vector declared low to high is not supported by BitSorter yet.");
                return Expr.Constant(LogicVec.AllX(selectWidth));
            }

            // A variable base: shift it down to bit 0, then take the bits.
            Expr baseExpr = BuildSelf(range.Left);
            Expr offset = OffsetFrom(baseExpr, msb, lsb);
            if (range.Kind == RangeKind.IndexedDown)
                offset = Expr.Apply(BinaryOp.Subtract, offset, Expr.Constant(LogicVec.FromInt64(selectWidth - 1, offset.Width)));

            // x above the vector, so bits selected past its top read x, as the standard says.
            Expr padded = Expr.Concat(Expr.Constant(LogicVec.AllX(selectWidth)), target);
            Expr shifted = Expr.Apply(BinaryOp.ShiftRight, padded, offset);
            return Expr.Slice(shifted, 0, selectWidth);
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

                case "<<": return Shift(BinaryOp.ShiftLeft, b, width, signed);
                case ">>": return Shift(BinaryOp.ShiftRight, b, width, signed);
                case "<<<": return Shift(BinaryOp.ArithmeticShiftLeft, b, width, signed);
                case ">>>": return Shift(BinaryOp.ArithmeticShiftRight, b, width, signed);

                case "**": return Power(b, width, signed);

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
                        return Expr.Constant(LogicVec.FromInt64(TypeOf(call.Arguments[0]).Width, 32, true));
                    break;
            }

            Error(call.Location, ElaborationCodes.NotYet, call.Name.StartsWith("$", StringComparison.Ordinal)
                ? $"{call.Name} is not supported here by BitSorter yet."
                : $"Calling the function {call.Name} arrives with behavioural code, in Part 5.");
            return Expr.Constant(LogicVec.AllX(1));
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

            // state_t'(n): the value at the type's width, then read with its signedness.
            if (_scope.Lookup(cast.TargetText) is TypeEntry type)
                return Expr.WithSignedness(Extend(value, type.Width, inner.Signed), type.Signed);

            Error(cast.Location, ElaborationCodes.NotYet, $"A cast to {cast.TargetText} is not supported by BitSorter yet.");
            return value;
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
        /// The signal -- or word of a memory -- one piece of a target writes, and which of its bits. A
        /// select only known as the design runs is allowed in a process alone.
        /// </summary>
        private bool TryLocate(Expression target, bool procedural, Func<NameExpression, SignalEntry> implicitNet, out ProcPart at)
        {
            at = default;
            Expression subject = target;
            Expression select = null;

            // The last select picks bits -- unless it is the one picking a memory's word.
            if (target is IndexExpression || target is RangeSelectExpression)
            {
                Expression inner = target is IndexExpression i ? i.Target : ((RangeSelectExpression)target).Target;
                if (!(target is IndexExpression && MemoryOf(inner) != null))
                {
                    subject = inner;
                    select = target;
                }
            }

            SignalEntry signal;
            Expr word = null;
            MemoryInfo memory = null;

            if (subject is IndexExpression pick && MemoryOf(pick.Target) is MemoryEntry m)
            {
                memory = m.Memory;
                var quiet = new DiagnosticBag();

                if (TryInteger(pick.Index, _scope, quiet, "an index", out long k))
                {
                    if (k < memory.Lowest || k > memory.Highest)
                    {
                        Error(pick.Location, ElaborationCodes.BadSelect,
                            $"{Printer.Print(pick)} is outside {Printer.Print(pick.Target)}, which has words {memory.Lowest} to {memory.Highest}.");
                        return false;
                    }

                    signal = WordEntry(m, memory.Word((int)k));
                    memory = null;
                }
                else if (!procedural)
                {
                    Error(pick.Location, ElaborationCodes.BadSelect,
                        $"{Printer.Print(pick)} picks its word as the design runs, which only a process may write; an assign needs a constant index.");
                    return false;
                }
                else
                {
                    Expr index = BuildSelf(pick.Index);
                    if (Failed)
                        return false;
                    word = WordOffset(index, memory);
                    signal = WordEntry(m, memory.First);
                }
            }
            else
            {
                Entry entry = Resolve(subject, report: false);
                if (entry == null && implicitNet != null && select == null && subject is NameExpression bare && bare.Path.Count == 1)
                    entry = implicitNet(bare);

                string name = Printer.Print(subject);
                switch (entry)
                {
                    case SignalEntry s when !procedural || s.Kind == SignalKind.Variable:
                        signal = s;
                        break;

                    case SignalEntry _:
                        Error(subject.Location, ElaborationCodes.BadLValue, $"{name} is a wire, and a process can only write a variable. Declare it logic.");
                        return false;

                    case MemoryEntry _:
                        Error(subject.Location, ElaborationCodes.BadLValue, $"{name} is a memory: write it a word at a time, as {name}[i].");
                        return false;

                    case null when subject is NameExpression one && one.Path.Count == 1:
                        Error(subject.Location, ElaborationCodes.UnknownName,
                            $"There is no {(procedural ? "variable" : "signal")} called {name} here to assign.");
                        return false;

                    case null when subject is NameExpression || subject is MemberExpression:
                        Resolve(subject, report: true);
                        Failed = true;
                        return false;

                    case null:
                        Error(target.Location, ElaborationCodes.BadLValue,
                            $"{Printer.Print(target)} cannot be assigned: the left side must be a {(procedural ? "variable" : "signal")}, a bit or a part of one, or a {{ }} of those.");
                        return false;

                    default:
                        Error(subject.Location, ElaborationCodes.BadLValue,
                            $"{name} is not a {(procedural ? "variable" : "signal")}, so nothing can be assigned to it.");
                        return false;
                }
            }

            if (select == null)
            {
                at = new ProcPart(signal, 0, signal.Width, null, word, memory);
                return true;
            }

            return TrySelect(select, signal, procedural, word, memory, out at);
        }

        /// <summary>A bit or part select on the signal or word a target writes.</summary>
        private bool TrySelect(Expression select, SignalEntry s, bool procedural, Expr word, MemoryInfo memory, out ProcPart at)
        {
            at = default;
            string name = Printer.Print(select is IndexExpression ix ? ix.Target : ((RangeSelectExpression)select).Target);
            var quiet = new DiagnosticBag();

            if (select is IndexExpression index)
            {
                if (TryInteger(index.Index, _scope, quiet, "an index", out long constant))
                {
                    long position = Position(constant, s.Msb, s.Lsb);
                    if (position < 0 || position >= s.Width)
                    {
                        Error(index.Location, ElaborationCodes.BadSelect, $"{Printer.Print(index)} is outside {name}, which is [{s.Msb}:{s.Lsb}].");
                        return false;
                    }

                    at = new ProcPart(s, (int)position, 1, null, word, memory);
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
                at = new ProcPart(s, 0, 1, OffsetFrom(i, s.Msb, s.Lsb), word, memory);
                return true;
            }

            var range = (RangeSelectExpression)select;
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

                if (!TryInteger(range.Left, _scope, quiet, "a part-select's base", out long b))
                {
                    if (!procedural)
                    {
                        TryInteger(range.Left, _scope, _diagnostics, "a part-select's base", out _);
                        Failed = true;
                        return false;
                    }

                    if (s.Msb < s.Lsb)
                    {
                        Error(range.Location, ElaborationCodes.NotYet, "A variable part-select of a vector declared low to high is not supported by BitSorter yet.");
                        return false;
                    }

                    Expr offset = OffsetFrom(BuildSelf(range.Left), s.Msb, s.Lsb);
                    if (range.Kind == RangeKind.IndexedDown)
                        offset = Expr.Apply(BinaryOp.Subtract, offset, Expr.Constant(LogicVec.FromInt64(w - 1, offset.Width)));
                    if (Failed)
                        return false;

                    at = new ProcPart(s, 0, (int)w, offset, word, memory);
                    return true;
                }

                left = range.Kind == RangeKind.IndexedUp ? b + w - 1 : b;
                right = range.Kind == RangeKind.IndexedUp ? b : b - w + 1;
            }

            long low = Math.Min(Position(left, s.Msb, s.Lsb), Position(right, s.Msb, s.Lsb));
            int width = (int)(Math.Abs(left - right) + 1);

            if (low < 0 || low + width > s.Width)
            {
                Error(range.Location, ElaborationCodes.BadSelect, $"{Printer.Print(range)} reaches outside {name}, which is [{s.Msb}:{s.Lsb}].");
                return false;
            }

            at = new ProcPart(s, (int)low, width, null, word, memory);
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
            Declared = memory.Declared,
        };
    }
}
