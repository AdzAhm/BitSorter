namespace BitSorter.LogicCore
{
    /// <summary>
    /// A read-only handle onto a <see cref="Simulation"/>, intended for renderers.
    /// </summary>
    /// <remarks>
    /// The node, edge and port types are already read-only outside this assembly -- everything
    /// that mutates simulation state is internal. What this view adds is the removal of the
    /// *control* surface: a renderer holding a view cannot call Tick, Run, Add or Connect, so it
    /// cannot advance or rewire the simulation it is drawing, accidentally or otherwise.
    ///
    /// A readonly struct, so obtaining one allocates nothing and it can be fetched per frame.
    ///
    /// Polling without allocating: use the Count properties with <see cref="GetNode"/> and
    /// <see cref="GetEdge"/> in an indexed loop. Do not foreach over
    /// <see cref="Simulation.Nodes"/> or <see cref="Simulation.Edges"/> on a hot path -- those
    /// are typed as IReadOnlyList and iterating one through the interface boxes an enumerator on
    /// every pass.
    ///
    /// <code>
    /// for (int e = 0; e &lt; view.EdgeCount; e++)
    /// {
    ///     Edge edge = view.GetEdge(e);
    ///     for (int b = 0; b &lt; edge.InTransitCount; b++)
    ///     {
    ///         BitInTransit bit = edge.GetBitInTransit(b);
    ///         Draw(edge.Id, bit.Value, bit.Progress);
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public readonly struct SimulationView
    {
        private readonly Simulation _simulation;

        internal SimulationView(Simulation simulation)
        {
            _simulation = simulation;
        }

        /// <summary>The tick the next call to Tick will execute.</summary>
        public int CurrentTick => _simulation.CurrentTick;

        /// <summary>Bits destroyed by collisions so far.</summary>
        public int CorruptedCount => _simulation.CorruptedCount;

        /// <summary>Where those collisions happened, without repeats.</summary>
        /// <remarks>
        /// Small and indexable, so unlike Nodes and Edges this one is fine to read directly. It
        /// holds one entry per port that has ever collided in this run, which is normally none.
        /// </remarks>
        public System.Collections.Generic.IReadOnlyList<InputPort> CorruptionSites =>
            _simulation.CorruptionSites;

        /// <summary>
        /// One past the highest id ever issued -- the bound for an id loop, not a population
        /// count. Removed ids leave nulls behind; see <see cref="LiveNodeCount"/>.
        /// </summary>
        public int NodeCount => _simulation.NodeCount;

        /// <inheritdoc cref="NodeCount"/>
        public int EdgeCount => _simulation.EdgeCount;

        /// <summary>How many nodes actually exist, ignoring removed ids.</summary>
        public int LiveNodeCount => _simulation.LiveNodeCount;

        /// <inheritdoc cref="LiveNodeCount"/>
        public int LiveEdgeCount => _simulation.LiveEdgeCount;

        /// <summary>
        /// The node with the given id, from 0 to NodeCount - 1, or **null** if that id has been
        /// removed. Ids are never reused, so callers must skip nulls rather than assume the range
        /// is dense.
        /// </summary>
        public Node GetNode(int id) => _simulation.GetNode(id);

        /// <summary>The edge with the given id, or **null** if it has been removed.</summary>
        /// <inheritdoc cref="GetNode"/>
        public Edge GetEdge(int id) => _simulation.GetEdge(id);
    }
}
