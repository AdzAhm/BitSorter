using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A save written by 3.0.2, read by this version with everything in it intact: the whole chain of
    /// changes since, at once.
    /// </summary>
    /// <remarks>
    /// Each change to the save has its own test, written against the file as it stood just before
    /// it. This one starts from the last release's file and goes the whole way, which is the trip a
    /// returning player actually makes: completions, bests, level boards, hints seen and milestones as
    /// they were; free play's one board as the first named board, moved to the wide board; and no
    /// blocks or library where none were.
    ///
    /// The file is made up here and written to a scratch path. The player's own is never opened.
    /// </remarks>
    public class WholeSaveMigrationTests
    {
        /// <summary>The shape 3.0.2 wrote, with a little of everything in it.</summary>
        private const string Release302 = @"{
            ""completed"": [ ""route-the-bit"", ""half-adder"", ""flip-on-one"" ],
            ""boards"": [
                { ""level"": ""half-adder"",
                  ""placements"": [ { ""x"": 0, ""y"": 1, ""kind"": ""XOR"" }, { ""x"": 0, ""y"": -1, ""kind"": ""AND"" } ],
                  ""wires"": [
                    { ""fromX"": -3, ""fromY"":  1, ""fromPort"": 0, ""toX"": 0, ""toY"":  1, ""toPort"": 0, ""delay"": 1 },
                    { ""fromX"": -3, ""fromY"": -1, ""fromPort"": 0, ""toX"": 0, ""toY"":  1, ""toPort"": 1, ""delay"": 1 },
                    { ""fromX"": -3, ""fromY"":  1, ""fromPort"": 0, ""toX"": 0, ""toY"": -1, ""toPort"": 0, ""delay"": 1 },
                    { ""fromX"": -3, ""fromY"": -1, ""fromPort"": 0, ""toX"": 0, ""toY"": -1, ""toPort"": 1, ""delay"": 1 },
                    { ""fromX"":  0, ""fromY"":  1, ""fromPort"": 0, ""toX"": 3, ""toY"":  1, ""toPort"": 0, ""delay"": 1 },
                    { ""fromX"":  0, ""fromY"": -1, ""fromPort"": 0, ""toX"": 3, ""toY"": -1, ""toPort"": 0, ""delay"": 1 }
                  ],
                  ""bestGates"": 2, ""bestLatency"": 2 },
                { ""level"": ""flip-on-one"",
                  ""placements"": [ { ""x"": 0, ""y"": 0, ""kind"": ""REG"" } ],
                  ""wires"": [ { ""fromX"": -3, ""fromY"": 0, ""fromPort"": 0, ""toX"": 0, ""toY"": 0, ""toPort"": 0, ""delay"": 1 } ],
                  ""bestGates"": 2, ""bestLatency"": 3 },
                { ""level"": ""sandbox"",
                  ""placements"": [ { ""x"": 0, ""y"": 0, ""kind"": ""NOT"" } ],
                  ""wires"": [
                    { ""fromX"": -4, ""fromY"": 2, ""fromPort"": 0, ""toX"": 0, ""toY"": 0, ""toPort"": 0, ""delay"": 2 },
                    { ""fromX"":  0, ""fromY"": 0, ""fromPort"": 0, ""toX"": 4, ""toY"": 2, ""toPort"": 0, ""delay"": 1 }
                  ],
                  ""bestGates"": 0, ""bestLatency"": 0,
                  ""sandbox"": { ""sources"": [ ""0101"", ""0011"" ], ""sinks"": 2, ""vectors"": 4, ""clock"": 1, ""layout"": 1 } }
            ],
            ""hintsSeen"": [ ""stalled"", ""collision"" ],
            ""milestones"": [ ""tutorial"", ""chapter.sequential"" ]
        }";

        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), $"bitsorter-302-{Guid.NewGuid():N}.json");
            File.WriteAllText(_path, Release302);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in new[] { _path, _path + ".tmp", _path + ".unreadable" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }

        private ProgressStore Read()
        {
            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsNull(store.LastError, "the old file did not read");
            return store;
        }

        private static CircuitBlueprint Restored(ProgressStore store, string levelName, out int dropped)
        {
            LevelLoadResult loaded = LevelLoader.Load(levelName, LevelTestFixtures.Board);
            Assert.IsTrue(loaded.IsValid, loaded.Error);

            var board = new CircuitBlueprint();
            dropped = BoardSerializer.Restore(store.BoardFor(levelName), loaded.Level, board, LevelTestFixtures.Board);
            return board;
        }

        [Test]
        public void TheRecords_AreAllThere()
        {
            ProgressStore store = Read();

            Assert.IsTrue(store.IsComplete("route-the-bit") && store.IsComplete("half-adder") && store.IsComplete("flip-on-one"));
            Assert.AreEqual(3, store.CompletedCount);
            Assert.AreEqual(2, store.BestGates("half-adder"));
            Assert.AreEqual(3, store.BestLatency("flip-on-one"));

            Assert.IsTrue(store.HasSeenHint(HintRules.Stalled));
            Assert.IsTrue(store.HasSeenHint(HintRules.Collision));
            Assert.IsFalse(store.HasSeenHint(HintRules.WireDelay), "a hint never shown reads as seen");

            Assert.IsTrue(store.HasMilestone(TutorialLevel.Key));
            Assert.IsTrue(store.HasMilestone(ChapterCard.Milestone));
        }

        [Test]
        public void TheLevelsBoards_ComeBackWhole()
        {
            ProgressStore store = Read();

            CircuitBlueprint halfAdder = Restored(store, "half-adder", out int dropped);
            Assert.AreEqual(0, dropped);
            Assert.AreEqual(2, halfAdder.Placements.Count);
            Assert.AreEqual(6, halfAdder.Wires.Count);

            CircuitBlueprint flip = Restored(store, "flip-on-one", out dropped);
            Assert.AreEqual(0, dropped, "the register, saved by its label, did not come back");
            Assert.IsTrue(flip.TryGetPlacement(Vector2Int.zero, out GateKind kind) && kind == GateKind.Register);
        }

        [Test]
        public void FreePlaysOneBoard_IsTheFirstNamedBoard_OnTheWideBoard()
        {
            ProgressStore store = Read();

            Assert.AreEqual(1, store.FreePlayCount);
            Assert.AreEqual(BoardNames.First, store.FreePlayName(0));

            SavedBoard saved = store.BoardFor(SandboxLevel.Key);
            Assert.AreEqual(SandboxConfig.CurrentLayout, saved.sandbox.layout);

            LevelDefinition freePlay = SandboxLevel.Build(saved.sandbox, SandboxLevel.Board);
            var board = new CircuitBlueprint();

            Assert.AreEqual(0, BoardSerializer.Restore(saved, freePlay, board, SandboxLevel.Board),
                "something on the old free-play board did not survive the move");
            Assert.AreEqual(new Vector2Int(-6, 3), board.Wires[0].From.Cell, "the wire did not follow source A");
            Assert.AreEqual(new Vector2Int(6, 3), board.Wires[1].To.Cell, "the wire did not follow the first sink");
            Assert.AreEqual(2, board.Wires[0].Delay);
        }

        [Test]
        public void NothingAboutBlocks_IsMadeUp_AndAWriteAndReadChangesNothing()
        {
            ProgressStore store = Read();

            Assert.AreEqual(0, store.Library.Count);
            Assert.IsEmpty(Restored(store, "half-adder", out int _).Blocks);

            store.Save();
            ProgressStore again = Read();

            Assert.AreEqual(3, again.CompletedCount);
            Assert.AreEqual(1, again.FreePlayCount, "reading the written file made a second free-play board");
            Assert.AreEqual(0, again.Library.Count);
            Assert.AreEqual(2, again.BestGates("half-adder"));
            Assert.IsTrue(again.HasMilestone(ChapterCard.Milestone));
        }

        // -----------------------------------------------------------------
        // On to 5.0: format 5
        // -----------------------------------------------------------------

        [Test]
        public void EveryBoard_BecomesADocument_AndTheFileIsFormat5_With4xsKeysAsTheyWere()
        {
            ProgressStore store = Read();
            Assert.AreEqual(0, store.LoadedFormat, "a 4.x file has no format");

            SaveMigration.MigrateAll(store);

            string freePlay = SaveMigration.FreePlayKey + BoardNames.First;
            CollectionAssert.AreEquivalent(new[] { "flip-on-one", "half-adder", freePlay }, store.ProjectKeys());

            var printed = new Dictionary<string, string[]>();
            foreach (string key in store.ProjectKeys())
            {
                var problems = new List<string>();
                Assert.IsTrue(store.TryGetProject(key, out Project project, problems));
                Assert.IsEmpty(problems, key);
                Assert.IsTrue(BoardCompiler.Compile(project).Succeeded, $"{key} does not compile");
                printed[key] = BoardPrinter.Print(project).Select(p => p.Text).ToArray();
            }

            // The half adder on the save is the level's solution, and still solves it.
            LevelDefinition level = LevelLoader.Load("half-adder", LevelTestFixtures.Board).Level;
            LegacyCircuit circuit = LegacyCircuits.From("half-adder", level, Restored(store, "half-adder", out int _));
            Assert.IsEmpty(Migrator.Migrate(circuit).Mismatches(circuit.expected));

            store.Save();
            ProgressStore again = Read();

            Assert.AreEqual(ProjectRecord.Format, again.LoadedFormat);
            foreach (string key in printed.Keys)
            {
                Assert.IsTrue(again.TryGetProject(key, out Project read, new List<string>()), key);
                CollectionAssert.AreEqual(printed[key], BoardPrinter.Print(read).Select(p => p.Text).ToArray(), key);
            }

            // And 4.x's own keys are there for 4.x to read, as they were.
            Assert.AreEqual(3, again.CompletedCount);
            Assert.AreEqual(2, Restored(again, "half-adder", out int dropped).Placements.Count);
            Assert.AreEqual(0, dropped);
            Assert.AreEqual(1, again.FreePlayCount);
        }

        [Test]
        public void MigratingAgain_ChangesNothing_AndNeverReplacesADocument()
        {
            ProgressStore store = Read();
            SaveMigration.MigrateAll(store);

            Project edited = Project.New("mine");
            store.SetProject("half-adder", edited);
            SaveMigration.MigrateAll(store);

            Assert.IsTrue(store.TryGetProject("half-adder", out Project kept, new List<string>()));
            Assert.AreEqual("mine", kept.Top, "a document edited in 5.0 is the player's, not the old board's");
            Assert.AreEqual(3, store.ProjectKeys().Length);
        }
    }
}
