using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The full matrix of what a level file may and may not say. JsonUtility gives no validation
    /// whatsoever -- unknown keys are dropped and missing ones become default values -- so every rule
    /// here is the only thing standing between a typo and a circuit that grades the player against
    /// something the author never wrote.
    /// </summary>
    /// <remarks>
    /// Driven with inline JSON rather than files in Resources, so the matrix can be exhaustive without
    /// shipping two dozen broken levels. The real files are covered by ShippedLevelTests.
    /// </remarks>
    public class LevelParsingTests
    {
        /// <summary>Matches PlacementGrid's defaults: 9 cells across, 5 down.</summary>
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private const string SourceIn =
            @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""0"" }";

        private const string BinOne =
            @"{ ""id"": ""binOne"", ""kind"": ""Sink"", ""cell"": { ""x"": 3, ""y"": 1 } }";

        private const string BinZero =
            @"{ ""id"": ""binZero"", ""kind"": ""Sink"", ""cell"": { ""x"": 3, ""y"": -1 } }";

        private const string ExpectOne = @"{ ""sink"": ""binOne"", ""values"": ""1"" }";
        private const string ExpectZeroEmpty = @"{ ""sink"": ""binZero"", ""values"": ""-"" }";

        /// <summary>Assembles a level file around whichever part a test is varying.</summary>
        private static string Json(
            string fixtures,
            string expected,
            string budget = @"{ ""kind"": ""Not"", ""count"": 1 }",
            string name = @"""Test level""",
            string tickLimit = "100")
        {
            return "{" +
                   $@" ""name"": {name}, ""hint"": ""a hint"", ""tickLimit"": {tickLimit}," +
                   $@" ""fixtures"": [{fixtures}]," +
                   $@" ""budget"": [{budget}]," +
                   $@" ""expected"": [{expected}] " +
                   "}";
        }

        private static LevelLoadResult Parse(string json) => LevelLoader.Parse(json, Board);

        // -----------------------------------------------------------------
        // A level's own board
        // -----------------------------------------------------------------

        /// <summary>A level with a source at <paramref name="x"/> and, if given, its own board.</summary>
        private static LevelLoadResult OnABoard(string board, int x)
        {
            string boardField = board == null ? string.Empty : $@" ""board"": {board},";

            return Parse("{" +
                         $@" ""name"": ""Board"", ""tickLimit"": 100,{boardField}" +
                         $@" ""fixtures"": [ {{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": {{ ""x"": {-x}, ""y"": 0 }}, ""stream"": ""0"" }}," +
                         @" { ""id"": ""out"", ""kind"": ""Sink"", ""cell"": { ""x"": 1, ""y"": 0 } } ]," +
                         @" ""budget"": [ { ""kind"": ""Not"", ""count"": 1 } ]," +
                         @" ""expected"": [ { ""sink"": ""out"", ""values"": ""1"" } ] }");
        }

        [Test]
        public void ALevelsOwnBoard_Wins()
        {
            LevelLoadResult wide = OnABoard(@"{ ""columns"": 13, ""rows"": 7 }", 6);

            Assert.IsTrue(wide.IsValid, wide.Error);
            Assert.AreEqual(new Vector2Int(6, 3), wide.Level.BoardHalfExtents);

            AssertRefused(OnABoard(@"{ ""columns"": 13, ""rows"": 7 }", 7));
        }

        [Test]
        public void WithoutABoard_TheFallbackIsTheBoard()
        {
            LevelLoadResult plain = OnABoard(null, 4);

            Assert.IsTrue(plain.IsValid, plain.Error);
            Assert.AreEqual(Board, plain.Level.BoardHalfExtents);

            AssertRefused(OnABoard(null, 5));
        }

        [TestCase(10, 7, "odd")]
        [TestCase(13, 6, "odd")]
        [TestCase(15, 7, "from 9 by 5")]
        [TestCase(13, 11, "from 9 by 5")]
        [TestCase(7, 5, "from 9 by 5")]
        [TestCase(13, 0, "give both")]
        public void ABoardOfTheWrongSize_IsRefusedWithAReason(int columns, int rows, string reason)
        {
            LevelLoadResult result = OnABoard($@"{{ ""columns"": {columns}, ""rows"": {rows} }}", 3);

            Assert.IsFalse(result.IsValid, "should have been refused");
            StringAssert.Contains(reason, result.Error);
        }

        private static LevelLoadResult ParseDefault() =>
            Parse(Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}"));

        // -----------------------------------------------------------------
        // Accepted
        // -----------------------------------------------------------------

        [Test]
        public void AValidLevel_ParsesEveryField()
        {
            LevelLoadResult result = ParseDefault();

            Assert.IsTrue(result.IsValid, result.Error);

            LevelDefinition level = result.Level;
            Assert.AreEqual("Test level", level.Name, "name");
            Assert.AreEqual("a hint", level.Hint, "hint");
            Assert.AreEqual(100, level.TickLimit, "tick limit");
            Assert.AreEqual(1, level.VectorCount, "one character of stream is one vector");
            Assert.AreEqual(3, level.Fixtures.Count, "fixtures");
            Assert.AreEqual(2, level.Expectations.Count, "expectations");

            LevelFixture source = level.FixtureById("in");
            Assert.AreEqual(FixtureKind.Source, source.Kind, "kind parsed from its name, not an int");
            Assert.AreEqual(new Vector2Int(-3, 0), source.Cell, "cell");
            Assert.AreEqual(1, source.Stream.Count, "stream length");
            Assert.AreEqual(Bit.Zero, source.Stream[0], "stream value");

            Assert.AreEqual(1, level.BudgetFor(GateKind.Not), "budget for a listed kind");
            Assert.AreEqual(0, level.BudgetFor(GateKind.And), "budget for an unlisted kind");
        }

        [Test]
        public void AnEmptyBudget_IsLegal()
        {
            // A level solvable with wires alone is a real level, and it is the shape the very first
            // tutorial in a chapter wants.
            LevelLoadResult result = Parse(
                Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}", budget: ""));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(0, result.Level.Budget.Count, "no budget entries");
        }

        [Test]
        public void AnOmittedTickLimit_GetsTheDefault()
        {
            // JsonUtility cannot tell a missing key from an explicit 0, so both mean "unspecified".
            LevelLoadResult result = Parse(
                Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}", tickLimit: "0"));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(LevelLoader.DefaultTickLimit, result.Level.TickLimit);
        }

        [Test]
        public void OmittedDelayFields_MeanNoBudgetAndTheDefaultCap()
        {
            // Absent has to mean unlimited, because JsonUtility yields 0 for a missing key. A level
            // that wants to forbid re-timing says maxWireDelay: 1 instead.
            LevelLoadResult result = ParseDefault();

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsFalse(result.Level.HasDelayBudget, "no budget means unrestricted");
            Assert.AreEqual(0, result.Level.DelayBudget);
            Assert.AreEqual(LevelDefinition.DefaultMaxWireDelay, result.Level.MaxWireDelay);
        }

        [Test]
        public void DelayFields_AreCarriedThrough()
        {
            string json = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}")
                .Replace(@"""tickLimit"": 100", @"""tickLimit"": 100, ""maxWireDelay"": 3, ""delayBudget"": 2");

            LevelLoadResult result = Parse(json);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(3, result.Level.MaxWireDelay);
            Assert.AreEqual(2, result.Level.DelayBudget);
            Assert.IsTrue(result.Level.HasDelayBudget);
        }

        [Test]
        public void ANegativeDelayField_IsRefused()
        {
            string capped = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}")
                .Replace(@"""tickLimit"": 100", @"""tickLimit"": 100, ""maxWireDelay"": -1");

            string budgeted = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}")
                .Replace(@"""tickLimit"": 100", @"""tickLimit"": 100, ""delayBudget"": -4");

            AssertRefused(Parse(capped));
            AssertRefused(Parse(budgeted));
        }

        [Test]
        public void DashesMapExpectedBitsBackToTheirVectors()
        {
            // The whole point of carrying a vector index on each expected bit. Positions 0 and 2
            // produce nothing, so the sink's first reception belongs to vector 1 and its second to
            // vector 3 -- and a failure has to be able to say so.
            string source =
                @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""0011"" }";
            string expected = @"{ ""sink"": ""binOne"", ""values"": ""-1-0"" }";

            LevelLoadResult result = Parse(Json($"{source}, {BinOne}", expected));

            Assert.IsTrue(result.IsValid, result.Error);

            LevelExpectation expectation = result.Level.Expectations[0];
            Assert.AreEqual(4, result.Level.VectorCount, "four characters, four vectors");
            Assert.AreEqual(2, expectation.Expected.Count, "two of the four produce a bit");

            Assert.AreEqual(Bit.One, expectation.Expected[0].Value);
            Assert.AreEqual(1, expectation.Expected[0].Vector, "first bit belongs to vector 1");

            Assert.AreEqual(Bit.Zero, expectation.Expected[1].Value);
            Assert.AreEqual(3, expectation.Expected[1].Vector, "second bit belongs to vector 3");
        }

        [Test]
        public void AnXInAnExpectation_IsADontCareThatKeepsItsSlot()
        {
            // 'x' and '-' are opposites, and the difference is the whole reason both exist. '-' means
            // no bit arrives, so it is dropped from the expected list. 'x' means a bit arrives and
            // either value passes, so it must keep its place -- otherwise every bit after it would be
            // compared against the wrong vector, which is the defect '-' already has.
            string source =
                @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""0011"" }";
            string expected = @"{ ""sink"": ""binOne"", ""values"": ""0x1x"" }";

            LevelLoadResult result = Parse(Json($"{source}, {BinOne}", expected));

            Assert.IsTrue(result.IsValid, result.Error);

            LevelExpectation expectation = result.Level.Expectations[0];
            Assert.AreEqual(4, result.Level.VectorCount, "four characters, four vectors");
            Assert.AreEqual(4, expectation.Expected.Count,
                "a don't-care still expects a bit, so all four keep their slots");

            Assert.AreEqual(1, expectation.Expected[1].Vector, "the slot still knows its vector");
            Assert.AreEqual(3, expectation.Expected[3].Vector);

            Assert.IsFalse(expectation.Expected[0].IsAny, "a literal is not a don't-care");
            Assert.IsTrue(expectation.Expected[1].IsAny);
            Assert.IsFalse(expectation.Expected[2].IsAny);
            Assert.IsTrue(expectation.Expected[3].IsAny);

            Assert.AreEqual(Bit.Zero, expectation.Expected[0].Value, "literals still carry a value");
            Assert.AreEqual(Bit.One, expectation.Expected[2].Value);
        }

        [Test]
        public void ADontCareAndASilentVector_CanShareOneExpectation()
        {
            // The two are easy to conflate, so pin down that they compose. "0x-1" is four vectors:
            // a literal, a don't-care that keeps its slot, a silent vector that does not, and another
            // literal -- leaving three expected bits whose vector indices skip 2.
            string source =
                @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""0011"" }";
            string expected = @"{ ""sink"": ""binOne"", ""values"": ""0x-1"" }";

            LevelLoadResult result = Parse(Json($"{source}, {BinOne}", expected));

            Assert.IsTrue(result.IsValid, result.Error);

            LevelExpectation expectation = result.Level.Expectations[0];
            Assert.AreEqual(3, expectation.Expected.Count, "only the '-' is dropped");

            Assert.AreEqual(0, expectation.Expected[0].Vector);
            Assert.AreEqual(1, expectation.Expected[1].Vector);
            Assert.IsTrue(expectation.Expected[1].IsAny);
            Assert.AreEqual(3, expectation.Expected[2].Vector, "vector 2 is silent, so 3 comes next");
        }

        // -----------------------------------------------------------------
        // Refused
        // -----------------------------------------------------------------

        [Test]
        public void MalformedJson_IsRefusedRatherThanThrowing()
        {
            // A broken level file is authoring feedback, not a crash.
            LevelLoadResult result = default;

            Assert.DoesNotThrow(() => result = Parse(@"{ ""name"": ""oops"" "));
            AssertRefused(result);
        }

        [Test]
        public void AnEmptyFile_IsRefused()
        {
            AssertRefused(Parse(""));
            AssertRefused(Parse("   "));
        }

        [Test]
        public void AMissingName_IsRefused()
        {
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", ExpectOne, name: @"""""")));
        }

        [Test]
        public void NoFixtures_IsRefused()
        {
            AssertRefused(Parse(Json("", ExpectOne)));
        }

        [Test]
        public void NoExpectations_IsRefused()
        {
            // Nothing would be graded, so every circuit would pass.
            AssertRefused(Parse(Json($"{SourceIn}, {BinOne}", "")));
        }

        [Test]
        public void NoSources_IsRefused()
        {
            AssertRefused(Parse(Json($"{BinOne}", ExpectOne)));
        }

        [Test]
        public void AnUnknownFixtureKind_IsRefused()
        {
            string bogus =
                @"{ ""id"": ""x"", ""kind"": ""Register"", ""cell"": { ""x"": 0, ""y"": 0 } }";

            AssertRefused(Parse(Json($"{SourceIn}, {BinOne}, {bogus}", ExpectOne)));
        }

        [Test]
        public void TwoFixturesSharingAnId_IsRefused()
        {
            string clash = @"{ ""id"": ""binOne"", ""kind"": ""Sink"", ""cell"": { ""x"": 2, ""y"": 2 } }";

            AssertRefused(Parse(Json($"{SourceIn}, {BinOne}, {clash}", ExpectOne)));
        }

        [Test]
        public void TwoFixturesSharingACell_IsRefused()
        {
            // One cell holds one node; the blueprint's whole addressing scheme depends on it.
            string overlap = @"{ ""id"": ""other"", ""kind"": ""Sink"", ""cell"": { ""x"": 3, ""y"": 1 } }";

            AssertRefused(Parse(Json($"{SourceIn}, {BinOne}, {overlap}", ExpectOne)));
        }

        [Test]
        public void AFixtureOffTheBoard_IsRefused()
        {
            // Would be unreachable and invisible, and the player could never wire to it.
            string offBoard = @"{ ""id"": ""far"", ""kind"": ""Sink"", ""cell"": { ""x"": 9, ""y"": 0 } }";

            AssertRefused(Parse(Json($"{SourceIn}, {offBoard}", @"{ ""sink"": ""far"", ""values"": ""1"" }")));
        }

        [Test]
        public void ASinkWithNoExpectation_IsRefused()
        {
            // The grader would hold it to the empty sequence silently. Relying on that is how a level
            // ships with a bin nobody checks, which the player then passes by wiring into it.
            AssertRefused(Parse(Json($"{SourceIn}, {BinOne}, {BinZero}", ExpectOne)));
        }

        [Test]
        public void ASinkCarryingAStream_IsRefused()
        {
            // Almost always a copy-pasted source. Ignoring it would leave the author believing the
            // sink emits something.
            string chatty =
                @"{ ""id"": ""binOne"", ""kind"": ""Sink"", ""cell"": { ""x"": 3, ""y"": 1 }, ""stream"": ""1"" }";

            AssertRefused(Parse(Json($"{SourceIn}, {chatty}", ExpectOne)));
        }

        [Test]
        public void AnExpectationNamingAnUnknownFixture_IsRefused()
        {
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", @"{ ""sink"": ""binTwo"", ""values"": ""1"" }")));
        }

        [Test]
        public void AnExpectationNamingASource_IsRefused()
        {
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}",
                    $@"{ExpectOne}, {{ ""sink"": ""in"", ""values"": ""1"" }}")));
        }

        [Test]
        public void TwoExpectationsForOneSink_AreRefused()
        {
            AssertRefused(Parse(Json($"{SourceIn}, {BinOne}", $"{ExpectOne}, {ExpectOne}")));
        }

        [Test]
        public void RaggedSourceStreams_AreRefused()
        {
            // Vector i is the i-th character across every source, so unequal lengths have no meaning.
            string longer =
                @"{ ""id"": ""b"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 2 }, ""stream"": ""0011"" }";

            AssertRefused(Parse(Json($"{SourceIn}, {longer}, {BinOne}", ExpectOne)));
        }

        [Test]
        public void AnExpectationShorterThanTheVectors_IsRefused()
        {
            // Shorter than the vector count is a level that forgot to say what one of its vectors
            // produces. Longer is legal and means the cycles after the last vector, which is what a
            // register puts there.
            string twoVectors =
                @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""01"" }";

            AssertRefused(Parse(
                Json($"{twoVectors}, {BinOne}", @"{ ""sink"": ""binOne"", ""values"": ""1"" }")));
        }

        [Test]
        public void ADashInASourceStream_IsRefusedAndSaysWhy()
        {
            // A source can stay quiet on a tick now, but not because a stream said so: clockPeriod
            // spaces every source out together, and a gap hand-written into one of them would put
            // that source out of step with the rest without saying anything.
            string sparse =
                @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""1-1"" }";

            LevelLoadResult result = Parse(
                Json($"{sparse}, {BinOne}", @"{ ""sink"": ""binOne"", ""values"": ""111"" }"));

            AssertRefused(result);
            StringAssert.Contains("clockPeriod", result.Error,
                "the reason has to point at the field that does this, or the author will try it again");
        }

        [TestCase("2", TestName = "AStrayDigitInAStream_IsRefused")]
        [TestCase("x", TestName = "AStrayLetterInAStream_IsRefused")]
        public void AStrayCharacterInAStream_IsRefused(string character)
        {
            string bogus =
                $@"{{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": {{ ""x"": -3, ""y"": 0 }}, ""stream"": ""{character}"" }}";

            AssertRefused(Parse(Json($"{bogus}, {BinOne}", ExpectOne)));
        }

        [Test]
        public void AGoal_IsCarriedThrough()
        {
            string json =
                @"{ ""name"": ""Aimed"", ""tickLimit"": 100," +
                @" ""goal"": ""make the bin receive A XOR B"", ""hint"": ""a hint""," +
                $@" ""fixtures"": [{SourceIn}, {BinOne}]," +
                $@" ""expected"": [{ExpectOne}] }}";

            LevelLoadResult result = Parse(json);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual("make the bin receive A XOR B", result.Level.Goal);
            Assert.AreEqual("a hint", result.Level.Hint, "the goal must not displace the hint");
        }

        [Test]
        public void AnOmittedGoal_IsEmptyRatherThanNull()
        {
            // Same contract as Hint. A level without one still loads -- the rule that every shipped
            // level names a goal is a curriculum rule, not a file-format one, because an inline test
            // level has no player to inform.
            LevelLoadResult result = ParseDefault();

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsNotNull(result.Level.Goal);
            Assert.IsEmpty(result.Level.Goal);
        }

        [Test]
        public void ALatencyCeiling_IsCarriedThrough()
        {
            string json =
                @"{ ""name"": ""Quick"", ""tickLimit"": 100, ""maxLatency"": 4," +
                $@" ""fixtures"": [{SourceIn}, {BinOne}]," +
                $@" ""expected"": [{ExpectOne}] }}";

            LevelLoadResult result = Parse(json);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(4, result.Level.MaxLatency);
            Assert.IsTrue(result.Level.HasLatencyLimit);
        }

        [Test]
        public void AnOmittedLatencyCeiling_MeansNoLimit()
        {
            LevelLoadResult result = ParseDefault();

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(0, result.Level.MaxLatency);
            Assert.IsFalse(result.Level.HasLatencyLimit,
                "every level written before this field must keep ignoring arrival ticks");
        }

        [Test]
        public void ANegativeLatencyCeiling_IsRefused()
        {
            // Same shape as the other numeric fields: negative is a mistake worth naming, zero has
            // to keep meaning "unspecified" because JsonUtility cannot tell it from a missing key.
            string json =
                @"{ ""name"": ""Slow"", ""tickLimit"": 100, ""maxLatency"": -1," +
                $@" ""fixtures"": [{SourceIn}, {BinOne}]," +
                $@" ""expected"": [{ExpectOne}] }}";

            AssertRefused(Parse(json));
        }

        [Test]
        public void AStrayCharacterInAnExpectation_IsRefused()
        {
            // Deliberately not 'x' -- that is a legal don't-care here, though it stays refused in a
            // source stream. '2' is the nearest plausible typo that is stray in both places.
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", @"{ ""sink"": ""binOne"", ""values"": ""2"" }")));
        }

        [Test]
        public void AnUnknownBudgetKind_IsRefused()
        {
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", ExpectOne,
                    budget: @"{ ""kind"": ""Nand2"", ""count"": 1 }")));
        }

        [Test]
        public void ADuplicateBudgetKind_IsRefused()
        {
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", ExpectOne,
                    budget: @"{ ""kind"": ""Not"", ""count"": 1 }, { ""kind"": ""Not"", ""count"": 2 }")));
        }

        [TestCase("0", TestName = "ABudgetCountOfZero_IsRefused")]
        [TestCase("-1", TestName = "ANegativeBudgetCount_IsRefused")]
        public void AnUnusableBudgetCount_IsRefused(string count)
        {
            // Zero would be indistinguishable from omitting the kind, which is already how a level says
            // "you may not place this".
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", ExpectOne,
                    budget: $@"{{ ""kind"": ""Not"", ""count"": {count} }}")));
        }

        private static void AssertRefused(LevelLoadResult result)
        {
            Assert.IsFalse(result.IsValid, "expected a refusal");
            Assert.IsNull(result.Level, "a refused level must not hand back a half-built definition");
            Assert.IsNotNull(result.Error, "a refusal needs a reason the author can act on");
            Assert.IsNotEmpty(result.Error);
        }

        // -----------------------------------------------------------------
        // The clock
        // -----------------------------------------------------------------

        [Test]
        public void ALevelWithoutAClock_RunsAVectorEveryTick()
        {
            // Absent means 1, the same way an absent tickLimit or maxWireDelay means its default:
            // JsonUtility cannot tell a missing key from a zero.
            LevelDefinition level = ParseDefault().Level;

            Assert.AreEqual(1, level.ClockPeriod);
            Assert.IsFalse(level.HasClock);
        }

        [Test]
        public void AClockPeriod_IsTakenFromTheFile()
        {
            string json = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}")
                .Replace(@"""tickLimit"": 100", @"""tickLimit"": 100, ""clockPeriod"": 3");

            LevelLoadResult result = Parse(json);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(3, result.Level.ClockPeriod);
            Assert.IsTrue(result.Level.HasClock);
        }

        [Test]
        public void ANegativeClockPeriod_IsRefused()
        {
            string json = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}")
                .Replace(@"""tickLimit"": 100", @"""tickLimit"": 100, ""clockPeriod"": -2");

            LevelLoadResult result = Parse(json);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("clockPeriod", result.Error);
        }

        // -----------------------------------------------------------------
        // Values after the last vector
        // -----------------------------------------------------------------

        [Test]
        public void AnExpectationMayRunPastTheLastVector()
        {
            // What a register makes happen: the bit it started out holding comes out in front of the
            // stream, so one more bit arrives than the level has vectors. The budget has to stock
            // the register -- a tail is the register's doing, and the loader now says so.
            string expect = @"{ ""sink"": ""binOne"", ""values"": ""10"" }";

            LevelLoadResult result = Parse(Json($"{SourceIn}, {BinOne}, {BinZero}",
                $"{expect}, {ExpectZeroEmpty}",
                budget: @"{ ""kind"": ""Register"", ""count"": 1 }"));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(2, result.Level.Expectations[0].Expected.Count,
                "both cycles are graded, the one past the last vector included");
        }

        /// <summary>
        /// A tail longer than the registers could shift is refused at load.
        /// </summary>
        /// <remarks>
        /// Only a register puts a bit out after the streams have stopped, and it shifts by one
        /// clock, so a chain of every register the level stocks is the longest tail that can ever
        /// arrive. Without this a doubled expectation string loaded happily and failed at run time
        /// with MissingOutput -- a level file's typo reported to the player as their mistake.
        /// </remarks>
        [Test]
        public void ATailLongerThanTheRegistersCouldShift_IsRefused()
        {
            string expect = @"{ ""sink"": ""binOne"", ""values"": ""101"" }";

            LevelLoadResult result = Parse(Json($"{SourceIn}, {BinOne}, {BinZero}",
                $"{expect}, {ExpectZeroEmpty}",
                budget: @"{ ""kind"": ""Register"", ""count"": 1 }"));

            Assert.IsFalse(result.IsValid, "one vector and one register cannot produce three bits");
            StringAssert.Contains("register", result.Error);
        }

        [Test]
        public void ATailWithNoRegisterStocked_IsRefused()
        {
            // Nothing on a board of gates alone can emit after the sources have stopped.
            string expect = @"{ ""sink"": ""binOne"", ""values"": ""10"" }";

            LevelLoadResult result = Parse(Json($"{SourceIn}, {BinOne}, {BinZero}",
                $"{expect}, {ExpectZeroEmpty}"));

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("register", result.Error);
        }

        [Test]
        public void SilenceAfterTheLastVector_IsRefused()
        {
            // "-" is dropped rather than taking a slot, so silence after the end is what an
            // expectation of exactly the vector count already says. Two spellings of one thing is
            // one thing to get wrong.
            string expect = @"{ ""sink"": ""binOne"", ""values"": ""1-"" }";

            LevelLoadResult result = Parse(Json($"{SourceIn}, {BinOne}, {BinZero}",
                $"{expect}, {ExpectZeroEmpty}"));

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("after the last vector", result.Error);
        }

        // -----------------------------------------------------------------
        // A starting circuit
        // -----------------------------------------------------------------

        /// <summary>The budget the starting-circuit tests stock unless told otherwise.</summary>
        private const string StartBudget =
            @"{ ""kind"": ""And"", ""count"": 1 }, { ""kind"": ""Not"", ""count"": 1 }, " +
            @"{ ""kind"": ""Register"", ""count"": 1 }";

        /// <summary>
        /// A level whose file carries a start of these gates and wires, on the usual fixtures: in at
        /// (-3, 0), binOne at (3, 1), binZero at (3, -1).
        /// </summary>
        private static LevelLoadResult WithStart(string gates, string wires, string limits = "")
        {
            string json = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}", StartBudget)
                .Replace(@"""tickLimit"": 100",
                    @"""tickLimit"": 100" + limits +
                    $@", ""start"": {{ ""gates"": [{gates}], ""wires"": [{wires}] }}");

            return Parse(json);
        }

        /// <summary>One gate of a start, in the file's own shape.</summary>
        private static string G(string kind, int x, int y) =>
            $@"{{ ""kind"": ""{kind}"", ""cell"": {{ ""x"": {x}, ""y"": {y} }} }}";

        /// <summary>One wire of a start, in the file's own shape; a null delay leaves the key out.</summary>
        private static string W(int fromX, int fromY, int toX, int toY, int toPort = 0, int? delay = 1)
        {
            string wire = $@"{{ ""from"": {{ ""x"": {fromX}, ""y"": {fromY} }}, ""fromPort"": 0, " +
                          $@"""to"": {{ ""x"": {toX}, ""y"": {toY} }}, ""toPort"": {toPort}";

            return wire + (delay.HasValue ? $@", ""delay"": {delay.Value} }}" : " }");
        }

        private static CircuitBlueprint StartOf(LevelDefinition level)
        {
            var board = new CircuitBlueprint();
            board.Restore(level.Start);
            return board;
        }

        [Test]
        public void ALevelWithoutAStart_OpensOnAnEmptyBoard()
        {
            LevelLoadResult result = Parse(Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}"));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsFalse(result.Level.HasStart);
            Assert.IsNull(result.Level.Start);
        }

        /// <summary>
        /// An empty start is no start: JsonUtility may hand back an empty object for a missing one, as
        /// it does for a missing board.
        /// </summary>
        [Test]
        public void AnEmptyStart_IsNoStart()
        {
            LevelLoadResult empty = WithStart(string.Empty, string.Empty);
            Assert.IsTrue(empty.IsValid, empty.Error);
            Assert.IsFalse(empty.Level.HasStart, "a start with nothing in it made a start");

            string braces = Json($"{SourceIn}, {BinOne}, {BinZero}", $"{ExpectOne}, {ExpectZeroEmpty}")
                .Replace(@"""tickLimit"": 100", @"""tickLimit"": 100, ""start"": {}");
            LevelLoadResult bare = Parse(braces);
            Assert.IsTrue(bare.IsValid, bare.Error);
            Assert.IsFalse(bare.Level.HasStart, "an empty start object made a start");
        }

        [Test]
        public void AStart_ArrivesAsWritten_InTheFilesOrder()
        {
            LevelLoadResult result = WithStart(
                G("Not", 0, 0),
                W(-3, 0, 0, 0, delay: 2) + ", " + W(0, 0, 3, 1));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsTrue(result.Level.HasStart);

            CircuitBlueprint board = StartOf(result.Level);

            Assert.AreEqual(1, board.Placements.Count);
            Assert.AreEqual(new Vector2Int(0, 0), board.Placements[0].Cell);
            Assert.AreEqual(GateKind.Not, board.Placements[0].Kind);

            Assert.AreEqual(2, board.Wires.Count);
            Assert.AreEqual(new Vector2Int(-3, 0), board.Wires[0].From.Cell, "the wires are not in the file's order");
            Assert.AreEqual(2, board.Wires[0].Delay);
            Assert.AreEqual(new Vector2Int(3, 1), board.Wires[1].To.Cell);
            Assert.AreEqual(1, board.Wires[1].Delay);
        }

        /// <summary>A wire whose delay is left out is one tick long, as a wire nobody has scrolled.</summary>
        [Test]
        public void AStartWireWithNoDelay_IsOneTick()
        {
            LevelLoadResult result = WithStart(G("Not", 0, 0), W(-3, 0, 0, 0, delay: null));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(1, StartOf(result.Level).Wires[0].Delay);
        }

        /// <summary>A wire from a part back into itself is allowed: a register's feedback is one.</summary>
        [Test]
        public void AStartWireBackIntoItsOwnPart_IsAllowed()
        {
            LevelLoadResult result = WithStart(G("Register", 0, 0), W(0, 0, 0, 0));

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(1, StartOf(result.Level).Wires.Count);
        }

        /// <summary>
        /// A start that loads is one a save of it restores: the loader and the save path ask the same
        /// questions of the same tables.
        /// </summary>
        [Test]
        public void AStartThatLoads_SurvivesASaveAndRestore()
        {
            LevelLoadResult result = WithStart(
                G("And", 0, 0) + ", " + G("Not", 0, 1) + ", " + G("Register", 1, -1),
                W(-3, 0, 0, 0) + ", " + W(-3, 0, 0, 1) + ", " + W(0, 1, 0, 0, toPort: 1) + ", " +
                W(0, 0, 1, -1) + ", " + W(1, -1, 3, 1));

            Assert.IsTrue(result.IsValid, result.Error);

            SavedBoard saved = BoardSerializer.ToSaved("test", StartOf(result.Level));
            var restored = new CircuitBlueprint();

            Assert.AreEqual(0, BoardSerializer.Restore(saved, result.Level, restored, Board),
                "a start the loader accepted was not all restored from a save of it");
            Assert.AreEqual(3, restored.Placements.Count);
            Assert.AreEqual(5, restored.Wires.Count);
        }

        private static IEnumerable<TestCaseData> BadStarts()
        {
            yield return Bad("AStartGateOffTheBoard", G("Not", 9, 0), "", "outside the board");
            yield return Bad("AStartGateOnAFixture", G("Not", -3, 0), "", "on the fixture 'in'");
            yield return Bad("TwoStartGatesInOneCell", G("Not", 0, 0) + ", " + G("And", 0, 0), "", "share the cell");
            yield return Bad("AStartGateOfNoKind", G("Nope", 0, 0), "", "has kind 'Nope'");
            yield return Bad("AStartGateTheBudgetDoesNotStock", G("Xor", 0, 0), "", "stocks none");
            yield return Bad("MoreStartGatesThanTheBudget", G("Not", 0, 0) + ", " + G("Not", 0, 1), "", "more than the budget");
            yield return Bad("AStartWireFromAnEmptyCell", G("Not", 0, 0), W(1, 1, 0, 0), "no output");
            yield return Bad("AStartWireFromASink", G("Not", 0, 0), W(3, 1, 0, 0), "no output");
            yield return Bad("AStartWireIntoASource", G("Not", 0, 0), W(0, 0, -3, 0), "no input");
            yield return Bad("AStartWireIntoAThirdInput", G("And", 0, 0), W(-3, 0, 0, 0, toPort: 2), "input 2");
            yield return Bad("AStartWireIntoANotsSecondInput", G("Not", 0, 0), W(-3, 0, 0, 0, toPort: 1), "input 1");
            yield return Bad("AStartWireLongerThanTheCap", G("Not", 0, 0), W(-3, 0, 0, 0, delay: 4), "delay 4",
                @", ""maxWireDelay"": 3");
            yield return Bad("AStartWireOfNegativeDelay", G("Not", 0, 0), W(-3, 0, 0, 0, delay: -1), "delay -1");
            yield return Bad("AStartDearerThanTheDelayBudget", G("Not", 0, 0),
                W(-3, 0, 0, 0, delay: 2) + ", " + W(0, 0, 3, 1, delay: 2), "delayBudget", @", ""delayBudget"": 1");
            yield return Bad("AStartWireTwice", G("Not", 0, 0), W(-3, 0, 0, 0) + ", " + W(-3, 0, 0, 0), "repeats");
            yield return Bad("TwoStartWiresIntoOneInput", G("And", 0, 0) + ", " + G("Not", 0, 1),
                W(-3, 0, 0, 0) + ", " + W(0, 1, 0, 0), "second wire into input 0");
        }

        private static TestCaseData Bad(string name, string gates, string wires, string reason, string limits = "") =>
            new TestCaseData(gates, wires, limits, reason).SetName(name + "_IsRefusedWithAReason");

        /// <summary>
        /// A start the player could not have built is refused, with a reason naming the problem --
        /// and before anything is placed, since the board throws on some of these rather than saying.
        /// </summary>
        [TestCaseSource(nameof(BadStarts))]
        public void ABadStart_IsRefused(string gates, string wires, string limits, string reason)
        {
            LevelLoadResult result = WithStart(gates, wires, limits);

            AssertRefused(result);
            StringAssert.Contains(reason, result.Error);
        }
    }
}
