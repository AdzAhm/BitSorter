using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// What a drawing of code's wires that no signal holds (Part 15h-4) -- an adder's output inside <c>assign y = a + b + c</c>
    /// -- are worked out from: the netlist, the net each of the layout's wires carries, and the signals it reads, by name.
    /// A model works the values out at a moment with the engine's own operators (<see cref="RtlEvaluator"/>), the signals
    /// read off its own run by those names -- never by the numbers of the design the netlist was made from, which an edit
    /// replayed under an older run does not share (the review of #108: undo and redo read the wrong signals, and threw).
    /// A wire inside an operator opened into its gates (the GATES switch) is on no net of the netlist; the drawing works it
    /// out from them (<see cref="RtlDrawing.Inner"/>).
    /// </summary>
    public sealed class DrawnValues
    {
        private readonly int[] _rtlNetOf;
        private readonly int[] _reads;
        private readonly string[] _names;
        private readonly int[] _widths;

        /// <param name="netlist">The netlist drawn.</param>
        /// <param name="rtlNetOf">
        /// For each of the layout's nets, by index, the netlist's net it carries; <c>-2 - k</c> for the k-th wire inside an
        /// opening into gates; -1 for none.
        /// </param>
        /// <param name="design">The design the netlist was made from, which its signal numbers are of.</param>
        /// <param name="drawing">The drawing, which works out the wires inside its openings into gates; null for none.</param>
        public DrawnValues(RtlNetlist netlist, int[] rtlNetOf, Design design, RtlDrawing drawing = null)
        {
            Netlist = netlist ?? throw new ArgumentNullException(nameof(netlist));
            Drawing = drawing;
            _rtlNetOf = rtlNetOf ?? throw new ArgumentNullException(nameof(rtlNetOf));
            if (design == null)
                throw new ArgumentNullException(nameof(design));
            SignalCount = design.Signals.Count;

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
            _names = new string[_reads.Length];
            _widths = new int[_reads.Length];
            for (int i = 0; i < _reads.Length; i++)
            {
                _names[i] = design.Signals[_reads[i]].Name;
                _widths[i] = design.Signals[_reads[i]].Width;
            }
        }

        /// <summary>The netlist drawn.</summary>
        public RtlNetlist Netlist { get; }

        /// <summary>The drawing, which works out the wires inside its openings into gates; null for none.</summary>
        public RtlDrawing Drawing { get; }

        /// <summary>How many signals the netlist's design has: what an array of its values is as long as.</summary>
        public int SignalCount { get; }

        /// <summary>The netlist's design's signals the values are worked out from, by its numbers.</summary>
        public IReadOnlyList<int> Reads => _reads;

        /// <summary>Each of <see cref="Reads"/>' full names: what a run's own signal is found by.</summary>
        public IReadOnlyList<string> ReadNames => _names;

        /// <summary>Each of <see cref="Reads"/>' widths: an x of it where a run has no such signal.</summary>
        public IReadOnlyList<int> ReadWidths => _widths;

        /// <summary>Whether the layout's net <paramref name="net"/> is one whose value is worked out here.</summary>
        public bool Has(int net) => net >= 0 && net < _rtlNetOf.Length && _rtlNetOf[net] != -1;

        /// <summary>The netlist's net the layout's net <paramref name="net"/> carries; <c>-2 - k</c> for the k-th wire inside an opening.</summary>
        public int RtlNetOf(int net) => _rtlNetOf[net];
    }
}
