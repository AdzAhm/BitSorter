using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 1-bit magnitude comparator: LT, EQ and GT, exactly one of them lit on every row.
    /// </summary>
    /// <remarks>
    /// LT is A'B and GT is AB', each read straight off the inputs. EQ is the one that teaches: the
    /// parts list has no XOR, XNOR or OR, so equality cannot be built from A and B directly with
    /// what is stocked -- but it is "neither less nor greater", which is a NOR of the other two
    /// outputs. One gate's output feeding both a bin and another gate is the point.
    ///
    /// Timing rides along as it does in the decoder: the inverted input sits a level deeper than
    /// the bare one, so each AND holds its bare input back a tick.
    /// </remarks>
    public class WhichIsBiggerLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, -1);

        private static readonly Vector2Int BinLt = new Vector2Int(4, 2);
        private static readonly Vector2Int BinEq = new Vector2Int(4, 0);
        private static readonly Vector2Int BinGt = new Vector2Int(4, -2);

        private static readonly Vector2Int NotA = new Vector2Int(-2, 1);
        private static readonly Vector2Int NotB = new Vector2Int(-2, -1);
        private static readonly Vector2Int AndLt = new Vector2Int(0, 1);
        private static readonly Vector2Int AndGt = new Vector2Int(0, -1);
        private static readonly Vector2Int NorEq = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("which-is-bigger", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped which-is-bigger.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>LT = A'B, GT = AB', EQ = NOR(LT, GT).</summary>
        internal static CircuitBlueprint Comparator(int bareBIntoLt = 2)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(NotA, GateKind.Not);
            blueprint.Place(NotB, GateKind.Not);
            blueprint.Place(AndLt, GateKind.And);
            blueprint.Place(AndGt, GateKind.And);
            blueprint.Place(NorEq, GateKind.Nor);

            LevelTestFixtures.Wire(blueprint, SourceA, NotA);
            LevelTestFixtures.Wire(blueprint, SourceB, NotB);

            // A'B: bare B waits a tick for A'.
            LevelTestFixtures.Wire(blueprint, NotA, AndLt, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, AndLt, toPort: 1, delay: bareBIntoLt);

            // AB': bare A waits a tick for B'.
            LevelTestFixtures.Wire(blueprint, SourceA, AndGt, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, NotB, AndGt, toPort: 1);

            // Neither less nor greater.
            LevelTestFixtures.Wire(blueprint, AndLt, NorEq, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndGt, NorEq, toPort: 1);

            LevelTestFixtures.Wire(blueprint, AndLt, BinLt);
            LevelTestFixtures.Wire(blueprint, NorEq, BinEq);
            LevelTestFixtures.Wire(blueprint, AndGt, BinGt);

            return blueprint;
        }

        [Test]
        public void TheComparator_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Comparator());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheComparator_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint comparator = Comparator();

            Assert.LessOrEqual(comparator.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.LessOrEqual(comparator.CountOf(GateKind.Not), _level.BudgetFor(GateKind.Not), "NOTs");
            Assert.LessOrEqual(comparator.CountOf(GateKind.And), _level.BudgetFor(GateKind.And), "ANDs");
            Assert.LessOrEqual(comparator.CountOf(GateKind.Nor), _level.BudgetFor(GateKind.Nor), "NOR");
        }

        /// <summary>Bare B on a plain wire reaches LT's AND a tick before A' does.</summary>
        [Test]
        public void ABareInputThatDoesNotWait_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Comparator(bareBIntoLt: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void EveryRow_LightsExactlyOneBin()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int lit = 0;

                foreach (LevelExpectation expectation in _level.Expectations)
                {
                    if (expectation.Values[vector] == '1')
                        lit++;
                }

                Assert.AreEqual(1, lit, $"row {vector + 1} lights {lit} bins");
            }
        }

        /// <summary>The bins say what their names say, row by row.</summary>
        [Test]
        public void TheBins_CompareAWithB()
        {
            LevelFixture a = _level.FixtureById("a");
            LevelFixture b = _level.FixtureById("b");

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int av = (int)a.Stream[vector];
                int bv = (int)b.Stream[vector];

                Assert.AreEqual(av < bv ? '1' : '0', _level.Expectations[0].Values[vector], "LT");
                Assert.AreEqual(av == bv ? '1' : '0', _level.Expectations[1].Values[vector], "EQ");
                Assert.AreEqual(av > bv ? '1' : '0', _level.Expectations[2].Values[vector], "GT");
            }
        }

        /// <summary>
        /// Nothing in the parts list computes equality from A and B in one gate, so EQ has to come
        /// from the other two outputs.
        /// </summary>
        [Test]
        public void EqualityHasToComeFromTheOtherTwo()
        {
            foreach (GateKind kind in new[] { GateKind.Xor, GateKind.Or, GateKind.Nand })
                Assert.AreEqual(0, _level.BudgetFor(kind), $"{kind} would give EQ another route");
        }

        [Test]
        public void ABareWire_FromEitherSource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA, SourceB })
            {
                foreach (Vector2Int bin in new[] { BinLt, BinEq, BinGt })
                {
                    var blueprint = new CircuitBlueprint();
                    LevelTestFixtures.Wire(blueprint, source, bin);

                    Assert.IsFalse(LevelTestFixtures.RunAndGrade(_level, blueprint).IsPass,
                        $"a bare wire from {source} to {bin} passed");
                }
            }
        }

        [Test]
        public void TheHint_SaysNothingOfHowItIsBuilt()
        {
            string hint = " " + _level.Hint.ToLowerInvariant() + " ";

            foreach (string word in new[] { " nor ", " not ", " and ", "neither", "invert" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }
    }
}
