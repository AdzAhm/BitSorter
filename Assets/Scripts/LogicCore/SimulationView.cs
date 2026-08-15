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

        public int NodeCount => _simulation.NodeCount;
        public int EdgeCount => _simulation.EdgeCount;

        /// <summary>The node with the given stable id. Ids run from 0 to NodeCount - 1.</summary>
        public Node GetNode(int id) => _simulation.GetNode(id);

        /// <summary>The edge with the given stable id. Ids run from 0 to EdgeCount - 1.</summary>
        public Edge GetEdge(int id) => _simulation.GetEdge(id);
    }
}
