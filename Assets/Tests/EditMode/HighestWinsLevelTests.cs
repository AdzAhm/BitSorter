using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 4-to-2 priority encoder with a valid output: Y1 Y0 name the highest input that is on,
    /// and V says whether any is.
    /// </summary>
    /// <remarks>
    /// Y1 = I3 + I2, Y0 = I3 + I2'I1, V = I3 + I2 + I1 + I0.
    ///
    /// The first level whose expectations use 'x'. With every input off, V is 0 and the number on
    /// Y1 Y0 means nothing -- which is exactly a don't-care that matters to two bins and not the
    /// third, and why it is written as an 'x' there rather than left out of the streams: vectors are
    /// shared by every bin, and V still has to be graded on that row.
    ///
    /// Y0 is the lesson. "I1 is on" is not enough; it only counts while I2 above it is off. Ignoring
    /// that gives the plain OR of the odd lines, which is right on most rows and wrong on 0110 -- I2
    /// and I1 both on, where I2 wins.
    /// </remarks>
    public class HighestWinsLevelTests
    {
        private static readonly Vector2Int SourceI3 = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceI2 = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceI1 = new Vector2Int(-4, -1);
        private static readonly Vector2Int SourceI0 = new Vector2Int(-4, -2);

        private static readonly Vector2Int BinY1 = new Vector2Int(4, 2);
        private static readonly Vector2Int BinY0 = new Vector2Int(4, 0);
        private static readonly Vector2Int BinV = new Vector2Int(4, -2);

        private static readonly Vector2Int OrHigh = new Vector2Int(-2, 2);
        private static readonly Vector2Int NotI2 = new Vector2Int(-2, 0);
        private static readonly Vector2Int OrLow = new Vector2Int(-2, -2);
        private static readonly Vector2Int AndLow = new Vector2Int(0, 0);
        private static readonly Vector2Int OrY0 = new Vector2Int(2, 0);
        private static readonly Vector2Int OrV = new Vector2Int(1, -2);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("highest-wins", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped highest-wins.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// The encoder, with I1 made to wait for I2' and I3 made to wait for the AND.
        /// </summary>
        private static CircuitBlueprint Encoder(int i3IntoY0 = 3, bool respectPriority = true)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(OrHigh, GateKind.Or);   // I3 + I2: Y1, and half of V
            blueprint.Place(OrLow, GateKind.Or);    // I1 + I0: the other half of V
            blueprint.Place(OrV, GateKind.Or);
            blueprint.Place(OrY0, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceI3, OrHigh, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceI2, OrHigh, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceI1, OrLow, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceI0, OrLow, toPort: 1);

            LevelTestFixtures.Wire(blueprint, OrHigh, OrV, toPort: 0);
            LevelTestFixtures.Wire(blueprint, OrLow, OrV, toPort: 1);

            if (respectPriority)
            {
                // I2'I1: I1 only counts while I2 is quiet.
                blueprint.Place(NotI2, GateKind.Not);
                blueprint.Place(AndLow, GateKind.And);

                LevelTestFixtures.Wire(blueprint, SourceI2, NotI2);
                LevelTestFixtures.Wire(blueprint, NotI2, AndLow, toPort: 0);
                LevelTestFixtures.Wire(blueprint, SourceI1, AndLow, toPort: 1, delay: 2);

                LevelTestFixtures.Wire(blueprint, SourceI3, OrY0, toPort: 0, delay: i3IntoY0);
                LevelTestFixtures.Wire(blueprint, AndLow, OrY0, toPort: 1);
            }
            else
            {
                // I3 + I1, as though I1 counted whatever I2 was doing.
                LevelTestFixtures.Wire(blueprint, SourceI3, OrY0, toPort: 0);
                LevelTestFixtures.Wire(blueprint, SourceI1, OrY0, toPort: 1);
            }

            LevelTestFixtures.Wire(blueprint, OrHigh, BinY1);
            LevelTestFixtures.Wire(blueprint, OrY0, BinY0);
            LevelTestFixtures.Wire(blueprint, OrV, BinV);

            return blueprint;
        }

        [Test]
        public void TheEncoder_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Encoder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheEncoder_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint encoder = Encoder();

            Assert.LessOrEqual(encoder.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.LessOrEqual(encoder.CountOf(GateKind.Or), _level.BudgetFor(GateKind.Or), "ORs");
            Assert.LessOrEqual(encoder.CountOf(GateKind.Not), _level.BudgetFor(GateKind.Not), "NOT");
            Assert.LessOrEqual(encoder.CountOf(GateKind.And), _level.BudgetFor(GateKind.And), "AND");
        }

        /// <summary>
        /// Y0 = I3 + I1 forgets that I2 outranks I1, and says so on the one row where it matters:
        /// 0110, the seventh.
        /// </summary>
        [Test]
        public void IgnoringPriority_IsWrongWhereAHigherLineIsOn()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Encoder(respectPriority: false));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
            Assert.AreEqual(6, verdict.Vector, "0110 is the seventh row");
            Assert.AreEqual("y0", verdict.SinkId);
            StringAssert.Contains("Row 7", verdict.Reason);
            StringAssert.Contains("Y0 should get 0", verdict.Reason);
        }

        /// <summary>I3 on a plain wire reaches Y0's OR two ticks before I2'I1 does.</summary>
        [Test]
        public void AnUnbalancedY0_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Encoder(i3IntoY0: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        /// <summary>
        /// Every row's expectation is the encoder's truth table -- with Y1 Y0 free, and only free,
        /// on the row where nothing is on.
        /// </summary>
        [Test]
        public void TheExpectations_AreTheEncodersTable()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int i3 = (int)_level.FixtureById("i3").Stream[vector];
                int i2 = (int)_level.FixtureById("i2").Stream[vector];
                int i1 = (int)_level.FixtureById("i1").Stream[vector];
                int i0 = (int)_level.FixtureById("i0").Stream[vector];

                bool any = (i3 | i2 | i1 | i0) == 1;
                int highest = i3 == 1 ? 3 : i2 == 1 ? 2 : i1 == 1 ? 1 : 0;

                char y1 = any ? (char)('0' + (highest >> 1)) : 'x';
                char y0 = any ? (char)('0' + (highest & 1)) : 'x';

                Assert.AreEqual(y1, _level.Expectations[0].Values[vector], $"Y1, row {vector + 1}");
                Assert.AreEqual(y0, _level.Expectations[1].Values[vector], $"Y0, row {vector + 1}");
                Assert.AreEqual(any ? '1' : '0', _level.Expectations[2].Values[vector], $"V, row {vector + 1}");
            }
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceI3, SourceI2, SourceI1, SourceI0 })
            {
                foreach (Vector2Int bin in new[] { BinY1, BinY0, BinV })
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

            foreach (string word in new[] { " or ", " and ", " not ", "priority", "encoder", "highest" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }
    }
}
