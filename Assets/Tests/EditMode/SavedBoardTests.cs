using System.IO;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Boards surviving a level switch and a restart, and refusing to restore anything the level no
    /// longer permits.
    /// </summary>
    /// <remarks>
    /// The restore path is where this can go quietly wrong. A board saved before its level was
    /// edited can name a cell that now holds a fixture, a gate the budget no longer stocks, or a
    /// port that no longer exists -- and restoring any of those puts the player on a board they
    /// could not have built and cannot fix. Most of what is here is that.
    /// </remarks>
    public class SavedBoardTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private static readonly Vector2Int SourceA = new Vector2Int(-3, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-3, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(3, 0);
        private static readonly Vector2Int Middle = new Vector2Int(0, 0);

        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "bitsorter-board-test.json");
            Delete();
        }

        [TearDown]
        public void TearDown() => Delete();

        private void Delete()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        private static LevelDefinition Level(string name = "the-long-way-round")
        {
            LevelLoadResult result = LevelLoader.Load(name, Board);
            Assert.IsTrue(result.IsValid, result.Error);
            return result.Level;
        }

        /// <summary>NOT A OR NOT B, one of the-long-way-round's two answers.</summary>
        private static CircuitBlueprint Solved()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Middle, GateKind.Not);

            LevelTestFixtures.Wire(blueprint, SourceA, Middle);
            LevelTestFixtures.Wire(blueprint, Middle, OutCell);

            return blueprint;
        }

        // -----------------------------------------------------------------
        // The round trip
        // -----------------------------------------------------------------

        [Test]
        public void ABoardSurvivesARestart()
        {
            LevelDefinition level = Level();

            var first = new ProgressStore(_path);
            first.Load();
            first.SaveBoard("the-long-way-round", BoardSerializer.ToSaved("the-long-way-round", Solved()));

            var second = new ProgressStore(_path);
            second.Load();

            var restored = new CircuitBlueprint();
            int dropped = BoardSerializer.Restore(
                second.BoardFor("the-long-way-round"), level, restored, Board);

            Assert.AreEqual(0, dropped, "nothing should have been rejected");
            Assert.AreEqual(1, restored.Placements.Count);
            Assert.AreEqual(2, restored.Wires.Count);
            Assert.IsTrue(restored.HasPlacementAt(Middle));
        }

        [Test]
        public void WireDelaysSurviveToo()
        {
            LevelDefinition level = Level("balance-the-paths");

            var blueprint = new CircuitBlueprint();
            blueprint.Place(new Vector2Int(0, 1), GateKind.Xor);
            LevelTestFixtures.Wire(blueprint, SourceA, new Vector2Int(0, 1), delay: 2);

            SavedBoard saved = BoardSerializer.ToSaved("balance-the-paths", blueprint);

            var restored = new CircuitBlueprint();
            BoardSerializer.Restore(saved, level, restored, Board);

            Assert.AreEqual(1, restored.Wires.Count);
            Assert.AreEqual(2, restored.Wires[0].Delay, "a re-timed wire must come back re-timed");
        }

        [Test]
        public void RestoringReplacesWhateverWasThere()
        {
            LevelDefinition level = Level();

            var blueprint = new CircuitBlueprint();
            blueprint.Place(new Vector2Int(1, 1), GateKind.Not);

            BoardSerializer.Restore(
                BoardSerializer.ToSaved("the-long-way-round", Solved()), level, blueprint, Board);

            Assert.IsFalse(blueprint.HasPlacementAt(new Vector2Int(1, 1)),
                "the old board should be gone, not merged with");
        }

        // -----------------------------------------------------------------
        // Refusing what no longer fits
        // -----------------------------------------------------------------

        [Test]
        public void AGateOnACellTheLevelNowUsesIsDropped()
        {
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new[]
                {
                    new SavedPlacement { x = SourceA.x, y = SourceA.y, kind = "Not" },
                },
                wires = new SavedWire[0],
            };

            var blueprint = new CircuitBlueprint();
            int dropped = BoardSerializer.Restore(saved, level, blueprint, Board);

            Assert.AreEqual(1, dropped);
            Assert.AreEqual(0, blueprint.Placements.Count, "a gate cannot sit on a fixture");
        }

        [Test]
        public void AGateTheBudgetNoLongerStocksIsDropped()
        {
            // the-long-way-round stocks no NAND -- that is the whole level.
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new[] { new SavedPlacement { x = 0, y = 0, kind = "Nand" } },
                wires = new SavedWire[0],
            };

            var blueprint = new CircuitBlueprint();
            int dropped = BoardSerializer.Restore(saved, level, blueprint, Board);

            Assert.AreEqual(1, dropped);
            Assert.AreEqual(0, blueprint.Placements.Count,
                "restoring must not hand the player a gate the level forbids");
        }

        [Test]
        public void MoreGatesThanTheBudgetAllowsAreDropped()
        {
            LevelDefinition level = Level();   // stocks two NOTs

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new[]
                {
                    new SavedPlacement { x = 0, y = 0, kind = "Not" },
                    new SavedPlacement { x = 0, y = 1, kind = "Not" },
                    new SavedPlacement { x = 0, y = 2, kind = "Not" },
                },
                wires = new SavedWire[0],
            };

            var blueprint = new CircuitBlueprint();
            int dropped = BoardSerializer.Restore(saved, level, blueprint, Board);

            Assert.AreEqual(1, dropped);
            Assert.AreEqual(2, blueprint.CountOf(GateKind.Not), "the budget still caps it");
        }

        [Test]
        public void AGateOffTheBoardIsDropped()
        {
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new[] { new SavedPlacement { x = 99, y = 99, kind = "Not" } },
                wires = new SavedWire[0],
            };

            var blueprint = new CircuitBlueprint();

            Assert.AreEqual(1, BoardSerializer.Restore(saved, level, blueprint, Board));
            Assert.AreEqual(0, blueprint.Placements.Count);
        }

        [Test]
        public void AWireToNowhereIsDropped()
        {
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new SavedPlacement[0],
                wires = new[]
                {
                    new SavedWire
                    {
                        fromX = SourceA.x, fromY = SourceA.y, fromPort = 0,
                        toX = 2, toY = 2, toPort = 0,   // empty cell
                        delay = 1,
                    },
                },
            };

            var blueprint = new CircuitBlueprint();

            Assert.AreEqual(1, BoardSerializer.Restore(saved, level, blueprint, Board));
            Assert.AreEqual(0, blueprint.Wires.Count);
        }

        [Test]
        public void AWireLongerThanTheLevelAllowsIsDropped()
        {
            // the-long-way-round fixes its wiring at delay 1 on purpose. A board saved when the cap
            // was looser must not smuggle a re-timed wire back in.
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new SavedPlacement[0],
                wires = new[]
                {
                    new SavedWire
                    {
                        fromX = SourceA.x, fromY = SourceA.y, fromPort = 0,
                        toX = OutCell.x, toY = OutCell.y, toPort = 0,
                        delay = 3,
                    },
                },
            };

            var blueprint = new CircuitBlueprint();

            Assert.AreEqual(1, BoardSerializer.Restore(saved, level, blueprint, Board));
            Assert.AreEqual(0, blueprint.Wires.Count);
        }

        [Test]
        public void APortThatDoesNotExistIsDropped()
        {
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new[] { new SavedPlacement { x = 0, y = 0, kind = "Not" } },
                wires = new[]
                {
                    new SavedWire
                    {
                        fromX = SourceA.x, fromY = SourceA.y, fromPort = 0,
                        toX = 0, toY = 0, toPort = 1,   // a NOT has one input
                        delay = 1,
                    },
                },
            };

            var blueprint = new CircuitBlueprint();

            Assert.AreEqual(1, BoardSerializer.Restore(saved, level, blueprint, Board));
            Assert.AreEqual(0, blueprint.Wires.Count);
        }

        [Test]
        public void GarbageInTheFileIsDroppedRatherThanCrashing()
        {
            LevelDefinition level = Level();

            var saved = new SavedBoard
            {
                level = "the-long-way-round",
                placements = new[] { null, new SavedPlacement { x = 0, y = 0, kind = "Wombat" } },
                wires = new SavedWire[] { null },
            };

            var blueprint = new CircuitBlueprint();

            Assert.DoesNotThrow(() => BoardSerializer.Restore(saved, level, blueprint, Board));
            Assert.AreEqual(0, blueprint.Placements.Count);
            Assert.AreEqual(0, blueprint.Wires.Count);
        }

        [Test]
        public void ANullBoardIsAnEmptyBoard()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Middle, GateKind.Not);

            Assert.AreEqual(0, BoardSerializer.Restore(null, Level(), blueprint, Board));
            Assert.IsTrue(blueprint.IsEmpty, "restoring nothing should leave nothing");
        }

        // -----------------------------------------------------------------
        // Personal bests
        // -----------------------------------------------------------------

        [Test]
        public void AFirstSolveSetsARecordWithoutBeatingOne()
        {
            var store = new ProgressStore(_path);
            store.Load();

            store.RecordBest("half-adder", 2, 3, out bool gates, out bool latency);

            Assert.IsFalse(gates, "there was nothing to beat");
            Assert.IsFalse(latency);
            Assert.AreEqual(2, store.BestGates("half-adder"));
            Assert.AreEqual(3, store.BestLatency("half-adder"));
        }

        [Test]
        public void ASmallerCircuitBeatsTheGateRecordOnly()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.RecordBest("half-adder", 4, 3, out _, out _);

            store.RecordBest("half-adder", 3, 3, out bool gates, out bool latency);

            Assert.IsTrue(gates, "three gates beats four");
            Assert.IsFalse(latency, "the same latency is not faster");
            Assert.AreEqual(3, store.BestGates("half-adder"));
        }

        [Test]
        public void TheTwoRecordsImproveIndependently()
        {
            // They trade against each other -- the XOR-trick mux is a gate smaller and a tick
            // dearer -- so a run that is worse on one axis must not undo the other.
            var store = new ProgressStore(_path);
            store.Load();
            store.RecordBest("pick-a-lane", 3, 5, out _, out _);

            store.RecordBest("pick-a-lane", 4, 4, out bool gates, out bool latency);

            Assert.IsFalse(gates, "four gates does not beat three");
            Assert.IsTrue(latency, "but four ticks beats five");

            Assert.AreEqual(3, store.BestGates("pick-a-lane"), "the gate record stands");
            Assert.AreEqual(4, store.BestLatency("pick-a-lane"));
        }

        [Test]
        public void RecordsSurviveARestart()
        {
            var first = new ProgressStore(_path);
            first.Load();
            first.RecordBest("four-corners", 8, 5, out _, out _);

            var second = new ProgressStore(_path);
            second.Load();

            Assert.AreEqual(8, second.BestGates("four-corners"));
            Assert.AreEqual(5, second.BestLatency("four-corners"));
        }

        [Test]
        public void ClearingTheBoardDoesNotClearTheRecord()
        {
            // Wiping a board is an editing decision. It must not cost the player the fact that they
            // once solved the level in four gates.
            var store = new ProgressStore(_path);
            store.Load();
            store.RecordBest("half-adder", 2, 3, out _, out _);

            store.SaveBoard("half-adder", BoardSerializer.ToSaved("half-adder", new CircuitBlueprint()));

            Assert.AreEqual(2, store.BestGates("half-adder"));
            Assert.AreEqual(3, store.BestLatency("half-adder"));
        }

        [Test]
        public void AFileWrittenBeforeBoardsExisted_StillRestoresItsCompletions()
        {
            // Forward compatibility with the save format that shipped first. Each array is guarded
            // on its own, so an older file loses nothing it had.
            File.WriteAllText(_path, "{\"completed\":[\"route-the-bit\"]}");

            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsTrue(store.IsComplete("route-the-bit"));
            Assert.IsNull(store.BoardFor("route-the-bit"));
            Assert.AreEqual(0, store.BestGates("route-the-bit"));
        }
    }
}
