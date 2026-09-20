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
        /// brightening, because bloom is already brightest at the middle of a node.
        /// </summary>
        public const float HeldBitSwell = 1.75f;

        /// <summary>The held bit at its largest, which is the size that has to fit.</summary>
        public const float HeldBitSwollenRadius = HeldBitRadius * HeldBitSwell;

        /// <summary>Where the held bit is drawn, given the register's centre.</summary>
        public static Vector2 HeldBitPositionOf(Vector2 nodeCentre) =>
            new Vector2(nodeCentre.x + HeldBitCentre * ShapeUnit, nodeCentre.y);

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
