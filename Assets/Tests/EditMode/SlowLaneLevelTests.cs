using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The delay-arithmetic level. A staircase where each stage takes a fresh literal off the left
    /// edge, so the padding a stage needs grows by one every time.
    /// </summary>
    /// <remarks>
    /// **This level is deliberately the exception to the two-solutions rule.** The topology is given
    /// by the expression and the delay assignment is forced by the topology, so there is exactly one
    /// right answer. That is the point: the skill being drilled is arithmetic, not synthesis. Widening
    /// it into a multi-solution level would hand the player a choice of shapes and let them dodge the
    /// sums, which is the whole lesson. If this file ever looks suspicious for having one solution,
    /// the suspicion is the bug -- see Docs/level-roadmap.md.
    /// </remarks>
    public class SlowLaneLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, 0);
        private static readonly Vector2Int SourceC = new Vector2Int(-4, -2);
        private static readonly Vector2Int OutCell = new Vector2Int(4, 0);

        private static readonly Vector2Int FirstXor = new Vector2Int(-2, 1);
        private static readonly Vector2Int SecondXor = new Vector2Int(0, 0);
        private static readonly Vector2Int AndCell = new Vector2Int(1, -1);
        private static readonly Vector2Int OrCell = new Vector2Int(2, 1);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("the-slow-lane", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped the-slow-lane.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// f = (((A XOR B) XOR C) AND A) OR B, with every wire delay exposed so a test can get one
        /// wrong on purpose.
        /// </summary>
        /// <remarks>
        /// The correct assignment is 2, 3, 4 -- each stage sits a level deeper, so the fresh literal
        /// it takes off the left edge has to wait one tick longer than the last one did. The staircase
        /// is the whole design: the arithmetic is not a single sum the player can guess.
        /// </remarks>
        private static CircuitBlueprint Wiring(
            int cToSecondXor = 2, int aToAnd = 3, int bToOr = 4, int firstXorToSecond = 1)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(FirstXor, GateKind.Xor);
            blueprint.Place(SecondXor, GateKind.Xor);
            blueprint.Place(AndCell, GateKind.And);
            blueprint.Place(OrCell, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceA, FirstXor, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, FirstXor, toPort: 1);

            LevelTestFixtures.Wire(blueprint, FirstXor, SecondXor, toPort: 0, delay: firstXorToSecond);
            LevelTestFixtures.Wire(blueprint, SourceC, SecondXor, toPort: 1, delay: cToSecondXor);

            LevelTestFixtures.Wire(blueprint, SecondXor, AndCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceA, AndCell, toPort: 1, delay: aToAnd);

            LevelTestFixtures.Wire(blueprint, AndCell, OrCell, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, OrCell, toPort: 1, delay: bToOr);

            LevelTestFixtures.Wire(blueprint, OrCell, OutCell);

            return blueprint;
        }

        // -----------------------------------------------------------------
        // The one right answer
        // -----------------------------------------------------------------

        [Test]
        public void TheBalancedWiring_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Wiring());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheSolution_SpendsTheBudgetExactly()
        {
            // Zero slack is the design. A wrong guess has to be taken back rather than absorbed,
            // which is what makes the player work the sums out instead of trying values.
            Assert.AreEqual(6, Wiring().ExtraDelay(), "one, two and three ticks of padding");
            Assert.AreEqual(6, _level.DelayBudget, "the budget is exactly the solution, with nothing over");
        }

        [Test]
        public void ThePaddingGrowsByOneAtEachStage()
        {
            // The staircase. If two stages ever needed the same padding, the player could guess one
            // number and apply it everywhere, and the level would stop being arithmetic.
            Assert.IsTrue(LevelTestFixtures.RunAndGrade(_level, Wiring(2, 3, 4)).IsPass,
                "2, 3, 4 is the assignment the topology forces");

            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring(2, 2, 2)).IsPass,
                "one number applied everywhere must not work");
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring(3, 3, 3)).IsPass);
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring(4, 4, 4)).IsPass);
        }

        [Test]
        public void EveryStageMustBeRight_NotJustTheLast()
        {
            // Each stage on its own, wrong by one, with the others correct. All three must fail, or
            // part of the arithmetic is decorative.
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring(cToSecondXor: 1)).IsPass,
                "the first stage");
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring(aToAnd: 2)).IsPass,
                "the second stage");
            Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, Wiring(bToOr: 3)).IsPass,
                "the third stage");
        }

        // -----------------------------------------------------------------
        // The mistakes
        // -----------------------------------------------------------------

        [Test]
        public void TheUnpaddedWiring_DestroysBits()
        {
            // What the player builds first: the expression wired straight through, every wire at one.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Wiring(1, 1, 1));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("destroyed", verdict.Reason);
        }

        [Test]
        public void PaddingTheLongPathInsteadOfTheShort_MakesItWorse()
        {
            // The intuitive-but-wrong fix, and the reason this level exists rather than another round
            // of balance-the-paths. Lengthening the side that already arrives late widens the gap and
            // spends budget doing it. You only ever lengthen the early side.
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(
                _level, Wiring(cToSecondXor: 1, firstXorToSecond: 2));

            Assert.IsFalse(verdict.IsPass);
            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void PaddingTheLongPath_AlsoWastesBudgetTheSolutionNeeds()
        {
            // With no slack, a tick spent in the wrong place is a tick the right place cannot have.
            CircuitBlueprint wrong = Wiring(cToSecondXor: 1, firstXorToSecond: 2);

            Assert.GreaterOrEqual(wrong.ExtraDelay(), 1, "the wrong fix still costs something");
            Assert.AreEqual(6, _level.DelayBudget,
                "and the budget has nothing spare to absorb it");
        }

        // -----------------------------------------------------------------
        // The level's shape
        // -----------------------------------------------------------------

        [Test]
        public void TheDeepestStage_NeedsTheLongestWireTheLevelAllows()
        {
            Assert.AreEqual(4, _level.MaxWireDelay,
                "the last stage needs four ticks; capping lower makes the level unsolvable");
        }

        [Test]
        public void TheBudgetIsExactlyTheStaircase()
        {
            Assert.AreEqual(2, _level.BudgetFor(GateKind.Xor));
            Assert.AreEqual(1, _level.BudgetFor(GateKind.And));
            Assert.AreEqual(1, _level.BudgetFor(GateKind.Or));

            // No spare gates. A fifth would let the player re-shape the expression and escape the sums.
            Assert.IsFalse(_level.Offers(GateKind.Not));
            Assert.IsFalse(_level.Offers(GateKind.Nand));
            Assert.IsFalse(_level.Offers(GateKind.Nor));
        }

        [Test]
        public void TheVectorsEnumerateAllThreeInputs()
        {
            Assert.AreEqual(8, _level.VectorCount);
            Assert.AreEqual("00111011", _level.Expectations[0].Values,
                "f = (((A XOR B) XOR C) AND A) OR B, in ABC order");
        }

        // -----------------------------------------------------------------
        // The hint
        // -----------------------------------------------------------------

        [Test]
        public void TheHint_ExplainsTheRuleWithoutDoingTheSum()
        {
            string hint = _level.Hint;

            Assert.IsNotEmpty(hint, "the level needs a hint");

            var words = new System.Collections.Generic.HashSet<string>();
            foreach (string word in hint.ToLowerInvariant().Split(
                         new[] { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'' },
                         System.StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(word);
            }

            foreach (string giveaway in
                     new[] { "xor", "and", "or", "delay", "lengthen", "longer", "pad", "balance" })
            {
                Assert.IsFalse(words.Contains(giveaway),
                    $"the hint should not use the word '{giveaway}'. Hint: {hint}");
            }

            foreach (char c in hint)
            {
                Assert.IsFalse(char.IsDigit(c),
                    $"the hint should name no number -- the numbers are the answer. Hint: {hint}");
            }
        }
    }
}
