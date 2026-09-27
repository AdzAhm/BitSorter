using System.IO;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Free play's named boards: what the store keeps, what it reads from an older file, and the
    /// rules for what a board may be called.
    /// </summary>
    /// <remarks>
    /// Every test here uses a scratch file of its own. The store never opens the player's save
    /// unless it is built from <see cref="ProgressStore.DefaultPath"/>, and nothing here does.
    /// </remarks>
    public class FreePlaySavesTests
    {
        private string _path;

        [SetUp]
        public void SetUp() =>
            _path = Path.Combine(Path.GetTempPath(), $"bitsorter-freeplay-{System.Guid.NewGuid():N}.json");

        [TearDown]
        public void TearDown()
        {
            foreach (string file in new[] { _path, _path + ".tmp", _path + ".unreadable" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }

        private static SavedBoard Board(int sources, params SavedPlacement[] placements)
        {
            SandboxConfig config = SandboxLevel.Default(SandboxLevel.Board);
            config.sources = new string[sources];

            for (int i = 0; i < sources; i++)
                config.sources[i] = "0101";

            return new SavedBoard { sandbox = config, placements = placements };
        }

        private static SavedPlacement Gate(int x, int y, string kind) =>
            new SavedPlacement { x = x, y = y, kind = kind };

        private ProgressStore Reloaded()
        {
            var store = new ProgressStore(_path);
            store.Load();
            return store;
        }

        // -----------------------------------------------------------------
        // Several boards
        // -----------------------------------------------------------------

        [Test]
        public void SeveralBoards_RoundTripWithTheirNamesAndWhichIsOpen()
        {
            var store = new ProgressStore(_path);

            Assert.AreEqual(0, store.AddFreePlay("Adder", Board(2, Gate(0, 0, "Xor"))));
            Assert.AreEqual(1, store.AddFreePlay("Counter", Board(1, Gate(1, 1, "Register"))));
            Assert.AreEqual(2, store.AddFreePlay("Scratch", Board(3)));
            Assert.IsTrue(store.SelectFreePlay(1));

            ProgressStore loaded = Reloaded();

            CollectionAssert.AreEqual(new[] { "Adder", "Counter", "Scratch" }, loaded.FreePlayNames());
            Assert.AreEqual(1, loaded.ActiveFreePlay);
            Assert.AreEqual("Register", loaded.BoardFor(SandboxLevel.Key).placements[0].kind);
            Assert.AreEqual(3, loaded.FreePlayBoard(2).sandbox.sources.Length);
        }

        [Test]
        public void SwitchingTheOpenBoard_ChangesWhatFreePlaysKeyReturns()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("One", Board(1));
            store.AddFreePlay("Two", Board(2));

            store.SelectFreePlay(0);
            Assert.AreEqual(1, store.BoardFor(SandboxLevel.Key).sandbox.sources.Length);

            store.SelectFreePlay(1);
            Assert.AreEqual(2, store.BoardFor(SandboxLevel.Key).sandbox.sources.Length);

            Assert.IsFalse(store.SelectFreePlay(2), "an index with no board was opened");
            Assert.AreEqual(1, store.ActiveFreePlay);
        }

        /// <summary>
        /// A save under free play's key goes to the open board, and carries its setup forward as it
        /// always did -- the tracker knows nothing about there being several.
        /// </summary>
        [Test]
        public void ASaveUnderFreePlaysKey_GoesToTheOpenBoard_AndKeepsItsSetup()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("One", Board(1));
            store.AddFreePlay("Two", Board(2));
            store.SelectFreePlay(1);

            store.SaveBoard(SandboxLevel.Key, new SavedBoard { placements = new[] { Gate(0, 0, "And") } });

            Assert.AreEqual("And", store.FreePlayBoard(1).placements[0].kind, "the open board was not saved");
            Assert.AreEqual(2, store.FreePlayBoard(1).sandbox.sources.Length, "the open board lost its setup");
            Assert.IsEmpty(store.FreePlayBoard(0).placements, "the other board was written over");
        }

        [Test]
        public void TheFirstSaveOfFreePlay_MakesTheFirstBoard()
        {
            var store = new ProgressStore(_path);
            Assert.IsNull(store.BoardFor(SandboxLevel.Key), "sanity: a new store has no free-play board");

            store.StageBoard(SandboxLevel.Key, Board(2));

            Assert.AreEqual(1, store.FreePlayCount);
            Assert.AreEqual(BoardNames.First, store.FreePlayName(0));
            Assert.AreEqual(2, store.BoardFor(SandboxLevel.Key).sandbox.sources.Length);
        }

        [Test]
        public void ALevelsBoard_IsNotAFreePlayBoard()
        {
            var store = new ProgressStore(_path);
            store.SaveBoard("half-adder", new SavedBoard { placements = new[] { Gate(0, 1, "Xor") } });

            Assert.AreEqual(0, store.FreePlayCount);
            Assert.AreEqual("Xor", Reloaded().BoardFor("half-adder").placements[0].kind);
        }

        // -----------------------------------------------------------------
        // A file from before boards had names
        // -----------------------------------------------------------------

        /// <summary>
        /// 3.0.2 kept free play's one board among the levels' boards. It becomes the first named
        /// board, on the wide board's slots, and the levels' boards are untouched.
        /// </summary>
        [Test]
        public void AFileWithOneFreePlayBoard_BecomesTheFirstNamedBoard()
        {
            File.WriteAllText(_path, @"{
                ""completed"": [ ""route-the-bit"" ],
                ""boards"": [
                    { ""level"": ""half-adder"", ""placements"": [ { ""x"": 0, ""y"": 1, ""kind"": ""Xor"" } ],
                      ""wires"": [], ""bestGates"": 2, ""bestLatency"": 2 },
                    { ""level"": ""sandbox"", ""placements"": [ { ""x"": 0, ""y"": 0, ""kind"": ""Not"" } ],
                      ""wires"": [ { ""fromX"": -4, ""fromY"": 2, ""fromPort"": 0, ""toX"": 0, ""toY"": 0, ""toPort"": 0, ""delay"": 1 } ],
                      ""sandbox"": { ""sources"": [ ""0101"", ""0011"" ], ""sinks"": 2, ""vectors"": 4, ""clock"": 1, ""layout"": 1 } }
                ]
            }");

            ProgressStore store = Reloaded();

            Assert.AreEqual(1, store.FreePlayCount);
            Assert.AreEqual(BoardNames.First, store.FreePlayName(0));

            SavedBoard board = store.BoardFor(SandboxLevel.Key);
            Assert.AreEqual("Not", board.placements[0].kind);
            Assert.AreEqual(SandboxConfig.CurrentLayout, board.sandbox.layout, "not moved to the wide board");
            Assert.AreEqual(new Vector2Int(-6, 3), new Vector2Int(board.wires[0].fromX, board.wires[0].fromY),
                "A's wire did not follow A to the wide board");

            Assert.AreEqual(2, store.BestGates("half-adder"), "a level's board was disturbed");
            Assert.IsTrue(store.IsComplete("route-the-bit"));
        }

        [Test]
        public void TheOldBoard_IsWrittenBackInOnePlaceOnly_AndReadingAgainChangesNothing()
        {
            File.WriteAllText(_path, @"{ ""boards"": [
                { ""level"": ""sandbox"", ""placements"": [ { ""x"": 0, ""y"": 0, ""kind"": ""Not"" } ], ""wires"": [],
                  ""sandbox"": { ""sources"": [ ""01"" ], ""sinks"": 1, ""vectors"": 2, ""clock"": 1, ""layout"": 1 } } ] }");

            Reloaded().Save();

            ProgressFile written = JsonUtility.FromJson<ProgressFile>(File.ReadAllText(_path));
            foreach (SavedBoard board in written.boards)
                Assert.AreNotEqual(SandboxLevel.Key, board.level, "free play's board is still among the levels'");

            ProgressStore again = Reloaded();
            Assert.AreEqual(1, again.FreePlayCount, "reading a second time made a second board");
            Assert.AreEqual("Not", again.BoardFor(SandboxLevel.Key).placements[0].kind);
        }

        [Test]
        public void AFileFromBeforeFreePlay_HasNoBoards()
        {
            File.WriteAllText(_path, @"{ ""completed"": [ ""route-the-bit"" ] }");

            ProgressStore store = Reloaded();

            Assert.AreEqual(0, store.FreePlayCount);
            Assert.IsNull(store.BoardFor(SandboxLevel.Key));
        }

        // -----------------------------------------------------------------
        // Limits
        // -----------------------------------------------------------------

        [Test]
        public void FreePlay_KeepsAtMostEightBoards()
        {
            var store = new ProgressStore(_path);

            for (int i = 0; i < BoardNames.MaxBoards; i++)
                Assert.AreEqual(i, store.AddFreePlay(BoardNames.NextDefault(store.FreePlayNames()), Board(1)));

            Assert.AreEqual(-1, store.AddFreePlay("One too many", Board(1)));
            Assert.AreEqual(BoardNames.MaxBoards, store.FreePlayCount);
        }

        [Test]
        public void TwoBoards_CannotShareAName_IgnoringCase()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("Adder", Board(1));

            Assert.AreEqual(-1, store.AddFreePlay("  adder ", Board(1)), "a second 'Adder' was added");
            Assert.AreEqual(1, store.AddFreePlay("Counter", Board(1)));

            Assert.IsFalse(store.RenameFreePlay(1, "ADDER", out string refusal));
            Assert.IsNotNull(refusal);
            Assert.AreEqual("Counter", store.FreePlayName(1));

            Assert.IsTrue(store.RenameFreePlay(0, "  adder  ", out _), "a board could not keep its own name");
            Assert.AreEqual("adder", store.FreePlayName(0), "the name was not trimmed");
        }

        // -----------------------------------------------------------------
        // Deleting
        // -----------------------------------------------------------------

        [Test]
        public void DeletingTheOpenBoard_OpensTheOneThatTookItsPlace()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("One", Board(1));
            store.AddFreePlay("Two", Board(2));
            store.AddFreePlay("Three", Board(3));
            store.SelectFreePlay(1);

            Assert.IsTrue(store.DeleteFreePlay(1));
            CollectionAssert.AreEqual(new[] { "One", "Three" }, store.FreePlayNames());
            Assert.AreEqual("Three", store.FreePlayName(store.ActiveFreePlay));

            Assert.IsTrue(store.DeleteFreePlay(1));
            Assert.AreEqual("One", store.FreePlayName(store.ActiveFreePlay), "deleting the last in the list");
        }

        [Test]
        public void DeletingABoardBeforeTheOpenOne_KeepsTheSameBoardOpen()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("One", Board(1));
            store.AddFreePlay("Two", Board(2));
            store.SelectFreePlay(1);

            store.DeleteFreePlay(0);

            Assert.AreEqual("Two", store.FreePlayName(store.ActiveFreePlay));
        }

        /// <summary>Free play always has a board to open, so the last one is emptied, keeping its name.</summary>
        [Test]
        public void DeletingTheLastBoard_EmptiesIt()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("Adder", Board(3, Gate(0, 0, "Xor")));

            Assert.IsTrue(store.DeleteFreePlay(0));

            Assert.AreEqual(1, store.FreePlayCount);
            Assert.AreEqual("Adder", store.FreePlayName(0));

            SavedBoard board = store.BoardFor(SandboxLevel.Key);
            Assert.IsTrue(board.placements == null || board.placements.Length == 0, "the gates are still there");
            Assert.AreEqual(SandboxLevel.Default(SandboxLevel.Board).sources.Length, board.sandbox.sources.Length,
                "the setup is not the default one");
        }

        [Test]
        public void ResettingProgress_ForgetsEveryBoard()
        {
            var store = new ProgressStore(_path);
            store.AddFreePlay("One", Board(1));
            store.AddFreePlay("Two", Board(2));

            store.Clear();

            Assert.AreEqual(0, store.FreePlayCount);
            Assert.AreEqual(0, Reloaded().FreePlayCount);
        }

        // -----------------------------------------------------------------
        // Names
        // -----------------------------------------------------------------

        [Test]
        public void AName_IsOneToTwentyFourCharacters_AfterTrimming()
        {
            Assert.IsNotNull(BoardNames.Refusal("", new string[0]));
            Assert.IsNotNull(BoardNames.Refusal("    ", new string[0]));
            Assert.IsNull(BoardNames.Refusal("  x  ", new string[0]));
            Assert.IsNull(BoardNames.Refusal(new string('a', BoardNames.MaxLength), new string[0]));
            Assert.IsNotNull(BoardNames.Refusal(new string('a', BoardNames.MaxLength + 1), new string[0]));
        }

        [Test]
        public void ANewBoard_IsCalledBoardTwoThreeAndSoOn_FillingGaps()
        {
            Assert.AreEqual("Board 2", BoardNames.NextDefault(new[] { BoardNames.First }));
            Assert.AreEqual("Board 3", BoardNames.NextDefault(new[] { BoardNames.First, "Board 2" }));
            Assert.AreEqual("Board 2", BoardNames.NextDefault(new[] { BoardNames.First, "Board 3" }));
            Assert.AreEqual("Board 3", BoardNames.NextDefault(new[] { "board 2" }), "compared ignoring case");
        }

        [Test]
        public void ACopy_SaysSo_AndFitsTheLengthLimit()
        {
            Assert.AreEqual("Adder copy", BoardNames.CopyOf("Adder", new[] { "Adder" }));
            Assert.AreEqual("Adder copy 2", BoardNames.CopyOf("Adder", new[] { "Adder", "Adder copy" }));

            string longName = new string('a', BoardNames.MaxLength);
            string copy = BoardNames.CopyOf(longName, new[] { longName });

            Assert.LessOrEqual(copy.Length, BoardNames.MaxLength);
            StringAssert.EndsWith(" copy", copy, "the suffix gave way instead of the name");
        }
    }
}
