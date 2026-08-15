using System.Collections.Generic;

namespace BitSorter.LogicCore
{
    /// <summary>
    /// An emission point on a node. Any number of edges may be attached; emitting pushes a copy
    /// of the value onto every one of them (fan-out).
    /// </summary>
    public sealed class OutputPort
    {
        private readonly List<Edge> _edges = new List<Edge>();

        public Node Owner { get; }
        public int Index { get; }
        public IReadOnlyList<Edge> Edges => _edges;

        internal OutputPort(Node owner, int index)
        {
            Owner = owner;
            Index = index;
        }

        internal void Attach(Edge edge)
        {
            _edges.Add(edge);
        }

        /// <summary>
        /// Stops this port emitting onto <paramref name="edge"/>. Called when an edge is removed;
        /// without it the port would keep pushing bits onto a dead edge forever.
        /// </summary>
        internal void Detach(Edge edge)
        {
            _edges.Remove(edge);
        }

        internal void Emit(Bit value)
        {
            for (int i = 0; i < _edges.Count; i++)
                _edges[i].Accept(value);
        }

        public override string ToString() => $"{Owner}.Out({Index})";
    }
}
