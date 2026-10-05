using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Where every port sits on screen, and how close a click has to be to count as hitting one.
    /// </summary>
    /// <remarks>
    /// Constants rather than serialized fields, on purpose. The stub renderer, the hit tester, the
    /// wire renderer and the bit renderer all call <see cref="PositionOf"/>, and the classic bug in
    /// this kind of UI is drawing stubs from one calculation while hit-testing against another, so
    /// clicks land slightly off what the player sees. One source of truth removes that whole class
    /// of defect. If these ever need tuning they should become a ScriptableObject every consumer
    /// reads -- never per-component fields, which is exactly how the two drift apart.
    /// </remarks>
    public static class PortGeometry
    {
        /// <summary>Must match the square NodeRenderer draws, since stubs sit on its faces.</summary>
        public const float NodeSize = 1.2f;

        public const float StubSize = 0.30f;

        /// <summary>Vertical gap between adjacent ports on the same face.</summary>
        public const float PortSpacing = 0.44f;

        /// <summary>
        /// Click tolerance. On the 2-unit grid, facing stubs of neighbouring nodes are 0.8 apart,
        /// so 0.34 keeps the two zones from ever overlapping (0.68 &lt; 0.8). Hit tests still take
        /// the nearest match, so the outcome stays deterministic if this is ever loosened.
        /// </summary>
        public const float HitRadius = 0.34f;

        /// <summary>How close a click must be to a wire to delete it.</summary>
        public const float WireHitRadius = 0.25f;

        // -----------------------------------------------------------------
        // The bit a register holds, drawn inside its body
        // -----------------------------------------------------------------

        /// <summary>
        /// A sprite drawn at <see cref="NodeSize"/> spans two shape units, so this converts one of
        /// them to world space.
        /// </summary>
        /// <remarks>
        /// Every predicate in <see cref="ProceduralSprites"/> works in -1..1 with the origin at the
        /// centre, which is the space the flip-flop's measurements are written in. Anything placed
        /// against those measurements has to come back out to world units to be positioned, and
        /// this is the only factor that does it.
        /// </remarks>
        public const float ShapeUnit = NodeSize * 0.5f;

        /// <summary>Where the held bit sits, relative to the register's centre, in shape units.</summary>
        /// <remarks>
        /// Right of centre, not on it. The notch is cut into the left edge and is the only mark
        /// that separates this box from a gate, so a disc centred on the node sits straight on top
        /// of it -- which is what shipped, and the notch was invisible on the board. Offset into
        /// the clear part of the body it leaves the cut showing, and puts the state on the Q side
        /// where a textbook draws it.
        /// </remarks>
        public const float HeldBitCentre = 0.14f;

        /// <summary>Radius of the held bit at rest, in shape units.</summary>
        /// <remarks>
        /// Sized from what has to fit rather than from what looks right in isolation: the swollen
        /// disc has to clear the notch tip on one side and the right edge on the other, which
        /// leaves about 0.35 to play with, and the rest follows from the swell.
        /// </remarks>
        public const float HeldBitRadius = 0.20f;

        /// <summary>
        /// How far it swells on the clock it captures a new bit. A swell rather than a
        /// brightening: a plain colour made brighter only moves towards white, which erases which
        /// value it was, where a change of size keeps both the colour and the digit.
        /// </summary>
        public const float HeldBitSwell = 1.75f;

        /// <summary>The held bit at its largest, which is the size that has to fit.</summary>
        public const float HeldBitSwollenRadius = HeldBitRadius * HeldBitSwell;

        /// <summary>
        /// How much of a plate shows round the bit sitting on it, in shape units -- the same for a
        /// register's held bit and a source's next one.
        /// </summary>
        public const float PlateRim = 0.05f;

        /// <summary>Radius of the solid plate a register's held bit sits on, in shape units.</summary>
        /// <remarks>
        /// Neon Board draws a register's body as glass, see-through in the middle, and a 0 there was
        /// about 2.5:1 against what showed through (measured 2026-09-28). The plate is the body's
        /// colour made solid under the bit. It keeps its size through a capture -- the bit swells
        /// over it -- so it is measured at rest, and has only the notch and the edges to clear.
        /// </remarks>
        public const float HeldBitPlateRadius = HeldBitRadius + PlateRim;

        /// <summary>Where the held bit is drawn, given the register's centre.</summary>
        public static Vector2 HeldBitPositionOf(Vector2 nodeCentre) =>
            new Vector2(nodeCentre.x + HeldBitCentre * ShapeUnit, nodeCentre.y);

        // -----------------------------------------------------------------
        // The bit a source will send next, drawn above it
        // -----------------------------------------------------------------

        /// <summary>
        /// Radius of the pale plate a source's next bit sits on, in shape units -- the outermost
        /// thing drawn, and so the size that has to fit.
        /// </summary>
        /// <remarks>
        /// A little under the largest that fits between the capsule and the top of the square the
        /// source occupies. That largest, 0.27, read as big beside the capsule and was taken down a
        /// step on request (2026-09-30). Not down to the register's size: on a 13 by 7 board a cell
        /// is small, and a disc that size was a few pixels across with its digit barely there.
        /// </remarks>
        public const float NextBitPlateRadius = 0.24f;

        /// <summary>Radius of a source's next bit on its plate, in shape units.</summary>
        /// <remarks>
        /// The rim the plate leaves around it is what shows where the disc ends, as the register's
        /// plate does for its held bit. A 0 on the bare board did not stand out from it.
        /// </remarks>
        public const float NextBitRadius = NextBitPlateRadius - PlateRim;

        /// <summary>Space between the top of a source's capsule and its plate, in shape units.</summary>
        public const float NextBitGap = 0.05f;

        /// <summary>How far above a source's centre its next bit sits, in shape units.</summary>
        /// <remarks>
        /// Above the capsule rather than on it, where a playtester asked for it (2026-09-28) -- and
        /// still inside the square the source occupies, so nothing laid out around a node, the
        /// banner's clearance of the top row included, has to know it is there.
        /// </remarks>
        public const float NextBitCentre =
            ProceduralSprites.CapsuleHalfHeight + NextBitGap + NextBitPlateRadius;

        /// <summary>Where a source's next bit is drawn, given the source's centre.</summary>
        public static Vector2 NextBitPositionOf(Vector2 nodeCentre) =>
            new Vector2(nodeCentre.x, nodeCentre.y + NextBitCentre * ShapeUnit);

        /// <summary>
        /// The local scale that draws <see cref="ProceduralSprites.Circle"/> at a wanted radius.
        /// </summary>
        public static float ScaleForRadius(float radius) =>
            NodeSize * radius / ProceduralSprites.CircleRadius;

        /// <summary>
        /// Inputs sit on the left face, outputs on the right. Port 0 of several is the top one.
        /// </summary>
        public static Vector2 PositionOf(Vector2 nodeCentre, bool isInput, int index, int count)
        {
            float x = nodeCentre.x + (isInput ? -NodeSize * 0.5f : NodeSize * 0.5f);
            float spread = count <= 1 ? 0f : ((count - 1) * 0.5f - index) * PortSpacing;
            return new Vector2(x, nodeCentre.y + spread);
        }

        // -----------------------------------------------------------------
        // Blocks
        // -----------------------------------------------------------------

        /// <summary>How wide a block's box is: a node's width, whatever it holds.</summary>
        /// <remarks>
        /// Not wider, because <see cref="HitRadius"/> is sized against the 0.8 between a node's face
        /// and its neighbour's. A box any wider would bring its ports closer to the next column's
        /// than that, and a click between them could land on the wrong one.
        /// </remarks>
        public const float BlockWidth = NodeSize;

        /// <summary>A block's box H cells tall, in world units: a node in each of the end cells and the gaps between.</summary>
        public static float BlockHeight(int cells, float cellSize) => (cells - 1) * cellSize + NodeSize;

        /// <summary>The middle of a block's box, given the centre of its top cell.</summary>
        public static Vector2 BlockCentre(Vector2 topCell, int cells, float cellSize) =>
            new Vector2(topCell.x, topCell.y - (cells - 1) * cellSize * 0.5f);

        /// <summary>
        /// A block's port: on its left face for an input and its right for an output, spread evenly
        /// over its height with port 0 at the top.
        /// </summary>
        /// <remarks>
        /// The ends sit as far in from the box's top and bottom as a gate's two inputs do, so a block
        /// one cell tall with two ports has them exactly where a gate has them, and a taller one
        /// spreads them over the whole box.
        /// </remarks>
        public static Vector2 BlockPortPosition(Vector2 centre, float height, bool isInput, int index, int count)
        {
            float x = centre.x + (isInput ? -BlockWidth * 0.5f : BlockWidth * 0.5f);

            if (count <= 1)
                return new Vector2(x, centre.y);

            float inset = (NodeSize - PortSpacing) * 0.5f;
            float spacing = (height - 2f * inset) / (count - 1);

            return new Vector2(x, centre.y + ((count - 1) * 0.5f - index) * spacing);
        }

        /// <summary>
        /// Where a block's port anchor stands, so that its one port that faces out lands on the box's
        /// face: half a node from the port, into the box, as a gate's centre is from its ports.
        /// </summary>
        public static Vector2 AnchorCentre(Vector2 port, bool isInput) =>
            new Vector2(port.x + (isInput ? NodeSize * 0.5f : -NodeSize * 0.5f), port.y);

        public static Vector2 EndpointOf(OutputPort port, Vector2 nodeCentre) =>
            PositionOf(nodeCentre, false, port.Index, port.Owner.OutputCount);

        public static Vector2 EndpointOf(InputPort port, Vector2 nodeCentre) =>
            PositionOf(nodeCentre, true, port.Index, port.Owner.InputCount);

        /// <summary>Shortest distance from a point to a line segment, for wire hit testing.</summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;

            if (lengthSquared < 1e-6f)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(point, a + ab * t);
        }
    }
}
