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

        // -----------------------------------------------------------------
        // The bit a register holds
        // -----------------------------------------------------------------

        /// <summary>
        /// The held bit stays inside the flip-flop body, at rest and at the top of its capture
        /// swell.
        /// </summary>
        /// <remarks>
        /// Seen in the browser build: a register capturing a bit drew a disc wider than the box it
        /// was inside, so the moment the state changed read as the body bursting rather than as a
        /// value being taken. A disc drawn inside a shape has to be measured against that shape,
        /// and nothing was measuring it -- the size lived on the renderer and the outline lived in
        /// the sprite builder, which is the drift <see cref="PortGeometry"/> exists to prevent.
        /// </remarks>
        [Test]
        public void TheBitARegisterHolds_StaysInsideTheFlipFlopBody()
        {
            float widest = PortGeometry.HeldBitCentre + PortGeometry.HeldBitSwollenRadius;

            Assert.Less(widest, ProceduralSprites.FlipFlopHalfWidth,
                "the held bit swells out through the right edge of the register");

            Assert.Less(PortGeometry.HeldBitSwollenRadius, ProceduralSprites.FlipFlopHalfHeight,
                "the held bit swells out through the top and bottom of the register");
        }

        /// <summary>
        /// It also clears the clock notch, which is the mark that says this box is a flip-flop.
        /// </summary>
        /// <remarks>
        /// The notch is cut out of the left edge rather than drawn inside it, precisely so the glow
        /// cannot eat it -- and then a disc centred on the node covered it anyway. The silhouette
        /// is the only cue that separates a register from a gate, so whatever is drawn on top of it
        /// has to leave that cut showing.
        /// </remarks>
        [Test]
        public void TheBitARegisterHolds_ClearsTheClockNotch()
        {
            // The notch narrows to a point this far in from the left edge.
            float tip = -ProceduralSprites.FlipFlopHalfWidth + ProceduralSprites.FlipFlopNotch;
            float leftmost = PortGeometry.HeldBitCentre - PortGeometry.HeldBitSwollenRadius;

            Assert.Greater(leftmost, tip,
                "the held bit is drawn over the clock notch, so the register reads as a gate");
        }

        /// <summary>
        /// The scale that draws a circle at a wanted radius, which is how the held bit is sized.
        /// </summary>
        [Test]
        public void ScaleForRadius_DrawsACircleAtThatRadius()
        {
            // A sprite drawn at NodeSize spans two shape units, and Circle's own radius is the
            // fraction of one that it fills -- so scaling it down by NodeSize gives the radius it
            // covers in the body's own measurements.
            float scale = PortGeometry.ScaleForRadius(PortGeometry.HeldBitRadius);
            float drawn = scale * ProceduralSprites.CircleRadius / PortGeometry.NodeSize;

            Assert.AreEqual(PortGeometry.HeldBitRadius, drawn, 0.0001f);
        }

        [Test]
        public void TheHeldBit_SitsOnTheRegistersOwnRow()
        {
            Vector2 held = PortGeometry.HeldBitPositionOf(Centre);

            Assert.AreEqual(Centre.y, held.y, 0.0001f, "the held bit is not level with the body");
            Assert.AreEqual(Centre.x + PortGeometry.HeldBitCentre * PortGeometry.ShapeUnit,
                held.x, 0.0001f);
        }
    }
}
