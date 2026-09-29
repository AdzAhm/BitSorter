using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A circuit whose every gate is right and whose bits arrive out of step: the first level that
    /// opens on a circuit, and the slow lane's lesson found on a board somebody else got wrong.
    /// </summary>
    /// <remarks>
    /// (A AND B) OR (NOT C AND D). The start pads the wrong side of the lower AND -- the NOT's wire
    /// instead of D's -- and pads the upper AND's wire into the OR correctly. The delay budget is
    /// exactly what the fix costs, and the start has already spent it, so the obvious move of
    /// padding D is refused until the wrong padding is taken back. With a vector every tick any
    /// balanced set of delays passes, so the budget is the only thing that makes a wrong padding a
    /// mistake rather than a detour.
    /// </remarks>
    public class OutOfStepLevelTests
    {
        private static readonly Vector2Int SourceC = new Vector2Int(-4, -1);
        private static readonly Vector2Int SourceD = new Vector2Int(-4, -2);
        private static readonly Vector2Int UpperAnd = new Vector2Int(-2, 1);
        private static readonly Vector2Int Not = new Vector2Int(-2, -1);
        private static readonly Vector2Int LowerAnd = new Vector2Int(0, -2);
        private static readonly Vector2Int Or = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("out-of-step", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped out-of-step.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>The start, re-timed the right way: the NOT's wire back to 1, D's out to 2.</summary>
        private CircuitBlueprint Fixed()
        {
            CircuitBlueprint board = LevelTestFixtures.FromStart(_level);
            LevelTestFixtures.Retime(board, Not, LowerAnd, toPort: 0, delay: 1);
            LevelTestFixtures.Retime(board, SourceD, LowerAnd, toPort: 1, delay: 2);
            return board;
        }

        [Test]
        public void TheStart_DestroysBits()
        {
            CircuitBlueprint start = LevelTestFixtures.FromStart(_level);
            BuiltCircuit built = CircuitBuilder.Build(_level, start);

            Assert.AreEqual(start.Wires.Count, built.Simulation.LiveEdgeCount, "a start wire was not built");

            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TheStart_HasAlreadySpentTheWholeDelayBudget()
        {
            Assert.AreEqual(_level.DelayBudget, LevelTestFixtures.FromStart(_level).ExtraDelay());
        }

        /// <summary>
        /// Padding D -- the obvious fix -- is refused while the wrong padding stands, and allowed once
        /// it is taken back.
        /// </summary>
        [Test]
        public void PaddingTheLateSide_IsRefused_UntilTheWrongPaddingComesOff()
        {
            CircuitBlueprint board = LevelTestFixtures.FromStart(_level);

            LevelVerdict first = LevelRules.CanSetDelay(_level, board, RunState.Editing, 1, 2);
            Assert.IsFalse(first.IsValid, "the budget let D be padded on top of the wrong padding");
            Assert.AreEqual(LevelOutcome.DelayBudgetSpent, first.Outcome);

            LevelTestFixtures.Retime(board, Not, LowerAnd, toPort: 0, delay: 1);

            Assert.IsTrue(LevelRules.CanSetDelay(_level, board, RunState.Editing, 1, 2).IsValid,
                "with the wrong padding back off, padding D should fit");
        }

        [Test]
        public void ReTimingTheLowerAnd_Passes_WithinTheBudget()
        {
            CircuitBlueprint board = Fixed();

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, board);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.LessOrEqual(board.ExtraDelay(), _level.DelayBudget);
        }

        /// <summary>The fix touches nothing but delays: every part and every wire is where it started.</summary>
        [Test]
        public void TheFix_MovesNoPartAndNoWire()
        {
            CircuitBlueprint start = LevelTestFixtures.FromStart(_level);
            CircuitBlueprint board = Fixed();

            Assert.AreEqual(start.Placements.Count, board.Placements.Count);
            Assert.AreEqual(start.Wires.Count, board.Wires.Count);

            for (int i = 0; i < start.Wires.Count; i++)
                Assert.IsTrue(board.HasWire(start.Wires[i].From, start.Wires[i].To), $"start wire {i} moved");
        }

        /// <summary>
        /// Leaving the lower AND alone and only balancing the OR cannot work: that collision is
        /// upstream of it.
        /// </summary>
        [Test]
        public void BalancingOnlyTheJoin_StillDestroysBits()
        {
            CircuitBlueprint board = LevelTestFixtures.FromStart(_level);
            LevelTestFixtures.Retime(board, UpperAnd, Or, toPort: 0, delay: 1);
            LevelTestFixtures.Retime(board, LowerAnd, Or, toPort: 1, delay: 1);

            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, board).IsPass);
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
