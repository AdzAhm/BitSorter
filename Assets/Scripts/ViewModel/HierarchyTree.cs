using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>One row of the hierarchy as it is drawn: a scope, how deep it is, and whether it opens.</summary>
    public readonly struct HierarchyRow
    {
        public HierarchyRow(ScopeNode node, int depth, bool opens, bool open)
        {
            Node = node;
            Depth = depth;
            Opens = opens;
            Open = open;
        }

        public ScopeNode Node { get; }

        /// <summary>How many scopes it is inside, as drawn: the top is 0.</summary>
        public int Depth { get; }

        /// <summary>Whether it has scopes inside it that are drawn, and whether they are.</summary>
        public bool Opens { get; }

        /// <inheritdoc cref="Opens"/>
        public bool Open { get; }

        /// <summary>What the row says: its name, and the module of an instance of the board's own.</summary>
        public string Label =>
            Node.Kind == ScopeNodeKind.Block && Node.Module != null ? Node.Name + " (" + Node.Module + ")" : Node.Name;
    }

    /// <summary>
    /// The design's hierarchy (Part 11c), Unity-free: the testbench, the design and every block and generate block in
    /// it, as rows to draw in the parts panel, each opening and shutting; and what a click on one adds to the waveform.
    /// </summary>
    /// <remarks>
    /// **What it shows** is <see cref="SignalIndex.Root"/>'s scopes but a library cell's -- every gate with delay is one,
    /// and a full adder of two half adders would list five -- and a process's named blocks. Every scope is open to begin
    /// with, so a design's blocks are in sight.
    ///
    /// **A click on a scope adds its ports to the waveform, as one group named by its path** (Ahmad's choice,
    /// 2026-10-25): <see cref="SignalsOf"/>, and <see cref="GroupNameOf"/> for the group. A scope with no ports -- the
    /// testbench, a generate block -- adds the signals it declares instead. Stepping inside a block is Part 12's.
    /// </remarks>
    public sealed class HierarchyTree
    {
        private readonly SignalIndex _index;
        private readonly HashSet<ScopeNode> _shut = new HashSet<ScopeNode>();
        private readonly List<HierarchyRow> _rows = new List<HierarchyRow>();

        public HierarchyTree(SignalIndex index)
        {
            _index = index;
            Lay();
        }

        /// <summary>The rows drawn now, the top first and every scope under its parent.</summary>
        public IReadOnlyList<HierarchyRow> Rows => _rows;

        /// <summary>Opens a scope's row that is shut, or shuts one that is open. True when anything changed.</summary>
        public bool Toggle(int row)
        {
            if (row < 0 || row >= _rows.Count || !_rows[row].Opens)
                return false;
            ScopeNode node = _rows[row].Node;
            if (!_shut.Remove(node))
                _shut.Add(node);
            Lay();
            return true;
        }

        /// <summary>
        /// What a click on <paramref name="node"/> adds to the waveform: the signals of its ports, in the order declared,
        /// or, with none, every signal it declares that a waveform can show.
        /// </summary>
        public List<int> SignalsOf(ScopeNode node)
        {
            var ports = new List<int>();
            var all = new List<int>();
            if (node == null)
                return ports;
            foreach (int signal in node.Signals)
            {
                all.Add(signal);
                if (_index.IsPort(signal))
                    ports.Add(signal);
            }

            return ports.Count > 0 ? ports : all;
        }

        /// <summary>
        /// The group a click on <paramref name="node"/> makes is named for it: its path as the waveform's keys write it --
        /// <c>h1</c>, <c>h1.g</c>, <c>tb</c> -- or, for the design itself, whose keys have no path, its module.
        /// </summary>
        public string GroupNameOf(ScopeNode node)
        {
            if (node == null)
                return "group";
            foreach (int signal in node.Signals)
            {
                string key = _index.KeyOf(signal);
                int dot = key == null ? -1 : key.LastIndexOf('.');
                if (dot > 0)
                    return key.Substring(0, dot);
                if (key != null)
                    break;
            }

            return node.Module ?? node.Name;
        }

        private static bool Drawn(ScopeNode node) => node.Kind != ScopeNodeKind.Cell && node.Kind != ScopeNodeKind.Named;

        private void Lay()
        {
            _rows.Clear();
            if (_index?.Root != null)
                Lay(_index.Root, 0);
        }

        private void Lay(ScopeNode node, int depth)
        {
            bool opens = false;
            foreach (ScopeNode child in node.Children)
                opens |= Drawn(child);
            bool open = opens && !_shut.Contains(node);
            _rows.Add(new HierarchyRow(node, depth, opens, open));
            if (!open)
                return;
            foreach (ScopeNode child in node.Children)
                if (Drawn(child))
                    Lay(child, depth + 1);
        }
    }
}
