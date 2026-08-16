using System;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The blueprint is the only authority on the circuit: the Simulation is derived from it and thrown
    /// away on every edit, on Run and on Reset. These tests pin the two properties that makes safe --
    /// that rebuilding is reproducible, and that removing a node cannot leave a wire pointing at
    /// nothing.
    /// </summary>
    public class CircuitBlueprintTests
    {
        private LevelDefinition _level;
        private CircuitBlueprint _blueprint;

        [SetUp]
        public void SetUp()
        {
            _level = LevelTestFixtures.Routing();
            _blueprint = new CircuitBlueprint();
        }

        // -----------------------------------------------------------------
        // Reproducibility
        // -----------------------------------------------------------------

        [Test]
        public void TwoBuildsOfOneBlueprint_ProduceIdenticalNodeIds()
        {
            // Node ids are nothing but Simulation.Add call order, and the layout table is keyed by id.
            // If a rebuild shuffled them, every node would silently swap places on screen.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinOneCell);

            BuiltCircuit first = CircuitBuilder.Build(_level, _blueprint);
            BuiltCircuit second = CircuitBuilder.Build(_level, _blueprint);

            Assert.AreEqual(first.Simulation.NodeCount, second.Simulation.NodeCount, "node count");
            Assert.AreEqual(first.Simulation.EdgeCount, second.Simulation.EdgeCount, "edge count");

            foreach (string id in new[] { "in", "binOne", "binZero" })
            {
                Assert.AreEqual(first.FixtureNodeIds[id], second.FixtureNodeIds[id],
                    $"fixture '{id}' must keep its id across a rebuild");
            }

            for (int id = 0; id < first.Simulation.NodeCount; id++)
            {
                Assert.AreEqual(first.Cells[id], second.Cells[id],
                    $"node {id} must land on the same cell in both builds");
            }
        }

        [Test]
        public void FixturesAreAddedBeforePlacements()
        {
            // Fixtures come from the level file and placements from the player, so fixtures having the
            // lower ids is what lets a level's expectations be resolved without consulting the player's
            // edits at all.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);

            Assert.AreEqual(3, _level.Fixtures.Count, "the routing level has three fixtures");
            Assert.AreEqual(4, built.Simulation.LiveNodeCount, "three fixtures plus one gate");

            foreach (string id in new[] { "in", "binOne", "binZero" })
                Assert.Less(built.FixtureNodeIds[id], 3, $"fixture '{id}' should hold an early id");
        }

        [Test]
        public void ARunAndResetCycle_LeavesTheBlueprintUnchanged()
        {
            // Reset is a re-derive, not a restore. Running must not consume or mutate the description.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinOneCell);

            BlueprintWire[] before = new BlueprintWire[_blueprint.Wires.Count];
            for (int i = 0; i < before.Length; i++)
                before[i] = _blueprint.Wires[i];

            BuiltCircuit built = CircuitBuilder.Build(_level, _blueprint);
            built.Simulation.Run(20);
            CircuitBuilder.Build(_level, _blueprint);   // the Reset

            Assert.AreEqual(1, _blueprint.Placements.Count, "placements");
            Assert.AreEqual(before.Length, _blueprint.Wires.Count, "wire count");

            for (int i = 0; i < before.Length; i++)
                Assert.AreEqual(before[i], _blueprint.Wires[i], $"wire {i}");
        }

        // -----------------------------------------------------------------
        // Removal
        // -----------------------------------------------------------------

        [Test]
        public void RemovingAGate_DropsTheWiresThatTouchedIt()
        {
            // Mirrors Simulation.Remove, which retires every edge touching a removed node. A wire left
            // pointing at an empty cell would silently vanish on the next build instead.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinOneCell);

            Assert.AreEqual(2, _blueprint.Wires.Count, "wired in and out");

            Assert.IsTrue(_blueprint.RemoveAt(LevelTestFixtures.MiddleCell));

            Assert.AreEqual(0, _blueprint.Placements.Count, "the gate is gone");
            Assert.AreEqual(0, _blueprint.Wires.Count, "both wires touched it");
        }

        [Test]
        public void RemovingAGate_LeavesUnrelatedWiresAlone()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            // Straight from the source to a bin, touching neither end of the gate.
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.BinZeroCell);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);

            _blueprint.RemoveAt(LevelTestFixtures.MiddleCell);

            Assert.AreEqual(1, _blueprint.Wires.Count, "the unrelated wire survives");
            Assert.AreEqual(LevelTestFixtures.BinZeroCell, _blueprint.Wires[0].To.Cell);
        }

        [Test]
        public void RemovingAnEmptyCell_ReportsFalse()
        {
            Assert.IsFalse(_blueprint.RemoveAt(LevelTestFixtures.MiddleCell));
        }

        [Test]
        public void PlacingTwiceOnOneCell_Throws()
        {
            // One cell holds one node. The blueprint's entire addressing scheme rests on it, so this is
            // a programming error rather than a refusable edit -- LevelRules turns the player's version
            // of this mistake into a message long before it reaches here.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            Assert.Throws<InvalidOperationException>(
                () => _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.And));
        }

        // -----------------------------------------------------------------
        // Bookkeeping
        // -----------------------------------------------------------------

        [Test]
        public void CountOf_TracksWhatIsPlaced()
        {
            Assert.AreEqual(0, _blueprint.CountOf(GateKind.Not), "nothing yet");

            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            _blueprint.Place(new Vector2Int(1, 1), GateKind.Not);
            _blueprint.Place(new Vector2Int(1, -1), GateKind.And);

            Assert.AreEqual(2, _blueprint.CountOf(GateKind.Not), "two NOTs");
            Assert.AreEqual(1, _blueprint.CountOf(GateKind.And), "one AND");
            Assert.AreEqual(0, _blueprint.CountOf(GateKind.Xor), "no XORs");
        }

        [Test]
        public void HasWire_MatchesAnExactPairOnly()
        {
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.BinOneCell);

            var from = new CellPort(LevelTestFixtures.SourceCell, false, 0);
            var to = new CellPort(LevelTestFixtures.BinOneCell, true, 0);
            var elsewhere = new CellPort(LevelTestFixtures.BinZeroCell, true, 0);

            Assert.IsTrue(_blueprint.HasWire(from, to), "the exact pair");
            Assert.IsFalse(_blueprint.HasWire(from, elsewhere), "a different target");
            Assert.IsFalse(_blueprint.HasWire(to, from), "reversed is not the same wire");
        }

        [Test]
        public void ACellPort_ComparesByAllThreeParts()
        {
            var cell = new Vector2Int(1, 1);
            var input = new CellPort(cell, true, 0);

            Assert.AreEqual(input, new CellPort(cell, true, 0), "identical");
            Assert.AreNotEqual(input, new CellPort(cell, false, 0), "input and output differ");
            Assert.AreNotEqual(input, new CellPort(cell, true, 1), "port index matters");
            Assert.AreNotEqual(input, new CellPort(new Vector2Int(2, 1), true, 0), "cell matters");
        }

        [Test]
        public void AnEmptyBlueprint_ReportsItself()
        {
            Assert.IsTrue(_blueprint.IsEmpty, "nothing placed or wired");

            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            Assert.IsFalse(_blueprint.IsEmpty);
        }

        [Test]
        public void Clear_DiscardsEverything()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);

            _blueprint.Clear();

            Assert.IsTrue(_blueprint.IsEmpty);
        }
    }
}
