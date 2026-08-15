using NUnit.Framework;
using UnityEngine;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Port placement. Worth pinning because the stub renderer, the hit tester, the wire renderer
    /// and the bit renderer all read these positions -- if this drifts, clicks stop landing where
    /// the player sees the stubs.
    /// </summary>
    public class PortGeometryTests
    {
        private static readonly Vector2 Centre = new Vector2(4f, -2f);

        [Test]
        public void InputsSitOnTheLeftFace_OutputsOnTheRight()
        {
            Vector2 input = PortGeometry.PositionOf(Centre, isInput: true, index: 0, count: 1);
            Vector2 output = PortGeometry.PositionOf(Centre, isInput: false, index: 0, count: 1);

            Assert.AreEqual(Centre.x - PortGeometry.NodeSize * 0.5f, input.x, 0.0001f);
            Assert.AreEqual(Centre.x + PortGeometry.NodeSize * 0.5f, output.x, 0.0001f);
            Assert.Less(input.x, output.x);
        }

        [Test]
        public void ASinglePort_IsCentredVertically()
        {
            Vector2 only = PortGeometry.PositionOf(Centre, isInput: true, index: 0, count: 1);

            Assert.AreEqual(Centre.y, only.y, 0.0001f);
        }

        [Test]
        public void PortZero_SitsAbovePortOne_AndTheyStraddleTheCentre()
        {
            Vector2 first = PortGeometry.PositionOf(Centre, isInput: true, index: 0, count: 2);
            Vector2 second = PortGeometry.PositionOf(Centre, isInput: true, index: 1, count: 2);

            Assert.Greater(first.y, second.y, "port 0 should be the top one");
            Assert.AreEqual(PortGeometry.PortSpacing, first.y - second.y, 0.0001f);
            Assert.AreEqual(Centre.y, (first.y + second.y) * 0.5f, 0.0001f, "pair should straddle the centre");
        }

        [Test]
        public void FacingStubsOfNeighbouringCells_CannotBothBeHit()
        {
            // Cell size 2: a node's right stub and its right-hand neighbour's left stub.
            Vector2 leftNode = Vector2.zero;
            Vector2 rightNode = new Vector2(2f, 0f);

            Vector2 leftOut = PortGeometry.PositionOf(leftNode, isInput: false, index: 0, count: 1);
            Vector2 rightIn = PortGeometry.PositionOf(rightNode, isInput: true, index: 0, count: 1);

            float gap = rightIn.x - leftOut.x;

            Assert.Greater(gap, PortGeometry.HitRadius * 2f,
                "hit zones of adjacent nodes' facing stubs must not overlap");
        }

        [Test]
        public void DistanceToSegment_MeasuresPerpendicularly_AndClampsToTheEnds()
        {
            Vector2 a = new Vector2(0f, 0f);
            Vector2 b = new Vector2(10f, 0f);

            // Beside the middle: perpendicular distance.
            Assert.AreEqual(3f, PortGeometry.DistanceToSegment(new Vector2(5f, 3f), a, b), 0.0001f);

            // Past an end: distance to that end, not to the infinite line.
            Assert.AreEqual(5f, PortGeometry.DistanceToSegment(new Vector2(15f, 0f), a, b), 0.0001f);

            // Degenerate segment.
            Assert.AreEqual(4f, PortGeometry.DistanceToSegment(new Vector2(0f, 4f), a, a), 0.0001f);
        }
    }
}
