using System.Text;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 3-bit ripple-carry adder: Pass it on with one more column, on a board wide enough for it.
    /// </summary>
    /// <remarks>
    /// Twelve gates: a half adder on bit 0 and a full adder on each of the two above it. The carry
    /// is the critical path and it gets longer at every column -- c0 at tick 1, c1 at 3 -- so
    /// whatever meets it has to wait: bit 2's propagate by two ticks and its generate by three,
    /// which is the wire of four the level allows. Latencies 2, 3, 5 and 6.
    ///
    /// Sixteen vectors of the sixty-four, chosen so every column sees every combination it can:
    /// all four of bit 0's, and all eight of (a, b, carry in) for each full adder. That is what
    /// makes a wrong gate anywhere visible -- each gate sees every input it can be given, and a
    /// wrong carry flips the sum above it. The sixteen also carry through both columns (3 + 1,
    /// 7 + 1, 7 + 7), use every value of A and of B, and set COUT on exactly half.
    ///
    /// The streams are listed A2 A1 A0, B2 B1 B0 -- the table's column order, so each row reads as
    /// A then B -- while on the board bit 0 sits at the top, beside the column that uses it.
    /// </remarks>
    public class CarryItFurtherLevelTests
    {
        private static readonly Vector2Int SourceA0 = new Vector2Int(-6, 3);
        private static readonly Vector2Int SourceB0 = new Vector2Int(-6, 2);
        private static readonly Vector2Int SourceA1 = new Vector2Int(-6, 1);
        private static readonly Vector2Int SourceB1 = new Vector2Int(-6, 0);
        private static readonly Vector2Int SourceA2 = new Vector2Int(-6, -1);
        private static readonly Vector2Int SourceB2 = new Vector2Int(-6, -2);

        private static readonly Vector2Int BinS0 = new Vector2Int(6, 3);
        private static readonly Vector2Int BinS1 = new Vector2Int(6, 1);
        private static readonly Vector2Int BinS2 = new Vector2Int(6, -1);
        private static readonly Vector2Int BinCout = new Vector2Int(6, -3);

        private static readonly Vector2Int XorS0 = new Vector2Int(-4, 3);
        private static readonly Vector2Int AndC0 = new Vector2Int(-4, 2);
        private static readonly Vector2Int XorP1 = new Vector2Int(-4, 1);
        private static readonly Vector2Int AndG1 = new Vector2Int(-4, 0);
        private static readonly Vector2Int XorP2 = new Vector2Int(-4, -1);
        private static readonly Vector2Int AndG2 = new Vector2Int(-4, -2);
        private static readonly Vector2Int XorS1 = new Vector2Int(-2, 2);
        private static readonly Vector2Int AndT1 = new Vector2Int(-2, 1);
        private static readonly Vector2Int OrC1 = new Vector2Int(0, 0);
        private static readonly Vector2Int XorS2 = new Vector2Int(2, -1);
        private static readonly Vector2Int AndT2 = new Vector2Int(2, -2);
        private static readonly Vector2Int OrCout = new Vector2Int(4, -3);

        private static readonly Vector2Int[] Xors = { XorS0, XorP1, XorS1, XorP2, XorS2 };

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("carry-it-further", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped carry-it-further.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// S0 = A0 xor B0, C0 = A0 B0; then for bit i: Pi = Ai xor Bi, Gi = Ai Bi, Si = Pi xor C,
        /// C' = Pi C + Gi.
        /// </summary>
        /// <param name="orAt">One of the XORs placed as an OR instead.</param>
        /// <param name="bit2TakesC0">Bit 2's carry in taken from bit 0, skipping bit 1.</param>
        /// <param name="generate2Delay">The wire bit 2's generate takes to the last OR.</param>
        /// <param name="coutWire">The wire into COUT's bin.</param>
        private static CircuitBlueprint Adder(
            Vector2Int? orAt = null, bool bit2TakesC0 = false, int generate2Delay = 4, int coutWire = 1)
        {
            var blueprint = new CircuitBlueprint();

            foreach (Vector2Int cell in Xors)
                blueprint.Place(cell, cell == orAt ? GateKind.Or : GateKind.Xor);

            blueprint.Place(AndC0, GateKind.And);
            blueprint.Place(AndG1, GateKind.And);
            blueprint.Place(AndT1, GateKind.And);
            blueprint.Place(AndG2, GateKind.And);
            blueprint.Place(AndT2, GateKind.And);
            blueprint.Place(OrC1, GateKind.Or);
            blueprint.Place(OrCout, GateKind.Or);

            // Bit 0: a half adder.
            LevelTestFixtures.Wire(blueprint, SourceA0, XorS0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, XorS0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA0, AndC0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, AndC0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorS0, BinS0);

            // Bit 1: the carry arrives with the propagate, and only the generate waits.
            LevelTestFixtures.Wire(blueprint, SourceA1, XorP1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, XorP1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA1, AndG1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, AndG1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP1, XorS1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndC0, XorS1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP1, AndT1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndC0, AndT1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, AndT1, OrC1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndG1, OrC1, toPort: 1, delay: 2);
            LevelTestFixtures.Wire(blueprint, XorS1, BinS1);

            // Bit 2: the carry is two ticks later than the propagate, three than the generate.
            Vector2Int carry = bit2TakesC0 ? AndC0 : OrC1;
            int carryDelay = bit2TakesC0 ? 3 : 1;

            LevelTestFixtures.Wire(blueprint, SourceA2, XorP2, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB2, XorP2, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA2, AndG2, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB2, AndG2, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP2, XorS2, toPort: 0, delay: 3);
            LevelTestFixtures.Wire(blueprint, carry, XorS2, toPort: 1, delay: carryDelay);
            LevelTestFixtures.Wire(blueprint, XorP2, AndT2, toPort: 0, delay: 3);
            LevelTestFixtures.Wire(blueprint, carry, AndT2, toPort: 1, delay: carryDelay);
            LevelTestFixtures.Wire(blueprint, AndT2, OrCout, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndG2, OrCout, toPort: 1, delay: generate2Delay);
            LevelTestFixtures.Wire(blueprint, XorS2, BinS2);
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

        private int ValueOf(string high, string middle, string low, int vector) =>
            ((int)_level.FixtureById(high).Stream[vector] << 2)
            | ((int)_level.FixtureById(middle).Stream[vector] << 1)
            | (int)_level.FixtureById(low).Stream[vector];

        // -----------------------------------------------------------------
        // The level
        // -----------------------------------------------------------------

        [Test]
        public void TheLevel_IsOnAThirteenBySevenBoard()
        {
            Assert.AreEqual(new Vector2Int(6, 3), _level.BoardHalfExtents);
        }

        [Test]
        public void TheExpectations_AreASum()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int sum = ValueOf("a2", "a1", "a0", vector) + ValueOf("b2", "b1", "b0", vector);

                Assert.AreEqual((char)('0' + ((sum >> 2) & 1)), _level.Expectations[0].Values[vector], $"S2, row {vector + 1}");
                Assert.AreEqual((char)('0' + ((sum >> 1) & 1)), _level.Expectations[1].Values[vector], $"S1, row {vector + 1}");
                Assert.AreEqual((char)('0' + (sum & 1)), _level.Expectations[2].Values[vector], $"S0, row {vector + 1}");
                Assert.AreEqual((char)('0' + (sum >> 3)), _level.Expectations[3].Values[vector], $"COUT, row {vector + 1}");
            }
        }

        /// <summary>
        /// Bit 0 sees all four of its inputs, and each full adder all eight of (a, b, carry in).
        /// </summary>
        [Test]
        public void EveryColumn_SeesEveryCombinationItCan()
        {
            var bit0 = new bool[4];
            var bit1 = new bool[8];
            var bit2 = new bool[8];

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int a = ValueOf("a2", "a1", "a0", vector);
                int b = ValueOf("b2", "b1", "b0", vector);

                int c0 = (a & b & 1);
                int c1 = ((a & 3) + (b & 3)) >> 2;

                bit0[((a & 1) << 1) | (b & 1)] = true;
                bit1[(((a >> 1) & 1) << 2) | (((b >> 1) & 1) << 1) | c0] = true;
                bit2[(((a >> 2) & 1) << 2) | (((b >> 2) & 1) << 1) | c1] = true;
            }

            for (int i = 0; i < 4; i++)
                Assert.IsTrue(bit0[i], $"bit 0 never sees a0 b0 = {i >> 1}{i & 1}");

            for (int i = 0; i < 8; i++)
            {
                Assert.IsTrue(bit1[i], $"bit 1 never sees a1 b1 c0 = {i >> 2}{(i >> 1) & 1}{i & 1}");
                Assert.IsTrue(bit2[i], $"bit 2 never sees a2 b2 c1 = {i >> 2}{(i >> 1) & 1}{i & 1}");
            }
        }

        /// <summary>The carry has to go the whole way: 3 + 1, 7 + 1 and 7 + 7 are all asked.</summary>
        [Test]
        public void TheCarry_IsAskedToRippleAllTheWay()
        {
            foreach ((int a, int b) in new[] { (3, 1), (7, 1), (7, 7) })
            {
                bool asked = false;

                for (int vector = 0; vector < _level.VectorCount; vector++)
                    asked |= ValueOf("a2", "a1", "a0", vector) == a && ValueOf("b2", "b1", "b0", vector) == b;

                Assert.IsTrue(asked, $"{a} + {b} is never asked");
            }
        }

        // -----------------------------------------------------------------
        // The adder
        // -----------------------------------------------------------------

        [Test]
        public void TheAdder_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        /// <summary>
        /// The same circuit on all sixty-four sums, on a copy of the level that asks every one --
        /// the sixteen are a sample of a function the adder gets right everywhere.
        /// </summary>
        [Test]
        public void TheAdder_IsRightOnAllSixtyFourSums()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(EverySum(), Adder());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheAdder_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint adder = Adder();

            Assert.AreEqual(8, adder.ExtraDelay(), "the adder's delay, which the budget is set from");
            Assert.LessOrEqual(adder.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.AreEqual(_level.BudgetFor(GateKind.Xor), adder.CountOf(GateKind.Xor), "XORs");
            Assert.AreEqual(_level.BudgetFor(GateKind.And), adder.CountOf(GateKind.And), "ANDs");
            Assert.AreEqual(_level.BudgetFor(GateKind.Or), adder.CountOf(GateKind.Or), "ORs");

            int longest = 0;
            foreach (BlueprintWire wire in adder.Wires)
                longest = Mathf.Max(longest, wire.Delay);

            Assert.AreEqual(_level.MaxWireDelay, longest, "the longest wire the adder needs is the level's cap");
        }

        /// <summary>The carry is the critical path, and it lengthens at every column.</summary>
        [Test]
        public void TheCarry_IsTheCriticalPath()
        {
            Assert.AreEqual(2, LatencyAt(Adder(), "s0"), "S0");
            Assert.AreEqual(3, LatencyAt(Adder(), "s1"), "S1");
            Assert.AreEqual(5, LatencyAt(Adder(), "s2"), "S2");
            Assert.AreEqual(6, LatencyAt(Adder(), "cout"), "COUT");
            Assert.AreEqual(6, _level.MaxLatency);
        }

        // -----------------------------------------------------------------
        // The wrong circuits the sixteen still catch
        // -----------------------------------------------------------------

        /// <summary>
        /// An OR where any of the five XORs should be. Timing is the adder's, so each fails on its
        /// answers.
        /// </summary>
        [Test]
        public void AnOrForAnyXor_GivesAWrongSum()
        {
            foreach (Vector2Int cell in Xors)
            {
                RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder(orAt: cell));

                Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, $"an OR at {cell}: {verdict}");
            }
        }

        /// <summary>Bit 2 given bit 0's carry, skipping bit 1's, and timed to meet it.</summary>
        [Test]
        public void ACarryIntoTheWrongBit_GivesAWrongSum()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder(bit2TakesC0: true));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>Bit 1 as a half adder: c0 never used, and its own generate passed up as the carry.</summary>
        [Test]
        public void AMissingCarryIntoBit1_GivesAWrongSum()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(XorS0, GateKind.Xor);
            blueprint.Place(XorP1, GateKind.Xor);
            blueprint.Place(AndG1, GateKind.And);
            blueprint.Place(XorP2, GateKind.Xor);
            blueprint.Place(AndG2, GateKind.And);
            blueprint.Place(XorS2, GateKind.Xor);
            blueprint.Place(AndT2, GateKind.And);
            blueprint.Place(OrCout, GateKind.Or);

            LevelTestFixtures.Wire(blueprint, SourceA0, XorS0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, XorS0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorS0, BinS0);

            LevelTestFixtures.Wire(blueprint, SourceA1, XorP1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, XorP1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA1, AndG1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, AndG1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP1, BinS1, delay: 2);

            LevelTestFixtures.Wire(blueprint, SourceA2, XorP2, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB2, XorP2, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA2, AndG2, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB2, AndG2, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP2, XorS2, toPort: 0, delay: 3);
            LevelTestFixtures.Wire(blueprint, AndG1, XorS2, toPort: 1, delay: 3);
            LevelTestFixtures.Wire(blueprint, XorP2, AndT2, toPort: 0, delay: 3);
            LevelTestFixtures.Wire(blueprint, AndG1, AndT2, toPort: 1, delay: 3);
            LevelTestFixtures.Wire(blueprint, AndT2, OrCout, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndG2, OrCout, toPort: 1, delay: 4);
            LevelTestFixtures.Wire(blueprint, XorS2, BinS2);
            LevelTestFixtures.Wire(blueprint, OrCout, BinCout);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>Bit 2 as a half adder: bits 0 and 1 right, their carry dropped at the top.</summary>
        [Test]
        public void AMissingCarryIntoBit2_GivesAWrongSum()
        {
            var blueprint = new CircuitBlueprint();
            blueprint.Place(XorS0, GateKind.Xor);
            blueprint.Place(AndC0, GateKind.And);
            blueprint.Place(XorP1, GateKind.Xor);
            blueprint.Place(XorS1, GateKind.Xor);
            blueprint.Place(XorP2, GateKind.Xor);
            blueprint.Place(AndG2, GateKind.And);

            LevelTestFixtures.Wire(blueprint, SourceA0, XorS0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, XorS0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA0, AndC0, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB0, AndC0, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorS0, BinS0);

            LevelTestFixtures.Wire(blueprint, SourceA1, XorP1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB1, XorP1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP1, XorS1, toPort: 0);
            LevelTestFixtures.Wire(blueprint, AndC0, XorS1, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorS1, BinS1);

            LevelTestFixtures.Wire(blueprint, SourceA2, XorP2, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB2, XorP2, toPort: 1);
            LevelTestFixtures.Wire(blueprint, SourceA2, AndG2, toPort: 0);
            LevelTestFixtures.Wire(blueprint, SourceB2, AndG2, toPort: 1);
            LevelTestFixtures.Wire(blueprint, XorP2, BinS2);
            LevelTestFixtures.Wire(blueprint, AndG2, BinCout);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, blueprint);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>Bit 2's generate on a plain wire reaches the last OR before the carry that passed.</summary>
        [Test]
        public void AGenerateThatDoesNotWait_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Adder(generate2Delay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        /// <summary>A tick of padding into COUT's bin is right, inside the budget, and too slow.</summary>
        [Test]
        public void ALongerWayOut_IsRight_ButTooSlow()
        {
            CircuitBlueprint slow = Adder(coutWire: 2);
            Assert.LessOrEqual(slow.ExtraDelay(), _level.DelayBudget, "sanity: within the budget");

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, slow);

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("7 ticks", verdict.Reason);
            StringAssert.Contains("allows 6", verdict.Reason);
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA0, SourceB0, SourceA1, SourceB1, SourceA2, SourceB2 })
            {
                foreach (Vector2Int bin in new[] { BinS0, BinS1, BinS2, BinCout })
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

        // -----------------------------------------------------------------
        // Every sum
        // -----------------------------------------------------------------

        /// <summary>The shipped level's fixtures, asked all sixty-four sums in order.</summary>
        private static LevelDefinition EverySum()
        {
            var a = new StringBuilder[3];
            var b = new StringBuilder[3];
            var sum = new StringBuilder[4];

            for (int i = 0; i < 3; i++)
            {
                a[i] = new StringBuilder();
                b[i] = new StringBuilder();
            }

            for (int i = 0; i < 4; i++)
                sum[i] = new StringBuilder();

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int bit = 0; bit < 3; bit++)
                    {
                        a[bit].Append((x >> bit) & 1);
                        b[bit].Append((y >> bit) & 1);
                    }

                    for (int bit = 0; bit < 4; bit++)
                        sum[bit].Append(((x + y) >> bit) & 1);
                }
            }

            return LevelTestFixtures.Parse($@"{{
                ""name"": ""Every sum"", ""tickLimit"": 100, ""maxLatency"": 6,
                ""board"": {{ ""columns"": 13, ""rows"": 7 }},
                ""fixtures"": [
                    {{ ""id"": ""a2"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"": -1 }}, ""stream"": ""{a[2]}"" }},
                    {{ ""id"": ""a1"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"":  1 }}, ""stream"": ""{a[1]}"" }},
                    {{ ""id"": ""a0"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"":  3 }}, ""stream"": ""{a[0]}"" }},
                    {{ ""id"": ""b2"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"": -2 }}, ""stream"": ""{b[2]}"" }},
                    {{ ""id"": ""b1"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"":  0 }}, ""stream"": ""{b[1]}"" }},
                    {{ ""id"": ""b0"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"":  2 }}, ""stream"": ""{b[0]}"" }},
                    {{ ""id"": ""s2"",   ""kind"": ""Sink"",   ""cell"": {{ ""x"":  6, ""y"": -1 }} }},
                    {{ ""id"": ""s1"",   ""kind"": ""Sink"",   ""cell"": {{ ""x"":  6, ""y"":  1 }} }},
                    {{ ""id"": ""s0"",   ""kind"": ""Sink"",   ""cell"": {{ ""x"":  6, ""y"":  3 }} }},
                    {{ ""id"": ""cout"", ""kind"": ""Sink"",   ""cell"": {{ ""x"":  6, ""y"": -3 }} }}
                ],
                ""budget"": [ {{ ""kind"": ""Xor"", ""count"": 5 }}, {{ ""kind"": ""And"", ""count"": 5 }}, {{ ""kind"": ""Or"", ""count"": 2 }} ],
                ""expected"": [
                    {{ ""sink"": ""s2"",   ""values"": ""{sum[2]}"" }},
                    {{ ""sink"": ""s1"",   ""values"": ""{sum[1]}"" }},
                    {{ ""sink"": ""s0"",   ""values"": ""{sum[0]}"" }},
                    {{ ""sink"": ""cout"", ""values"": ""{sum[3]}"" }}
                ]
            }}");
        }
    }
}
