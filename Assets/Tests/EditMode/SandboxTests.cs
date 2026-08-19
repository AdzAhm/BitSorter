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
