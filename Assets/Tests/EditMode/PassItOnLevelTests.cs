using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 2-bit ripple-carry adder: a half adder on the low column, a full adder on the high one,
    /// and the carry passed between them.
    /// </summary>
    /// <remarks>
    /// Seven gates, not eight: bit 0 has no carry in, so its column is a half adder. The critical
    /// path is the carry: the low column's AND, then the high column's AND and OR, which puts COUT
    /// a gate later than S1 and two later than S0 -- latencies 4, 3 and 2.
    ///
    /// The combinational counterpart of Add as you go, the serial adder at the end of the
    /// sequential chapter: the same carry, passed across the board rather than across a clock.
    ///
    /// The streams are listed A1, A0, B1, B0 -- the table's column order, so each row reads as
    /// A then B -- while on the board bit 0 sits at the top, beside the column that uses it.
    /// </remarks>
    public class PassItOnLevelTests
    {
        private static readonly Vector2Int SourceA0 = new Vector2Int(-4, 2);
        private static readonly Vector2Int SourceB0 = new Vector2Int(-4, 1);
        private static readonly Vector2Int SourceA1 = new Vector2Int(-4, -1);
        private static readonly Vector2Int SourceB1 = new Vector2Int(-4, -2);

        private static readonly Vector2Int BinS0 = new Vector2Int(4, 2);
        private static readonly Vector2Int BinS1 = new Vector2Int(4, 0);
        private static readonly Vector2Int BinCout = new Vector2Int(4, -2);

        private static readonly Vector2Int XorS0 = new Vector2Int(-2, 2);
        private static readonly Vector2Int AndC0 = new Vector2Int(-2, 1);
        private static readonly Vector2Int XorP1 = new Vector2Int(-2, -1);
        private static readonly Vector2Int AndG1 = new Vector2Int(-2, -2);
        private static readonly Vector2Int XorS1 = new Vector2Int(0, 0);
        private static readonly Vector2Int AndT = new Vector2Int(0, -1);
        private static readonly Vector2Int OrCout = new Vector2Int(2, -2);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("pass-it-on", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped pass-it-on.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// S0 = A0 xor B0, C0 = A0 B0; P1 = A1 xor B1, G1 = A1 B1; S1 = P1 xor C0; COUT = P1 C0 + G1.
        /// </summary>
        internal static CircuitBlueprint Adder(int generateDelay = 2, int coutWire = 1)
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(XorS0, GateKind.Xor);
            blueprint.Place(AndC0, GateKind.And);
            blueprint.Place(XorP1, GateKind.Xor);
            blueprint.Place(AndG1, GateKind.And);
            blueprint.Place(XorS1, GateKind.Xor);
            blueprint.Place(AndT, GateKind.And);
            blueprint.Place(OrCout, GateKind.Or);

            // The low column: a half adder.
            LevelTestFixtures.Wire(blueprint, SourceA0, XorS0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, XorS0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA0, AndC0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, AndC0, toPort: 1);

            // The high column's propagate and generate.
            LevelTestFixtures.Wire(blueprint, SourceA1, XorP1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, XorP1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA1, AndG1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, AndG1, toPort: 1);

            // The carry in: S1, and whether it passes on.
            LevelTestFixtures.Wire(blueprint, XorP1, XorS1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndC0, XorS1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP1, AndT, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndC0, AndT, toPort: 1);

            // G1 is a level early and waits a tick for the carry that passed.
            LevelTestFixtures.Wire(blueprint, AndT, OrCout, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndG1, OrCout, toPort: 1, delay: generateDelay);

            LevelTestFixtures.Wire(blueprint, XorS0, BinS0);
            LevelTestFixtures.Wire(blueprint, XorS1, BinS1);
            LevelTestFixtures.Wire(blueprint, OrCout, BinCout, delay: coutWire);

            return blueprint;
        }

        private int LatencyAt(CircuitBlueprint blueprint, string sink)
        {
            BuiltCircuit built = CircuitBuilder.Build(_level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, _level, built.FixtureNodeIds);

            var bin = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds[sink]);
            Assert.Greater(bin.Received.Count, 0, $"nothing reached {sink}");

            return bin.Received[0].Tick;
        }

        [Test]
        public void TheAdder_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheAdder_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint adder = Adder();

            Assert.LessOrEqual(adder.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.LessOrEqual(adder.CountOf(GateKind.Xor), _level.BudgetFor(GateKind.Xor), "XORs");
            Assert.LessOrEqual(adder.CountOf(GateKind.And), _level.BudgetFor(GateKind.And), "ANDs");
            Assert.LessOrEqual(adder.CountOf(GateKind.Or), _level.BudgetFor(GateKind.Or), "OR");
        }

        /// <summary>The carry is the critical path: COUT a gate after S1, S1 a gate after S0.</summary>
        [Test]
        public void TheCarry_IsTheCriticalPath()
        {
            Assert.AreEqual(2, LatencyAt(Adder(), "s0"), "S0");
            Assert.AreEqual(3, LatencyAt(Adder(), "s1"), "S1");
            Assert.AreEqual(4, LatencyAt(Adder(), "cout"), "COUT");
        }

        /// <summary>G1 on a plain wire reaches the OR a tick before the carry that passed.</summary>
        [Test]
        public void AGenerateThatDoesNotWait_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder(generateDelay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        /// <summary>
        /// A tick of padding on the way out is right and too slow -- and inside the delay budget, so
        /// it fails on time, not on budget.
        /// </summary>
        [Test]
        public void ALongerWayOut_IsRight_ButTooSlow()
        {
            CircuitBlueprint slow = Adder(coutWire: 2);
            Assert.LessOrEqual(slow.ExtraDelay(), _level.DelayBudget, "sanity: within the budget");

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, slow);

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("5 ticks", verdict.Reason);
            StringAssert.Contains("allows 4", verdict.Reason);
        }

        [Test]
        public void TheExpectations_AreASum()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int a = ((int)_level.FixtureById("a1").Stream[vector] << 1) | (int)_level.FixtureById("a0").Stream[vector];
                int b = ((int)_level.FixtureById("b1").Stream[vector] << 1) | (int)_level.FixtureById("b0").Stream[vector];
                int sum = a + b;

                Assert.AreEqual((char)('0' + ((sum >> 1) & 1)), _level.Expectations[0].Values[vector], $"S1, row {vector + 1}");
                Assert.AreEqual((char)('0' + (sum & 1)), _level.Expectations[1].Values[vector], $"S0, row {vector + 1}");
                Assert.AreEqual((char)('0' + (sum >> 2)), _level.Expectations[2].Values[vector], $"COUT, row {vector + 1}");
            }
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA0, SourceB0, SourceA1, SourceB1 })
            {
                foreach (Vector2Int bin in new[] { BinS0, BinS1, BinCout })
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

            foreach (string word in new[] { "xor", " and ", " or ", "ripple", "adder", "half", "full" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }
    }
}
