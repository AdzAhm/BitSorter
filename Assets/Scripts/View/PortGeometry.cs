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
