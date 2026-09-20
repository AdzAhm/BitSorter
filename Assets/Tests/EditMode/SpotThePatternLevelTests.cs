using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A state machine that watches for 1-0-1, overlaps included.
    /// </summary>
    /// <remarks>
    /// Three states -- nothing yet, saw a 1, saw 1 then 0 -- and the parts list stocks two
    /// registers, which is the level's way of saying that three states fit in two bits. That is
    /// state minimisation stated as a budget rather than as a lecture.
    ///
    /// The surprise worth having is that the minimal encoding has **no feedback at all**. Taking
    /// "saw a 1" to mean the previous bit makes the low state bit a plain register on the input,
    /// and the machine collapses into two remembered bits and two gates. Players who draw the state
    /// diagram first tend to wire a loop they do not need.
    /// </remarks>
    public class SpotThePatternLevelTests
    {
        private static readonly Vector2Int SourceCell = new Vector2Int(-4, 0);
        private static readonly Vector2Int Previous = new Vector2Int(-2, 1);
        private static readonly Vector2Int NotCell = new Vector2Int(-2, -1);
        private static readonly Vector2Int SawOneThenZero = new Vector2Int(0, 0);
        private static readonly Vector2Int Older = new Vector2Int(1, 1);
        private static readonly Vector2Int Detect = new Vector2Int(2, -1);
        private static readonly Vector2Int OutCell = new Vector2Int(4, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("spot-the-pattern", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped spot-the-pattern.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The machine: one register keeps the previous bit, the other keeps "the one before that
        /// was a 1 and the previous one a 0", and the answer is that with the current bit.
        /// </summary>
        private static CircuitBlueprint Detector(int intoFirstAnd = 2, int straightToDetect = 3)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Previous, GateKind.Register);
            blueprint.Place(NotCell, GateKind.Not);
            blueprint.Place(SawOneThenZero, GateKind.And);
            blueprint.Place(Older, GateKind.Register);
            blueprint.Place(Detect, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceCell, Previous);
            LevelTestFixtures.Wire(blueprint, SourceCell, NotCell);
            LevelTestFixtures.Wire(blueprint, Previous, SawOneThenZero, toPort: 0, delay: intoFirstAnd);
            LevelTestFixtures.Wire(blueprint, NotCell, SawOneThenZero, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SawOneThenZero, Older);
            LevelTestFixtures.Wire(blueprint, Older, Detect, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceCell, Detect, toPort: 1, delay: straightToDetect);
            LevelTestFixtures.Wire(blueprint, Detect, OutCell);

            return blueprint;
        }

        [Test]
        public void TheDetector_Passes()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Detector());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void ItCatchesTheOverlap()
        {
            // The stream is built so the second match shares a bit with the first. A machine that
            // resets after a match instead of carrying the trailing 1 misses it, and the level's
            // expected values are what make that a failure rather than a matter of taste.
            Assert.AreEqual("0001010", Expected(_level));
        }

        [Test]
        public void WiringItWithoutThePadding_DestroysBits()
        {
            // Both registers hand their bits on a clock early, so the paths that skip them have to
            // be lengthened. The obvious wiring is a tick out in two places at once.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(
                _level, Detector(intoFirstAnd: 1, straightToDetect: 1));

            Assert.IsFalse(verdict.IsPass, "unpadded, the three paths cannot meet");
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void ItStocksTwoRegisters_ForThreeStates()
        {
            Assert.AreEqual(2, _level.BudgetFor(GateKind.Register));
        }

        [Test]
        public void ThePaddingBudgetIsExactlyWhatTheMachineNeeds()
        {
            Assert.AreEqual(3, _level.DelayBudget);
        }

        private static string Expected(LevelDefinition level)
        {
            var text = new System.Text.StringBuilder();

            foreach (ExpectedBit bit in level.Expectations[0].Expected)
                text.Append(bit.IsAny ? 'x' : (char)('0' + (int)bit.Value));

            return text.ToString();
        }
    }
}
