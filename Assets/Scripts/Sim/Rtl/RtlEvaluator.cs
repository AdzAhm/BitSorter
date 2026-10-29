using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// Every net of an <see cref="RtlNetlist"/> worked out from the values of what feeds it (Part 15h): its inputs, and the
    /// outputs of its instances and processes, read off the design's signals at a moment. Each cell applies the engine's own
    /// operator (<see cref="Expr.Apply(UnaryOp, LogicVec)"/> and the rest), and a join puts its drivers together as the engine
    /// does -- so where the netlist is the design, every net it names a signal of reads what the engine has there.
    /// </summary>
    /// <remarks>
    /// What the tests hold the extractor to, and what the workbench reads the value of a wire inside an expression from: an
    /// adder's output in <c>assign y = a + b + c</c> is a net no signal holds. A loop of logic is cut where it closes, each net
    /// it passes through read off its signal when it has one.
    /// </remarks>
    public static class RtlEvaluator
    {
        /// <summary>Every net's value, by <see cref="RtlNet.Id"/>, with the design's signals' values by index in <paramref name="signals"/>.</summary>
        public static LogicVec[] Evaluate(RtlNetlist netlist, LogicVec[] signals)
        {
            if (netlist == null)
                throw new ArgumentNullException(nameof(netlist));
            if (signals == null)
                throw new ArgumentNullException(nameof(signals));

            IReadOnlyList<RtlNet> nets = netlist.Nets;
            var values = new LogicVec[nets.Count];
            var known = new bool[nets.Count];

            // What nothing in the netlist works out: its inputs, and what its boxes drive.
            foreach (RtlNet net in nets)
            {
                RtlCell driver = net.Driver >= 0 ? netlist.Cells[net.Driver] : null;
                if (driver == null || IsBox(driver))
                {
                    // A register of some of a signal's bits holds those bits of it.
                    values[net.Id] = net.Signal >= 0 ? signals[net.Signal]
                        : driver != null && driver.Kind == RtlKind.Register && driver.Signal >= 0 ? signals[driver.Signal].Slice(driver.Lowest, net.Width)
                        : LogicVec.AllX(net.Width);
                    known[net.Id] = true;
                }
            }

            // The cells in an order each one's inputs come first; those in a loop of logic, their outputs seeded first.
            List<RtlCell> order = Order(netlist, known, out List<RtlCell> looped);
            foreach (RtlCell cell in looped)
                foreach (int output in cell.OutputList)
                    if (!known[output])
                    {
                        values[output] = nets[output].Signal >= 0 ? signals[nets[output].Signal] : LogicVec.AllX(nets[output].Width);
                        known[output] = true;
                    }

            foreach (RtlCell cell in order)
                values[cell.Output] = Apply(cell, values, signals, nets);
            foreach (RtlCell cell in looped)
                values[cell.Output] = Apply(cell, values, signals, nets);
            return values;
        }

        /// <summary>The combinational cells in an order inputs-first, and, apart, the ones a loop of logic leaves unordered.</summary>
        private static List<RtlCell> Order(RtlNetlist netlist, bool[] known, out List<RtlCell> looped)
        {
            var order = new List<RtlCell>();
            var waiting = new int[netlist.Cells.Count];
            var readers = new List<int>[netlist.Nets.Count];
            var ready = new Queue<RtlCell>();
            foreach (RtlCell cell in netlist.Cells)
            {
                if (IsBox(cell))
                    continue;
                foreach (int input in cell.InputList)
                {
                    if (input < 0 || known[input])
                        continue;
                    waiting[cell.Id]++;
                    (readers[input] ??= new List<int>()).Add(cell.Id);
                }
                if (waiting[cell.Id] == 0)
                    ready.Enqueue(cell);
            }

            var done = new bool[netlist.Cells.Count];
            while (ready.Count > 0)
            {
                RtlCell cell = ready.Dequeue();
                done[cell.Id] = true;
                order.Add(cell);
                foreach (int output in cell.OutputList)
                    if (readers[output] != null)
                        foreach (int reader in readers[output])
                            if (--waiting[reader] == 0)
                                ready.Enqueue(netlist.Cells[reader]);
            }

            looped = new List<RtlCell>();
            foreach (RtlCell cell in netlist.Cells)
                if (!done[cell.Id] && !IsBox(cell))
                    looped.Add(cell);
            return order;
        }

        /// <summary>
        /// Whether a cell's outputs are read off the design rather than worked out: an instance's, a process box's, and a
        /// register's, which holds what its clock last gave it.
        /// </summary>
        internal static bool IsBox(RtlCell cell) => cell.Kind == RtlKind.Instance || cell.Kind == RtlKind.Process || cell.Kind == RtlKind.Register;

        private static LogicVec In(RtlCell cell, int pin, LogicVec[] values) => values[cell.InputList[pin]];

        /// <summary>One cell's output, from its inputs' values: the engine's own operator for each kind.</summary>
        private static LogicVec Apply(RtlCell cell, LogicVec[] values, LogicVec[] signals, IReadOnlyList<RtlNet> nets)
        {
            switch (cell.Kind)
            {
                case RtlKind.Constant:
                    return cell.Value;
                case RtlKind.Gate:
                {
                    var inputs = new LogicVec[cell.InputList.Length];
                    for (int i = 0; i < inputs.Length; i++)
                        inputs[i] = In(cell, i, values);
                    return Gates.Evaluate(cell.Gate, inputs);
                }
                case RtlKind.Unary:
                    return Expr.Apply(cell.Unary, In(cell, 0, values));
                case RtlKind.Binary:
                    return Expr.Apply(cell.Binary, In(cell, 0, values), In(cell, 1, values));
                case RtlKind.Mux:
                    return LogicVec.Conditional(In(cell, 0, values), In(cell, 1, values), In(cell, 2, values));
                case RtlKind.Concat:
                    return LogicVec.Concat(In(cell, 0, values), In(cell, 1, values));
                case RtlKind.Replicate:
                    return LogicVec.Replicate(In(cell, 0, values), cell.Count);
                case RtlKind.Slice:
                    return In(cell, 0, values).Slice(cell.Lowest, cell.Width);
                case RtlKind.Bit:
                    return In(cell, 0, values).Bit(In(cell, 1, values));
                case RtlKind.Part:
                {
                    LogicVec unknown = LogicVec.AllX(cell.Width);
                    return Expr.TryOffset(In(cell, 1, values), out long at) ? Expr.PartAt(In(cell, 0, values), at, cell.Step, cell.Width, unknown) : unknown;
                }
                case RtlKind.Resize:
                    return In(cell, 0, values).Extend(cell.Width, cell.Signed);
                case RtlKind.Signedness:
                    return cell.Signed ? In(cell, 0, values).AsSigned() : In(cell, 0, values).AsUnsigned();
                case RtlKind.TwoState:
                    return In(cell, 0, values).ToTwoState();
                case RtlKind.Join:
                    return Join(cell, values, nets);
                case RtlKind.Word:
                {
                    MemoryInfo memory = cell.Memory;
                    int at = memory == null ? -1 : Expr.WordAt(In(cell, 0, values), memory.Depth);
                    return at >= 0 ? signals[memory.First.Index + at] : LogicVec.AllX(nets[cell.Output].Width);
                }
                case RtlKind.Opaque:
                    return cell.Source.Evaluate(signals);
                case RtlKind.Put:
                {
                    LogicVec old = In(cell, 0, values), part = In(cell, 1, values);
                    if (!RtlBuilder.OffsetOf(In(cell, 2, values), out int lowest) || !Simulator.Clip(old.Width, ref lowest, ref part))
                        return old;
                    LogicVec spliced = Simulator.Splice(old, lowest, part);
                    LogicVec typed = cell.Signed ? spliced.AsSigned() : spliced.AsUnsigned();
                    return cell.TwoStateTarget ? typed.ToTwoState() : typed;
                }
                case RtlKind.Truth:
                    return LogicVec.FromUInt64(LogicVec.Truth(In(cell, 0, values)) == Logic4.One ? 1UL : 0UL, 1);
                case RtlKind.Match:
                    return LogicVec.FromUInt64(Matching.CaseMatches(In(cell, 0, values), In(cell, 1, values), cell.Case) ? 1UL : 0UL, 1);
                default:
                    return LogicVec.AllX(nets[cell.Output].Width);
            }
        }

        /// <summary>A signal's drivers put together as the engine puts them: each fitted to its slice, resolved, and held as the signal holds it.</summary>
        private static LogicVec Join(RtlCell cell, LogicVec[] values, IReadOnlyList<RtlNet> nets)
        {
            RtlNet net = nets[cell.Output];
            int width = net.Width;
            var info = new JoinTarget(width, cell);
            int count = cell.InputList.Length;
            if (count == 0)
                return LogicVec.AllX(width);

            // A driver's output at its slice's width, as plain bits; the slice's width is what the driver drives.
            LogicVec Piece(int i)
            {
                int lowest = cell.Lowests[i];
                int pieceWidth = i < cell.PieceWidths.Count ? cell.PieceWidths[i] : width - lowest;
                LogicVec v = values[cell.InputList[i]];
                return (v.Width == pieceWidth ? v : v.Extend(pieceWidth, false)).AsUnsigned();
            }

            bool sliced = false;
            for (int i = 0; i < count; i++)
                if (cell.Lowests[i] != 0 || (i < cell.PieceWidths.Count && cell.PieceWidths[i] != width))
                    sliced = true;

            LogicVec value;
            if (!sliced)
            {
                value = Piece(0);
                for (int i = 1; i < count; i++)
                    value = LogicVec.ResolveWire(value, Piece(i));
            }
            else if (!cell.Wire)
            {
                value = LogicVec.AllX(width);
                for (int i = 0; i < count; i++)
                    value = Simulator.Splice(value, cell.Lowests[i], Piece(i));
            }
            else
            {
                LogicVec undriven = LogicVec.Fill(Logic4.Z, width);
                value = Simulator.Splice(undriven, cell.Lowests[0], Piece(0));
                for (int i = 1; i < count; i++)
                    value = LogicVec.ResolveWire(value, Simulator.Splice(undriven, cell.Lowests[i], Piece(i)));
            }

            return info.Shape(value);
        }

        /// <summary>How a join's signal holds a value: its width, its sign, and whether it is 2-state.</summary>
        private readonly struct JoinTarget
        {
            private readonly int _width;
            private readonly bool _signed;
            private readonly bool _twoState;

            public JoinTarget(int width, RtlCell cell)
            {
                _width = width;
                _signed = cell.Signed;
                _twoState = cell.TwoStateTarget;
            }

            public LogicVec Shape(LogicVec value)
            {
                LogicVec sized = value.Width == _width ? value : value.Extend(_width, false);
                LogicVec typed = _signed ? sized.AsSigned() : sized.AsUnsigned();
                return _twoState ? typed.ToTwoState() : typed;
            }
        }
    }
}
