using System;
using System.Collections.Generic;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// How a block is built into the graph, and the one promise that matters most: a block takes
    /// exactly as long as its contents placed directly, and loses exactly the same bits.
    /// </summary>
    /// <remarks>
    /// A block's boundary is spliced (see <see cref="CircuitBuilder"/>): the wire drawn to a port and
    /// the block's own wire from it become one edge of the two delays less one. Every test here that
    /// compares runs builds the same circuit both ways and asks the simulator, rather than working out
    /// what the splice ought to give.
    /// </remarks>
    public class BlockBuildTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, -1);
        private static readonly Vector2Int SinkY = new Vector2Int(4, 1);
        private static readonly Vector2Int SinkZ = new Vector2Int(4, -1);

        private static LevelDefinition TwoInTwoOut() => LevelTestFixtures.Parse(@"{
                ""name"": ""Two in, two out"", ""tickLimit"": 80,
                ""fixtures"": [
                    { ""id"": ""a"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"":  1 }, ""stream"": ""0110"" },
                    { ""id"": ""b"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"": -1 }, ""stream"": ""0101"" },
                    { ""id"": ""y"", ""kind"": ""Sink"",   ""cell"": { ""x"":  4, ""y"":  1 } },
                    { ""id"": ""z"", ""kind"": ""Sink"",   ""cell"": { ""x"":  4, ""y"": -1 } }
                ],
                ""budget"": [ { ""kind"": ""Not"", ""count"": 4 }, { ""kind"": ""Xor"", ""count"": 2 } ],
                ""expected"": [ { ""sink"": ""y"", ""values"": ""----"" }, { ""sink"": ""z"", ""values"": ""----"" } ]
            }");

        private static BlueprintWire Wire(Vector2Int from, int fromPort, Vector2Int to, int toPort, int delay) =>
            new BlueprintWire(new CellPort(from, false, fromPort), new CellPort(to, true, toPort), delay);

        private static BlockDefinition Define(
            string name, (string, Vector2Int)[] inputs, (string, Vector2Int)[] outputs, GatePlacement[] gates,
            params BlueprintWire[] wires)
        {
            var ins = new BlockPort[inputs.Length];
            var outs = new BlockPort[outputs.Length];

            for (int i = 0; i < ins.Length; i++)
                ins[i] = new BlockPort(inputs[i].Item1, inputs[i].Item2);

            for (int j = 0; j < outs.Length; j++)
                outs[j] = new BlockPort(outputs[j].Item1, outputs[j].Item2);

            Assert.IsTrue(BlockRules.TryDefine(name, ins, outs, gates, wires, out BlockDefinition block, out string refusal),
                $"sanity: {name} was refused: {refusal}");

            return block;
        }

        private static readonly Vector2Int In = new Vector2Int(-2, 0);
        private static readonly Vector2Int Out = new Vector2Int(2, 0);
        private static readonly Vector2Int Inner = new Vector2Int(0, 0);
        private static readonly Vector2Int InnerUp = new Vector2Int(0, 1);
        private static readonly Vector2Int InnerDown = new Vector2Int(0, -1);

        /// <summary>One NOT in a box, with its two wires inside as long as asked.</summary>
        private static BlockDefinition Inverter(int insideIn, int insideOut) => Define(
            "INV", new[] { ("a", In) }, new[] { ("y", Out) }, new[] { new GatePlacement(Inner, GateKind.Not) },
            Wire(In, 0, Inner, 0, insideIn), Wire(Inner, 0, Out, 0, insideOut));

        /// <summary>One input that two NOTs inside both read, each out at its own port.</summary>
        private static BlockDefinition Split(int toUpper, int toLower) => Define(
            "SPLT", new[] { ("a", In) }, new[] { ("y", new Vector2Int(2, 1)), ("z", new Vector2Int(2, -1)) },
            new[] { new GatePlacement(InnerUp, GateKind.Not), new GatePlacement(InnerDown, GateKind.Not) },
            Wire(In, 0, InnerUp, 0, toUpper), Wire(In, 0, InnerDown, 0, toLower),
            Wire(InnerUp, 0, new Vector2Int(2, 1), 0, 1), Wire(InnerDown, 0, new Vector2Int(2, -1), 0, 1));

        /// <summary>Two NOTs in a row, so the block has a wire between two gates inside.</summary>
        private static BlockDefinition TwoInARow() => Define(
            "BUF", new[] { ("a", In) }, new[] { ("y", Out) },
            new[] { new GatePlacement(InnerUp, GateKind.Not), new GatePlacement(InnerDown, GateKind.Not) },
            Wire(In, 0, InnerUp, 0, 1), Wire(InnerUp, 0, InnerDown, 0, 2), Wire(InnerDown, 0, Out, 0, 1));

        // -----------------------------------------------------------------
        // Running both ways
        // -----------------------------------------------------------------

        private sealed class Run
        {
            public BuiltCircuit Built;
            public RunVerdict Verdict;
        }

        private static Run RunOf(LevelDefinition level, CircuitBlueprint board)
        {
            BuiltCircuit built = CircuitBuilder.Build(level, board);
            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, level, built.FixtureNodeIds);
            return new Run { Built = built, Verdict = verdict };
        }

        private static IReadOnlyList<SinkNode.Reception> Received(Run run, string sink) =>
            ((SinkNode)run.Built.Simulation.GetNode(run.Built.FixtureNodeIds[sink])).Received;

        /// <summary>Every sink received the same bits on the same ticks, and the same bits were lost.</summary>
        private static void AssertSameRun(LevelDefinition level, CircuitBlueprint direct, CircuitBlueprint boxed)
        {
            Run one = RunOf(level, direct);
            Run other = RunOf(level, boxed);

            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind != FixtureKind.Sink)
                    continue;

                CollectionAssert.AreEqual(Received(one, fixture.Id), Received(other, fixture.Id),
                    $"{fixture.Id} received differently, or on different ticks, through the block");
            }

            Assert.AreEqual(one.Built.Simulation.CorruptedCount, other.Built.Simulation.CorruptedCount,
                "the block lost a different number of bits");
            Assert.AreEqual(one.Verdict.IsPass, other.Verdict.IsPass, $"{one.Verdict} against {other.Verdict}");
        }

        /// <summary>
        /// A board holding nothing but a block made from <paramref name="direct"/>, its top on
        /// <paramref name="at"/>, wired at one tick from the level's sources and to its sinks.
        /// </summary>
        private static CircuitBlueprint Boxed(LevelDefinition level, CircuitBlueprint direct, Vector2Int at)
        {
            Assert.IsTrue(BlockRules.TryMakeFromBoard("BLK", level, direct, out BlockDefinition block, out string refusal),
                $"the reference circuit did not make a block: {refusal}");

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(at, block);

            for (int i = 0; i < block.Inputs.Count; i++)
                boxed.AddWire(Wire(block.Inputs[i].Cell, 0, at, i, 1));

            for (int j = 0; j < block.Outputs.Count; j++)
                boxed.AddWire(Wire(at, j, block.Outputs[j].Cell, 0, 1));

            return boxed;
        }

        // -----------------------------------------------------------------
        // No delay at the boundary
        // -----------------------------------------------------------------

        /// <summary>
        /// A NOT in a box, wired in and out, against a NOT on the board whose two wires are the joined
        /// lengths: each pair of wires meeting at the box is one wire, a tick shorter than the two.
        /// </summary>
        [TestCase(1, 1, 1, 1)]
        [TestCase(2, 1, 1, 1)]
        [TestCase(1, 3, 1, 1)]
        [TestCase(1, 1, 2, 3)]
        [TestCase(3, 2, 4, 1)]
        public void ABlock_TakesExactlyAsLongAsItsContents(int intoBlock, int insideIn, int insideOut, int outOfBlock)
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(at, Inverter(insideIn, insideOut));
            boxed.AddWire(Wire(SourceA, 0, at, 0, intoBlock));
            boxed.AddWire(Wire(at, 0, SinkY, 0, outOfBlock));

            var direct = new CircuitBlueprint();
            direct.Place(at, GateKind.Not);
            direct.AddWire(Wire(SourceA, 0, at, 0, intoBlock + insideIn - 1));
            direct.AddWire(Wire(at, 0, SinkY, 0, insideOut + outOfBlock - 1));

            AssertSameRun(level, direct, boxed);

            Run run = RunOf(level, boxed);
            Assert.AreEqual(intoBlock + insideIn + insideOut + outOfBlock - 2, Received(run, "y")[0].Tick,
                "vector 0, sent on tick 0, should arrive after the four wires less the two joins");
        }

        [Test]
        public void ABlockBoundary_AddsNoDelay()
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(at, Inverter(1, 1));
            boxed.AddWire(Wire(SourceA, 0, at, 0, 1));
            boxed.AddWire(Wire(at, 0, SinkY, 0, 1));

            Assert.AreEqual(2, Received(RunOf(level, boxed), "y")[0].Tick,
                "a NOT between a source and a sink takes two ticks on the board, and must in a box too");
        }

        [Test]
        public void FromOneBlockToAnother_TheThreeWiresAreOne()
        {
            LevelDefinition level = TwoInTwoOut();
            var left = new Vector2Int(-1, 1);
            var right = new Vector2Int(1, 1);

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(left, Inverter(2, 3));
            boxed.PlaceBlock(right, Inverter(4, 1));
            boxed.AddWire(Wire(SourceA, 0, left, 0, 1));
            boxed.AddWire(Wire(left, 0, right, 0, 2));
            boxed.AddWire(Wire(right, 0, SinkY, 0, 1));

            var direct = new CircuitBlueprint();
            direct.Place(left, GateKind.Not);
            direct.Place(right, GateKind.Not);
            direct.AddWire(Wire(SourceA, 0, left, 0, 1 + 2 - 1));
            direct.AddWire(Wire(left, 0, right, 0, 3 + 2 + 4 - 2));
            direct.AddWire(Wire(right, 0, SinkY, 0, 1 + 1 - 1));

            AssertSameRun(level, direct, boxed);
        }

        /// <summary>
        /// The shipped combinational levels' own reference circuits, each run as it is and again packed
        /// into one block wired at a tick each way.
        /// </summary>
        private static IEnumerable<TestCaseData> ShippedCircuits()
        {
            yield return Shipped("carry-the-one", () => CarryTheOneLevelTests.FullAdder());
            yield return Shipped("carry-the-one", () => CarryTheOneLevelTests.FullAdder(cinDelay: 1), "unbalanced");
            yield return Shipped("which-is-bigger", () => WhichIsBiggerLevelTests.Comparator());
            yield return Shipped("pick-a-lane", () => PickALaneLevelTests.TextbookMux());
            yield return Shipped("four-corners", () => FourCornersLevelTests.FirstCover());
            yield return Shipped("nothing-but-nand", () => NothingButNandLevelTests.CanonicalFour());
            yield return Shipped("balance-the-paths", () => BalanceLevelTests.Wiring(directDelay: 2));
            yield return Shipped("balance-the-paths", () => BalanceLevelTests.Wiring(), "unbalanced");
            yield return Shipped("one-of-four", () => OneOfFourLevelTests.Decoder());
            yield return Shipped("odd-one-out", () => OddOneOutLevelTests.Tree());
            yield return Shipped("dont-care", () => DontCareLevelTests.SumOfProducts());
            yield return Shipped("highest-wins", () => HighestWinsLevelTests.Encoder());
            yield return Shipped("pass-it-on", () => PassItOnLevelTests.Adder());
        }

        private static TestCaseData Shipped(string level, Func<CircuitBlueprint> circuit, string variant = "solved") =>
            new TestCaseData(level, circuit, variant == "unbalanced")
                .SetName($"AShippedCircuit_RunsTheSameInABlock({level}, {variant})");

        /// <param name="unbalanced">
        /// A variant that loses bits, so the comparison covers the collisions as well as the answers.
        /// </param>
        [TestCaseSource(nameof(ShippedCircuits))]
        public void AShippedCircuit_RunsTheSameInABlock(string levelName, Func<CircuitBlueprint> circuit, bool unbalanced)
        {
            LevelLoadResult loaded = LevelLoader.Load(levelName, LevelTestFixtures.Board);
            Assert.IsTrue(loaded.IsValid, loaded.Error);

            LevelDefinition level = loaded.Level;
            int top = level.HasBoard ? level.BoardHalfExtents.y : LevelTestFixtures.Board.y;
            CircuitBlueprint direct = circuit();

            AssertSameRun(level, direct, Boxed(level, direct, new Vector2Int(0, top)));

            Run run = RunOf(level, direct);
            Assert.AreEqual(!unbalanced, run.Verdict.IsPass, $"sanity: the reference circuit {run.Verdict}");

            if (unbalanced)
                Assert.Greater(run.Built.Simulation.CorruptedCount, 0, "sanity: the unbalanced variant lost nothing");
        }

        // -----------------------------------------------------------------
        // Fan-out, fan-in, and ports left unwired
        // -----------------------------------------------------------------

        [Test]
        public void AWireIntoAnInputTwoGatesRead_IsTwoEdges_DrawnAsOne()
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(at, Split(1, 3));
            boxed.AddWire(Wire(SourceA, 0, at, 0, 2));

            BuiltCircuit built = CircuitBuilder.Build(level, boxed);
            int anchor = built.Blocks[0].InputAnchors[0];

            Assert.AreEqual(2, built.Simulation.EdgeCount, "one edge for each gate the input leads to");
            Assert.AreEqual(2, built.Simulation.GetEdge(0).Delay, "2 drawn, joined to the 1 inside");
            Assert.AreEqual(4, built.Simulation.GetEdge(1).Delay, "2 drawn, joined to the 3 inside");

            for (int id = 0; id < 2; id++)
            {
                EdgeShape shape = built.Shapes[id];

                Assert.AreSame(built.Simulation.GetNode(anchor).In(0), shape.DrawnTo, "not drawn to the box's port");
                Assert.AreEqual(2, shape.DrawnDelay);
                Assert.AreEqual(0, shape.HiddenBefore);
                Assert.AreEqual(0, shape.Wire);
            }

            Assert.AreEqual(0, built.Shapes[0].HiddenAfter);
            Assert.AreEqual(2, built.Shapes[1].HiddenAfter);
            Assert.IsTrue(built.Shapes[0].Drawn);
            Assert.IsFalse(built.Shapes[1].Drawn, "the one drawn wire was drawn twice");
        }

        [Test]
        public void TwoWiresIntoOneBlockInput_LoseABitAtEachGateInside_AsTheContentsWould()
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);
            var upper = new Vector2Int(0, 1);
            var lower = new Vector2Int(0, -1);

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(at, Split(1, 1));
            boxed.AddWire(Wire(SourceA, 0, at, 0, 1));
            boxed.AddWire(Wire(SourceB, 0, at, 0, 1));
            boxed.AddWire(Wire(at, 0, SinkY, 0, 1));
            boxed.AddWire(Wire(at, 1, SinkZ, 0, 1));

            var direct = new CircuitBlueprint();
            direct.Place(upper, GateKind.Not);
            direct.Place(lower, GateKind.Not);
            direct.AddWire(Wire(SourceA, 0, upper, 0, 1));
            direct.AddWire(Wire(SourceA, 0, lower, 0, 1));
            direct.AddWire(Wire(SourceB, 0, upper, 0, 1));
            direct.AddWire(Wire(SourceB, 0, lower, 0, 1));
            direct.AddWire(Wire(upper, 0, SinkY, 0, 1));
            direct.AddWire(Wire(lower, 0, SinkZ, 0, 1));

            AssertSameRun(level, direct, boxed);
            Assert.Greater(RunOf(level, boxed).Built.Simulation.CorruptedCount, 0, "sanity: nothing collided");
        }

        /// <remarks>
        /// Waiting is not losing nothing: the gate holds the first bit on its fed input, and every bit
        /// after it collides there, exactly as on a gate placed on the board with one input unwired.
        /// </remarks>
        [Test]
        public void ABlockInputLeftUnwired_LeavesItsGatesWaiting_AndTheRunStillEnds()
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);

            BlockDefinition xor = Define(
                "XOR", new[] { ("a", new Vector2Int(-2, 1)), ("b", new Vector2Int(-2, -1)) }, new[] { ("y", Out) },
                new[] { new GatePlacement(Inner, GateKind.Xor) },
                Wire(new Vector2Int(-2, 1), 0, Inner, 0, 1), Wire(new Vector2Int(-2, -1), 0, Inner, 1, 1),
                Wire(Inner, 0, Out, 0, 1));

            var boxed = new CircuitBlueprint();
            boxed.PlaceBlock(at, xor);
            boxed.AddWire(Wire(SourceA, 0, at, 0, 1));
            boxed.AddWire(Wire(at, 0, SinkY, 0, 1));

            Run run = RunOf(level, boxed);

            Assert.IsTrue(LevelGrader.IsSettled(run.Built.Simulation.View), "a block with a dead input never settled");
            Assert.IsEmpty(Received(run, "y"));

            var direct = new CircuitBlueprint();
            direct.Place(at, GateKind.Xor);
            direct.AddWire(Wire(SourceA, 0, at, 0, 1));
            direct.AddWire(Wire(at, 0, SinkY, 0, 1));

            AssertSameRun(level, direct, boxed);
        }

        // -----------------------------------------------------------------
        // Ids, shapes and anchors
        // -----------------------------------------------------------------

        [Test]
        public void NodesAndEdges_ComeInTheContractsOrder()
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);
            var gate = new Vector2Int(-2, -1);

            var board = new CircuitBlueprint();
            board.PlaceBlock(at, TwoInARow());
            board.Place(gate, GateKind.Not);
            board.AddWire(Wire(SourceA, 0, at, 0, 1));
            board.AddWire(Wire(at, 0, SinkY, 0, 1));
            board.AddWire(Wire(SourceB, 0, gate, 0, 1));

            BuiltCircuit built = CircuitBuilder.Build(level, board);
            Simulation sim = built.Simulation;

            // Fixtures 0-3, then the gate, then the block: its anchor in, its anchor out, its gates.
            Assert.AreEqual(9, sim.NodeCount);
            Assert.IsInstanceOf<NotGate>(sim.GetNode(4), "the gate on the board did not come before the block");
            Assert.AreEqual("BUF.a", sim.GetNode(5).Name);
            Assert.AreEqual("BUF.y", sim.GetNode(6).Name);
            Assert.IsInstanceOf<NotGate>(sim.GetNode(7));
            Assert.IsInstanceOf<NotGate>(sim.GetNode(8));
            Assert.IsTrue(built.IsInsideABlock(7) && built.IsInsideABlock(8));

            // The three drawn wires in order, then the one between the two gates inside.
            Assert.AreEqual(4, sim.EdgeCount);
            Assert.AreEqual(0, built.Shapes[0].Wire);
            Assert.AreEqual(1, built.Shapes[1].Wire);
            Assert.AreEqual(2, built.Shapes[2].Wire);
            Assert.AreEqual(-1, built.Shapes[3].Wire, "the wire inside did not come last");
            Assert.IsFalse(built.Shapes[3].Drawn);
        }

        [Test]
        public void TwoBuildsOfABoardWithBlocks_AreTheSameGraph()
        {
            LevelLoadResult loaded = LevelLoader.Load("carry-the-one", LevelTestFixtures.Board);
            Assert.IsTrue(loaded.IsValid, loaded.Error);

            CircuitBlueprint board = Boxed(loaded.Level, CarryTheOneLevelTests.FullAdder(), new Vector2Int(0, 2));
            BuiltCircuit one = CircuitBuilder.Build(loaded.Level, board);
            BuiltCircuit two = CircuitBuilder.Build(loaded.Level, board);

            Assert.AreEqual(one.Simulation.NodeCount, two.Simulation.NodeCount);

            for (int id = 0; id < one.Simulation.NodeCount; id++)
            {
                Node a = one.Simulation.GetNode(id);
                Node b = two.Simulation.GetNode(id);

                Assert.AreEqual(a.GetType(), b.GetType(), $"node {id}");
                Assert.AreEqual(a.Name, b.Name, $"node {id}");
            }

            Assert.AreEqual(one.Simulation.EdgeCount, two.Simulation.EdgeCount);

            for (int id = 0; id < one.Simulation.EdgeCount; id++)
            {
                Edge a = one.Simulation.GetEdge(id);
                Edge b = two.Simulation.GetEdge(id);

                Assert.AreEqual(Describe(a), Describe(b), $"edge {id}");
                Assert.AreEqual(one.Shapes[id].ToString(), two.Shapes[id].ToString(), $"shape {id}");
            }
        }

        private static string Describe(Edge edge) =>
            $"{edge.Source.Owner.Id}.{edge.Source.Index} -> {edge.Target.Owner.Id}.{edge.Target.Index} ({edge.Delay})";

        [Test]
        public void OnABoardWithNoBlocks_EveryShapeIsItsEdge()
        {
            LevelLoadResult loaded = LevelLoader.Load("carry-the-one", LevelTestFixtures.Board);
            Assert.IsTrue(loaded.IsValid, loaded.Error);

            BuiltCircuit built = CircuitBuilder.Build(loaded.Level, CarryTheOneLevelTests.FullAdder());

            Assert.AreEqual(built.Simulation.EdgeCount, built.Shapes.Count);

            for (int id = 0; id < built.Shapes.Count; id++)
            {
                Edge edge = built.Simulation.GetEdge(id);
                EdgeShape shape = built.Shapes[id];

                Assert.AreSame(edge.Source, shape.DrawnFrom, $"edge {id}");
                Assert.AreSame(edge.Target, shape.DrawnTo, $"edge {id}");
                Assert.AreEqual(edge.Delay, shape.DrawnDelay, $"edge {id}");
                Assert.IsTrue(shape.IsPlain && shape.Drawn, $"edge {id}");
                Assert.AreEqual(id, shape.Wire, "every wire resolves, so wire and edge share an index");
            }
        }

        [Test]
        public void AnAnchor_StandsForItsBoxesPort_OnTheSideThatFacesOut()
        {
            LevelDefinition level = TwoInTwoOut();
            var at = new Vector2Int(0, 1);

            var board = new CircuitBlueprint();
            board.PlaceBlock(at, Split(1, 1));
            BuiltCircuit built = CircuitBuilder.Build(level, board);
            BuiltBlock block = built.Blocks[0];

            int input = block.InputAnchors[0];
            int lowerOutput = block.OutputAnchors[1];
            int inside = block.Gates[0];

            Assert.IsTrue(built.TryBoardPort(input, true, 0, out CellPort port));
            Assert.AreEqual(new CellPort(at, true, 0), port);
            Assert.IsFalse(built.TryBoardPort(input, false, 0, out CellPort _), "an input anchor's inner side faced out");

            Assert.IsTrue(built.TryBoardPort(lowerOutput, false, 0, out port));
            Assert.AreEqual(new CellPort(at, false, 1), port, "the lower output is the box's port 1");

            Assert.IsFalse(built.TryBoardPort(inside, true, 0, out CellPort _), "a gate inside stood for a board port");
            Assert.IsTrue(built.IsInsideABlock(inside));
            Assert.IsTrue(built.IsAnchor(input) && !built.IsInsideABlock(input));
            Assert.AreEqual(0, built.BlockOf(input));

            int source = built.FixtureNodeIds["a"];
            Assert.AreEqual(-1, built.BlockOf(source));
            Assert.IsTrue(built.TryBoardPort(source, false, 0, out port));
            Assert.AreEqual(new CellPort(SourceA, false, 0), port);
        }
    }
}
