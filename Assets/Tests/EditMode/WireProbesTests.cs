using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="WireProbes"/>: up to four wires in the timing diagram, in slots that stay put, held
    /// by the ports each wire joins so they survive the rebuild every edit makes.
    /// </summary>
    public class WireProbesTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int SumSink = new Vector2Int(3, 1);
        private static readonly Vector2Int XorCell = new Vector2Int(0, 1);

        private static LevelDefinition HalfAdder()
        {
            LevelLoadResult result = LevelLoader.Load("half-adder", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped half-adder.json is invalid: {result.Error}");
            return result.Level;
        }

        private static CircuitBlueprint XorBoard()
        {
            var board = new CircuitBlueprint();
            board.Place(XorCell, GateKind.Xor);
            LevelTestFixtures.Wire(board, SourceA, XorCell, toPort: 0);
            LevelTestFixtures.Wire(board, SourceB, XorCell, toPort: 1);
            LevelTestFixtures.Wire(board, XorCell, SumSink);
            return board;
        }

        private static WireKey Key(Vector2Int from, Vector2Int to, int toPort) =>
            new WireKey(new CellPort(from, false, 0), new CellPort(to, true, toPort));

        [Test]
        public void AWire_GoesInTheFirstFreeSlot_AndComesOutAgain()
        {
            var probes = new WireProbes();
            WireKey a = Key(SourceA, XorCell, 0);
            WireKey b = Key(SourceB, XorCell, 1);

            Assert.AreEqual(ProbeToggle.Added, probes.Toggle(a, 0));
            Assert.AreEqual(ProbeToggle.Added, probes.Toggle(b, 1));
            Assert.AreEqual(0, probes.SlotOf(a));
            Assert.AreEqual(1, probes.SlotOf(b));

            Assert.AreEqual(ProbeToggle.Removed, probes.Toggle(a, 0));
            Assert.IsFalse(probes.IsUsed(0));
            Assert.AreEqual(1, probes.SlotOf(b), "taking W1 out renumbered W2");
            Assert.AreEqual(-1, probes.EdgeIdAt(0));

            Assert.AreEqual(ProbeToggle.Added, probes.Toggle(a, 0));
            Assert.AreEqual(0, probes.SlotOf(a), "the freed slot was not reused first");
        }

        [Test]
        public void AFifthWire_IsRefused_AndNothingMoves()
        {
            var probes = new WireProbes();

            for (int i = 0; i < WireProbes.Slots; i++)
                Assert.AreEqual(ProbeToggle.Added, probes.Toggle(Key(new Vector2Int(i, 0), XorCell, 0), i));

            int revision = probes.Revision;

            Assert.AreEqual(ProbeToggle.Full, probes.Toggle(Key(SourceA, SumSink, 0), 9));
            Assert.AreEqual(revision, probes.Revision, "a refused wire changed the slots");
            Assert.AreEqual(-1, probes.SlotOf(Key(SourceA, SumSink, 0)));
        }

        /// <summary>
        /// A rebuild renumbers edges after a deletion, and the wire is found again by its ports; a
        /// wire the new graph does not have takes its slot with it.
        /// </summary>
        [Test]
        public void AfterARebuild_EachWireIsFoundAgain_OrDropped()
        {
            LevelDefinition level = HalfAdder();
            CircuitBlueprint board = XorBoard();
            BuiltCircuit built = CircuitBuilder.Build(level, board);

            var probes = new WireProbes();
            WireKey xorToSum = Key(XorCell, SumSink, 0);
            WireKey bToXor = Key(SourceB, XorCell, 1);

            int before = WireProbes.EdgeIdOf(built.Simulation.View, built.Cells, xorToSum);
            Assert.GreaterOrEqual(before, 0, "sanity: the XOR's wire into sum was not built");

            probes.Toggle(xorToSum, before);
            probes.Toggle(bToXor, WireProbes.EdgeIdOf(built.Simulation.View, built.Cells, bToXor));

            // Take a wire out ahead of both in build order: every later edge id moves down.
            LevelTestFixtures.Unwire(board, SourceA, XorCell, toPort: 0);
            BuiltCircuit rebuilt = CircuitBuilder.Build(level, board);
            probes.Resolve(rebuilt.Simulation.View, rebuilt.Cells);

            int after = probes.EdgeIdAt(probes.SlotOf(xorToSum));
            Assert.AreEqual(WireProbes.EdgeIdOf(rebuilt.Simulation.View, rebuilt.Cells, xorToSum), after);
            Assert.AreNotEqual(before, after, "sanity: the deletion should have renumbered the edge");

            // Take a picked wire itself out: its slot empties.
            LevelTestFixtures.Unwire(board, SourceB, XorCell, toPort: 1);
            BuiltCircuit again = CircuitBuilder.Build(level, board);
            probes.Resolve(again.Simulation.View, again.Cells);

            Assert.AreEqual(-1, probes.SlotOf(bToXor), "a deleted wire kept its slot");
            Assert.GreaterOrEqual(probes.SlotOf(xorToSum), 0, "the other wire was dropped with it");
        }

        [Test]
        public void Clearing_EmptiesEverySlot()
        {
            var probes = new WireProbes();
            probes.Toggle(Key(SourceA, XorCell, 0), 0);
            probes.Clear();

            for (int slot = 0; slot < WireProbes.Slots; slot++)
                Assert.IsFalse(probes.IsUsed(slot));
        }
    }
}
