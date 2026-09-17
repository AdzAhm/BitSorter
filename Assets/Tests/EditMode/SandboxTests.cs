using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Free play: a level built in code rather than loaded from a file, with no limits and no
    /// grading.
    /// </summary>
    /// <remarks>
    /// <see cref="SandboxLevel.Build"/> is pure for exactly this reason -- the whole matrix of
    /// fixtures, budgets and stream normalisation is checkable without a Canvas or a scene.
    /// </remarks>
    public class SandboxTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private static SandboxConfig Config(int sources, int sinks, int vectors, params string[] streams)
        {
            var config = new SandboxConfig
            {
                sources = new string[sources],
                sinks = sinks,
                vectors = vectors,
            };

            for (int i = 0; i < sources; i++)
                config.sources[i] = i < streams.Length ? streams[i] : string.Empty;

            return config;
        }

        private static int CountOf(LevelDefinition level, FixtureKind kind)
        {
            int count = 0;

            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind == kind)
                    count++;
            }

            return count;
        }

        // -----------------------------------------------------------------
        // Shape
        // -----------------------------------------------------------------

        [Test]
        public void ASandbox_IsNotGraded()
        {
            LevelDefinition level = SandboxLevel.Build(SandboxLevel.Default(Board), Board);

            Assert.IsFalse(level.IsGraded);
            Assert.IsEmpty(level.Expectations, "free play grades nothing, so it expects nothing");
        }

        [Test]
        public void ASandbox_BuildsTheFixturesItWasAskedFor()
        {
            LevelDefinition level = SandboxLevel.Build(Config(3, 2, 4, "1010", "1100", "0001"), Board);

            Assert.AreEqual(3, CountOf(level, FixtureKind.Source));
            Assert.AreEqual(2, CountOf(level, FixtureKind.Sink));
        }

        [Test]
        public void EveryFixture_GetsItsOwnCellInsideTheBoard()
        {
            LevelDefinition level = SandboxLevel.Build(Config(5, 5, 4), Board);

            var taken = new HashSet<Vector2Int>();

            foreach (LevelFixture fixture in level.Fixtures)
            {
                Assert.IsTrue(taken.Add(fixture.Cell), $"{fixture.Id} shares a cell at {fixture.Cell}");

                Assert.LessOrEqual(Mathf.Abs(fixture.Cell.x), Board.x, $"{fixture.Id} is off the board");
                Assert.LessOrEqual(Mathf.Abs(fixture.Cell.y), Board.y, $"{fixture.Id} is off the board");
            }
        }

        [Test]
        public void SourcesAndSinks_TakeOppositeColumns()
        {
            LevelDefinition level = SandboxLevel.Build(Config(2, 2, 4), Board);

            foreach (LevelFixture fixture in level.Fixtures)
            {
                int expected = fixture.Kind == FixtureKind.Source ? -Board.x : Board.x;
                Assert.AreEqual(expected, fixture.Cell.x, $"{fixture.Id} is in the wrong column");
            }
        }

        [Test]
        public void AskingForMoreFixturesThanTheBoardHasRows_IsClampedRatherThanDropped()
        {
            // The board is five rows, so a sixth source has nowhere to go.
            LevelDefinition level = SandboxLevel.Build(Config(9, 9, 4), Board);

            Assert.AreEqual(SandboxLevel.Capacity(Board), CountOf(level, FixtureKind.Source));
            Assert.AreEqual(SandboxLevel.Capacity(Board), CountOf(level, FixtureKind.Sink));
        }

        // -----------------------------------------------------------------
        // Fixtures stay where the circuit expects them
        // -----------------------------------------------------------------

        /// <summary>
        /// Adding a source or a sink never moves one that was already there.
        /// </summary>
        /// <remarks>
        /// Wires are stored by cell. Fixtures used to be re-centred in their column whenever their
        /// count changed, so going from one source to two moved A up a row and put B where A had
        /// been -- and every wire the player had drawn from A now came from B. Nothing said so; the
        /// circuit simply started computing something else.
        /// </remarks>
        [Test]
        public void AddingAFixture_NeverMovesTheOnesAlreadyThere()
        {
            int capacity = SandboxLevel.Capacity(Board);

            for (int count = 1; count < capacity; count++)
            {
                LevelDefinition fewer = SandboxLevel.Build(Config(count, count, 4), Board);
                LevelDefinition more = SandboxLevel.Build(Config(count + 1, count + 1, 4), Board);

                foreach (LevelFixture fixture in fewer.Fixtures)
                {
                    Assert.AreEqual(fixture.Cell, CellOf(more, fixture.Id),
                        $"going from {count} to {count + 1}, {fixture.Id} moved");
                }
            }
        }

        /// <summary>
        /// A cell that a later source or sink could need is not somewhere a gate can go.
        /// </summary>
        /// <remarks>
        /// Otherwise adding a source can land it on a gate the player placed there, and the restore
        /// that follows drops the gate -- along with every wire into it -- without a word.
        /// </remarks>
        [Test]
        public void ACellAFixtureMayLaterNeed_IsNotSomewhereAGateCanGo()
        {
            LevelDefinition level = SandboxLevel.Build(Config(2, 2, 4), Board);
            LevelDefinition full = SandboxLevel.Build(
                Config(SandboxLevel.Capacity(Board), SandboxLevel.Capacity(Board), 4), Board);

            foreach (LevelFixture fixture in full.Fixtures)
            {
                LevelVerdict verdict = LevelRules.CanPlace(
                    level, new CircuitBlueprint(), RunState.Editing, GateKind.Not, fixture.Cell, Board);

                Assert.IsFalse(verdict.IsValid,
                    $"a gate may go on {fixture.Cell}, where {fixture.Id} would appear");
            }
        }

        private static Vector2Int CellOf(LevelDefinition level, string id)
        {
            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Id == id)
                    return fixture.Cell;
            }

            Assert.Fail($"no fixture {id}");
            return default;
        }

        // -----------------------------------------------------------------
        // Boards saved while fixtures were centred
        // -----------------------------------------------------------------

        [Test]
        public void ANewSetup_IsOnTheCurrentLayout()
        {
            // A new board mistaken for an old one would have its wires moved the next time it loads.
            SandboxConfig config = SandboxLevel.Default(Board);

            Assert.AreEqual(SandboxConfig.CurrentLayout, config.layout);
            Assert.AreEqual(SandboxConfig.CurrentLayout, config.Clone().layout, "a copy forgot the layout");
        }

        [Test]
        public void AnOldBoard_KeepsItsWiresOnTheSameFixtures()
        {
            // Two of each on five rows were centred one row down: A and OUT 1 on row 1, B and
            // OUT 2 on row 0.
            SavedBoard board = LegacyBoard(Config(2, 2, 4, "0011", "0101"),
                Wire(-4, 0, 0, 0),    // B into the gate
                Wire(0, 0, 4, 0),     // the gate into OUT 2
                Wire(-4, 1, 4, 1));   // A straight into OUT 1

            Assert.IsTrue(SandboxLevel.MigrateLegacyBoard(board, Board));

            AssertRestoresAs(board,
                ("B", "gate"),
                ("gate", "OUT 2"),
                ("A", "OUT 1"));
        }

        [Test]
        public void AnOldBoard_MovesEachColumnByItsOwnCount()
        {
            // One source was centred two rows down and three sinks one row down, so the two columns
            // moved by different amounts.
            SavedBoard board = LegacyBoard(Config(1, 3, 4, "0101"),
                Wire(-4, 0, 0, 0),     // A into the gate
                Wire(0, 0, 4, -1),     // the gate into OUT 3
                Wire(-4, 0, 4, 1));    // A straight into OUT 1

            Assert.IsTrue(SandboxLevel.MigrateLegacyBoard(board, Board));

            AssertRestoresAs(board,
                ("A", "gate"),
                ("gate", "OUT 3"),
                ("A", "OUT 1"));
        }

        [Test]
        public void ABoardOnTheCurrentLayout_IsLeftAlone()
        {
            SandboxConfig config = SandboxLevel.Default(Board);
            SavedBoard board = LegacyBoard(config, Wire(-4, 2, 0, 0));

            Assert.IsFalse(SandboxLevel.MigrateLegacyBoard(board, Board));
            Assert.AreEqual(2, board.wires[0].fromY, "a current board was moved");

            Assert.IsFalse(SandboxLevel.MigrateLegacyBoard(null, Board));
            Assert.IsFalse(SandboxLevel.MigrateLegacyBoard(new SavedBoard(), Board));
        }

        [Test]
        public void MigratingTwice_MovesNothingTheSecondTime()
        {
            SavedBoard board = LegacyBoard(Config(2, 2, 4), Wire(-4, 0, 4, 0));

            Assert.IsTrue(SandboxLevel.MigrateLegacyBoard(board, Board));
            Assert.IsFalse(SandboxLevel.MigrateLegacyBoard(board, Board));

            Assert.AreEqual(SandboxConfig.CurrentLayout, board.sandbox.layout);
            Assert.AreEqual(1, board.wires[0].fromY, "B moved on from its new slot");
        }

        /// <summary>A saved sandbox board with a NOT gate in the middle and the given wires.</summary>
        private static SavedBoard LegacyBoard(SandboxConfig config, params SavedWire[] wires) =>
            new SavedBoard
            {
                level = SandboxLevel.Key,
                sandbox = config,
                placements = new[] { new SavedPlacement { x = 0, y = 0, kind = "Not" } },
                wires = wires,
            };

        private static SavedWire Wire(int fromX, int fromY, int toX, int toY) =>
            new SavedWire
            {
                fromX = fromX, fromY = fromY, fromPort = 0,
                toX = toX, toY = toY, toPort = 0,
                delay = 1,
            };

        /// <summary>
        /// Restores the board into the level its setup builds, and checks every wire joins the named
        /// ends, in order. "gate" is the one in the middle.
        /// </summary>
        private static void AssertRestoresAs(SavedBoard board, params (string from, string to)[] expected)
        {
            LevelDefinition level = SandboxLevel.Build(board.sandbox, Board);
            var blueprint = new CircuitBlueprint();

            Assert.AreEqual(0, BoardSerializer.Restore(board, level, blueprint, Board),
                "the restore dropped part of the circuit");
            Assert.AreEqual(expected.Length, blueprint.Wires.Count);

            for (int i = 0; i < expected.Length; i++)
            {
                BlueprintWire wire = blueprint.Wires[i];

                Assert.AreEqual(expected[i].from, NameAt(level, wire.From.Cell), $"wire {i} starts in the wrong place");
                Assert.AreEqual(expected[i].to, NameAt(level, wire.To.Cell), $"wire {i} ends in the wrong place");
            }
        }

        private static string NameAt(LevelDefinition level, Vector2Int cell) =>
            level.FixtureAt(cell)?.Id ?? (cell == Vector2Int.zero ? "gate" : $"nothing at {cell}");

        // -----------------------------------------------------------------
        // Saying why nothing happens
        // -----------------------------------------------------------------

        [Test]
        public void ASetupThatCanRun_HasNothingToWarnAbout()
        {
            Assert.IsNull(SandboxLevel.Warning(SandboxLevel.Default(Board)));
        }

        [Test]
        public void WithNoSources_TheGoalSaysNothingIsEmitted()
        {
            LevelDefinition level = SandboxLevel.Build(Config(0, 2, 4), Board);

            Assert.IsNotNull(SandboxLevel.Warning(Config(0, 2, 4)));
            StringAssert.Contains("No sources", level.Goal);
        }

        [Test]
        public void WithNoSinks_TheGoalSaysBitsHaveNowhereToLand()
        {
            LevelDefinition level = SandboxLevel.Build(Config(2, 0, 4, "1010", "0011"), Board);

            StringAssert.Contains("No sinks", level.Goal);
        }

        [Test]
        public void TheWarningAndTheGoal_AreTheSameSentence()
        {
            // One wording in two places -- the status banner and the sandbox panel -- rather than
            // two that can drift apart.
            SandboxConfig broken = Config(0, 1, 4);

            Assert.AreEqual(SandboxLevel.Warning(broken), SandboxLevel.GoalFor(broken));
        }

        [Test]
        public void AnEmptySandbox_StillBuildsAndStillGradesNothing()
        {
            // Reachable from the panel, so it must not throw on the way to saying so.
            LevelDefinition level = SandboxLevel.Build(Config(0, 0, 4), Board);

            Assert.IsFalse(level.IsGraded);
            Assert.IsEmpty(level.Fixtures);
            Assert.IsNotEmpty(level.Goal);
        }

        // -----------------------------------------------------------------
        // Streams
        // -----------------------------------------------------------------

        [Test]
        public void EveryStream_IsNormalisedToTheVectorCount()
        {
            // One stream too short, one too long, one absent entirely.
            SandboxConfig config = Config(3, 1, 4, "1", "101010", null);

            LevelDefinition level = SandboxLevel.Build(config, Board);

            Assert.AreEqual(4, level.VectorCount);

            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind != FixtureKind.Source)
                    continue;

                Assert.AreEqual(4, fixture.Stream.Count,
                    $"{fixture.Id} does not match the vector count");
            }
        }

        [Test]
        public void ShorteningTheVectorCount_TruncatesEveryStreamTogether()
        {
            SandboxConfig config = Config(2, 1, 6, "111111", "000000");
            config.vectors = 2;

            LevelDefinition level = SandboxLevel.Build(config, Board);

            Assert.AreEqual(2, level.VectorCount);

            foreach (LevelFixture fixture in level.Fixtures)
            {
                if (fixture.Kind == FixtureKind.Source)
                    Assert.AreEqual(2, fixture.Stream.Count);
            }
        }

        [Test]
        public void PaddingAStream_AddsZerosAndKeepsWhatWasThere()
        {
            LevelDefinition level = SandboxLevel.Build(Config(1, 1, 4, "11"), Board);

            LevelFixture source = level.FixtureById(SandboxLevel.SourceId(0));

            Assert.AreEqual(
                new[] { Bit.One, Bit.One, Bit.Zero, Bit.Zero },
                new List<Bit>(source.Stream).ToArray());
        }

        [Test]
        public void AStreamWithRubbishInIt_ReadsAsZeros()
        {
            LevelDefinition level = SandboxLevel.Build(Config(1, 1, 3, "1x?"), Board);

            LevelFixture source = level.FixtureById(SandboxLevel.SourceId(0));

            Assert.AreEqual(
                new[] { Bit.One, Bit.Zero, Bit.Zero },
                new List<Bit>(source.Stream).ToArray());
        }

        [Test]
        public void TheVectorCount_IsHeldInsideItsBounds()
        {
            SandboxConfig config = Config(1, 1, 999, "1");

            LevelDefinition level = SandboxLevel.Build(config, Board);

            Assert.AreEqual(SandboxConfig.MaxVectors, level.VectorCount);
        }

        // -----------------------------------------------------------------
        // Unlimited budget
        // -----------------------------------------------------------------

        [Test]
        public void EveryGateKind_IsOfferedWithoutLimit()
        {
            LevelDefinition level = SandboxLevel.Build(SandboxLevel.Default(Board), Board);

            foreach (GateKind kind in new[]
                     {
                         GateKind.Not, GateKind.And, GateKind.Or,
                         GateKind.Xor, GateKind.Nand, GateKind.Nor,
                     })
            {
                Assert.IsTrue(level.Offers(kind), $"{kind} is not offered");
                Assert.IsTrue(level.IsUnlimited(kind), $"{kind} is capped");
            }
        }

        [Test]
        public void AnUnlimitedBudget_KeepsAcceptingGatesPastAnyCount()
        {
            LevelDefinition level = SandboxLevel.Build(SandboxLevel.Default(Board), Board);
            var blueprint = new CircuitBlueprint();

            // Well past what any authored level stocks, and past the point a counted budget would
            // have refused.
            for (int i = 0; i < 12; i++)
            {
                var cell = new Vector2Int(i % 7 - 3, i / 7 - 1);

                LevelVerdict verdict = LevelRules.CanPlace(
                    level, blueprint, RunState.Editing, GateKind.And, cell, Board);

                Assert.IsTrue(verdict.IsValid, $"refused gate {i} at {cell}: {verdict}");
                blueprint.Place(cell, GateKind.And);
            }

            Assert.AreEqual(12, blueprint.CountOf(GateKind.And));
        }

        [Test]
        public void AnUnlimitedBudget_ReportsItselfAsUnlimitedRatherThanAsExhausted()
        {
            LevelDefinition level = SandboxLevel.Build(SandboxLevel.Default(Board), Board);
            var blueprint = new CircuitBlueprint();

            blueprint.Place(new Vector2Int(0, 0), GateKind.Xor);

            Assert.AreEqual(
                LevelDefinition.UnlimitedBudget,
                LevelRules.RemainingFor(level, blueprint, GateKind.Xor));
        }

        [Test]
        public void AGateAKindIsNotStockedFor_IsStillRefused()
        {
            // The unlimited sentinel is negative, and a careless <= 0 test would read a counted
            // level's missing kind and an unlimited one as the same thing. This is the level that
            // proves they stayed apart.
            LevelDefinition counted = LevelTestFixtures.Routing();
            var blueprint = new CircuitBlueprint();

            foreach (GateKind kind in new[] { GateKind.Not, GateKind.And, GateKind.Or, GateKind.Xor })
            {
                if (counted.Offers(kind))
                    continue;

                LevelVerdict verdict = LevelRules.CanPlace(
                    counted, blueprint, RunState.Editing, kind, LevelTestFixtures.MiddleCell, Board);

                Assert.AreEqual(LevelOutcome.NotInBudget, verdict.Outcome,
                    $"{kind} is not stocked and should have been refused");
            }
        }

        // -----------------------------------------------------------------
        // Saving
        // -----------------------------------------------------------------

        [Test]
        public void AConfig_SurvivesACloneUnchanged()
        {
            SandboxConfig config = Config(2, 3, 4, "1010", "0101");
            config.Normalise(5, 5);

            SandboxConfig copy = config.Clone();

            Assert.AreEqual(config.sinks, copy.sinks);
            Assert.AreEqual(config.vectors, copy.vectors);
            Assert.AreEqual(config.sources, copy.sources);
        }

        [Test]
        public void ACloneIsACopy_NotTheSameArray()
        {
            SandboxConfig config = Config(1, 1, 4, "1111");
            SandboxConfig copy = config.Clone();

            copy.sources[0] = "0000";

            Assert.AreEqual("1111", config.sources[0], "editing the copy changed the original");
        }

        [Test]
        public void StagingASetup_DoesNotTouchTheFile()
        {
            // The sandbox panel stages rather than saves, so that changing a source costs the one
            // write the level swap already causes instead of a second one. If staging ever starts
            // writing, that is two whole-file writes per click again.
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"bitsorter-stage-{System.Guid.NewGuid():N}.json");

            try
            {
                var store = new ProgressStore(path);

                store.StageBoard(SandboxLevel.Key, new SavedBoard { sandbox = Config(1, 1, 4, "1010") });

                Assert.IsFalse(System.IO.File.Exists(path), "staging wrote the progress file");

                // Still in memory, and still there for the next real save to carry.
                Assert.IsNotNull(store.BoardFor(SandboxLevel.Key).sandbox);

                store.SaveBoard(SandboxLevel.Key, new SavedBoard());

                Assert.IsTrue(System.IO.File.Exists(path), "a real save did not write");
                Assert.IsNotNull(store.BoardFor(SandboxLevel.Key).sandbox,
                    "the staged setup was lost by the save that should have carried it");
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }

        [Test]
        public void AnOrdinaryBoardSave_DoesNotWipeTheSandboxSetup()
        {
            // ProgressTracker knows nothing about sandboxes and saves boards with no config on them.
            // The store has to carry the existing one forward, or saving the gates would throw away
            // the sources they are wired to.
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"bitsorter-sandbox-{System.Guid.NewGuid():N}.json");

            try
            {
                var store = new ProgressStore(path);

                store.SaveBoard(SandboxLevel.Key, new SavedBoard
                {
                    sandbox = Config(2, 2, 4, "1010", "0011"),
                });

                store.SaveBoard(SandboxLevel.Key, new SavedBoard
                {
                    placements = new[] { new SavedPlacement { x = 0, y = 0, kind = "And" } },
                });

                SavedBoard board = store.BoardFor(SandboxLevel.Key);

                Assert.IsNotNull(board.sandbox, "the setup was wiped by an ordinary board save");
                Assert.AreEqual(2, board.sandbox.sources.Length);
                Assert.AreEqual(1, board.placements.Length);
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }
    }
}
