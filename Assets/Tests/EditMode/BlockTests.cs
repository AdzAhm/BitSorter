using System.Collections.Generic;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// What a block may be (<see cref="BlockRules"/>), and how a board holds one
    /// (<see cref="CircuitBlueprint"/>).
    /// </summary>
    /// <remarks>
    /// How a block is built and what it costs in time is <see cref="BlockBuildTests"/>.
    /// </remarks>
    public class BlockTests
    {
        private static readonly Vector2Int InA = new Vector2Int(-4, 1);
        private static readonly Vector2Int InB = new Vector2Int(-4, 0);
        private static readonly Vector2Int InC = new Vector2Int(-4, -1);
        private static readonly Vector2Int OutY = new Vector2Int(4, 1);
        private static readonly Vector2Int OutZ = new Vector2Int(4, -1);
        private static readonly Vector2Int First = new Vector2Int(0, 1);
        private static readonly Vector2Int Second = new Vector2Int(0, -1);

        internal static BlueprintWire Wire(Vector2Int from, Vector2Int to, int toPort = 0, int delay = 1, int fromPort = 0) =>
            new BlueprintWire(new CellPort(from, false, fromPort), new CellPort(to, true, toPort), delay);

        private static BlockPort[] Ports(params (string id, Vector2Int cell)[] ports)
        {
            var result = new BlockPort[ports.Length];

            for (int i = 0; i < ports.Length; i++)
                result[i] = new BlockPort(ports[i].id, ports[i].cell);

            return result;
        }

        /// <summary>A, B and C into two XORs in a row, out at Y: three inputs, so two cells tall.</summary>
        internal static BlockDefinition Parity(string name = "PAR")
        {
            bool made = BlockRules.TryDefine(
                name,
                Ports(("a", InA), ("b", InB), ("c", InC)),
                Ports(("y", OutY)),
                new[] { new GatePlacement(First, GateKind.Xor), new GatePlacement(Second, GateKind.Xor) },
                new[]
                {
                    Wire(InA, First, 0), Wire(InB, First, 1),
                    Wire(First, Second, 0), Wire(InC, Second, 1, delay: 2),
                    Wire(Second, OutY),
                },
                out BlockDefinition block, out string refusal);

            Assert.IsTrue(made, $"sanity: the parity block was refused: {refusal}");
            return block;
        }

        /// <summary>Tries a one-input, one-output block with the given parts and says why it failed.</summary>
        private static string Refusal(
            BlockPort[] inputs, BlockPort[] outputs, GatePlacement[] gates, BlueprintWire[] wires, string name = "B")
        {
            bool made = BlockRules.TryDefine(name, inputs, outputs, gates, wires, out BlockDefinition block, out string refusal);

            Assert.IsFalse(made, "a block that breaks a rule was made");
            Assert.IsNull(block);
            Assert.IsFalse(string.IsNullOrEmpty(refusal), "refused without saying why");

            return refusal;
        }

        private static readonly GatePlacement[] OneNot = { new GatePlacement(First, GateKind.Not) };

        // -----------------------------------------------------------------
        // The rules
        // -----------------------------------------------------------------

        [Test]
        public void AnInverterInABox_IsABlock()
        {
            Assert.IsTrue(BlockRules.TryDefine(
                "INV", Ports(("a", InA)), Ports(("y", OutY)), OneNot,
                new[] { Wire(InA, First), Wire(First, OutY) }, out BlockDefinition block, out string refusal), refusal);

            Assert.AreEqual("INV", block.Name);
            Assert.AreEqual(1, block.Height);
        }

        [Test]
        public void AName_IsNeededAndShort()
        {
            BlueprintWire[] wires = { Wire(InA, First), Wire(First, OutY) };

            StringAssert.Contains("name", Refusal(Ports(("a", InA)), Ports(("y", OutY)), OneNot, wires, name: "  "));
            StringAssert.Contains($"{BlockRules.MaxNameLength}",
                Refusal(Ports(("a", InA)), Ports(("y", OutY)), OneNot, wires, name: new string('X', BlockRules.MaxNameLength + 1)));
        }

        [Test]
        public void PortCounts_AreOneToFiveOnEachFace()
        {
            var cells = new List<(string, Vector2Int)>();

            for (int i = 0; i <= BlockRules.MaxPorts; i++)
                cells.Add(($"p{i}", new Vector2Int(-4, 3 - i)));

            StringAssert.Contains("input", Refusal(Ports(), Ports(("y", OutY)), OneNot, new BlueprintWire[0]));
            StringAssert.Contains("output", Refusal(Ports(("a", InA)), Ports(), OneNot, new[] { Wire(InA, First) }));
            StringAssert.Contains($"at most {BlockRules.MaxPorts} inputs",
                Refusal(Ports(cells.ToArray()), Ports(("y", OutY)), OneNot, new BlueprintWire[0]));
        }

        [Test]
        public void ARegister_IsNotAllowedInside()
        {
            StringAssert.Contains("register", Refusal(
                Ports(("a", InA)), Ports(("y", OutY)), new[] { new GatePlacement(First, GateKind.Register) },
                new[] { Wire(InA, First), Wire(First, OutY) }));
        }

        [Test]
        public void EveryInput_LeadsSomewhereInside()
        {
            StringAssert.Contains("Input b", Refusal(
                Ports(("a", InA), ("b", InB)), Ports(("y", OutY)), OneNot,
                new[] { Wire(InA, First), Wire(First, OutY) }));
        }

        [Test]
        public void EveryOutput_IsFedByExactlyOneWire()
        {
            GatePlacement[] two = { new GatePlacement(First, GateKind.Not), new GatePlacement(Second, GateKind.Not) };

            StringAssert.Contains("output z", Refusal(
                Ports(("a", InA)), Ports(("y", OutY), ("z", OutZ)), OneNot,
                new[] { Wire(InA, First), Wire(First, OutY) }));

            StringAssert.Contains("takes one", Refusal(
                Ports(("a", InA)), Ports(("y", OutY)), two,
                new[] { Wire(InA, First), Wire(InA, Second), Wire(First, OutY), Wire(Second, OutY) }));
        }

        [Test]
        public void AnInput_IsNotWiredStraightToAnOutput()
        {
            StringAssert.Contains("straight", Refusal(
                Ports(("a", InA)), Ports(("y", OutY), ("z", OutZ)), OneNot,
                new[] { Wire(InA, First), Wire(First, OutY), Wire(InA, OutZ) }));
        }

        [Test]
        public void WiresInside_JoinRealPorts_TheRightWayRound_WithADelay()
        {
            BlockPort[] a = Ports(("a", InA));
            BlockPort[] y = Ports(("y", OutY));

            StringAssert.Contains("starts at", Refusal(a, y, OneNot, new[] { Wire(InA, First), Wire(First, OutY, fromPort: 1) }));
            StringAssert.Contains("ends at", Refusal(a, y, OneNot, new[] { Wire(InA, First, toPort: 1), Wire(First, OutY) }));
            StringAssert.Contains("tick", Refusal(a, y, OneNot, new[] { Wire(InA, First, delay: 0), Wire(First, OutY) }));
            StringAssert.Contains("same two ports", Refusal(a, y, OneNot,
                new[] { Wire(InA, First), Wire(InA, First, delay: 2), Wire(First, OutY) }));

            var backwards = new BlueprintWire(new CellPort(First, true, 0), new CellPort(InA, false, 0), 1);
            StringAssert.Contains("from an input", Refusal(a, y, OneNot, new[] { backwards, Wire(InA, First), Wire(First, OutY) }));
        }

        [Test]
        public void PortsAndParts_EachHaveACellAndANameOfTheirOwn()
        {
            StringAssert.Contains("both called", Refusal(
                Ports(("a", InA)), Ports(("a", OutY)), OneNot, new[] { Wire(InA, First), Wire(First, OutY) }));

            StringAssert.Contains("share the cell", Refusal(
                Ports(("a", InA)), Ports(("y", First)), OneNot, new[] { Wire(InA, First), Wire(First, First) }));
        }

        [Test]
        public void ABlockIsAsTallAsHalfItsBusierFace_RoundedUp()
        {
            Assert.AreEqual(2, Parity().Height, "three inputs need two cells");
        }

        // -----------------------------------------------------------------
        // From a board
        // -----------------------------------------------------------------

        [Test]
        public void ABoard_BecomesABlock_WithItsPortsTopToBottom_AndItsDeadWiresLeftOut()
        {
            LevelDefinition level = LevelTestFixtures.Parse(@"{
                ""name"": ""Two in"", ""tickLimit"": 40,
                ""fixtures"": [
                    { ""id"": ""low"",  ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"": -1 }, ""stream"": ""01"" },
                    { ""id"": ""high"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"":  1 }, ""stream"": ""01"" },
                    { ""id"": ""out"",  ""kind"": ""Sink"",   ""cell"": { ""x"":  4, ""y"":  0 } }
                ],
                ""budget"": [ { ""kind"": ""And"", ""count"": 1 } ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""01"" } ]
            }");

            var board = new CircuitBlueprint();
            board.Place(new Vector2Int(0, 0), GateKind.And);
            board.AddWire(Wire(new Vector2Int(-4, 1), new Vector2Int(0, 0), 0));
            board.AddWire(Wire(new Vector2Int(-4, -1), new Vector2Int(0, 0), 1));
            board.AddWire(Wire(new Vector2Int(0, 0), new Vector2Int(4, 0)));
            board.AddWire(Wire(new Vector2Int(0, 0), new Vector2Int(2, 2)));   // into an empty cell

            Assert.IsTrue(BlockRules.TryMakeFromBoard("AND", level, board, out BlockDefinition block, out string refusal), refusal);

            Assert.AreEqual("high", block.Inputs[0].Id, "the top source is the top input");
            Assert.AreEqual("low", block.Inputs[1].Id);
            Assert.AreEqual(3, block.Wires.Count, "the wire into an empty cell came along");
            Assert.AreEqual(1, block.Gates.Count);
        }

        [Test]
        public void ABoardWithABlockOnIt_DoesNotBecomeOne()
        {
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(0, 2), Parity());

            Assert.IsFalse(BlockRules.TryMakeFromBoard(
                "BOX", LevelTestFixtures.Routing(), board, out BlockDefinition _, out string refusal));
            StringAssert.Contains("block on it", refusal);
        }

        // -----------------------------------------------------------------
        // On the board
        // -----------------------------------------------------------------

        [Test]
        public void ABlock_TakesEveryCellItCovers_AndOnlyThose()
        {
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(0, 1), Parity());

            Assert.IsTrue(board.HasPlacementAt(new Vector2Int(0, 1)));
            Assert.IsTrue(board.HasPlacementAt(new Vector2Int(0, 0)), "its second cell is free");
            Assert.IsFalse(board.HasPlacementAt(new Vector2Int(0, -1)), "it reaches a cell too far");
            Assert.IsFalse(board.HasPlacementAt(new Vector2Int(1, 0)));

            Assert.IsTrue(board.TryGetBlock(new Vector2Int(0, 0), out BlockPlacement found));
            Assert.AreEqual(new Vector2Int(0, 1), found.Cell, "found by its lower cell, it names another top");

            Assert.Throws<System.InvalidOperationException>(() => board.Place(new Vector2Int(0, 0), GateKind.Not));
            Assert.Throws<System.InvalidOperationException>(() => board.PlaceBlock(new Vector2Int(0, 2), Parity()));
            Assert.IsFalse(board.IsEmpty);
        }

        [Test]
        public void RemovingABlock_FromAnyOfItsCells_TakesItsWires()
        {
            var board = new CircuitBlueprint();
            var top = new Vector2Int(0, 1);

            board.PlaceBlock(top, Parity());
            board.Place(new Vector2Int(2, 0), GateKind.Not);
            board.AddWire(new BlueprintWire(new CellPort(new Vector2Int(-4, 0), false, 0), new CellPort(top, true, 2), 1));
            board.AddWire(new BlueprintWire(new CellPort(top, false, 0), new CellPort(new Vector2Int(2, 0), true, 0), 1));
            board.AddWire(Wire(new Vector2Int(2, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(board.RemoveAt(new Vector2Int(0, 0)), "the lower cell did not remove it");

            Assert.AreEqual(0, board.Blocks.Count);
            Assert.AreEqual(1, board.Wires.Count, "the block's wires stayed behind");
            Assert.AreEqual(new Vector2Int(2, 0), board.Wires[0].From.Cell);
        }

        [Test]
        public void ABlock_IsCountedByName_AndByTheGatesInside()
        {
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(-1, 1), Parity());
            board.PlaceBlock(new Vector2Int(1, 1), Parity());
            board.Place(new Vector2Int(0, -2), GateKind.Not);

            Assert.AreEqual(2, board.CountOfBlock("PAR"));
            Assert.AreEqual(0, board.CountOfBlock("FA"));
            Assert.AreEqual(4, board.GatesInBlocks(), "a block scores the gates inside it");
            Assert.AreEqual(1, board.CountOf(GateKind.Not), "a gate inside a block counted as one on the board");
        }

        [Test]
        public void Undo_KeepsTheBlocks()
        {
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(0, 1), Parity());
            BlueprintSnapshot before = board.Snapshot();

            board.Clear();
            Assert.IsTrue(board.IsEmpty);

            board.Restore(before);

            Assert.AreEqual(1, board.Blocks.Count);
            Assert.IsTrue(before.Matches(board.Snapshot()));
            Assert.IsTrue(before.DescribesBoard(board));
        }

        [Test]
        public void TwoBoards_AreTheSame_OnlyWithTheSameBlocksOnTheSameCells()
        {
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(0, 1), Parity());
            BlueprintSnapshot one = board.Snapshot();

            var copy = new CircuitBlueprint();
            copy.PlaceBlock(new Vector2Int(0, 1), Parity());
            Assert.IsTrue(one.Matches(copy.Snapshot()), "an equal copy of the block did not match");
            Assert.IsTrue(one.DescribesBoard(copy));

            var renamed = new CircuitBlueprint();
            renamed.PlaceBlock(new Vector2Int(0, 1), Parity("XOR3"));
            Assert.IsFalse(one.Matches(renamed.Snapshot()), "a different block matched");
            Assert.IsFalse(one.DescribesBoard(renamed));

            var moved = new CircuitBlueprint();
            moved.PlaceBlock(new Vector2Int(1, 1), Parity());
            Assert.IsFalse(one.DescribesBoard(moved), "the block on another cell matched");
        }
    }
}
