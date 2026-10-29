using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The values of a drawing of code's wires that no signal holds (Part 15h-4) -- an adder's output inside
    /// <c>assign y = a + b + c</c> -- worked out at a moment from the signals the netlist reads, with the engine's own
    /// operators (<see cref="RtlEvaluator"/>): once a moment, however many wires ask.
    /// </summary>
    public sealed class DrawnValues
    {
        private readonly RtlNetlist _netlist;
        private readonly int[] _rtlNetOf;
        private readonly int[] _reads;
        private LogicVec[] _signals;
        private LogicVec[] _values;
        private double _at = double.NaN;

        /// <param name="netlist">The netlist drawn.</param>
        /// <param name="rtlNetOf">For each of the layout's nets, by index, the netlist's net it carries; -1 for none.</param>
        public DrawnValues(RtlNetlist netlist, int[] rtlNetOf)
        {
            _netlist = netlist ?? throw new ArgumentNullException(nameof(netlist));
            _rtlNetOf = rtlNetOf ?? throw new ArgumentNullException(nameof(rtlNetOf));

            // What the netlist reads off the design: every net that is a signal, a register's signal, a memory's words.
            var reads = new SortedSet<int>();
            foreach (RtlNet net in netlist.Nets)
                if (net.Signal >= 0)
                    reads.Add(net.Signal);
            foreach (RtlCell cell in netlist.Cells)
            {
                if (cell.Kind == RtlKind.Register && cell.Signal >= 0)
                    reads.Add(cell.Signal);
                if (cell.Kind == RtlKind.Word && cell.Memory != null)
                    for (int word = 0; word < cell.Memory.Depth; word++)
                        reads.Add(cell.Memory.First.Index + word);
            }
            _reads = new int[reads.Count];
            reads.CopyTo(_reads);
        }

        /// <summary>The design's signals the values are worked out from, by index: what the model keeps tracks for.</summary>
        public IReadOnlyList<int> Reads => _reads;

        /// <summary>Whether the layout's net <paramref name="net"/> is one whose value is worked out here.</summary>
        public bool Has(int net) => net >= 0 && net < _rtlNetOf.Length && _rtlNetOf[net] >= 0;

        /// <summary>
        /// The layout's net <paramref name="net"/>'s value at <paramref name="time"/>, the signals read through
        /// <paramref name="read"/>: worked out again only when the moment changes.
        /// </summary>
        public LogicVec ValueAt(int net, double time, int signals, Func<int, LogicVec> read)
        {
            if (!Has(net))
                throw new ArgumentOutOfRangeException(nameof(net));
            if (_signals == null || _signals.Length != signals)
            {
                _signals = new LogicVec[signals];
                for (int i = 0; i < signals; i++)
                    _signals[i] = LogicVec.AllX(1);
                _at = double.NaN;
            }
            if (!(time == _at))
            {
                foreach (int signal in _reads)
                    _signals[signal] = read(signal);
                _values = RtlEvaluator.Evaluate(_netlist, _signals);
                _at = time;
            }
            return _values[_rtlNetOf[net]];
        }
    }
}
