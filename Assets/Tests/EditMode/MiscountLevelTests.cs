using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A two-bit counter with one wire in the wrong place: the high bit's toggle reads the low
    /// register where it should read its own.
    /// </summary>
    /// <remarks>
    /// The high bit then becomes LOW AND NOT IN, which stays 0 as long as every 0 in the stream
    /// arrives while LOW is 0 -- so the stream is chosen to keep it there, and the counter never gets
    /// past one while LOW counts correctly. The more obvious miswiring, the carry AND reading the
    /// high register, makes a three-wire loop on a two-tick clock and destroys bits instead of
    /// counting wrong, which is a different lesson. Every wire is one tick long and cannot be
    /// scrolled, so the only lever is where the wires go.
    /// </remarks>
    public class MiscountLevelTests
    {
        private static readonly Vector2Int LowRegister = new Vector2Int(1, -1);
        private static readonly Vector2Int HighXor = new Vector2Int(1, 1);
        private static readonly Vector2Int HighRegister = new Vector2Int(3, 1);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("miscount", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped miscount.json is invalid: {result.Error}");

            _level = result.Level;
        }

        [Test]
        public void TheStart_NeverCountsPastOne()
        {
            CircuitBlueprint start = LevelTestFixtures.FromStart(_level);
            BuiltCircuit built = CircuitBuilder.Build(_level, start);

            Assert.AreEqual(start.Wires.Count, built.Simulation.LiveEdgeCount, "a start wire was not built");

            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual("high", verdict.SinkId, "LOW is meant to count correctly");
            Assert.AreEqual(0, built.Simulation.CorruptedCount, "every wire's timing is meant to be right");
        }

        [Test]
        public void TakingTheHighToggleFromItsOwnRegister_Passes()
        {
            CircuitBlueprint board = LevelTestFixtures.FromStart(_level);
            LevelTestFixtures.Unwire(board, LowRegister, HighXor, toPort: 0);
            LevelTestFixtures.Wire(board, HighRegister, HighXor, toPort: 0);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, board);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        /// <summary>The right wire added beside the wrong one, rather than instead of it, collides.</summary>
        [Test]
        public void AddingTheRightWire_WithoutTakingTheWrongOneOff_DestroysBits()
        {
            CircuitBlueprint board = LevelTestFixtures.FromStart(_level);
            LevelTestFixtures.Wire(board, HighRegister, HighXor, toPort: 0);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, board);

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void EveryWire_IsOneTick_AndStaysThatWay()
        {
            Assert.AreEqual(1, _level.MaxWireDelay, "re-timing would be a second lever");
            Assert.AreEqual(0, LevelTestFixtures.FromStart(_level).ExtraDelay());
        }

        [Test]
        public void TheBudget_IsExactlyTheStartsParts()
        {
            CircuitBlueprint start = LevelTestFixtures.FromStart(_level);

            foreach (LevelBudgetEntry entry in _level.Budget)
                Assert.AreEqual(entry.Count, start.CountOf(entry.Kind), $"{entry.Kind}: the level stocks spares");
        }
    }
}
