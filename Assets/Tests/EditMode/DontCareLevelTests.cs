using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The four-input K-map with don't-cares. Ten of the sixteen combinations are tested; the other
    /// six never happen, and using them is what makes the circuit small: A + BC + BD.
    /// </summary>
    /// <remarks>
    /// The don't-cares are forced, not merely offered. The parts list is AND and OR only, and those
    /// build nothing but monotone functions -- turning an input from 0 to 1 can never turn the
    /// output from 1 to 0. Read with the six missing rows as 0s, the function is not monotone: 1000
    /// wants a 1 and 1010 would want a 0. So no circuit on this parts list computes the fully
    /// specified function, and every circuit that passes has quietly filled some of the six rows
    /// with 1s. The map is where the player sees why that is allowed.
    ///
    /// The ten rows are 0000 to 1001, in counting order, as the streams are read top to bottom.
    /// </remarks>
    public class DontCareLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceC = new Vector2Int(-4, -1);
        private static readonly Vector2Int SourceD = new Vector2Int(-4, -2);
        private static readonly Vector2Int OutCell = new Vector2Int(4, 0);

        private static readonly Vector2Int Upper = new Vector2Int(-2, 1);
        private static readonly Vector2Int Lower = new Vector2Int(-2, -1);
        private static readonly Vector2Int Middle = new Vector2Int(0, 0);
        private static readonly Vector2Int Join = new Vector2Int(2, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("dont-care", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped dont-care.json is invalid: {result.Error}");

            _level = result.Level;
        }

        // -----------------------------------------------------------------
        // Two answers
        // -----------------------------------------------------------------

        /// <summary>
        /// A + BC + BD, as a sum of products. Four gates, two ticks of delay.
        /// </summary>
        /// <remarks>
        /// Both products sit at level 1 and the OR joining them at level 2, so the last OR is level
        /// 3 and A, which goes straight there, waits two ticks on its wire to meet it.
        /// </remarks>
        internal static CircuitBlueprint SumOfProducts(int aDelay = 3)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Upper, GateKind.And);    // BC
            blueprint.Place(Lower, GateKind.And);    // BD
            blueprint.Place(Middle, GateKind.Or);    // BC + BD
            blueprint.Place(Join, GateKind.Or);      // A + that

            LevelTestFixtures.Wire(blueprint, SourceB, Upper, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceC, Upper, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceB, Lower, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceD, Lower, toPort: 1);

            LevelTestFixtures.Wire(blueprint, Upper, Middle, toPort: 0);
            LevelTestFixtures.Wire(blueprint, Lower, Middle, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceA, Join, toPort: 0, delay: aDelay);
            LevelTestFixtures.Wire(blueprint, Middle, Join, toPort: 1);

            LevelTestFixtures.Wire(blueprint, Join, OutCell);

            return blueprint;
        }

        /// <summary>
        /// A + B(C + D), factored. Three gates, three ticks of delay: B waits one tick for C + D,
        /// and A two for the AND.
        /// </summary>
        private static CircuitBlueprint Factored()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Lower, GateKind.Or);     // C + D
            blueprint.Place(Middle, GateKind.And);   // B(C + D)
            blueprint.Place(Join, GateKind.Or);      // A + that

            LevelTestFixtures.Wire(blueprint, SourceC, Lower, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceD, Lower, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceB, Middle, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, Lower, Middle, toPort: 1);

            LevelTestFixtures.Wire(blueprint, SourceA, Join, toPort: 0, delay: 3);
            LevelTestFixtures.Wire(blueprint, Middle, Join, toPort: 1);

            LevelTestFixtures.Wire(blueprint, Join, OutCell);

            return blueprint;
        }

        [Test]
        public void TheSumOfProducts_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, SumOfProducts());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheFactoredForm_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Factored());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void BothAnswers_FitThePartsListAndTheDelayBudget()
        {
            foreach (CircuitBlueprint answer in new[] { SumOfProducts(), Factored() })
            {
                Assert.LessOrEqual(answer.ExtraDelay(), _level.DelayBudget, "delay budget");
                Assert.LessOrEqual(answer.CountOf(GateKind.And), _level.BudgetFor(GateKind.And), "ANDs");
                Assert.LessOrEqual(answer.CountOf(GateKind.Or), _level.BudgetFor(GateKind.Or), "ORs");
            }
        }

        /// <summary>The trade Pick a lane teaches, arrived at by factoring: a gate saved costs a tick.</summary>
        [Test]
        public void TheFactoredForm_SavesAGate_AndSpendsDelay()
        {
            Assert.AreEqual(4, Gates(SumOfProducts()));
            Assert.AreEqual(3, Gates(Factored()));

            Assert.AreEqual(2, SumOfProducts().ExtraDelay());
            Assert.AreEqual(3, Factored().ExtraDelay());
        }

        private static int Gates(CircuitBlueprint blueprint) =>
            blueprint.CountOf(GateKind.And) + blueprint.CountOf(GateKind.Or);

        // -----------------------------------------------------------------
        // The mistakes it is built to catch
        // -----------------------------------------------------------------

        /// <summary>
        /// A + BC, which leaves out the BD group. Right on every row but 0101, the sixth.
        /// </summary>
        [Test]
        public void OneGroupShort_IsWrongOnTheRowItLeftOut()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(Upper, GateKind.And);
            blueprint.Place(Join, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceB, Upper, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceC, Upper, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA, Join, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, Upper, Join, toPort: 1);
            LevelTestFixtures.Wire(blueprint, Join, OutCell);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(5, verdict.Vector, "0101 is the sixth row");
            StringAssert.Contains("Row 6", verdict.Reason);
        }

        /// <summary>A left on a plain wire arrives two ticks before the products it joins.</summary>
        [Test]
        public void AnUnbalancedJoin_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, SumOfProducts(aDelay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA, SourceB, SourceC, SourceD })
            {
                var blueprint = new CircuitBlueprint();
                LevelTestFixtures.Wire(blueprint, source, OutCell);

                RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

                Assert.IsFalse(verdict.IsPass, $"a bare wire from {source} passed");
            }
        }

        // -----------------------------------------------------------------
        // The level itself
        // -----------------------------------------------------------------

        [Test]
        public void TheTenRows_Count0000To1001()
        {
            Assert.AreEqual(10, _level.VectorCount);

            var sources = new List<LevelFixture>();

            foreach (LevelFixture fixture in _level.Fixtures)
            {
                if (fixture.Kind == FixtureKind.Source)
                    sources.Add(fixture);
            }

            Assert.AreEqual(4, sources.Count);

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int row = 0;

                foreach (LevelFixture source in sources)
                    row = (row << 1) | (int)source.Stream[vector];

                Assert.AreEqual(vector, row, $"vector {vector} is not the row it should be");
            }
        }

        /// <summary>
        /// The claim in this class's remarks, checked: with the six missing rows read as 0s, the
        /// function is not monotone, so no circuit of ANDs and ORs builds it.
        /// </summary>
        [Test]
        public void TheFullySpecifiedFunction_IsOutOfReach_SoTheDontCaresAreForced()
        {
            Assert.AreEqual(0, _level.BudgetFor(GateKind.Not), "an inverter would make any function reachable");
            Assert.AreEqual(0, _level.BudgetFor(GateKind.Nand));
            Assert.AreEqual(0, _level.BudgetFor(GateKind.Nor));
            Assert.AreEqual(0, _level.BudgetFor(GateKind.Xor));

            string wanted = _level.Expectations[0].Values;

            int ValueAt(int row) => row < wanted.Length && wanted[row] == '1' ? 1 : 0;

            bool monotone = true;

            for (int low = 0; low < 16; low++)
            {
                for (int high = 0; high < 16; high++)
                {
                    bool below = (low & high) == low;   // every 1 in low is also in high

                    if (below && ValueAt(low) > ValueAt(high))
                        monotone = false;
                }
            }

            Assert.IsFalse(monotone,
                "with the missing rows as 0s the function is monotone, so AND and OR could build it " +
                "without the don't-cares, and the level no longer teaches them");
        }

        [Test]
        public void TheHint_NamesNoGate_AndDoesNotSayTheAnswer()
        {
            string hint = _level.Hint.ToLowerInvariant();

            foreach (string word in new[] { " and ", " or ", "don't care", "dont care" })
                StringAssert.DoesNotContain(word, " " + hint + " ", $"the hint says '{word.Trim()}'");
        }

        [Test]
        public void ItsMap_MarksTheSixRowsThatNeverHappen()
        {
            Assert.IsTrue(KarnaughMap.Applies(_level));

            string[] lines = KarnaughMap.Format(_level, "out").Split('\n');

            // Rows AB, columns CD in Gray order: the AB = 11 row, and the right half of AB = 10.
            Assert.AreEqual("11   x  x  x  x", lines[4]);
            Assert.AreEqual("10   1  1  x  x", lines[5]);
            StringAssert.DoesNotContain("x", lines[2] + lines[3], "rows 00xx and 01xx are all tested");
        }
    }
}
