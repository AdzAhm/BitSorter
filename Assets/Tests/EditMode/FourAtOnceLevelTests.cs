using System.Text;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The 4-bit ripple-carry adder, built from four full adders in boxes: the first level that
    /// stocks a block, and the first on a 13 by 9 board.
    /// </summary>
    /// <remarks>
    /// Nine sources down the left edge in bit order -- A0, B0, CIN, then A and B for each bit above
    /// -- and the four FA blocks in a staircase, each beside the sources it reads, with its carry
    /// out wired to the carry in of the one below.
    ///
    /// **The FA takes its carry a tick after A and B.** Inside, A and B go straight into an XOR and
    /// an AND, and the carry meets the XOR's answer one gate later, so a carry arriving with A and B
    /// would wait in its port while the next one ran into it. That is the full adder as hardware
    /// builds one -- the carry is the slow signal, so it gets the short way through -- and it is
    /// also what makes this level fit the wire cap: carry in to carry out is a tick, the wire to the
    /// next box another, so each column is two ticks after the one before and A3, B3 wait seven. An
    /// FA that wanted all three together would make it three a column, and A3 would need ten.
    ///
    /// Latencies 3, 5, 7 and 9 for the sums and 10 for COUT, which is the level's limit. Twenty
    /// gates, scored as their contents (Ahmad's choice, 2026-10-04).
    ///
    /// Sixteen vectors of the 512: every value of A once and of B once, each column seeing all
    /// eight of (a, b, carry in), the carry rippling the whole way both from CIN (14 + 1 + 1) and
    /// from bit 0 (13 + 10 + 1 has a carry out of bit 0 that every column above passes on), and
    /// COUT set on exactly half.
    /// </remarks>
    public class FourAtOnceLevelTests
    {
        internal static readonly Vector2Int SourceA0 = new Vector2Int(-6, 4);
        internal static readonly Vector2Int SourceB0 = new Vector2Int(-6, 3);
        internal static readonly Vector2Int SourceCin = new Vector2Int(-6, 2);
        internal static readonly Vector2Int SourceA1 = new Vector2Int(-6, 1);
        internal static readonly Vector2Int SourceB1 = new Vector2Int(-6, 0);
        internal static readonly Vector2Int SourceA2 = new Vector2Int(-6, -1);
        internal static readonly Vector2Int SourceB2 = new Vector2Int(-6, -2);
        internal static readonly Vector2Int SourceA3 = new Vector2Int(-6, -3);
        internal static readonly Vector2Int SourceB3 = new Vector2Int(-6, -4);

        internal static readonly Vector2Int BinS0 = new Vector2Int(6, 3);
        internal static readonly Vector2Int BinS1 = new Vector2Int(6, 1);
        internal static readonly Vector2Int BinS2 = new Vector2Int(6, -1);
        internal static readonly Vector2Int BinS3 = new Vector2Int(6, -3);
        internal static readonly Vector2Int BinCout = new Vector2Int(6, -4);

        /// <summary>Each FA's top cell: a staircase down and to the right, bit 0 first.</summary>
        internal static readonly Vector2Int[] Adders =
        {
            new Vector2Int(-4, 3), new Vector2Int(-2, 1), new Vector2Int(0, -1), new Vector2Int(2, -3),
        };

        internal static readonly Vector2Int[] SourcesA = { SourceA0, SourceA1, SourceA2, SourceA3 };
        internal static readonly Vector2Int[] SourcesB = { SourceB0, SourceB1, SourceB2, SourceB3 };
        internal static readonly Vector2Int[] BinsS = { BinS0, BinS1, BinS2, BinS3 };

        // The FA's ports, in the order its faces show them.
        internal const int PortA = 0;
        internal const int PortB = 1;
        internal const int PortCarryIn = 2;
        internal const int PortSum = 0;
        internal const int PortCarryOut = 1;

        private LevelDefinition _level;

        [SetUp]
        public void SetUp()
        {
            LevelLoadResult result = LevelLoader.Load("four-at-once", LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"shipped four-at-once.json is invalid: {result.Error}");

            _level = result.Level;
        }

        /// <summary>
        /// Bit 0's carry in from CIN, every other from the carry out of the bit below; A and B of bit
        /// i wait 2i + 1 ticks, CIN two.
        /// </summary>
        /// <param name="level">The level whose FA to place: this one, or one that stocks the same.</param>
        /// <param name="cinDelay">The wire from CIN.</param>
        /// <param name="bit2CarryFrom">Which bit's carry out bit 2 takes, normally 1.</param>
        /// <param name="bit3Delay">The wires A3 and B3 take.</param>
        /// <param name="coutWire">The wire into COUT's bin.</param>
        /// <param name="bin1">The bin bit 1's sum goes to.</param>
        /// <param name="bin2">The bin bit 2's sum goes to.</param>
        internal static CircuitBlueprint Ripple(
            LevelDefinition level, int cinDelay = 2, int bit2CarryFrom = 1, int bit3Delay = 7, int coutWire = 1,
            Vector2Int? bin1 = null, Vector2Int? bin2 = null)
        {
            var board = new CircuitBlueprint();
            BlockDefinition fa = level.BlockNamed("FA");

            foreach (Vector2Int at in Adders)
                board.PlaceBlock(at, fa);

            for (int bit = 0; bit < 4; bit++)
            {
                int delay = bit == 3 ? bit3Delay : 2 * bit + 1;

                LevelTestFixtures.Wire(board, SourcesA[bit], Adders[bit], toPort: PortA, delay: delay);
                LevelTestFixtures.Wire(board, SourcesB[bit], Adders[bit], toPort: PortB, delay: delay);
            }

            LevelTestFixtures.Wire(board, SourceCin, Adders[0], toPort: PortCarryIn, delay: cinDelay);
            LevelTestFixtures.Wire(board, Adders[0], Adders[1], fromPort: PortCarryOut, toPort: PortCarryIn);

            // Bit 2 normally meets bit 1's carry a tick after its own A and B. Bit 0's carry is two
            // ticks earlier than that, so taken in its place the wire is two ticks longer.
            int skipped = 1 - bit2CarryFrom;
            LevelTestFixtures.Wire(board, Adders[bit2CarryFrom], Adders[2],
                fromPort: PortCarryOut, toPort: PortCarryIn, delay: 1 + 2 * skipped);
            LevelTestFixtures.Wire(board, Adders[2], Adders[3], fromPort: PortCarryOut, toPort: PortCarryIn);

            LevelTestFixtures.Wire(board, Adders[0], BinS0, fromPort: PortSum);
            LevelTestFixtures.Wire(board, Adders[1], bin1 ?? BinS1, fromPort: PortSum);
            LevelTestFixtures.Wire(board, Adders[2], bin2 ?? BinS2, fromPort: PortSum);
            LevelTestFixtures.Wire(board, Adders[3], BinS3, fromPort: PortSum);
            LevelTestFixtures.Wire(board, Adders[3], BinCout, fromPort: PortCarryOut, delay: coutWire);

            return board;
        }

        private CircuitBlueprint Ripple(
            int cinDelay = 2, int bit2CarryFrom = 1, int bit3Delay = 7, int coutWire = 1,
            Vector2Int? bin1 = null, Vector2Int? bin2 = null) =>
            Ripple(_level, cinDelay, bit2CarryFrom, bit3Delay, coutWire, bin1, bin2);

        internal static int LatencyAt(LevelDefinition level, CircuitBlueprint blueprint, string sink)
        {
            BuiltCircuit built = CircuitBuilder.Build(level, blueprint);
            LevelGrader.RunToCompletion(built.Simulation, level, built.FixtureNodeIds);

            var bin = (SinkNode)built.Simulation.GetNode(built.FixtureNodeIds[sink]);
            Assert.Greater(bin.Received.Count, 0, $"nothing reached {sink}");

            return bin.Received[0].Tick;
        }

        /// <summary>A, B or the carry in on one vector, as a number.</summary>
        internal static int ValueOf(LevelDefinition level, string prefix, int vector)
        {
            int value = 0;

            for (int bit = 0; bit < 4; bit++)
                value |= (int)level.FixtureById(prefix + bit).Stream[vector] << bit;

            return value;
        }

        internal static int CarryIn(LevelDefinition level, int vector) =>
            (int)level.FixtureById("cin").Stream[vector];

        internal static string ExpectedAt(LevelDefinition level, string sink)
        {
            foreach (LevelExpectation expectation in level.Expectations)
            {
                if (expectation.SinkId == sink)
                    return expectation.Values;
            }

            Assert.Fail($"nothing is expected of {sink}");
            return null;
        }

        // -----------------------------------------------------------------
        // The level
        // -----------------------------------------------------------------

        [Test]
        public void TheLevel_IsOnAThirteenByNineBoard()
        {
            Assert.AreEqual(new Vector2Int(6, 4), _level.BoardHalfExtents);
        }

        [Test]
        public void TheLevel_StocksFourFullAdders_AndNoGates()
        {
            Assert.AreEqual(0, _level.Budget.Count, "a gate is stocked");
            Assert.AreEqual(4, _level.BlockBudgetFor("FA"));

            BlockDefinition fa = _level.BlockNamed("FA");
            Assert.AreEqual(new[] { "a", "b", "cin" }, new[] { fa.Inputs[0].Id, fa.Inputs[1].Id, fa.Inputs[2].Id });
            Assert.AreEqual(new[] { "s", "cout" }, new[] { fa.Outputs[0].Id, fa.Outputs[1].Id });
            Assert.AreEqual(5, fa.Gates.Count, "two XORs, two ANDs and an OR");
            Assert.AreEqual(2, fa.Height, "three inputs take two cells");
        }

        [Test]
        public void TheExpectations_AreASum()
        {
            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                int sum = ValueOf(_level, "a", vector) + ValueOf(_level, "b", vector) + CarryIn(_level, vector);

                for (int bit = 0; bit < 4; bit++)
                {
                    Assert.AreEqual((char)('0' + ((sum >> bit) & 1)),
                        ExpectedAt(_level, "s" + bit)[vector], $"S{bit}, row {vector + 1}");
                }

                Assert.AreEqual((char)('0' + (sum >> 4)), ExpectedAt(_level, "cout")[vector],
                    $"COUT, row {vector + 1}");
            }
        }

        [Test]
        public void EveryValueOfAAndOfB_IsAskedOnce()
        {
            var a = new bool[16];
            var b = new bool[16];

            for (int vector = 0; vector < _level.VectorCount; vector++)
            {
                a[ValueOf(_level, "a", vector)] = true;
                b[ValueOf(_level, "b", vector)] = true;
            }

            for (int value = 0; value < 16; value++)
            {
                Assert.IsTrue(a[value], $"A is never {value}");
                Assert.IsTrue(b[value], $"B is never {value}");
            }
        }

        /// <summary>Each FA sees all eight of (a, b, carry in), so a wrong wire into any of them shows.</summary>
        [Test]
        public void EveryColumn_SeesEveryCombination()
        {
            AssertEveryColumnSeesEveryCombination(_level);
        }

        internal static void AssertEveryColumnSeesEveryCombination(LevelDefinition level)
        {
            var seen = new bool[4, 8];

            for (int vector = 0; vector < level.VectorCount; vector++)
            {
                int a = ValueOf(level, "a", vector);
                int b = ValueOf(level, "b", vector);
                int carry = CarryIn(level, vector);

                for (int bit = 0; bit < 4; bit++)
                {
                    int ai = (a >> bit) & 1;
                    int bi = (b >> bit) & 1;

                    seen[bit, (ai << 2) | (bi << 1) | carry] = true;
                    carry = (ai + bi + carry) >> 1;
                }
            }

            for (int bit = 0; bit < 4; bit++)
            {
                for (int i = 0; i < 8; i++)
                {
                    Assert.IsTrue(seen[bit, i],
                        $"bit {bit} never sees a b carry = {i >> 2}{(i >> 1) & 1}{i & 1}");
                }
            }
        }

        /// <summary>
        /// The carry has to go the whole way, from CIN and from bit 0: a row where every column
        /// passes a carry in through, with CIN set, and one where bit 0 makes a carry that every
        /// column above passes through.
        /// </summary>
        [Test]
        public void TheCarry_IsAskedToRippleAllTheWay()
        {
            AssertTheCarryRipplesAllTheWay(_level);
        }

        internal static void AssertTheCarryRipplesAllTheWay(LevelDefinition level)
        {
            bool fromCin = false;
            bool fromBit0 = false;

            for (int vector = 0; vector < level.VectorCount; vector++)
            {
                int a = ValueOf(level, "a", vector);
                int b = ValueOf(level, "b", vector);

                fromCin |= (a ^ b) == 15 && CarryIn(level, vector) == 1;
                fromBit0 |= (a & b & 1) == 1 && ((a ^ b) >> 1) == 7;
            }

            Assert.IsTrue(fromCin, "no row carries CIN through all four columns");
            Assert.IsTrue(fromBit0, "no row carries bit 0's carry through the three above it");
        }

        [Test]
        public void CarryOut_IsSetOnHalfTheRows()
        {
            int set = 0;
            foreach (char c in ExpectedAt(_level, "cout"))
                set += c == '1' ? 1 : 0;

            Assert.AreEqual(_level.VectorCount / 2, set);
        }

        // -----------------------------------------------------------------
        // The adder
        // -----------------------------------------------------------------

        [Test]
        public void TheRipple_Solves()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Ripple());

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        /// <summary>
        /// The same circuit on all 512 sums, on a copy of the level that asks every one -- the
        /// sixteen are a sample of a function the adder gets right everywhere.
        /// </summary>
        [Test]
        public void TheRipple_IsRightOnEverySum()
        {
            LevelDefinition every = EverySum("four-at-once", maxLatency: 10);
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(every, Ripple(every));

            Assert.IsTrue(verdict.IsPass, verdict.ToString());
        }

        [Test]
        public void TheRipple_FitsThePartsListAndTheDelayBudget()
        {
            CircuitBlueprint ripple = Ripple();

            Assert.AreEqual(25, ripple.ExtraDelay(), "the adder's delay, which the budget is set from");
            Assert.LessOrEqual(ripple.ExtraDelay(), _level.DelayBudget, "delay budget");
            Assert.AreEqual(_level.BlockBudgetFor("FA"), ripple.CountOfBlock("FA"), "FAs");

            int longest = 0;
            foreach (BlueprintWire wire in ripple.Wires)
                longest = Mathf.Max(longest, wire.Delay);

            Assert.AreEqual(_level.MaxWireDelay, longest, "the longest wire the adder needs is the level's cap");
        }

        [Test]
        public void TheRipple_ScoresTheGatesInsideItsBoxes()
        {
            Assert.AreEqual(20, Ripple().GatesInBlocks());
        }

        /// <summary>The carry is the critical path, two ticks longer at every column.</summary>
        [Test]
        public void TheCarry_IsTheCriticalPath()
        {
            Assert.AreEqual(3, LatencyAt(_level, Ripple(), "s0"), "S0");
            Assert.AreEqual(5, LatencyAt(_level, Ripple(), "s1"), "S1");
            Assert.AreEqual(7, LatencyAt(_level, Ripple(), "s2"), "S2");
            Assert.AreEqual(9, LatencyAt(_level, Ripple(), "s3"), "S3");
            Assert.AreEqual(10, LatencyAt(_level, Ripple(), "cout"), "COUT");
            Assert.AreEqual(10, _level.MaxLatency);
        }

        // -----------------------------------------------------------------
        // The wrong circuits the sixteen still catch
        // -----------------------------------------------------------------

        /// <summary>
        /// CIN arriving with A0 and B0 rather than a tick after them waits in its port, and the next
        /// carry runs into it -- what the goal's "a tick after" is there to warn about.
        /// </summary>
        [Test]
        public void ACarryInWithAAndB_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Ripple(cinDelay: 1));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        /// <summary>Bit 2 given bit 0's carry, skipping bit 1's, and timed to meet it.</summary>
        [Test]
        public void ACarryIntoTheWrongBit_GivesAWrongSum()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Ripple(bit2CarryFrom: 0));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>Bit 1's carry in from CIN, timed to meet it, where bit 0's carry out should be.</summary>
        [Test]
        public void ACarryFromCinIntoBit1_GivesAWrongSum()
        {
            CircuitBlueprint board = Ripple();
            board.RemoveWireAt(board.IndexOfWire(
                new CellPort(Adders[0], false, PortCarryOut), new CellPort(Adders[1], true, PortCarryIn)));
            LevelTestFixtures.Wire(board, SourceCin, Adders[1], toPort: PortCarryIn, delay: 4);

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, board);

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        [Test]
        public void TwoSumsInEachOthersBins_GiveAWrongSum()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Ripple(bin1: BinS2, bin2: BinS1));

            Assert.AreEqual(RunOutcome.WrongOutput, verdict.Outcome, verdict.ToString());
        }

        /// <summary>
        /// The top column waiting five ticks, as it would if each column were one tick after the
        /// one before rather than two: its A and B arrive before its carry and are run into.
        /// </summary>
        [Test]
        public void ATopColumnThatDoesNotWaitLongEnough_DestroysBits()
        {
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, Ripple(bit3Delay: 5));

            Assert.AreEqual(RunOutcome.Corrupted, verdict.Outcome, verdict.ToString());
        }

        /// <summary>A tick of padding into COUT's bin is right, inside the budget, and too slow.</summary>
        [Test]
        public void ALongerWayOut_IsRight_ButTooSlow()
        {
            CircuitBlueprint slow = Ripple(coutWire: 2);
            Assert.LessOrEqual(slow.ExtraDelay(), _level.DelayBudget, "sanity: within the budget");

            RunVerdict verdict = LevelTestFixtures.RunAndGrade(_level, slow);

            Assert.AreEqual(RunOutcome.TooSlow, verdict.Outcome, verdict.ToString());
            StringAssert.Contains("11 ticks", verdict.Reason);
            StringAssert.Contains("allows 10", verdict.Reason);
        }

        [Test]
        public void ABareWire_FromAnySource_Fails()
        {
            foreach (Vector2Int source in new[] { SourceA0, SourceB0, SourceCin, SourceA1, SourceB1, SourceA2, SourceB2, SourceA3, SourceB3 })
            {
                foreach (Vector2Int bin in new[] { BinS0, BinS1, BinS2, BinS3, BinCout })
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

            foreach (string word in new[] { "xor", " and ", " or ", "ripple", "adder", "half", "full", "seven", "7" })
                StringAssert.DoesNotContain(word, hint, $"the hint says '{word.Trim()}'");
        }

        // -----------------------------------------------------------------
        // Every sum
        // -----------------------------------------------------------------

        /// <summary>
        /// A shipped 4-bit adder level's fixtures and blocks, asked all 512 sums: A and B from 0 to
        /// 15, each with CIN 0 and 1.
        /// </summary>
        internal static LevelDefinition EverySum(string shipped, int maxLatency)
        {
            LevelDefinition level = LevelLoader.Load(shipped, LevelTestFixtures.Board).Level;

            var a = new StringBuilder[4];
            var b = new StringBuilder[4];
            var sum = new StringBuilder[5];
            var cin = new StringBuilder();

            for (int i = 0; i < 4; i++)
            {
                a[i] = new StringBuilder();
                b[i] = new StringBuilder();
            }

            for (int i = 0; i < 5; i++)
                sum[i] = new StringBuilder();

            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int c = 0; c < 2; c++)
                    {
                        for (int bit = 0; bit < 4; bit++)
                        {
                            a[bit].Append((x >> bit) & 1);
                            b[bit].Append((y >> bit) & 1);
                        }

                        cin.Append(c);

                        for (int bit = 0; bit < 5; bit++)
                            sum[bit].Append(((x + y + c) >> bit) & 1);
                    }
                }
            }

            var fixtures = new StringBuilder();

            foreach (LevelFixture fixture in level.Fixtures)
            {
                string stream = fixture.Kind != FixtureKind.Source ? ""
                    : fixture.Id == "cin" ? cin.ToString()
                    : (fixture.Id[0] == 'a' ? a : b)[fixture.Id[1] - '0'].ToString();

                fixtures.Append($@"{{ ""id"": ""{fixture.Id}"", ""kind"": ""{fixture.Kind}"", " +
                                $@"""cell"": {{ ""x"": {fixture.Cell.x}, ""y"": {fixture.Cell.y} }}" +
                                (stream.Length > 0 ? $@", ""stream"": ""{stream}""" : "") + " }, ");
            }

            string blocks = BlocksOf(shipped);
            var budget = new StringBuilder();

            foreach (LevelBlockBudgetEntry entry in level.BlockBudget)
                budget.Append($@"{{ ""block"": ""{entry.Block}"", ""count"": {entry.Count} }}, ");

            string expected =
                $@"{{ ""sink"": ""s3"", ""values"": ""{sum[3]}"" }}, {{ ""sink"": ""s2"", ""values"": ""{sum[2]}"" }}, " +
                $@"{{ ""sink"": ""s1"", ""values"": ""{sum[1]}"" }}, {{ ""sink"": ""s0"", ""values"": ""{sum[0]}"" }}, " +
                $@"{{ ""sink"": ""cout"", ""values"": ""{sum[4]}"" }}";

            return LevelTestFixtures.Parse($@"{{
                ""name"": ""Every sum"", ""tickLimit"": 600, ""maxLatency"": {maxLatency},
                ""board"": {{ ""columns"": 13, ""rows"": 9 }},
                ""fixtures"": [ {fixtures.ToString().TrimEnd(' ', ',')} ],
                ""blocks"": {blocks},
                ""budget"": [ {budget.ToString().TrimEnd(' ', ',')} ],
                ""expected"": [ {expected} ]
            }}");
        }

        /// <summary>The <c>blocks</c> array of a shipped level, as it is written in the file.</summary>
        internal static string BlocksOf(string shipped)
        {
            string json = Resources.Load<TextAsset>(LevelLoader.ResourcePath + "/" + shipped).text;

            int start = json.IndexOf("\"blocks\"", System.StringComparison.Ordinal);
            int open = json.IndexOf('[', start);
            int depth = 0;

            for (int i = open; i < json.Length; i++)
            {
                if (json[i] == '[')
                    depth++;
                else if (json[i] == ']' && --depth == 0)
                    return json.Substring(open, i - open + 1);
            }

            Assert.Fail($"{shipped} has no blocks");
            return null;
        }
    }
}
