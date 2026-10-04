using System;
using System.IO;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Blocks in a level file, the rule for placing one, and how boards and free play's library keep
    /// them in the save.
    /// </summary>
    public class BlockLevelTests
    {
        /// <summary>A half adder in a box: A and B in, the sum and the carry out.</summary>
        private const string HalfAdderBlock = @"{
            ""name"": ""HA"",
            ""inputs"":  [ { ""id"": ""a"", ""cell"": { ""x"": -2, ""y"": 1 } }, { ""id"": ""b"", ""cell"": { ""x"": -2, ""y"": -1 } } ],
            ""outputs"": [ { ""id"": ""s"", ""cell"": { ""x"":  2, ""y"": 1 } }, { ""id"": ""c"", ""cell"": { ""x"":  2, ""y"": -1 } } ],
            ""gates"": [ { ""kind"": ""Xor"", ""cell"": { ""x"": 0, ""y"": 1 } }, { ""kind"": ""And"", ""cell"": { ""x"": 0, ""y"": -1 } } ],
            ""wires"": [
                { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"": 0, ""y"":  1 }, ""toPort"": 0 },
                { ""from"": { ""x"": -2, ""y"": -1 }, ""to"": { ""x"": 0, ""y"":  1 }, ""toPort"": 1 },
                { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"": 0, ""y"": -1 }, ""toPort"": 0 },
                { ""from"": { ""x"": -2, ""y"": -1 }, ""to"": { ""x"": 0, ""y"": -1 }, ""toPort"": 1 },
                { ""from"": { ""x"":  0, ""y"":  1 }, ""to"": { ""x"": 2, ""y"":  1 } },
                { ""from"": { ""x"":  0, ""y"": -1 }, ""to"": { ""x"": 2, ""y"": -1 } }
            ]
        }";

        /// <summary>Three inputs in, so two cells tall: the parity of A, B and C.</summary>
        private const string ParityBlock = @"{
            ""name"": ""PAR"",
            ""inputs"":  [ { ""id"": ""a"", ""cell"": { ""x"": -2, ""y"": 1 } }, { ""id"": ""b"", ""cell"": { ""x"": -2, ""y"": 0 } },
                           { ""id"": ""c"", ""cell"": { ""x"": -2, ""y"": -1 } } ],
            ""outputs"": [ { ""id"": ""y"", ""cell"": { ""x"": 2, ""y"": 0 } } ],
            ""gates"": [ { ""kind"": ""Xor"", ""cell"": { ""x"": 0, ""y"": 1 } }, { ""kind"": ""Xor"", ""cell"": { ""x"": 0, ""y"": -1 } } ],
            ""wires"": [
                { ""from"": { ""x"": -2, ""y"":  1 }, ""to"": { ""x"": 0, ""y"":  1 }, ""toPort"": 0 },
                { ""from"": { ""x"": -2, ""y"":  0 }, ""to"": { ""x"": 0, ""y"":  1 }, ""toPort"": 1 },
                { ""from"": { ""x"":  0, ""y"":  1 }, ""to"": { ""x"": 0, ""y"": -1 }, ""toPort"": 0 },
                { ""from"": { ""x"": -2, ""y"": -1 }, ""to"": { ""x"": 0, ""y"": -1 }, ""toPort"": 1, ""delay"": 2 },
                { ""from"": { ""x"":  0, ""y"": -1 }, ""to"": { ""x"": 2, ""y"":  0 } }
            ]
        }";

        private static string Level(string blocks, string budget) => $@"{{
            ""name"": ""Boxed"", ""tickLimit"": 60,
            ""fixtures"": [
                {{ ""id"": ""a"",     ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"":  1 }}, ""stream"": ""0011"" }},
                {{ ""id"": ""b"",     ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"": -1 }}, ""stream"": ""0101"" }},
                {{ ""id"": ""sum"",   ""kind"": ""Sink"",   ""cell"": {{ ""x"":  4, ""y"":  1 }} }},
                {{ ""id"": ""carry"", ""kind"": ""Sink"",   ""cell"": {{ ""x"":  4, ""y"": -1 }} }}
            ],
            ""blocks"": [ {blocks} ],
            ""budget"": [ {budget} ],
            ""expected"": [ {{ ""sink"": ""sum"", ""values"": ""0110"" }}, {{ ""sink"": ""carry"", ""values"": ""0001"" }} ]
        }}";

        private static LevelDefinition HalfAdderLevel() =>
            LevelTestFixtures.Parse(Level(HalfAdderBlock, @"{ ""block"": ""HA"", ""count"": 1 }"));

        private static LevelDefinition ParityLevel() => LevelTestFixtures.Parse(Level(
            ParityBlock, @"{ ""block"": ""PAR"", ""count"": 1 }, { ""kind"": ""Not"", ""count"": 1 }"));

        private static string Refused(string blocks, string budget)
        {
            LevelLoadResult result = LevelLoader.Parse(Level(blocks, budget), LevelTestFixtures.Board);

            Assert.IsFalse(result.IsValid, "a level breaking a block rule loaded");
            return result.Error;
        }

        private static readonly Vector2Int SourceA = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, -1);
        private static readonly Vector2Int SinkSum = new Vector2Int(4, 1);
        private static readonly Vector2Int SinkCarry = new Vector2Int(4, -1);

        /// <summary>The half adder block on the middle cell, wired from both sources to both bins.</summary>
        private static CircuitBlueprint Solved(LevelDefinition level)
        {
            var at = new Vector2Int(0, 0);
            var board = new CircuitBlueprint();

            board.PlaceBlock(at, level.BlockNamed("HA"));
            board.AddWire(BlockTests.Wire(SourceA, at, 0));
            board.AddWire(BlockTests.Wire(SourceB, at, 1));
            board.AddWire(BlockTests.Wire(at, SinkSum, fromPort: 0));
            board.AddWire(BlockTests.Wire(at, SinkCarry, fromPort: 1));
            return board;
        }

        // -----------------------------------------------------------------
        // In a level file
        // -----------------------------------------------------------------

        [Test]
        public void ALevelsOwnBlock_IsDefinedStockedAndSolvesIt()
        {
            LevelDefinition level = HalfAdderLevel();

            Assert.AreEqual(1, level.Blocks.Count);
            Assert.IsNotNull(level.BlockNamed("HA"));
            Assert.AreEqual(1, level.BlockBudgetFor("HA"));
            Assert.AreEqual(0, level.BlockBudgetFor("FA"));
            Assert.AreEqual(0, level.Budget.Count, "a block's row landed among the gates'");
            Assert.IsFalse(level.AnyBlock);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, Solved(level));
            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void ALevelFile_IsRefused_ForEveryMistakeInItsBlocks()
        {
            string ha = @"{ ""block"": ""HA"", ""count"": 1 }";

            StringAssert.Contains("does not define", Refused(HalfAdderBlock, ha + @", { ""block"": ""FA"", ""count"": 1 }"));
            StringAssert.Contains("never stocks", Refused(HalfAdderBlock, @"{ ""kind"": ""Not"", ""count"": 1 }"));
            StringAssert.Contains("two blocks are called", Refused(HalfAdderBlock + ", " + HalfAdderBlock, ha));
            StringAssert.Contains("twice", Refused(HalfAdderBlock, ha + ", " + ha));
            StringAssert.Contains("omit it", Refused(HalfAdderBlock, @"{ ""block"": ""HA"", ""count"": 0 }"));
            StringAssert.Contains("one or the other", Refused(HalfAdderBlock, @"{ ""block"": ""HA"", ""kind"": ""Xor"", ""count"": 1 }"));

            string unwired = HalfAdderBlock.Replace(
                @"{ ""from"": { ""x"":  0, ""y"": -1 }, ""to"": { ""x"": 2, ""y"": -1 } }",
                @"{ ""from"": { ""x"":  0, ""y"": -1 }, ""to"": { ""x"": 2, ""y"":  1 } }");
            StringAssert.Contains("block 'HA': Output s has 2 wires", Refused(unwired, ha));

            StringAssert.Contains("has kind", Refused(HalfAdderBlock.Replace(@"""Xor""", @"""Xnor"""), ha));
        }

        // -----------------------------------------------------------------
        // Placing one
        // -----------------------------------------------------------------

        private static LevelVerdict Place(LevelDefinition level, CircuitBlueprint board, string block, Vector2Int at,
            RunState state = RunState.Editing) =>
            LevelRules.CanPlaceBlock(level, board, state, level.BlockNamed(block) ?? BlockTests.Parity(block), at,
                LevelTestFixtures.Board);

        [Test]
        public void ABlock_GoesWhereEveryCellItCoversIsFree()
        {
            LevelDefinition level = ParityLevel();
            var board = new CircuitBlueprint();

            Assert.IsTrue(Place(level, board, "PAR", new Vector2Int(0, 1)).IsValid);

            LevelVerdict offTheBottom = Place(level, board, "PAR", new Vector2Int(0, -2));
            Assert.AreEqual(LevelOutcome.OffBoard, offTheBottom.Outcome);
            StringAssert.Contains("2 cells tall", offTheBottom.Reason);

            LevelVerdict onASource = Place(level, board, "PAR", new Vector2Int(-4, 2));
            Assert.AreEqual(LevelOutcome.CellTaken, onASource.Outcome);
            Assert.AreEqual("PAR is 2 cells tall, and A is on the cell below it.", onASource.Reason);

            board.Place(new Vector2Int(1, 0), GateKind.Not);
            LevelVerdict onAGate = Place(level, board, "PAR", new Vector2Int(1, 1));
            Assert.AreEqual("PAR is 2 cells tall, and the cell below it is taken.", onAGate.Reason);
            Assert.AreEqual("That cell is taken.", Place(level, board, "PAR", new Vector2Int(1, 0)).Reason);
        }

        [Test]
        public void ABlock_ComesOutOfItsOwnBudget()
        {
            LevelDefinition level = ParityLevel();
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(0, 1), level.BlockNamed("PAR"));

            LevelVerdict second = Place(level, board, "PAR", new Vector2Int(2, 1));
            Assert.AreEqual(LevelOutcome.BudgetSpent, second.Outcome);
            Assert.AreEqual("No PAR left. The only one is placed.", second.Reason);
            Assert.AreEqual(0, LevelRules.RemainingForBlock(level, board, "PAR"));

            LevelVerdict unstocked = Place(level, board, "XOR3", new Vector2Int(2, 1));
            Assert.AreEqual(LevelOutcome.NotInBudget, unstocked.Outcome);

            Assert.AreEqual(LevelOutcome.NotEditing,
                Place(level, new CircuitBlueprint(), "PAR", new Vector2Int(0, 1), RunState.Running).Outcome);
        }

        // -----------------------------------------------------------------
        // In the save
        // -----------------------------------------------------------------

        private static CircuitBlueprint ThroughTheFile(SavedBoard saved, LevelDefinition level, out int dropped)
        {
            SavedBoard read = JsonUtility.FromJson<SavedBoard>(JsonUtility.ToJson(saved));
            var board = new CircuitBlueprint();
            dropped = BoardSerializer.Restore(read, level, board, LevelTestFixtures.Board);
            return board;
        }

        [Test]
        public void ABoardWithABlock_ComesBackFromTheFileWhole()
        {
            LevelDefinition level = HalfAdderLevel();
            CircuitBlueprint board = Solved(level);

            CircuitBlueprint back = ThroughTheFile(BoardSerializer.ToSaved("boxed", board), level, out int dropped);

            Assert.AreEqual(0, dropped);
            Assert.IsTrue(board.Snapshot().DescribesBoard(back), "the board came back different");
        }

        [Test]
        public void ALevelsBoard_TakesTheLevelsBlockOfThatName_NotTheCopy()
        {
            LevelDefinition level = HalfAdderLevel();
            SavedBoard saved = BoardSerializer.ToSaved("boxed", Solved(level));
            saved.blocks[0].block.wires[0].delay = 3;   // as an older version of the level had it

            CircuitBlueprint back = ThroughTheFile(saved, level, out int dropped);

            Assert.AreEqual(0, dropped);
            Assert.AreSame(level.BlockNamed("HA"), back.Blocks[0].Block);
        }

        [Test]
        public void ABlockTheLevelNoLongerStocks_OrHasNoRoomFor_IsDropped()
        {
            LevelDefinition level = HalfAdderLevel();
            SavedBoard saved = BoardSerializer.ToSaved("boxed", Solved(level));

            saved.blocks = new[] { saved.blocks[0], saved.blocks[0] };   // over the budget of one
            saved.blocks[1] = new SavedBlockPlacement { x = 2, y = 0, block = saved.blocks[0].block };
            ThroughTheFile(saved, level, out int dropped);
            Assert.AreEqual(1, dropped, "a second block came back past a budget of one");

            SavedBoard renamed = BoardSerializer.ToSaved("boxed", Solved(level));
            renamed.blocks[0].block.name = "HB";
            CircuitBlueprint back = ThroughTheFile(renamed, level, out dropped);
            Assert.AreEqual(0, back.Blocks.Count, "a block the level does not stock came back");
            Assert.AreEqual(5, dropped, "the block and its four wires");

            LevelDefinition withANot = LevelTestFixtures.Parse(Level(
                HalfAdderBlock, @"{ ""block"": ""HA"", ""count"": 1 }, { ""kind"": ""Not"", ""count"": 1 }"));
            SavedBoard crowded = BoardSerializer.ToSaved("boxed", Solved(withANot));
            crowded.placements = new[] { new SavedPlacement { x = 0, y = 0, kind = "Not" } };
            back = ThroughTheFile(crowded, withANot, out dropped);
            Assert.AreEqual(1, back.Placements.Count, "sanity: the gate did not come back");
            Assert.AreEqual(0, back.Blocks.Count, "a block came back onto a cell a gate holds");
        }

        [Test]
        public void AWireOnABlocksLowerCell_IsDropped()
        {
            LevelDefinition level = ParityLevel();
            var board = new CircuitBlueprint();
            board.PlaceBlock(new Vector2Int(0, 1), level.BlockNamed("PAR"));
            board.AddWire(BlockTests.Wire(SourceA, new Vector2Int(0, 1), 2));
            board.AddWire(BlockTests.Wire(SourceB, new Vector2Int(0, 0), 0));   // stored against the wrong cell

            CircuitBlueprint back = ThroughTheFile(BoardSerializer.ToSaved("par", board), level, out int dropped);

            Assert.AreEqual(1, dropped);
            Assert.AreEqual(1, back.Wires.Count);
            Assert.AreEqual(2, back.Wires[0].To.Index);
        }

        /// <summary>Free play's rules over a level's fixtures: any block, from the copy it carries.</summary>
        private static LevelDefinition AnyBlock(LevelDefinition level) => new LevelDefinition(
            level.Name, level.Hint, level.TickLimit, level.VectorCount, level.Fixtures, level.Budget, level.Expectations,
            level.MaxWireDelay, level.DelayBudget, level.MaxLatency, level.Order, level.Goal, level.IsGraded,
            level.ReservedSlots, level.ClockPeriod, level.BoardHalfExtents, level.Start, anyBlock: true);

        [Test]
        public void FreePlay_KeepsTheCopyItsBoardCarries()
        {
            LevelDefinition level = HalfAdderLevel();
            SavedBoard saved = BoardSerializer.ToSaved("sandbox", Solved(level));
            saved.blocks[0].block.wires[0].delay = 3;

            CircuitBlueprint back = ThroughTheFile(saved, AnyBlock(level), out int dropped);

            Assert.AreEqual(0, dropped);
            Assert.AreEqual(3, back.Blocks[0].Block.Wires[0].Delay, "free play did not keep its own copy");

            saved.blocks[0].block.outputs = new SavedBlockPort[0];
            back = ThroughTheFile(saved, AnyBlock(level), out dropped);
            Assert.AreEqual(0, back.Blocks.Count, "a copy that is no longer a block came back");
        }

        // -----------------------------------------------------------------
        // Free play's library
        // -----------------------------------------------------------------

        [Test]
        public void TheLibrary_IsKeptInTheSave_AndGoesWithAReset()
        {
            string path = Path.Combine(Path.GetTempPath(), $"bitsorter-blocks-{Guid.NewGuid():N}.json");

            try
            {
                BlockDefinition parity = ParityLevel().BlockNamed("PAR");

                var store = new ProgressStore(path);
                store.AddBlock(BoardSerializer.ToSaved(parity));
                store.AddBlock(BoardSerializer.ToSaved(HalfAdderLevel().BlockNamed("HA")));
                store.Save();

                var again = new ProgressStore(path);
                again.Load();

                Assert.AreEqual(2, again.Library.Count);
                Assert.IsTrue(again.HasBlockNamed("par"), "a name differing in case was not taken as the same");
                Assert.IsTrue(BoardSerializer.TryFromSaved(again.Library[0], out BlockDefinition read));
                Assert.IsTrue(parity.Matches(read), "the block came back different");

                Assert.IsTrue(again.DeleteBlock(0));
                Assert.AreEqual("HA", again.Library[0].name);

                again.Clear();
                Assert.AreEqual(0, again.Library.Count);
            }
            finally
            {
                foreach (string file in new[] { path, path + ".tmp" })
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
        }
    }
}
