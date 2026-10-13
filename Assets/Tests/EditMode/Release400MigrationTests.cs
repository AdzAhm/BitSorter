using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using NUnit.Framework;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A save written by 4.0.0 -- the last 4.x release -- made into 5.0's documents: named free-play
    /// boards, a block from the library on one of them, the library itself, and a level board of
    /// blocks, each a document that runs as its board did.
    /// </summary>
    /// <remarks>
    /// The file is made up here, in 4.0.0's shape, and written to a scratch path; the player's own is
    /// never opened. Four at once's board, which is four blocks and their wiring, is put in through
    /// the store from the level's own solution rather than written out by hand.
    /// </remarks>
    public class Release400MigrationTests
    {
        /// <summary>A little block of the player's: a NOT in a box.</summary>
        private const string NotBlock = @"{ ""name"": ""NOTB"",
            ""inputs"": [ { ""id"": ""a"", ""x"": -1, ""y"": 0 } ],
            ""outputs"": [ { ""id"": ""y"", ""x"": 1, ""y"": 0 } ],
            ""gates"": [ { ""x"": 0, ""y"": 0, ""kind"": ""NOT"" } ],
            ""wires"": [
                { ""fromX"": -1, ""fromY"": 0, ""fromPort"": 0, ""toX"": 0, ""toY"": 0, ""toPort"": 0, ""delay"": 1 },
                { ""fromX"": 0, ""fromY"": 0, ""fromPort"": 0, ""toX"": 1, ""toY"": 0, ""toPort"": 0, ""delay"": 1 } ] }";

        private const string FreePlaySetup = @"""sandbox"": { ""sources"": [ ""0101"", ""0011"" ], ""sinks"": 2, ""vectors"": 4, ""clock"": 1, ""layout"": 2 }";

        /// <summary>The shape 4.0.0 wrote: no format, and free play's named boards and library.</summary>
        private static readonly string Release400 = @"{
            ""completed"": [ ""half-adder"", ""four-at-once"" ],
            ""boards"": [
                { ""level"": ""half-adder"",
                  ""placements"": [ { ""x"": 0, ""y"": 1, ""kind"": ""XOR"" }, { ""x"": 0, ""y"": -1, ""kind"": ""AND"" } ],
                  ""wires"": [
                    { ""fromX"": -3, ""fromY"":  1, ""fromPort"": 0, ""toX"": 0, ""toY"":  1, ""toPort"": 0, ""delay"": 1 },
                    { ""fromX"": -3, ""fromY"": -1, ""fromPort"": 0, ""toX"": 0, ""toY"":  1, ""toPort"": 1, ""delay"": 1 },
                    { ""fromX"": -3, ""fromY"":  1, ""fromPort"": 0, ""toX"": 0, ""toY"": -1, ""toPort"": 0, ""delay"": 1 },
                    { ""fromX"": -3, ""fromY"": -1, ""fromPort"": 0, ""toX"": 0, ""toY"": -1, ""toPort"": 1, ""delay"": 1 },
                    { ""fromX"":  0, ""fromY"":  1, ""fromPort"": 0, ""toX"": 3, ""toY"":  1, ""toPort"": 0, ""delay"": 1 },
                    { ""fromX"":  0, ""fromY"": -1, ""fromPort"": 0, ""toX"": 3, ""toY"": -1, ""toPort"": 0, ""delay"": 1 } ],
                  ""bestGates"": 2, ""bestLatency"": 2 }
            ],
            ""hintsSeen"": [ ""stalled"" ],
            ""milestones"": [ ""tutorial"" ],
            ""freePlay"": { ""active"": 1, ""saves"": [
                { ""name"": ""Inverters"", ""board"": { ""level"": ""sandbox"",
                    ""placements"": [ { ""x"": 0, ""y"": 3, ""kind"": ""NOT"" } ],
                    ""wires"": [
                      { ""fromX"": -6, ""fromY"": 3, ""fromPort"": 0, ""toX"": 0, ""toY"": 3, ""toPort"": 0, ""delay"": 3 },
                      { ""fromX"": 0, ""fromY"": 3, ""fromPort"": 0, ""toX"": 6, ""toY"": 3, ""toPort"": 0, ""delay"": 1 } ],
                    " + FreePlaySetup + @" } },
                { ""name"": ""Boxed"", ""board"": { ""level"": ""sandbox"",
                    ""placements"": [],
                    ""blocks"": [ { ""x"": 0, ""y"": 2, ""block"": " + NotBlock + @" } ],
                    ""wires"": [
                      { ""fromX"": -6, ""fromY"": 2, ""fromPort"": 0, ""toX"": 0, ""toY"": 2, ""toPort"": 0, ""delay"": 1 },
                      { ""fromX"": 0, ""fromY"": 2, ""fromPort"": 0, ""toX"": 6, ""toY"": 2, ""toPort"": 0, ""delay"": 2 } ],
                    " + FreePlaySetup + @" } } ] },
            ""blocks"": [ " + NotBlock + @" ]
        }";

        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), $"bitsorter-400-{Guid.NewGuid():N}.json");
            File.WriteAllText(_path, Release400);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in new[] { _path, _path + ".tmp", _path + ".unreadable" })
                if (File.Exists(file))
                    File.Delete(file);
        }

        private ProgressStore Read()
        {
            var store = new ProgressStore(_path);
            store.Load();
            Assert.IsNull(store.LastError, "the 4.0.0 file did not read");
            return store;
        }

        /// <summary>A free-play board on its own setup, with what its bins should get, migrated.</summary>
        private static (MigrationResult, LegacyCircuit) FreePlay(ProgressStore store, int index, params string[] expected)
        {
            SavedBoard saved = store.FreePlayBoard(index);
            LevelDefinition level = SandboxLevel.Build(saved.sandbox, SandboxLevel.Board);
            var board = new CircuitBlueprint();
            Assert.AreEqual(0, BoardSerializer.Restore(saved, level, board, SandboxLevel.Board), "sanity: the board came back whole");

            LegacyCircuit circuit = LegacyCircuits.From(store.FreePlayName(index), level, board);
            circuit.expected = expected.Select((values, i) => new LegacyExpectation { sink = SandboxLevel.SinkId(i), values = values }).ToArray();
            return (Migrator.Migrate(circuit), circuit);
        }

        [Test]
        public void TheFile_ReadsAs400Wrote_It()
        {
            ProgressStore store = Read();

            Assert.AreEqual(0, store.LoadedFormat);
            Assert.AreEqual(2, store.FreePlayCount);
            CollectionAssert.AreEqual(new[] { "Inverters", "Boxed" }, store.FreePlayNames());
            Assert.AreEqual(1, store.Library.Count);
        }

        [Test]
        public void EveryBoard_BecomesADocument_ThatRunsAsTheBoardDid()
        {
            ProgressStore store = Read();
            LevelDefinition fourAtOnce = LevelLoader.Load("four-at-once", LevelTestFixtures.Board).Level;
            store.SaveBoard("four-at-once", BoardSerializer.ToSaved("four-at-once", FourAtOnceLevelTests.Ripple(fourAtOnce)));

            SaveMigration.MigrateAll(store);

            CollectionAssert.AreEquivalent(
                new[] { "half-adder", "four-at-once", SaveMigration.FreePlayKey + "Inverters", SaveMigration.FreePlayKey + "Boxed" },
                store.ProjectKeys());

            // Four at once: a module for the level's full adder, and the board a module of blocks of it.
            Assert.IsTrue(store.TryGetProject("four-at-once", out Project adder, new List<string>()));
            Assert.AreEqual(2, adder.Modules.Count, "the board, and one module for its block");
            Assert.AreEqual(4, adder.TopModule.Parts.OfType<BlockPart>().Count());
            Assert.IsTrue(BoardCompiler.Compile(adder).Succeeded);

            // Free play: A through a slow wire and a NOT into the first bin; B through the NOT in a box into the second.
            (MigrationResult inverters, LegacyCircuit invertersCircuit) = FreePlay(store, 0, "1010");
            Assert.IsEmpty(inverters.Mismatches(invertersCircuit.expected));
            Assert.AreEqual("OUT_1", inverters.PortOf[SandboxLevel.SinkId(0)], "a bin's name made one SystemVerilog takes");

            (MigrationResult boxed, LegacyCircuit boxedCircuit) = FreePlay(store, 1, "", "1100");
            Assert.IsEmpty(boxed.Mismatches(boxedCircuit.expected));
            Assert.IsTrue(boxed.Project.Modules.Any(m => m.Name == "NOTB"), "the library's block, a module");

            // What the save keeps is what migrating the board gives.
            Assert.IsTrue(store.TryGetProject(SaveMigration.FreePlayKey + "Boxed", out Project kept, new List<string>()));
            CollectionAssert.AreEqual(
                BoardPrinter.Print(boxed.Project).Select(p => p.Text).ToArray(),
                BoardPrinter.Print(kept).Select(p => p.Text).ToArray());
        }

        [Test]
        public void Written_ItIsFormat5_AndEverything4xHadIsStillThere()
        {
            ProgressStore store = Read();
            SaveMigration.MigrateAll(store);
            store.Save();

            ProgressStore again = Read();

            Assert.AreEqual(ProjectRecord.Format, again.LoadedFormat);
            Assert.AreEqual(3, again.ProjectKeys().Length);
            Assert.AreEqual(2, again.FreePlayCount);
            Assert.AreEqual(1, again.Library.Count);
            Assert.AreEqual(2, again.BestGates("half-adder"));
            Assert.IsTrue(again.IsComplete("four-at-once"));
            Assert.IsTrue(again.HasSeenHint(HintRules.Stalled));
        }
    }
}
