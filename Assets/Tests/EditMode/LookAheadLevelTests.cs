using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using static BitSorter.LogicCore.Tests.FourAtOnceLevelTests;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The same 4-bit adder as Four at once, in two ticks less than its ripple can manage: the carry
    /// into the top half, and the carry out of it, worked out without waiting for the columns below.
    /// </summary>
    /// <remarks>
    /// **LA is lookahead over two columns**, five inputs (two bits of A and B, and the carry into
    /// them) and one output, the carry out of both: c2 = g1 + p1 g0 + p1 p0 c0, built as the group's
    /// generate G = g1 + p1 g0 and propagate P = p1 p0 and then G + P c0. Nine gates. Like FA it
    /// takes its carry a tick after its other inputs -- the carry meets P a gate later than the bits
    /// meet anything, and the wire from it is a tick long, so a carry arriving with the bits would
    /// wait a tick -- and that is the one rule the goal gives for both blocks.
    ///
    /// **The reference is a carry-lookahead over two groups of two**, ripple inside each group:
    /// FA0 and FA1 ripple as before, LA_low hands bit 2 its carry at tick 4 rather than 5, FA2 and
    /// FA3 ripple from there, and LA_high takes that carry and two more columns to COUT. Latencies
    /// 3, 5, 6 and 8, and 8 for COUT, against the ripple's 9 and 10, so a limit of 8 refuses the
    /// ripple by a tick on S3 and two on COUT. One LA is not enough: with only LA_low, COUT still
    /// ripples through FA2 and FA3 and arrives at 9.
    ///
    /// **Why blocks, and not gates and P/G blocks as Part E's plan first said.** By hand the same
    /// lookahead is 26 gates, 22 parts and about fifty-six wires, every one of them balanced
    /// exactly because there is no clock before the register's chapter. In blocks it is six parts
    /// and twenty-seven wires, and the puzzle is the one this level is about: where the carry's
    /// path can be cut, and what that does to the latency. The gates are still inside the boxes,
    /// and still score: 38, against the ripple's 20.
    ///
    /// Sixteen vectors, chosen as Four at once's were, and also so that each two-column group carries
    /// for each of its reasons -- its upper bit generating, its lower bit generating with the upper
    /// passing it on, both passing on the carry in -- and fails to carry.
    /// </remarks>
    public class LookAheadLevelTests
    {
        /// <summary>LA's ports, in the order its faces show them.</summary>
        private const int LaA0 = 0;
        private const int LaB0 = 1;
        private const int LaCarryIn = 2;
        private const int LaA1 = 3;
        private const int LaB1 = 4;
        private const int LaCarryOut = 0;

        private static readonly Vector2Int LookaheadLow = new Vector2Int(-1, 3);
        private static readonly Vector2Int LookaheadHigh = new Vector2Int(1, 0);

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("look-ahead", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped look-ahead.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// Two groups of two: each FA's sum as in Four at once, bit 2's carry from LA_low and COUT
        /// from LA_high.
        /// </summary>
        /// <param name="level">The level whose blocks to place.</param>
        /// <param name="highLookahead">Whether COUT comes from LA_high, or rippled out of FA3.</param>
        /// <param name="highCarryFromCin">LA_high given CIN, timed to meet it, in place of LA_low's carry.</param>
        private static CircuitBlueprint Lookahead(
            LevelDefinition level, bool highLookahead = true, bool highCarryFromCin = false)
        {
            var board = new CircuitBlueprint();
            BlockDefinition fa = level.BlockNamed("FA");
            BlockDefinition la = level.BlockNamed("LA");

            foreach (Vector2Int at in Adders)
                board.PlaceBlock(at, fa);

            board.PlaceBlock(LookaheadLow, la);

            // Bit 0 and bit 1 ripple: A and B at 1 and 3, CIN a tick after bit 0's.
            int[] bitDelay = { 1, 3, 4, 6 };

            for (int bit = 0; bit < 4; bit++)
            {
                LevelTestFixtures.Wire(board, SourcesA[bit], Adders[bit], toPort: PortA, delay: bitDelay[bit]);
                LevelTestFixtures.Wire(board, SourcesB[bit], Adders[bit], toPort: PortB, delay: bitDelay[bit]);
                LevelTestFixtures.Wire(board, Adders[bit], BinsS[bit], fromPort: PortSum);
            }

            LevelTestFixtures.Wire(board, SourceCin, Adders[0], toPort: PortCarryIn, delay: 2);
            LevelTestFixtures.Wire(board, Adders[0], Adders[1], fromPort: PortCarryOut, toPort: PortCarryIn);

            // The low group's lookahead: its bits straight in, CIN a tick later, as FA0's.
            LevelTestFixtures.Wire(board, SourceA0, LookaheadLow, toPort: LaA0);
            LevelTestFixtures.Wire(board, SourceB0, LookaheadLow, toPort: LaB0);
            LevelTestFixtures.Wire(board, SourceCin, LookaheadLow, toPort: LaCarryIn, delay: 2);
            LevelTestFixtures.Wire(board, SourceA1, LookaheadLow, toPort: LaA1);
            LevelTestFixtures.Wire(board, SourceB1, LookaheadLow, toPort: LaB1);

            // Bit 2 takes its carry from the lookahead, at 4 where FA1's would come at 5.
            LevelTestFixtures.Wire(board, LookaheadLow, Adders[2], fromPort: LaCarryOut, toPort: PortCarryIn);
            LevelTestFixtures.Wire(board, Adders[2], Adders[3], fromPort: PortCarryOut, toPort: PortCarryIn);

            if (!highLookahead)
            {
                LevelTestFixtures.Wire(board, Adders[3], BinCout, fromPort: PortCarryOut);
                return board;
            }

            board.PlaceBlock(LookaheadHigh, la);

            // The high group's bits wait for the carry into them, which comes out of LA_low at 4.
            LevelTestFixtures.Wire(board, SourceA2, LookaheadHigh, toPort: LaA0, delay: 4);
            LevelTestFixtures.Wire(board, SourceB2, LookaheadHigh, toPort: LaB0, delay: 4);
            LevelTestFixtures.Wire(board, SourceA3, LookaheadHigh, toPort: LaA1, delay: 4);
            LevelTestFixtures.Wire(board, SourceB3, LookaheadHigh, toPort: LaB1, delay: 4);

            if (highCarryFromCin)
                LevelTestFixtures.Wire(board, SourceCin, LookaheadHigh, toPort: LaCarryIn, delay: 5);
            else
                LevelTestFixtures.Wire(board, LookaheadLow, LookaheadHigh, fromPort: LaCarryOut, toPort: LaCarryIn);

            LevelTestFixtures.Wire(board, LookaheadHigh, BinCout, fromPort: LaCarryOut);
            return board;
        }

        private CircuitBlueprint Lookahead(bool highLookahead = true, bool highCarryFromCin = false) =>
            Lookahead(_level, highLookahead, highCarryFromCin);

        // -----------------------------------------------------------------
        // The level
        // -----------------------------------------------------------------

        [Test]
        public void TheLevel_IsFourAtOncesBoard_WithTheSameFixturesAndTheSameFA()
        {
            LevelDefinition before = LevelLoader.Load("four-at-once", LevelTestFixtures.Board).Level;

            Assert.AreEqual(before.BoardHalfExtents, _level.BoardHalfExtents);
            Assert.AreEqual(before.Fixtures.Count, _level.Fixtures.Count);

            for (int i = 0; i < before.Fixtures.Count; i++)
            {
                Assert.AreEqual(before.Fixtures[i].Id, _level.Fixtures[i].Id);
                Assert.AreEqual(before.Fixtures[i].Cell, _level.Fixtures[i].Cell, before.Fixtures[i].Id);
            }

            Assert.IsTrue(before.BlockNamed("FA").Matches(_level.BlockNamed("FA")),
                "the FA here is not the one the player just learned");
        }

        [Test]
        public void TheLevel_StocksFourFullAddersAndTwoLookaheads_AndNoGates()
        {
            Assert.AreEqual(0, _level.Budget.Count, "a gate is stocked");
            Assert.AreEqual(4, _level.BlockBudgetFor("FA"));
            Assert.AreEqual(2, _level.BlockBudgetFor("LA"));

            BlockDefinition la = _level.BlockNamed("LA");
            Assert.AreEqual(5, la.Inputs.Count);
            Assert.AreEqual(1, la.Outputs.Count);
            Assert.AreEqual(9, la.Gates.Count);
            Assert.AreEqual(3, la.Height, "five inputs take three cells");
        }

        [Test]
        public void TheExpectations_AreASum()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int sum = ValueOf(_level, "a", vector) + ValueOf(_level, "b", vector) + CarryIn(_level, vector);

                for (int bit = 0; bit < 4; bit++)
                {
                    Assert.AreEqual((char)('0' + ((sum >> bit) & 1)), ExpectedAt(_level, "s" + bit)[vector],
                        $"S{bit}, row {vector + 1}");
                }

                Assert.AreEqual((char)('0' + (sum >> 4)), ExpectedAt(_level, "cout")[vector], $"COUT, row {vector + 1}");
            }
        }

        [Test]
        public void EveryColumn_SeesEveryCombination()
        {
            AssertEveryColumnSeesEveryCombination(_level);
        }

        [Test]
        public void TheCarry_IsAskedToRippleAllTheWay()
        {
            AssertTheCarryRipplesAllTheWay(_level);
        }

        /// <summary>
        /// Each group of two columns carries out for each of its three reasons, and on some rows not
        /// at all, so an LA wired to the wrong bits or the wrong carry shows.
        /// </summary>
        [Test]
        public void EachGroup_CarriesForEveryReason()
        {
            var low = new bool[4];
            var high = new bool[4];

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int a = ValueOf(_level, "a", vector);
                int b = ValueOf(_level, "b", vector);
                int c0 = CarryIn(_level, vector);
                int c2 = ((a & 3) + (b & 3) + c0) >> 2;

                low[Reason(a, b, 0, c0)] = true;
                high[Reason(a, b, 2, c2)] = true;
            }

            string[] names = { "no carry", "its upper bit generating", "its lower bit generating", "its carry in passing" };

            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(low[i], $"bits 0 and 1 never carry out for {names[i]}");
                Assert.IsTrue(high[i], $"bits 2 and 3 never carry out for {names[i]}");
            }
        }

        private static int Reason(int a, int b, int lower, int carryIn)
        {
            int g0 = (a >> lower) & (b >> lower) & 1;
            int p0 = ((a ^ b) >> lower) & 1;
            int g1 = (a >> (lower + 1)) & (b >> (lower + 1)) & 1;
            int p1 = ((a ^ b) >> (lower + 1)) & 1;

            if (g1 == 1)
                return 1;

            if (p1 == 1 && g0 == 1)
                return 2;

            return p1 == 1 && p0 == 1 && carryIn == 1 ? 3 : 0;
        }

        // -----------------------------------------------------------------
        // The lookahead block
        // -----------------------------------------------------------------

        /// <summary>
        /// LA alone, on all thirty-two of its inputs: the carry out of two columns, with its carry in
        /// a tick after its bits and the answer three ticks after them.
        /// </summary>
        [Test]
        public void TheLookahead_IsTheCarryOutOfTwoColumns()
        {
            var a0 = new System.Text.StringBuilder();
            var b0 = new System.Text.StringBuilder();
            var cin = new System.Text.StringBuilder();
            var a1 = new System.Text.StringBuilder();
            var b1 = new System.Text.StringBuilder();
            var carry = new System.Text.StringBuilder();

            for (int i = 0; i < 32; i++)
            {
                int a = (i & 1) | ((i >> 2) & 2);
                int b = ((i >> 1) & 1) | ((i >> 3) & 2);
                int c = (i >> 2) & 1;

                a0.Append(a & 1);
                b0.Append(b & 1);
                cin.Append(c);
                a1.Append(a >> 1);
                b1.Append(b >> 1);
                carry.Append((a + b + c) >> 2);
            }

            LevelDefinition alone = LevelTestFixtures.Parse($@"{{
                ""name"": ""Two columns"", ""tickLimit"": 60,
                ""fixtures"": [
                    {{ ""id"": ""a0"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"":  2 }}, ""stream"": ""{a0}"" }},
                    {{ ""id"": ""b0"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"":  1 }}, ""stream"": ""{b0}"" }},
                    {{ ""id"": ""cin"", ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"":  0 }}, ""stream"": ""{cin}"" }},
                    {{ ""id"": ""a1"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"": -1 }}, ""stream"": ""{a1}"" }},
                    {{ ""id"": ""b1"",  ""kind"": ""Source"", ""cell"": {{ ""x"": -4, ""y"": -2 }}, ""stream"": ""{b1}"" }},
                    {{ ""id"": ""cout"", ""kind"": ""Sink"",  ""cell"": {{ ""x"":  4, ""y"":  0 }} }}
                ],
                ""blocks"": {BlocksOf("look-ahead")},
                ""budget"": [ {{ ""block"": ""FA"", ""count"": 1 }}, {{ ""block"": ""LA"", ""count"": 1 }} ],
                ""expected"": [ {{ ""sink"": ""cout"", ""values"": ""{carry}"" }} ]
            }}");

            var at = new Vector2Int(0, 1);
            var board = new CircuitBlueprint();
            board.PlaceBlock(at, alone.BlockNamed("LA"));

            LevelTestFixtures.Wire(board, new Vector2Int(-4, 2), at, toPort: LaA0);
            LevelTestFixtures.Wire(board, new Vector2Int(-4, 1), at, toPort: LaB0);
            LevelTestFixtures.Wire(board, new Vector2Int(-4, 0), at, toPort: LaCarryIn, delay: 2);
            LevelTestFixtures.Wire(board, new Vector2Int(-4, -1), at, toPort: LaA1);
            LevelTestFixtures.Wire(board, new Vector2Int(-4, -2), at, toPort: LaB1);
            LevelTestFixtures.Wire(board, at, new Vector2Int(4, 0), fromPort: LaCarryOut);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(alone, board);
            Assert.IsTrue(verdict.IsPass, verdict.ToString());
            Assert.AreEqual(5, LatencyAt(alone, board, "cout"), "bits at 1, the carry in at 2, the answer out at 4");

            LevelTestFixtures.Retime(board, new Vector2Int(-4, 0), at, LaCarryIn, 1);
            Assert.AreEqual(RunOutcome.Corrupted, LevelTestFixtures.RunAndGrade(alone, board).Outcome,
                "a carry in arriving with the bits did not wait and collide");
        }

        // -----------------------------------------------------------------
        // The adder
        // -----------------------------------------------------------------

        [Test]
        public void TheLookahead_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Lookahead());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheLookahead_IsRightOnEverySum()
        {
            LevelDefinition every = EverySum("look-ahead", maxLatency: 8);
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(every, Lookahead(every));

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheLookahead_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint lookahead = Lookahead();

            Assert.AreEqual(34, lookahead.ExtraDelay(), "the lookahead's delay, which the budget is set from");
            Assert.LessOrEqual(lookahead.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.AreEqual(_level.BlockBudgetFor("FA"), lookahead.CountOfBlock("FA"), "FAs");
            Assert.AreEqual(_level.BlockBudgetFor("LA"), lookahead.CountOfBlock("LA"), "LAs");

            foreach (BlueprintWire wire in lookahead.Wires)
                Assert.LessOrEqual(wire.Delay, _level.MaxWireDelay, wire.ToString());
        }

        [Test]
        public void TheLookahead_ScoresTheGatesInsideItsBoxes()
        {
            Assert.AreEqual(38, Lookahead().GatesInBlocks());
        }

        [Test]
        public void TheCarry_ReachesTheTopSooner()
        {
            Assert.AreEqual(3, LatencyAt(_level, Lookahead(), "s0"), "S0");
            Assert.AreEqual(5, LatencyAt(_level, Lookahead(), "s1"), "S1");
            Assert.AreEqual(6, LatencyAt(_level, Lookahead(), "s2"), "S2");
            Assert.AreEqual(8, LatencyAt(_level, Lookahead(), "s3"), "S3");
            Assert.AreEqual(8, LatencyAt(_level, Lookahead(), "cout"), "COUT");
            Assert.AreEqual(8, _level.MaxLatency);
        }

        // -----------------------------------------------------------------
        // What the limit refuses
        // -----------------------------------------------------------------

        /// <summary>
        /// Four at once's own answer fits this level's parts, wires and budget, gets every sum right,
        /// and is two ticks too slow -- the level's whole point, met by the circuit the player has.
        /// </summary>
        [Test]
        public void FourAtOncesRipple_IsRight_ButTooSlow()
        {
            CircuitBlueprint ripple = Ripple(_level);
            Assert.LessOrEqual(ripple.ExtraDelay(), _level.DelayBudget, "sanity: within the budget");

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, ripple);

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("10 ticks", verdict.Reason);
            StringAssert.Contains("allows 8", verdict.Reason);
        }

        /// <summary>Only the low group looked ahead: COUT ripples out of FA3 a tick late.</summary>
        [Test]
        public void OneLookahead_IsNotEnough()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Lookahead(highLookahead: false));

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("9 ticks", verdict.Reason);
        }

        /// <summary>The high group's lookahead given CIN, timed to meet it, rather than the carry into bit 2.</summary>
        [Test]
        public void TheWrongCarryIntoTheHighLookahead_GivesAWrongSum()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Lookahead(highCarryFromCin: true));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TheHint_SaysNothingOfHowItIsBuilt()
        {
            string hint = " " + _level.Hint.ToLowerInvariant() + " ";

            foreach (string word in new[] { "xor", " and ", " or ", "lookahead", " la ", "group", "two columns" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }
    }
}
