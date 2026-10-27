using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>The unary operators an expression can apply.</summary>
    public enum UnaryOp
    {
        Not, Negate, LogicalNot,
        ReduceAnd, ReduceOr, ReduceXor, ReduceNand, ReduceNor, ReduceXnor,

        /// <summary><c>$countones</c>: an <c>int</c>.</summary>
        CountOnes,

        /// <summary><c>$onehot</c>, <c>$onehot0</c>, <c>$isunknown</c>: one bit each.</summary>
        OneHot, OneHot0, IsUnknown,
    }

    /// <summary>The binary operators an expression can apply.</summary>
    public enum BinaryOp
    {
        And, Or, Xor, Xnor,
        Add, Subtract, Multiply, Divide, Modulo,
        Less, LessOrEqual, Greater, GreaterOrEqual,
        Equal, NotEqual, CaseEqual, CaseNotEqual,
        LogicalAnd, LogicalOr,
        ShiftLeft, ShiftRight, ArithmeticShiftLeft, ArithmeticShiftRight,

        /// <summary><c>==?</c> and <c>!=?</c>: x and z on the right match anything.</summary>
        WildcardEqual, WildcardNotEqual,
    }

    /// <summary>
    /// An expression the engine evaluates: what a continuous assignment drives, and later what a
    /// procedural statement computes. Built by <see cref="NetlistBuilder"/> now and by the
    /// elaborator from Part 4.
    /// </summary>
    /// <remarks>
    /// A tree of nodes that each apply one <see cref="LogicVec"/> operator, read through virtual
    /// calls on the simulator's array of current values. Nothing is allocated evaluating a tree whose
    /// values are 64 bits or fewer. Every node knows its result's width, which is the operator's own
    /// (IEEE 1800, table 11-21); widening it to a context is a <see cref="Resize"/> node the builder
    /// or elaborator puts in.
    /// </remarks>
    public abstract partial class Expr
    {
        /// <summary>How wide the value it gives is.</summary>
        public abstract int Width { get; }

        /// <summary>Its value, given every signal's current value by index.</summary>
        public abstract LogicVec Evaluate(LogicVec[] values);

        /// <summary>Adds the signals it reads -- what it is sensitive to -- to <paramref name="reads"/>.</summary>
        /// <remarks>
        /// A call of a function adds its arguments' reads, not what the function's statements read: a
        /// continuous assignment and <c>@*</c> wait on a call's arguments only (IEEE 1800, 9.2.2.2.1). An
        /// <c>always_comb</c> waits on what the functions it calls read as well, through <see cref="CollectCalls"/>.
        /// </remarks>
        public abstract void CollectReads(ICollection<int> reads);

        /// <summary>Adds every function it calls, at any depth of the tree, to <paramref name="calls"/>.</summary>
        internal virtual void CollectCalls(ICollection<FunctionInfo> calls) { }

        /// <summary>
        /// How deep the tree goes, itself counting one: how many evaluations one evaluation of it nests, each a frame on the
        /// machine's stack. A function's calls are held to a stack's worth of them (<see cref="FunctionRunner.MaxFrames"/>,
        /// Part 13f). Every expression with operands walks them here as it does in <see cref="CollectCalls"/>.
        /// </summary>
        internal virtual int Height => 1;

        internal static int HeightOf(Expr e) => e?.Height ?? 0;

        internal static int MaxHeight(IEnumerable<Expr> all)
        {
            int height = 0;
            foreach (Expr e in all)
                height = Math.Max(height, HeightOf(e));
            return height;
        }

        internal static int MaxHeight<T>(IEnumerable<T> all, Func<T, Expr> of)
        {
            int height = 0;
            foreach (T item in all)
                height = Math.Max(height, HeightOf(of(item)));
            return height;
        }

        /// <summary>
        /// Whether it is a read of one whole signal and nothing more -- <c>a</c>, never <c>~a</c>, <c>a[0]</c>
        /// or <c>{a, b}</c> -- and of which. A port connected to a net is joined to it by a zero-delay
        /// assignment of exactly this kind, which is how a waveform finds every name one node has, down
        /// through the blocks (Part 9b-2b).
        /// </summary>
        public virtual bool TryRead(out SignalId signal)
        {
            signal = default;
            return false;
        }

        /// <summary>
        /// What each bit of its value depends on, for the timing (Part 14a): by default every bit on every bit it reads, a
        /// change passed either way. A node that knows better -- a read, a select, a concatenation, a bitwise operator --
        /// says so itself.
        /// </summary>
        /// <param name="first">The timing graph's first node of each signal, by index, and one past the last signal's.</param>
        internal virtual BitDeps Depends(int[] first) => BitDeps.Unknown(this, first);

        /// <summary>
        /// Whether its value can be z, so a driver of it can let go of a bus (Part 14e, for lint): a z written in it, or a
        /// signal read, selected or joined that <paramref name="floats"/> says can be z -- a net passed on, as a port is.
        /// </summary>
        internal virtual bool CanFloat(Func<int, bool> floats) => false;

        /// <summary>Its value, when it is a constant and nothing else.</summary>
        internal virtual bool TryConstant(out LogicVec value)
        {
            value = default;
            return false;
        }

        // -----------------------------------------------------------------
        // Building expressions
        // -----------------------------------------------------------------

        /// <summary>A signal's current value.</summary>
        public static Expr Read(SignalId signal, int width) => new ReadExpr(signal.Index, width);

        /// <summary>A constant.</summary>
        public static Expr Constant(LogicVec value) => new ConstantExpr(value);

        public static Expr Apply(UnaryOp op, Expr a) => new UnaryExpr(op, a);

        public static Expr Apply(BinaryOp op, Expr a, Expr b) => new BinaryExpr(op, a, b);

        /// <summary><c>{high, low}</c>.</summary>
        public static Expr Concat(Expr high, Expr low) => new ConcatExpr(high, low);

        /// <summary><c>{count{a}}</c>.</summary>
        public static Expr Replicate(Expr a, int count) => new ReplicateExpr(a, count);

        /// <summary>The bits <paramref name="lowest"/> up to <paramref name="lowest"/> + <paramref name="width"/> - 1.</summary>
        public static Expr Slice(Expr a, int lowest, int width) => new SliceExpr(a, lowest, width);

        /// <summary><c>a[index]</c>.</summary>
        public static Expr Bit(Expr a, Expr index) => new BitExpr(a, index);

        /// <summary>
        /// An element of a packed array, <c>a[i]</c>, or an indexed part-select, <c>a[i +: 4]</c>, with
        /// <c>i</c> known only as the design runs (Part L4): the <paramref name="width"/> bits from bit
        /// <paramref name="offset"/> times <paramref name="step"/>. A bit outside <paramref name="a"/> reads
        /// x, and every bit does when the offset is unknown; a signed offset may be below 0.
        /// </summary>
        public static Expr Part(Expr a, Expr offset, int step, int width) => new PartExpr(a, offset, step, width);

        /// <summary><c>condition ? whenTrue : whenFalse</c>.</summary>
        public static Expr Conditional(Expr condition, Expr whenTrue, Expr whenFalse) =>
            new ConditionalExpr(condition, whenTrue, whenFalse);

        /// <summary>The value at another width, sign-extended or not: how a context widens an operand.</summary>
        public static Expr Resize(Expr a, int width, bool signExtend) =>
            a.Width == width ? a : new ResizeExpr(a, width, signExtend);

        /// <summary>
        /// <c>mem[i]</c> with <c>i</c> known only as the design runs: the word <paramref name="offset"/>
        /// after <paramref name="first"/>, or x when the offset is unknown or past the memory's
        /// <paramref name="depth"/> words. It reads every word, so it is evaluated again when any changes.
        /// </summary>
        public static Expr WordOf(SignalId first, int depth, int width, Expr offset) => new WordExpr(first.Index, depth, width, offset);

        private sealed class WordExpr : Expr
        {
            private readonly int _first;
            private readonly int _depth;
            private readonly int _width;
            private readonly Expr _offset;
            private readonly LogicVec _unknown;

            public WordExpr(int first, int depth, int width, Expr offset)
            {
                _first = first;
                _depth = depth;
                _width = width;
                _offset = offset ?? throw new ArgumentNullException(nameof(offset));
                _unknown = LogicVec.AllX(width);
            }

            public override int Width => _width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                LogicVec offset = _offset.Evaluate(values);
                if (!offset.IsKnown || (offset.IsSigned && offset[offset.Width - 1] == Logic4.One))
                    return _unknown;

                return offset.TryGetUInt64(out ulong at) && at < (ulong)_depth ? values[_first + (int)at] : _unknown;
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _offset.CollectReads(reads);
                for (int i = 0; i < _depth; i++)
                    reads.Add(_first + i);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _offset.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_offset);

            /// <summary>Bit i of the word is bit i of any word, as the offset picks, and the offset either way.</summary>
            internal override BitDeps Depends(int[] first)
            {
                SourceSet offset = _offset.Depends(first).Union.Through(Sense.Either);
                return BitDeps.Bits(_width, routing: false, i =>
                {
                    var parts = new (SourceSet, Sense)[_depth + 1];
                    parts[0] = (offset, Sense.Positive);
                    for (int w = 0; w < _depth; w++)
                        parts[w + 1] = (i < first[_first + w + 1] - first[_first + w] ? SourceSet.Of(first[_first + w] + i) : SourceSet.Empty, Sense.Positive);
                    return SourceSet.Union(parts);
                });
            }
        }

        /// <summary><c>$signed(a)</c> or <c>$unsigned(a)</c>: the same bits, read with another signedness.</summary>
        public static Expr WithSignedness(Expr a, bool signed) => new SignednessExpr(a, signed);

        /// <summary>The value as a 2-state type holds it, x and z as 0: what a cast to <c>int</c> or <c>bit</c> does.</summary>
        public static Expr AsTwoState(Expr a) => new TwoStateExpr(a);

        private sealed class TwoStateExpr : Expr
        {
            private readonly Expr _a;

            public TwoStateExpr(Expr a) => _a = a ?? throw new ArgumentNullException(nameof(a));

            public override int Width => _a.Width;
            public override LogicVec Evaluate(LogicVec[] values) => _a.Evaluate(values).ToTwoState();
            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);
            internal override BitDeps Depends(int[] first) => _a.Depends(first);
        }

        private sealed class SignednessExpr : Expr
        {
            private readonly Expr _a;
            private readonly bool _signed;

            public SignednessExpr(Expr a, bool signed)
            {
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _signed = signed;
            }

            public override int Width => _a.Width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                LogicVec v = _a.Evaluate(values);
                return _signed ? v.AsSigned() : v.AsUnsigned();
            }

            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);

            internal override bool CanFloat(Func<int, bool> floats) => _a.CanFloat(floats);
            internal override BitDeps Depends(int[] first) => _a.Depends(first);
        }

        // -----------------------------------------------------------------
        // The nodes
        // -----------------------------------------------------------------

        private sealed class ReadExpr : Expr
        {
            private readonly int _signal;
            private readonly int _width;

            public ReadExpr(int signal, int width)
            {
                _signal = signal;
                _width = width;
            }

            public override int Width => _width;
            public override LogicVec Evaluate(LogicVec[] values) => values[_signal];
            public override void CollectReads(ICollection<int> reads) => reads.Add(_signal);

            internal override bool CanFloat(Func<int, bool> floats) => floats != null && floats(_signal);

            internal override BitDeps Depends(int[] first)
            {
                int bits = first[_signal + 1] - first[_signal];
                return BitDeps.Bits(_width, routing: true, i => i < bits ? SourceSet.Of(first[_signal] + i) : SourceSet.Empty);
            }

            public override bool TryRead(out SignalId signal)
            {
                signal = new SignalId(_signal);
                return true;
            }
        }

        private sealed class ConstantExpr : Expr
        {
            private readonly LogicVec _value;

            public ConstantExpr(LogicVec value) => _value = value;

            public override int Width => _value.Width;
            public override LogicVec Evaluate(LogicVec[] values) => _value;
            public override void CollectReads(ICollection<int> reads) { }

            internal override bool CanFloat(Func<int, bool> floats)
            {
                for (int i = 0; i < _value.Width; i++)
                    if (_value[i] == Logic4.Z)
                        return true;
                return false;
            }
            internal override BitDeps Depends(int[] first) => BitDeps.Bits(_value.Width, routing: true, i => SourceSet.Empty);

            internal override bool TryConstant(out LogicVec value)
            {
                value = _value;
                return true;
            }
        }

        private sealed class UnaryExpr : Expr
        {
            private readonly UnaryOp _op;
            private readonly Expr _a;

            public UnaryExpr(UnaryOp op, Expr a)
            {
                _op = op;
                _a = a ?? throw new ArgumentNullException(nameof(a));
            }

            public override int Width => _op == UnaryOp.Not || _op == UnaryOp.Negate ? _a.Width : _op == UnaryOp.CountOnes ? 32 : 1;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                LogicVec a = _a.Evaluate(values);
                switch (_op)
                {
                    case UnaryOp.Not: return LogicVec.Not(a);
                    case UnaryOp.Negate: return LogicVec.Negate(a);
                    case UnaryOp.LogicalNot: return LogicVec.LogicalNot(a);
                    case UnaryOp.ReduceAnd: return LogicVec.ReduceAnd(a);
                    case UnaryOp.ReduceOr: return LogicVec.ReduceOr(a);
                    case UnaryOp.ReduceXor: return LogicVec.ReduceXor(a);
                    case UnaryOp.ReduceNand: return LogicVec.ReduceNand(a);
                    case UnaryOp.ReduceNor: return LogicVec.ReduceNor(a);
                    case UnaryOp.CountOnes: return LogicVec.CountOnes(a);
                    case UnaryOp.OneHot: return LogicVec.OneHot(a);
                    case UnaryOp.OneHot0: return LogicVec.OneHot0(a);
                    case UnaryOp.IsUnknown: return LogicVec.IsUnknown(a);
                    default: return LogicVec.ReduceXnor(a);
                }
            }

            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);

            /// <summary>A NOT inverts each bit; a negation is a sum; AND and OR of all the bits pass a change the same way round, NAND, NOR and ! the other.</summary>
            internal override BitDeps Depends(int[] first)
            {
                BitDeps a = _a.Depends(first);
                switch (_op)
                {
                    case UnaryOp.Not:
                        return BitDeps.Bits(Width, routing: false, i => a.Of(i).Through(Sense.Negative));
                    case UnaryOp.Negate:
                        return BitDeps.Upward(Width, a);
                    case UnaryOp.ReduceAnd:
                    case UnaryOp.ReduceOr:
                        return BitDeps.Uniform(Width, a.Union);
                    case UnaryOp.LogicalNot:
                    case UnaryOp.ReduceNand:
                    case UnaryOp.ReduceNor:
                        return BitDeps.Uniform(Width, a.Union.Through(Sense.Negative));
                    default:
                        return BitDeps.Uniform(Width, a.Union.Through(Sense.Either));
                }
            }
        }

        private sealed class BinaryExpr : Expr
        {
            private readonly BinaryOp _op;
            private readonly Expr _a;
            private readonly Expr _b;

            public BinaryExpr(BinaryOp op, Expr a, Expr b)
            {
                _op = op;
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _b = b ?? throw new ArgumentNullException(nameof(b));
            }

            public override int Width
            {
                get
                {
                    switch (_op)
                    {
                        case BinaryOp.ShiftLeft: case BinaryOp.ShiftRight:
                        case BinaryOp.ArithmeticShiftLeft: case BinaryOp.ArithmeticShiftRight:
                            return _a.Width;
                        case BinaryOp.And: case BinaryOp.Or: case BinaryOp.Xor: case BinaryOp.Xnor:
                        case BinaryOp.Add: case BinaryOp.Subtract: case BinaryOp.Multiply:
                        case BinaryOp.Divide: case BinaryOp.Modulo:
                            return Math.Max(_a.Width, _b.Width);
                        default:
                            return 1;
                    }
                }
            }

            public override LogicVec Evaluate(LogicVec[] values)
            {
                LogicVec a = _a.Evaluate(values);
                LogicVec b = _b.Evaluate(values);

                switch (_op)
                {
                    case BinaryOp.And: return LogicVec.And(a, b);
                    case BinaryOp.Or: return LogicVec.Or(a, b);
                    case BinaryOp.Xor: return LogicVec.Xor(a, b);
                    case BinaryOp.Xnor: return LogicVec.Xnor(a, b);
                    case BinaryOp.Add: return LogicVec.Add(a, b);
                    case BinaryOp.Subtract: return LogicVec.Subtract(a, b);
                    case BinaryOp.Multiply: return LogicVec.Multiply(a, b);
                    case BinaryOp.Divide: return LogicVec.Divide(a, b);
                    case BinaryOp.Modulo: return LogicVec.Modulo(a, b);
                    case BinaryOp.Less: return LogicVec.LessThan(a, b);
                    case BinaryOp.LessOrEqual: return LogicVec.LessOrEqual(a, b);
                    case BinaryOp.Greater: return LogicVec.GreaterThan(a, b);
                    case BinaryOp.GreaterOrEqual: return LogicVec.GreaterOrEqual(a, b);
                    case BinaryOp.Equal: return LogicVec.Equal(a, b);
                    case BinaryOp.NotEqual: return LogicVec.NotEqual(a, b);
                    case BinaryOp.CaseEqual: return LogicVec.CaseEqual(a, b);
                    case BinaryOp.CaseNotEqual: return LogicVec.CaseNotEqual(a, b);
                    case BinaryOp.LogicalAnd: return LogicVec.LogicalAnd(a, b);
                    case BinaryOp.LogicalOr: return LogicVec.LogicalOr(a, b);
                    case BinaryOp.ShiftLeft: return LogicVec.ShiftLeft(a, b);
                    case BinaryOp.ShiftRight: return LogicVec.ShiftRight(a, b);
                    case BinaryOp.ArithmeticShiftLeft: return LogicVec.ArithmeticShiftLeft(a, b);
                    case BinaryOp.WildcardEqual: return LogicVec.WildcardEqual(a, b);
                    case BinaryOp.WildcardNotEqual: return LogicVec.WildcardNotEqual(a, b);
                    default: return LogicVec.ArithmeticShiftRight(a, b);
                }
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _a.CollectReads(reads);
                _b.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _a.CollectCalls(calls);
                _b.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_a), HeightOf(_b));

            /// <summary>
            /// AND and OR bit by bit, the same way round; XOR either way; a sum or a difference from the bottom bit up; a shift
            /// by a constant moves the bits; &amp;&amp; and || the same way round. Anything else mixes every bit of both.
            /// </summary>
            internal override BitDeps Depends(int[] first)
            {
                BitDeps a = _a.Depends(first), b = _b.Depends(first);
                switch (_op)
                {
                    case BinaryOp.And:
                    case BinaryOp.Or:
                        return BitDeps.Bits(Width, routing: false, i => SourceSet.Union((a.Of(i), Sense.Positive), (b.Of(i), Sense.Positive)));
                    case BinaryOp.Xor:
                    case BinaryOp.Xnor:
                        return BitDeps.Bits(Width, routing: false, i => SourceSet.Union((a.Of(i), Sense.Either), (b.Of(i), Sense.Either)));
                    case BinaryOp.Add:
                    case BinaryOp.Subtract:
                        return BitDeps.Upward(Width, a, b);
                    case BinaryOp.LogicalAnd:
                    case BinaryOp.LogicalOr:
                        return BitDeps.Uniform(Width, SourceSet.Union((a.Union, Sense.Positive), (b.Union, Sense.Positive)));
                    case BinaryOp.ShiftLeft:
                    case BinaryOp.ArithmeticShiftLeft:
                    case BinaryOp.ShiftRight:
                    case BinaryOp.ArithmeticShiftRight:
                        if (_b.TryConstant(out LogicVec by) && by.TryGetUInt64(out ulong k) && k <= int.MaxValue)
                        {
                            int n = (int)k, top = _a.Width - 1;
                            if (_op == BinaryOp.ShiftLeft || _op == BinaryOp.ArithmeticShiftLeft)
                                return BitDeps.Bits(Width, a.Routing, i => (long)i - n >= 0 ? a.Of(i - n) : SourceSet.Empty);
                            bool arithmetic = _op == BinaryOp.ArithmeticShiftRight;
                            return BitDeps.Bits(Width, a.Routing, i => (long)i + n <= top ? a.Of(i + n) : arithmetic ? a.Of(top) : SourceSet.Empty);
                        }
                        return BitDeps.Uniform(Width, BitDeps.Mixed(a, b));
                    default:
                        return BitDeps.Uniform(Width, BitDeps.Mixed(a, b));
                }
            }
        }

        private sealed class ConcatExpr : Expr
        {
            private readonly Expr _high;
            private readonly Expr _low;

            public ConcatExpr(Expr high, Expr low)
            {
                _high = high ?? throw new ArgumentNullException(nameof(high));
                _low = low ?? throw new ArgumentNullException(nameof(low));
            }

            public override int Width => _high.Width + _low.Width;
            public override LogicVec Evaluate(LogicVec[] values) => LogicVec.Concat(_high.Evaluate(values), _low.Evaluate(values));

            internal override bool CanFloat(Func<int, bool> floats) => _high.CanFloat(floats) || _low.CanFloat(floats);

            public override void CollectReads(ICollection<int> reads)
            {
                _high.CollectReads(reads);
                _low.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _high.CollectCalls(calls);
                _low.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_high), HeightOf(_low));

            internal override BitDeps Depends(int[] first)
            {
                BitDeps high = _high.Depends(first), low = _low.Depends(first);
                int below = _low.Width;
                return BitDeps.Bits(Width, high.Routing && low.Routing, i => i < below ? low.Of(i) : high.Of(i - below));
            }
        }

        private sealed class ReplicateExpr : Expr
        {
            private readonly Expr _a;
            private readonly int _count;

            public ReplicateExpr(Expr a, int count)
            {
                _a = a ?? throw new ArgumentNullException(nameof(a));
                if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "A replication repeats at least once.");
                _count = count;
            }

            public override int Width => _a.Width * _count;
            public override LogicVec Evaluate(LogicVec[] values) => LogicVec.Replicate(_a.Evaluate(values), _count);
            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);

            internal override bool CanFloat(Func<int, bool> floats) => _a.CanFloat(floats);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);

            internal override BitDeps Depends(int[] first)
            {
                BitDeps a = _a.Depends(first);
                int each = _a.Width;
                return BitDeps.Bits(Width, a.Routing, i => each == 0 ? SourceSet.Empty : a.Of(i % each));
            }
        }

        private sealed class SliceExpr : Expr
        {
            private readonly Expr _a;
            private readonly int _lowest;
            private readonly int _width;

            public SliceExpr(Expr a, int lowest, int width)
            {
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _lowest = lowest;
                _width = width;
            }

            public override int Width => _width;
            public override LogicVec Evaluate(LogicVec[] values) => _a.Evaluate(values).Slice(_lowest, _width);

            internal override bool CanFloat(Func<int, bool> floats) => _a.CanFloat(floats);
            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);

            internal override BitDeps Depends(int[] first)
            {
                BitDeps a = _a.Depends(first);
                return BitDeps.Bits(_width, a.Routing, i => a.Of(_lowest + i));
            }
        }

        private sealed class BitExpr : Expr
        {
            private readonly Expr _a;
            private readonly Expr _index;

            public BitExpr(Expr a, Expr index)
            {
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _index = index ?? throw new ArgumentNullException(nameof(index));
            }

            public override int Width => 1;
            public override LogicVec Evaluate(LogicVec[] values) => _a.Evaluate(values).Bit(_index.Evaluate(values));

            internal override bool CanFloat(Func<int, bool> floats) => _a.CanFloat(floats);

            public override void CollectReads(ICollection<int> reads)
            {
                _a.CollectReads(reads);
                _index.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _a.CollectCalls(calls);
                _index.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_a), HeightOf(_index));

            /// <summary>The bit an index picks: any bit of the value the same way round, and the index either way.</summary>
            internal override BitDeps Depends(int[] first) =>
                BitDeps.Uniform(1, SourceSet.Union((_a.Depends(first).Union, Sense.Positive), (_index.Depends(first).Union, Sense.Either)));
        }

        private sealed class PartExpr : Expr
        {
            private readonly Expr _a;
            private readonly Expr _offset;
            private readonly int _step;
            private readonly int _width;
            private readonly LogicVec _unknown;

            internal override bool CanFloat(Func<int, bool> floats) => _a.CanFloat(floats);

            public PartExpr(Expr a, Expr offset, int step, int width)
            {
                if (step < 1)
                    throw new ArgumentOutOfRangeException(nameof(step), step, "A step is at least one bit.");

                _a = a ?? throw new ArgumentNullException(nameof(a));
                _offset = offset ?? throw new ArgumentNullException(nameof(offset));
                _step = step;
                _width = width;
                _unknown = LogicVec.AllX(width);
            }

            public override int Width => _width;

            public override LogicVec Evaluate(LogicVec[] values)
            {
                LogicVec offset = _offset.Evaluate(values);
                if (!offset.IsKnown)
                    return _unknown;

                long at;
                if (offset.IsSigned)
                {
                    if (!offset.TryGetInt64(out at))
                        return _unknown;
                }
                else if (offset.TryGetUInt64(out ulong unsigned) && unsigned <= long.MaxValue)
                {
                    at = (long)unsigned;
                }
                else
                {
                    return _unknown;
                }

                // Far enough past either end, nothing of the value is in it; that also keeps the product small.
                LogicVec value = _a.Evaluate(values);
                long reach = (value.Width + (long)_width) / _step + 1;
                if (at > reach || at < -reach)
                    return _unknown;

                long lowest = at * _step;
                if (lowest >= value.Width || lowest + _width <= 0)
                    return _unknown;

                return value.Slice((int)lowest, _width);
            }

            public override void CollectReads(ICollection<int> reads)
            {
                _a.CollectReads(reads);
                _offset.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _a.CollectCalls(calls);
                _offset.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_a), HeightOf(_offset));

            internal override BitDeps Depends(int[] first) =>
                BitDeps.Uniform(_width, SourceSet.Union((_a.Depends(first).Union, Sense.Positive), (_offset.Depends(first).Union, Sense.Either)));
        }

        private sealed class ConditionalExpr : Expr
        {
            private readonly Expr _condition;
            private readonly Expr _whenTrue;
            private readonly Expr _whenFalse;

            public ConditionalExpr(Expr condition, Expr whenTrue, Expr whenFalse)
            {
                _condition = condition ?? throw new ArgumentNullException(nameof(condition));
                _whenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
                _whenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
            }

            public override int Width => Math.Max(_whenTrue.Width, _whenFalse.Width);

            public override LogicVec Evaluate(LogicVec[] values) =>
                LogicVec.Conditional(_condition.Evaluate(values), _whenTrue.Evaluate(values), _whenFalse.Evaluate(values));

            public override void CollectReads(ICollection<int> reads)
            {
                _condition.CollectReads(reads);
                _whenTrue.CollectReads(reads);
                _whenFalse.CollectReads(reads);
            }

            internal override void CollectCalls(ICollection<FunctionInfo> calls)
            {
                _condition.CollectCalls(calls);
                _whenTrue.CollectCalls(calls);
                _whenFalse.CollectCalls(calls);
            }

            internal override int Height => 1 + Math.Max(HeightOf(_condition), Math.Max(HeightOf(_whenTrue), HeightOf(_whenFalse)));

            internal override bool CanFloat(Func<int, bool> floats) => _whenTrue.CanFloat(floats) || _whenFalse.CanFloat(floats);

            /// <summary>Each bit of either side the same way round, and the condition, either way, under every bit.</summary>
            internal override BitDeps Depends(int[] first)
            {
                SourceSet condition = _condition.Depends(first).Union;
                BitDeps t = _whenTrue.Depends(first), f = _whenFalse.Depends(first);
                return BitDeps.Bits(Width, routing: false, i => SourceSet.Union((t.Of(i), Sense.Positive), (f.Of(i), Sense.Positive), (condition, Sense.Either)));
            }
        }

        private sealed class ResizeExpr : Expr
        {
            private readonly Expr _a;
            private readonly int _width;
            private readonly bool _signExtend;

            public ResizeExpr(Expr a, int width, bool signExtend)
            {
                _a = a ?? throw new ArgumentNullException(nameof(a));
                _width = width;
                _signExtend = signExtend;
            }

            public override int Width => _width;
            public override LogicVec Evaluate(LogicVec[] values) => _a.Evaluate(values).Extend(_width, _signExtend);
            public override void CollectReads(ICollection<int> reads) => _a.CollectReads(reads);
            internal override void CollectCalls(ICollection<FunctionInfo> calls) => _a.CollectCalls(calls);

            internal override int Height => 1 + HeightOf(_a);

            internal override bool CanFloat(Func<int, bool> floats) => _a.CanFloat(floats);

            /// <summary>Widened, the bits above are the top bit again when the sign is extended, and nothing otherwise.</summary>
            internal override BitDeps Depends(int[] first)
            {
                BitDeps a = _a.Depends(first);
                int top = _a.Width - 1;
                return BitDeps.Bits(_width, a.Routing, i => i <= top ? a.Of(i) : _signExtend ? a.Of(top) : SourceSet.Empty);
            }
        }
    }
}
