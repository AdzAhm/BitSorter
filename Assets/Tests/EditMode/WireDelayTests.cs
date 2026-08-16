using System;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// What the player may do to a wire's delay: the floor of one tick, the level's per-wire cap, and
    /// the total budget. LevelRules is pure and static so the whole matrix is testable without a scene.
    /// </summary>
    /// <remarks>
    /// The order-preserving half of this matters more than it looks. Re-timing a wire rebuilds the
    /// graph underneath a player who is hovering that same wire, and the hover is remembered by edge
    /// id. If a re-time moved the wire in the list, the ids would shuffle and the highlight would slide
    /// onto a different wire mid-scroll.
    /// </remarks>
    public class WireDelayTests
    {
        private LevelDefinition _capped;
        private LevelDefinition _uncapped;
        private CircuitBlueprint _blueprint;

        [SetUp]
        public void SetUp()
        {
            // Two extra ticks to spend, no single wire above three.
            _capped = LevelTestFixtures.Parse(@"{
                ""name"": ""Capped"",
                ""tickLimit"": 40,
                ""maxWireDelay"": 3,
                ""delayBudget"": 2,
                ""fixtures"": [
                    { ""id"": ""in"",  ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""01"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"": 0 } }
                ],
                ""budget"": [ { ""kind"": ""Not"", ""count"": 1 } ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""10"" } ]
            }");

            _uncapped = LevelTestFixtures.Routing();

            _blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 0));
        }

        private LevelVerdict Set(int from, int to, RunState state = RunState.Editing) =>
            LevelRules.CanSetDelay(_capped, _blueprint, state, from, to);

        // -----------------------------------------------------------------
        // Limits
        // -----------------------------------------------------------------

        [Test]
        public void RaisingAWireWithinBothLimits_IsAllowed()
        {
            Assert.IsTrue(Set(1, 2).IsValid);
        }

        [Test]
        public void GoingBelowOneTick_IsRefusedAndSaysWhy()
        {
            // Not an interface limitation: a zero-delay edge would let one node see another's output
            // inside a single tick, which is what makes evaluation order irrelevant. Scrolling into the
            // floor is exactly where a player wonders, so it gets a message rather than silence.
            LevelVerdict verdict = Set(1, 0);

            Assert.IsFalse(verdict.IsValid);
            Assert.AreEqual(LevelOutcome.DelayAtMinimum, verdict.Outcome);
            Assert.IsNotNull(verdict.Reason, "the floor is a real rule and deserves an explanation");
        }

        [Test]
        public void ExceedingThePerWireCap_IsRefused()
        {
            LevelVerdict verdict = Set(1, 4);

            Assert.AreEqual(LevelOutcome.DelayAtMaximum, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("3", verdict.Reason, "the reason should name the cap");
        }

        [Test]
        public void ALevelWithMaxWireDelayOfOne_HasFixedWiring()
        {
            // How a level says "no re-timing at all". A delayBudget of 0 cannot say it, because
            // JsonUtility yields 0 for a missing key and absent has to mean unlimited.
            LevelDefinition fixed_ = LevelTestFixtures.Parse(@"{
                ""name"": ""Fixed"",
                ""maxWireDelay"": 1,
                ""fixtures"": [
                    { ""id"": ""in"",  ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""1"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  3, ""y"": 0 } }
                ],
                ""budget"": [],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""1"" } ]
            }");

            LevelVerdict verdict = LevelRules.CanSetDelay(fixed_, _blueprint, RunState.Editing, 1, 2);

            Assert.AreEqual(LevelOutcome.DelayAtMaximum, verdict.Outcome);
            StringAssert.Contains("fixed", verdict.Reason, "a cap of 1 should not read as a number");
        }

        [Test]
        public void ExhaustingTheBudget_IsRefused()
        {
            // Spend both ticks on the one wire, then try to lengthen it again.
            _blueprint.SetDelayAt(0, 3);

            Assert.AreEqual(2, _blueprint.ExtraDelay(), "two ticks above the default");
            Assert.AreEqual(0, LevelRules.RemainingDelay(_capped, _blueprint), "nothing left");

            // A second wire, so the cap is not what refuses this.
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 1));

            LevelVerdict verdict = LevelRules.CanSetDelay(_capped, _blueprint, RunState.Editing, 1, 2);

            Assert.AreEqual(LevelOutcome.DelayBudgetSpent, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TheCapAndTheBudget_ReadDifferently()
        {
            // They need different reactions: one means "not on this wire", the other "take it off
            // another wire first".
            LevelVerdict cap = Set(1, 4);

            _blueprint.SetDelayAt(0, 3);
            LevelTestFixtures.Wire(_blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 1));
            LevelVerdict budget = LevelRules.CanSetDelay(_capped, _blueprint, RunState.Editing, 1, 2);

            Assert.AreNotEqual(cap.Outcome, budget.Outcome);
            Assert.AreNotEqual(cap.Reason, budget.Reason);
        }

        [Test]
        public void ShorteningAWire_IsAlwaysAllowedAndRefundsAtOnce()
        {
            // Budget is computed from the wires rather than tallied as it is spent, so experimenting
            // costs nothing: a wrong guess is undone by scrolling back down.
            _blueprint.SetDelayAt(0, 3);
            Assert.AreEqual(0, LevelRules.RemainingDelay(_capped, _blueprint));

            Assert.IsTrue(LevelRules.CanSetDelay(_capped, _blueprint, RunState.Editing, 3, 2).IsValid);

            _blueprint.SetDelayAt(0, 1);

            Assert.AreEqual(0, _blueprint.ExtraDelay(), "back to the default");
            Assert.AreEqual(2, LevelRules.RemainingDelay(_capped, _blueprint), "fully refunded");
        }

        [Test]
        public void ALevelWithNoDelayBudget_LeavesLengtheningUnrestricted()
        {
            // Safe because grading ignores arrival ticks -- a longer route cannot buy a wrong answer.
            Assert.IsFalse(_uncapped.HasDelayBudget);
            Assert.AreEqual(-1, LevelRules.RemainingDelay(_uncapped, _blueprint), "-1 means no limit");
            Assert.IsTrue(LevelRules.CanSetDelay(_uncapped, _blueprint, RunState.Editing, 1, 9).IsValid);
        }

        [Test]
        public void ADefaultCapStillApplies()
        {
            // Unlimited budget is not unlimited delay: the number is drawn on the wire and has to stay
            // one digit.
            Assert.AreEqual(LevelDefinition.DefaultMaxWireDelay, _uncapped.MaxWireDelay);

            Assert.AreEqual(LevelOutcome.DelayAtMaximum,
                LevelRules.CanSetDelay(_uncapped, _blueprint, RunState.Editing, 9, 10).Outcome);
        }

        [TestCase(RunState.Running)]
        [TestCase(RunState.Passed)]
        [TestCase(RunState.Failed)]
        public void ReTiming_IsRefusedWhileNotEditing(RunState state)
        {
            AssertRefused(Set(1, 2, state), LevelOutcome.NotEditing);
        }

        [Test]
        public void ScrollingWithNothingToChange_IsRefusedSilently()
        {
            // A scroll that lands on the value a wire already has should say nothing at all.
            LevelVerdict verdict = Set(2, 2);

            Assert.IsFalse(verdict.IsValid);
            Assert.IsNull(verdict.Reason);
        }

        // -----------------------------------------------------------------
        // Blueprint mechanics
        // -----------------------------------------------------------------

        [Test]
        public void ReTiming_LeavesWireOrderAndThereforeEdgeIdsUntouched()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(LevelTestFixtures.MiddleCell, GateKind.Not);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, LevelTestFixtures.MiddleCell);
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.MiddleCell, LevelTestFixtures.BinOneCell);

            BuiltCircuit before = CircuitBuilder.Build(_uncapped, blueprint);
            int firstFrom = before.Simulation.GetEdge(0).Source.Owner.Id;
            int secondFrom = before.Simulation.GetEdge(1).Source.Owner.Id;

            blueprint.SetDelayAt(0, 4);

            BuiltCircuit after = CircuitBuilder.Build(_uncapped, blueprint);

            Assert.AreEqual(2, blueprint.Wires.Count, "no wire was added or dropped");
            Assert.AreEqual(4, blueprint.Wires[0].Delay, "the first wire is the one that changed");
            Assert.AreEqual(before.Simulation.EdgeCount, after.Simulation.EdgeCount, "edge count");

            // Same ids, same wires, so a hover tracked by id survives the rebuild the scroll triggered.
            Assert.AreEqual(firstFrom, after.Simulation.GetEdge(0).Source.Owner.Id, "edge 0");
            Assert.AreEqual(secondFrom, after.Simulation.GetEdge(1).Source.Owner.Id, "edge 1");
            Assert.AreEqual(4, after.Simulation.GetEdge(0).Delay, "the delay reached the graph");
        }

        [Test]
        public void ExtraDelay_CountsOnlyWhatIsAboveTheDefault()
        {
            var blueprint = new CircuitBlueprint();
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 0));
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, 1));
            LevelTestFixtures.Wire(blueprint, LevelTestFixtures.SourceCell, new Vector2Int(3, -1));

            Assert.AreEqual(0, blueprint.ExtraDelay(), "three wires at the default cost nothing");

            blueprint.SetDelayAt(0, 3);
            blueprint.SetDelayAt(1, 2);

            Assert.AreEqual(3, blueprint.ExtraDelay(), "two above on one, one above on another");
        }

        [Test]
        public void SetDelayAt_RejectsAnIllegalDelay()
        {
            // The same floor Edge enforces, checked early so an illegal blueprint cannot exist even
            // briefly.
            Assert.Throws<ArgumentOutOfRangeException>(() => _blueprint.SetDelayAt(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _blueprint.SetDelayAt(9, 2));
        }

        [Test]
        public void IndexOfWire_FindsAWireRegardlessOfItsDelay()
        {
            // Deletion and re-timing both look a wire up by its two ends. Matching on delay as well
            // would break the moment a wire is re-timed.
            _blueprint.SetDelayAt(0, 3);

            var from = new CellPort(LevelTestFixtures.SourceCell, false, 0);
            var to = new CellPort(new Vector2Int(3, 0), true, 0);

            Assert.AreEqual(0, _blueprint.IndexOfWire(from, to));
            Assert.IsTrue(_blueprint.HasWire(from, to));
        }

        private static void AssertRefused(LevelVerdict verdict, LevelOutcome expected)
        {
            Assert.IsFalse(verdict.IsValid, "expected a refusal");
            Assert.AreEqual(expected, verdict.Outcome);
        }
    }
}
