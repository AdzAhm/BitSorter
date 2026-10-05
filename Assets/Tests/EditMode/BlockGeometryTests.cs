using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Where a block's box and ports are drawn (<see cref="PortGeometry"/>): the one place the box
    /// renderer, the sockets and the wiring hit test all read it from.
    /// </summary>
    public class BlockGeometryTests
    {
        private const float Cell = 2f;
        private static readonly Vector2 Centre = new Vector2(4f, -2f);

        private static void AssertNear(Vector2 expected, Vector2 actual, string message)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-5f, message);
            Assert.AreEqual(expected.y, actual.y, 1e-5f, message);
        }

        [Test]
        public void ABlockOneCellTall_PutsTwoPortsWhereAGateHasThem()
        {
            float height = PortGeometry.BlockHeight(1, Cell);

            for (int i = 0; i < 2; i++)
            {
                AssertNear(PortGeometry.PositionOf(Centre, true, i, 2),
                    PortGeometry.BlockPortPosition(Centre, height, true, i, 2), $"input {i}");
                AssertNear(PortGeometry.PositionOf(Centre, false, i, 2),
                    PortGeometry.BlockPortPosition(Centre, height, false, i, 2), $"output {i}");
            }

            AssertNear(PortGeometry.PositionOf(Centre, true, 0, 1),
                PortGeometry.BlockPortPosition(Centre, height, true, 0, 1), "a lone port is not level with the middle");
        }

        [Test]
        public void ATallerBlock_SpreadsItsPortsOverItsWholeHeight_TopFirst()
        {
            float height = PortGeometry.BlockHeight(2, Cell);
            Vector2 top = PortGeometry.BlockPortPosition(Centre, height, true, 0, 3);
            Vector2 middle = PortGeometry.BlockPortPosition(Centre, height, true, 1, 3);
            Vector2 bottom = PortGeometry.BlockPortPosition(Centre, height, true, 2, 3);

            float inset = (PortGeometry.NodeSize - PortGeometry.PortSpacing) * 0.5f;

            Assert.AreEqual(Centre.y + height * 0.5f - inset, top.y, 1e-5f, "the top port is not as far in as a gate's");
            Assert.AreEqual(Centre.y, middle.y, 1e-5f);
            Assert.AreEqual(Centre.y - height * 0.5f + inset, bottom.y, 1e-5f);
            Assert.AreEqual(Centre.x - PortGeometry.BlockWidth * 0.5f, top.x, 1e-5f, "an input is not on the left face");
        }

        [Test]
        public void ABlocksBox_RunsFromItsTopCellDown()
        {
            var topCell = new Vector2(0f, 4f);

            Assert.AreEqual(PortGeometry.NodeSize, PortGeometry.BlockHeight(1, Cell), 1e-5f);
            Assert.AreEqual(Cell * 2f + PortGeometry.NodeSize, PortGeometry.BlockHeight(3, Cell), 1e-5f);
            AssertNear(new Vector2(0f, 2f), PortGeometry.BlockCentre(topCell, 3, Cell), "three cells down from the top");

            Vector2 body = PortGeometry.BlockBodySize(PortGeometry.BlockHeight(1, Cell));
            Assert.AreEqual(body.x, body.y, 1e-5f, "a one-cell box is not drawn as square as a gate");
            Assert.Less(body.x, PortGeometry.BlockWidth, "the box reaches past the faces its ports are on");
        }

        [Test]
        public void AnAnchor_StandsWhereItsOnePortLandsOnTheBox()
        {
            var port = new Vector2(-1f, 3f);

            AssertNear(port, PortGeometry.PositionOf(PortGeometry.AnchorCentre(port, true), true, 0, 1),
                "an input anchor's port is off the box");
            AssertNear(port, PortGeometry.PositionOf(PortGeometry.AnchorCentre(port, false), false, 0, 1),
                "an output anchor's port is off the box");
        }
    }
}
