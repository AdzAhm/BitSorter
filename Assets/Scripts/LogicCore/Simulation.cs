using System;
using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// Owns a graph of nodes and edges and advances it one integer tick at a time.
    /// </summary>
    /// <remarks>
    /// Each tick runs three phases in order: advance in-transit bits, deliver those that have
    /// arrived, then evaluate every ready node. Because emission happens in the third phase and
    /// delivery in the second, and every edge has a delay of at least one tick, no node can
    /// observe another node's output within the same tick -- so the order in which nodes are
    /// evaluated cannot affect the result.
    /// </remarks>
    public sealed class Simulation
    {
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly List<Arrival> _arrivals = new List<Arrival>();

        /// <summary>The tick the next call to <see cref="Tick"/> will execute. Starts at 0.</summary>
        public int CurrentTick { get; private set; }

        /// <summary>
        /// Bits destroyed by collisions at input ports. Counts bits, not collision events: a
        /// collision between differing values destroys two bits and so adds two.
        /// </summary>
        public int CorruptedCount { get; private set; }

        public IReadOnlyList<Node> Nodes => _nodes;
        public IReadOnlyList<Edge> Edges => _edges;

        public T Add<T>(T node) where T : Node
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (_nodes.Contains(node))
                throw new InvalidOperationException($"Node {node} has already been added.");

            _nodes.Add(node);
            return node;
        }

        /// <summary>
        /// Connects an output port to an input port. Fan-out (several edges leaving one output)
        /// and fan-in (several edges targeting one input) are both permitted; fan-in is how
        /// collisions arise.
        /// </summary>
        public Edge Connect(OutputPort from, InputPort to, int delay)
        {
            Edge edge = new Edge(from, to, delay);
            _edges.Add(edge);
            return edge;
        }

        public void Tick()
        {
            int tick = CurrentTick;

            // Phase 1: advance every in-transit bit, collecting arrivals in edge insertion order.
            _arrivals.Clear();
            for (int i = 0; i < _edges.Count; i++)
                _edges[i].AdvanceAndCollect(_arrivals);

            // Phase 2: deliver. The only place an input port is written, and so the only place a
            // collision can occur. Matching values leave the port holding that value; differing
            // values destroy both bits and poison the port for the rest of this phase. Both the
            // resulting port state and the tally are therefore independent of edge order.
            for (int i = 0; i < _arrivals.Count; i++)
            {
                Arrival arrival = _arrivals[i];
                CorruptedCount += arrival.Target.Deliver(arrival.Value, tick);
            }

            // Phase 3: evaluate every ready node. See the remarks above for why this order is free.
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                if (node.IsReadyToEvaluate)
                    node.Evaluate(tick);
            }

            CurrentTick = tick + 1;
        }

        public void Run(int tickCount)
        {
            if (tickCount < 0)
                throw new ArgumentOutOfRangeException(nameof(tickCount), tickCount, "Cannot be negative.");

            for (int i = 0; i < tickCount; i++)
                Tick();
        }
    }
}
