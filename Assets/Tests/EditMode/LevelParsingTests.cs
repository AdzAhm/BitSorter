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
        public void AnExpectationOfTheWrongLength_IsRefused()
        {
            AssertRefused(Parse(
                Json($"{SourceIn}, {BinOne}", @"{ ""sink"": ""binOne"", ""values"": ""101"" }")));
        }

        [Test]
        public void ADashInASourceStream_IsRefusedAndSaysWhy()
        {
            // A SourceNode emits every tick from tick 0 with no way to skip one, so sparse streams have
            // no honest implementation. LogicCore's core decisions say not to add one preemptively, so
            // the loader refuses rather than emitting something else.
            string sparse =
                @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""1-1"" }";

            LevelLoadResult result = Parse(
                Json($"{sparse}, {BinOne}", @"{ ""sink"": ""binOne"", ""values"": ""111"" }"));

            AssertRefused(result);
            StringAssert.Contains("cannot skip", result.Error,
                "the reason has to explain why, or the author will just try it again");
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
    }
}
