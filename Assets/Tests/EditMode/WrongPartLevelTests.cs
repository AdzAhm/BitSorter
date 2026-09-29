using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A full adder, correctly timed, whose carries are joined by an AND.
    /// </summary>
    /// <remarks>
    /// Two carries can never both be 1 -- A AND B being 1 forces A XOR B to 0 -- so an AND joining
    /// them is 0 on every row, and COUT goes wrong exactly where it should get a 1. There is no swap
    /// gesture: taking the AND off takes its three wires with it, one of them re-timed to 2, so the
    /// repair is a part and three wires and a scroll -- and redrawing that wire at 1 is its own
    /// failure. The budget stocks a third XOR as well as the one OR, so the parts list does not
    /// point at the answer, and an XOR join is a second fix.
    /// </remarks>
    public class WrongPartLevelTests
    {
        private static readonly Vector2Int FirstCarry = new Vector2Int(-2, 0);
        private static readonly Vector2Int SecondCarry = new Vector2Int(0, -2);
        private static readonly Vector2Int Join = new Vector2Int(2, -1);
        private static readonly Vector2Int CarryOut = new Vector2Int(4, -1);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("wrong-part", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped wrong-part.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The start with its join replaced, as a player does it: off, a new part on, and the three
        /// wires drawn again.
        /// </summary>
        private CircuitBlueprint Replaced(GateKind join, int firstCarryDelay = 2)
        {
            CircuitBlueprint board = LevelTestFixtures.FromStart(_level);

            Assert.IsTrue(board.RemoveAt(Join), "sanity: no part on the join's cell");
            Assert.AreEqual(9, board.Wires.Count, "sanity: taking the join off should take its three wires");

            board.Place(Join, join);
            LevelTestFixtures.Wire(board, FirstCarry, Join, toPort: 0, delay: firstCarryDelay);
            LevelTestFixtures.Wire(board, SecondCarry, Join, toPort: 1);
            LevelTestFixtures.Wire(board, Join, CarryOut);

            return board;
        }

        [Test]
        public void TheStart_GetsTheCarryWrong_AndOnlyTheCarry()
        {
            CircuitBlueprint start = LevelTestFixtures.FromStart(_level);
            BuiltCircuit built = CircuitBuilder.Build(_level, start);

            Assert.AreEqual(start.Wires.Count, built.Simulation.LiveEdgeCount, "a start wire was not built");

            RunVerdict verdict = LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual("cout", verdict.SinkId, "the sum should be right: its timing and parts are");
            Assert.AreEqual(0, built.Simulation.CorruptedCount, "the start's timing is meant to be right");
        }

        [Test]
        public void AnOrJoiningTheCarries_Passes()
        {
            CircuitBlueprint board = Replaced(GateKind.Or);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, board);

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.LessOrEqual(board.ExtraDelay(), _level.DelayBudget);
        }

        /// <summary>The carries are never both 1, so XOR joins them as well as OR does.</summary>
        [Test]
        public void AnXorJoiningTheCarries_AlsoPasses()
        {
            Assert.IsTrue(LevelTestFixtures.RunAndGrade(_level, Replaced(GateKind.Xor)).IsPass);
        }

        /// <summary>
        /// The trap in the repair: the first carry's wire was two ticks long, and a wire drawn again
        /// is one.
        /// </summary>
        [Test]
        public void RedrawingTheFirstCarryAtOneTick_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Replaced(GateKind.Or, firstCarryDelay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TheBudget_IsTheStartsPartsAndOneSpareOfEachJoin()
        {
            CircuitBlueprint start = LevelTestFixtures.FromStart(_level);

            Assert.AreEqual(start.CountOf(GateKind.Xor) + 1, _level.BudgetFor(GateKind.Xor));
            Assert.AreEqual(start.CountOf(GateKind.And), _level.BudgetFor(GateKind.And));
            Assert.AreEqual(0, start.CountOf(GateKind.Or));
            Assert.AreEqual(1, _level.BudgetFor(GateKind.Or));
        }
    }
}
