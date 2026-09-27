using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 2-to-4 decoder. Each bin is one row of the table -- one minterm -- and lights on that row
    /// alone.
    /// </summary>
    /// <remarks>
    /// Three of the four minterms need an inverted input, and the inverters put those at level 2
    /// while a bare input is at level 1. So each AND that mixes a bare input with an inverted one
    /// has to hold the bare one back a tick: the same timing lesson as every level since the delay
    /// tutorial, met four times over in one small circuit, and not at all in the AND of two bare
    /// inputs.
    /// </remarks>
    public class OneOfFourLevelTests
    {
        private static readonly Vector2Int SourceA = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceB = new Vector2Int(-4, -1);

        private static readonly Vector2Int BinD0 = new Vector2Int(4, 2);
        private static readonly Vector2Int BinD1 = new Vector2Int(4, 1);
        private static readonly Vector2Int BinD2 = new Vector2Int(4, -1);
        private static readonly Vector2Int BinD3 = new Vector2Int(4, -2);

        private static readonly Vector2Int NotA = new Vector2Int(-2, 1);
        private static readonly Vector2Int NotB = new Vector2Int(-2, -1);

        private static readonly Vector2Int And0 = new Vector2Int(1, 2);
        private static readonly Vector2Int And1 = new Vector2Int(1, 1);
        private static readonly Vector2Int And2 = new Vector2Int(1, -1);
        private static readonly Vector2Int And3 = new Vector2Int(1, -2);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("one-of-four", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped one-of-four.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// D0 = A'B', D1 = A'B, D2 = AB', D3 = AB. The inverters are shared, one per input.
        /// </summary>
        private static CircuitBlueprint Decoder(int bareBIntoD1 = 2)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(NotA, GateKind.Not);
            blueprint.Place(NotB, GateKind.Not);
            blueprint.Place(And0, GateKind.And);
            blueprint.Place(And1, GateKind.And);
            blueprint.Place(And2, GateKind.And);
            blueprint.Place(And3, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceA, NotA);
            LevelTestFixtures.Wire(blueprint, SourceB, NotB);

            // A'B': both from inverters, so both at level 2 already.
            LevelTestFixtures.Wire(blueprint, NotA, And0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, NotB, And0, toPort: 1);

            // A'B: bare B waits a tick for A'.
            LevelTestFixtures.Wire(blueprint, NotA, And1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, And1, toPort: 1, delay: bareBIntoD1);

            // AB': bare A waits a tick for B'.
            LevelTestFixtures.Wire(blueprint, SourceA, And2, toPort: 0, delay: 2);
            LevelTestFixtures.Wire(blueprint, NotB, And2, toPort: 1);

            // AB: two bare inputs, both at level 1, and nothing to wait for.
            LevelTestFixtures.Wire(blueprint, SourceA, And3, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB, And3, toPort: 1);

            LevelTestFixtures.Wire(blueprint, And0, BinD0);
            LevelTestFixtures.Wire(blueprint, And1, BinD1);
            LevelTestFixtures.Wire(blueprint, And2, BinD2);
            LevelTestFixtures.Wire(blueprint, And3, BinD3);

            return blueprint;
        }

        [Test]
        public void TheDecoder_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Decoder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheDecoder_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint decoder = Decoder();

            Assert.LessOrEqual(decoder.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.LessOrEqual(decoder.CountOf(GateKind.Not), _level.BudgetFor(GateKind.Not), "NOTs");
            Assert.LessOrEqual(decoder.CountOf(GateKind.And), _level.BudgetFor(GateKind.And), "ANDs");
        }

        /// <summary>Bare B on a plain wire reaches D1's AND a tick before A' does.</summary>
        [Test]
        public void ABareInputThatDoesNotWait_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Decoder(bareBIntoD1: 1));

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

        /// <summary>Each bin is the minterm its name counts to: D<i>k</i> lights on the row A B = <i>k</i>.</summary>
        [Test]
        public void EachBin_LightsOnTheRowItsNumberNames()
        {
            LevelFixture a = _level.FixtureById("a");
            LevelFixture b = _level.FixtureById("b");

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int row = ((int)a.Stream[vector] << 1) | (int)b.Stream[vector];

                foreach (LevelExpectation expectation in _level.Expectations)
                {
                    int number = expectation.SinkId[1] - '0';
                    char wanted = number == row ? '1' : '0';

                    Assert.AreEqual(wanted, expectation.Values[vector],
                        $"{expectation.SinkId} on A B = {row / 2}{row % 2}");
                }
            }
        }

        [Test]
        public void ABareWire_FromEitherSource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA, SourceB })
            {
                foreach (Vector2Int bin in new[] { BinD0, BinD1, BinD2, BinD3 })
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

            foreach (string word in new[] { " not ", " and ", "invert", "decoder", "minterm" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }
    }
}
