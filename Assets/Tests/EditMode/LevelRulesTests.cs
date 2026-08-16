using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// What a level permits the player to do: the parts budget, the fixed nodes, the board edge, and
    /// the run state. LevelRules is pure and static precisely so this matrix can be exhaustive without
    /// a scene or a MonoBehaviour.
    /// </summary>
    public class LevelRulesTests
    {
        private LevelDefinition _level;
        private CircuitBlueprint _blueprint;

        [SetUp]
        public void SetUp()
        {
            _level = LevelTestFixtures.Routing();
            _blueprint = new CircuitBlueprint();
        }

        private LevelVerdict Place(GateKind kind, Vector2Int cell, RunState state = RunState.Editing) =>
            LevelRules.CanPlace(_level, _blueprint, state, kind, cell, LevelTestFixtures.Board);

        private LevelVerdict Remove(Vector2Int cell, RunState state = RunState.Editing) =>
            LevelRules.CanRemove(_level, _blueprint, state, cell);

        // -----------------------------------------------------------------
        // Budget
        // -----------------------------------------------------------------

        [Test]
        public void PlacingABudgetedKindOnAnEmptyCell_IsAllowed()
        {
            LevelVerdict verdict = Place(GateKind.Not, LevelTestFixtures.MiddleCell);

            Assert.IsTrue(verdict.IsValid, verdict.ToString());
        }

        [Test]
        public void PlacingAKindTheLevelDoesNotOffer_IsRefused()
        {
            // This is the NAND-only puzzle's whole mechanism: a level offers a palette, not the palette.
            AssertRefused(Place(GateKind.Or, LevelTestFixtures.MiddleCell), LevelOutcome.NotInBudget);
        }

        [Test]
        public void PlacingMoreThanTheBudgetAllows_IsRefused()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            AssertRefused(Place(GateKind.Not, new Vector2Int(1, 1)), LevelOutcome.BudgetSpent);
        }

        [Test]
        public void AnExhaustedBudget_ReadsDifferentlyFromAForbiddenKind()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            // One means "remove one first", the other "never in this level". Same refusal, and the
            // player needs to react differently, so they must not share a message.
            LevelVerdict spent = Place(GateKind.Not, new Vector2Int(1, 1));
            LevelVerdict forbidden = Place(GateKind.Or, new Vector2Int(1, 1));

            Assert.AreNotEqual(spent.Reason, forbidden.Reason);
        }

        [Test]
        public void RemovingAPlacedGate_ReturnsItToTheBudget()
        {
            // Remaining is computed, never stored, so this needs no bookkeeping to be correct -- which
            // is exactly what the test is pinning.
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            Assert.AreEqual(0, LevelRules.RemainingFor(_level, _blueprint, GateKind.Not), "spent");

            _blueprint.RemoveAt(LevelTestFixtures.MiddleCell);

            Assert.AreEqual(1, LevelRules.RemainingFor(_level, _blueprint, GateKind.Not), "returned");
            Assert.IsTrue(Place(GateKind.Not, LevelTestFixtures.MiddleCell).IsValid, "placeable again");
        }

        [Test]
        public void RemainingForAKindTheLevelDoesNotOffer_IsZero()
        {
            Assert.AreEqual(0, LevelRules.RemainingFor(_level, _blueprint, GateKind.Xor));
        }

        // -----------------------------------------------------------------
        // Cells
        // -----------------------------------------------------------------

        [Test]
        public void PlacingOnAFixtureCell_IsRefused()
        {
            AssertRefused(Place(GateKind.Not, LevelTestFixtures.SourceCell), LevelOutcome.CellTaken);
            AssertRefused(Place(GateKind.Not, LevelTestFixtures.BinOneCell), LevelOutcome.CellTaken);
        }

        [Test]
        public void PlacingOnACellThatAlreadyHasAGate_IsRefused()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            AssertRefused(Place(GateKind.Not, LevelTestFixtures.MiddleCell), LevelOutcome.CellTaken);
        }

        [Test]
        public void PlacingOffTheBoard_IsRefused()
        {
            AssertRefused(Place(GateKind.Not, new Vector2Int(9, 0)), LevelOutcome.OffBoard);
            AssertRefused(Place(GateKind.Not, new Vector2Int(0, 5)), LevelOutcome.OffBoard);
        }

        [Test]
        public void RemovingAFixture_IsRefusedAndSaysWhichOne()
        {
            LevelVerdict verdict = Remove(LevelTestFixtures.SourceCell);

            AssertRefused(verdict, LevelOutcome.Fixed);
            StringAssert.Contains("in", verdict.Reason, "the reason should name the fixture");
        }

        [Test]
        public void RemovingAnEmptyCell_IsRefusedSilently()
        {
            LevelVerdict verdict = Remove(LevelTestFixtures.MiddleCell);

            Assert.IsFalse(verdict.IsValid);
            Assert.AreEqual(LevelOutcome.NothingThere, verdict.Outcome);

            // A right click on empty space goes on to delete the nearest wire. A message here would
            // fire every single time the player deletes a wire.
            Assert.IsNull(verdict.Reason, "an empty cell must refuse without scolding");
        }

        [Test]
        public void RemovingAPlacedGate_IsAllowed()
        {
            _blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);

            Assert.IsTrue(Remove(LevelTestFixtures.MiddleCell).IsValid);
        }

        // -----------------------------------------------------------------
        // Run state
        // -----------------------------------------------------------------

        [TestCase(RunState.Running)]
        [TestCase(RunState.Passed)]
        [TestCase(RunState.Failed)]
        public void EveryEdit_IsRefusedWhileNotEditing(RunState state)
        {
            // Editing a running graph would mean adding and removing nodes mid-stream, so it is refused
            // rather than queued.
            AssertRefused(Place(GateKind.Not, LevelTestFixtures.MiddleCell, state), LevelOutcome.NotEditing);
            AssertRefused(Remove(LevelTestFixtures.MiddleCell, state), LevelOutcome.NotEditing);
            AssertRefused(LevelRules.CanEdit(state), LevelOutcome.NotEditing);
        }

        [Test]
        public void TheRunStateGate_OutranksEveryOtherReason()
        {
            // An off-board click during a run should explain the run, not the board edge: the run is
            // what the player has to deal with first.
            AssertRefused(
                Place(GateKind.Or, new Vector2Int(9, 9), RunState.Running), LevelOutcome.NotEditing);
        }

        [Test]
        public void EditingState_AllowsEdits()
        {
            Assert.IsTrue(LevelRules.CanEdit(RunState.Editing).IsValid);
        }

        private static void AssertRefused(LevelVerdict verdict, LevelOutcome expected)
        {
            Assert.IsFalse(verdict.IsValid, "expected a refusal");
            Assert.AreEqual(expected, verdict.Outcome);
        }
    }
}
