using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// How one edge of a built graph is drawn: between which two ports, and over which of its ticks.
    /// </summary>
    /// <remarks>
    /// On a board with no blocks every edge is a wire the player drew, and its shape is the edge itself.
    /// A block changes that, because its boundary is spliced rather than given a node of its own (see
    /// <see cref="CircuitBuilder"/>). The wire drawn to a block's input and the block's own wire from
    /// that input become one edge, so the boundary costs no tick. That edge runs from a port the player
    /// can see to one inside the box they cannot: it spends <see cref="DrawnDelay"/> ticks on the drawn
    /// wire, and <see cref="HiddenAfter"/> more out of sight. Out of a block it is the other way round,
    /// and from one block to another it is hidden at both ends.
    ///
    /// Which ports it is drawn between are the box's port anchors, the inert nodes that stand on its
    /// faces, so everything that finds a port by position keeps finding one there.
    /// </remarks>
    public readonly struct EdgeShape
    {
        /// <summary>The port the drawn wire leaves: the edge's own, or a block's output anchor.</summary>
        public readonly OutputPort DrawnFrom;

        /// <summary>The port the drawn wire enters: the edge's own, or a block's input anchor.</summary>
        public readonly InputPort DrawnTo;

        /// <summary>Ticks spent inside a block before the drawn wire begins.</summary>
        public readonly int HiddenBefore;

        /// <summary>The delay of the wire the player drew, which is what the wire is labelled.</summary>
        public readonly int DrawnDelay;

        /// <summary>Ticks spent inside a block after the drawn wire ends.</summary>
        public readonly int HiddenAfter;

        /// <summary>The blueprint wire this edge carries, or -1 for a wire inside a block.</summary>
        public readonly int Wire;

        /// <summary>
        /// Whether this edge draws its wire. False inside a block, and for every edge of a drawn wire
        /// but the first: a wire into a block input that leads to two gates inside is two edges, which
        /// carry the same bits along the same drawn wire at the same moments.
        /// </summary>
        public readonly bool Drawn;

        public EdgeShape(
            OutputPort drawnFrom, InputPort drawnTo, int hiddenBefore, int drawnDelay, int hiddenAfter, int wire,
            bool drawn)
        {
            DrawnFrom = drawnFrom;
            DrawnTo = drawnTo;
            HiddenBefore = hiddenBefore;
            DrawnDelay = drawnDelay;
            HiddenAfter = hiddenAfter;
            Wire = wire;
            Drawn = drawn;
        }

        /// <summary>The shape of an edge that is exactly the wire the player drew.</summary>
        public static EdgeShape Of(Edge edge, int wire) =>
            new EdgeShape(edge.Source, edge.Target, 0, edge.Delay, 0, wire, true);

        /// <summary>The shape of a wire inside a block, which is never drawn.</summary>
        public static EdgeShape Inside(Edge edge) =>
            new EdgeShape(edge.Source, edge.Target, 0, edge.Delay, 0, -1, false);

        /// <summary>Whether the edge is all the drawn wire, with nothing hidden at either end.</summary>
        public bool IsPlain => HiddenBefore == 0 && HiddenAfter == 0;

        public override string ToString() =>
            Wire < 0 ? "inside a block" : $"wire {Wire} (+{HiddenBefore}, {DrawnDelay}, +{HiddenAfter})";
    }

    /// <summary>A block as a build expanded it: where it stands, and the nodes it became.</summary>
    public sealed class BuiltBlock
    {
        public BuiltBlock(
            int index, Vector2Int cell, BlockDefinition definition, int[] inputAnchors, int[] outputAnchors,
            int[] gates)
        {
            Index = index;
            Cell = cell;
            Definition = definition;
            InputAnchors = inputAnchors;
            OutputAnchors = outputAnchors;
            Gates = gates;
        }

        /// <summary>Its place in <see cref="CircuitBlueprint.Blocks"/>.</summary>
        public int Index { get; }

        /// <summary>Its top cell, where its wires are stored.</summary>
        public Vector2Int Cell { get; }

        public BlockDefinition Definition { get; }

        /// <summary>Node ids of the anchors on its left face, one an input, top to bottom.</summary>
        public IReadOnlyList<int> InputAnchors { get; }

        /// <summary>Node ids of the anchors on its right face, one an output, top to bottom.</summary>
        public IReadOnlyList<int> OutputAnchors { get; }

        /// <summary>Node ids of the gates inside, in definition order.</summary>
        public IReadOnlyList<int> Gates { get; }

        public override string ToString() => $"{Definition.Name} at {Cell}";
    }
}
