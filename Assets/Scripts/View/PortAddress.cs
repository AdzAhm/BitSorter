using System;

namespace BitSorter.View
{
    /// <summary>
    /// Identifies one port as (node id, side, index).
    /// </summary>
    /// <remarks>
    /// Holds a node **id**, not a Node reference, so it stays meaningful across a graph edit. A
    /// drag that began before the player removed a node ends up holding an address whose node is
    /// gone, which reads as a null from GetNode rather than a stale object.
    ///
    /// Deliberately free of UnityEngine types, so the wiring rules that consume it can be tested
    /// without the engine.
    /// </remarks>
    public readonly struct PortAddress : IEquatable<PortAddress>
    {
        public readonly int NodeId;
        public readonly bool IsInput;
        public readonly int Index;

        public PortAddress(int nodeId, bool isInput, int index)
        {
            NodeId = nodeId;
            IsInput = isInput;
            Index = index;
        }

        /// <summary>No port -- what a hit test returns when nothing is close enough.</summary>
        public static PortAddress None => new PortAddress(-1, false, 0);

        public bool IsValid => NodeId >= 0;

        public bool Equals(PortAddress other) =>
            NodeId == other.NodeId && IsInput == other.IsInput && Index == other.Index;

        public override bool Equals(object obj) => obj is PortAddress other && Equals(other);

        public override int GetHashCode() =>
            (((NodeId * 397) ^ Index) * 2) + (IsInput ? 1 : 0);

        public static bool operator ==(PortAddress left, PortAddress right) => left.Equals(right);

        public static bool operator !=(PortAddress left, PortAddress right) => !left.Equals(right);

        public override string ToString() =>
            IsValid ? $"node {NodeId} {(IsInput ? "in" : "out")}({Index})" : "no port";
    }
}
