using System.Collections.Generic;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="KarnaughMap"/>: which levels get a map, and what each map says.
    /// </summary>
    /// <remarks>
    /// The map is derived from the same streams and expectations as the truth table, so these hold
    /// it to the level rather than to a second copy of the function.
    /// </remarks>
    public class KarnaughMapTests
    {
        private static LevelDefinition Shipped(string name)
        {
            LevelLoadResult result = LevelLoader.Load(name, LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, $"{name}: {result.Error}");
            return result.Level;
        }

        private static IEnumerable<KeyValuePair<string, LevelDefinition>> EveryShippedLevel()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, $"{asset.name}: {parsed.Error}");
                yield return new KeyValuePair<string, LevelDefinition>(asset.name, parsed.Level);
            }
        }

        /// <summary>Two sources, A and B, with the given streams, into one bin.</summary>
        private static LevelDefinition TwoInputs(
            string a, string b, string expected, int clockPeriod = 1)
        {
            return LevelTestFixtures.Parse($@"{{
                ""name"": ""Two inputs"",
                ""tickLimit"": 100,
                ""clockPeriod"": {clockPeriod},
                ""fixtures"": [
                    {{ ""id"": ""a"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -3, ""y"":  1 }}, ""stream"": ""{a}"" }},
                    {{ ""id"": ""b"",   ""kind"": ""Source"", ""cell"": {{ ""x"": -3, ""y"": -1 }}, ""stream"": ""{b}"" }},
                    {{ ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": {{ ""x"":  3, ""y"":  0 }} }}
                ],
                ""budget"": [ {{ ""kind"": ""And"", ""count"": 1 }} ],
                ""expected"": [ {{ ""sink"": ""out"", ""values"": ""{expected}"" }} ]
            }}");
        }

        /// <summary>
        /// Four sources counting from 0000 to 1001 -- ten of the sixteen combinations -- into one
        /// bin wanting A + B(C + D). The shape of a level whose missing rows are don't-cares.
        /// </summary>
        private static LevelDefinition TenOfSixteen()
        {
            return LevelTestFixtures.Parse(@"{
                ""name"": ""Ten of sixteen"",
                ""tickLimit"": 100,
                ""fixtures"": [
                    { ""id"": ""a"",   ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"":  2 }, ""stream"": ""0000000011"" },
                    { ""id"": ""b"",   ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"":  1 }, ""stream"": ""0000111100"" },
                    { ""id"": ""c"",   ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"": -1 }, ""stream"": ""0011001100"" },
                    { ""id"": ""d"",   ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"": -2 }, ""stream"": ""0101010101"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  4, ""y"":  0 } }
                ],
                ""budget"": [ { ""kind"": ""And"", ""count"": 2 }, { ""kind"": ""Or"", ""count"": 2 } ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""0000011111"" } ]
            }");
        }

        // -----------------------------------------------------------------
        // Which levels get a map
        // -----------------------------------------------------------------

        [Test]
        public void ACombinationalLevelOfTwoOrThreeInputs_HasAMap()
        {
            Assert.IsTrue(KarnaughMap.Applies(Shipped("four-corners")), "three inputs");
            Assert.IsTrue(KarnaughMap.Applies(Shipped("half-adder")), "two inputs");
        }

        [Test]
        public void OneInput_HasNoMap()
        {
            Assert.IsFalse(KarnaughMap.Applies(Shipped("route-the-bit")));
        }

        [Test]
        public void NoSequentialLevel_HasAMap()
        {
            int sequential = 0;

            foreach (KeyValuePair<string, LevelDefinition> level in EveryShippedLevel())
            {
                if (!LevelCatalog.IsSequential(level.Value))
                    continue;

                sequential++;
                Assert.IsFalse(KarnaughMap.Applies(level.Value), level.Key);
            }

            Assert.Greater(sequential, 0, "sanity: no sequential levels were found");
        }

        [Test]
        public void AClockedLevel_HasNoMap_EvenWithNoRegister()
        {
            Assert.IsTrue(KarnaughMap.Applies(TwoInputs("0011", "0101", "0001")), "sanity: unclocked");
            Assert.IsFalse(KarnaughMap.Applies(TwoInputs("0011", "0101", "0001", clockPeriod: 2)));
        }

        [Test]
        public void ALevelThatTestsACombinationTwice_HasNoMap()
        {
            // AB = 11 is tested on the second vector and again on the fourth: one cell, two rows.
            Assert.IsFalse(KarnaughMap.Applies(TwoInputs("0101", "0111", "0100")));
        }

        [Test]
        public void ALevelWhoseOnlyBinMustStayEmpty_HasNoMap()
        {
            Assert.IsFalse(KarnaughMap.Applies(TwoInputs("0011", "0101", "----")));
            CollectionAssert.IsEmpty(KarnaughMap.Bins(TwoInputs("0011", "0101", "----")));
        }

        [Test]
        public void ALevelWithNoMap_FormatsNothing()
        {
            LevelDefinition level = Shipped("route-the-bit");

            Assert.AreEqual(string.Empty, KarnaughMap.Format(level, "binOne"));
            Assert.AreEqual(0, KarnaughMap.LineCount(level));
            CollectionAssert.IsEmpty(KarnaughMap.Bins(level));
            Assert.AreEqual(string.Empty, KarnaughMap.Format(null, "out"));
        }

        // -----------------------------------------------------------------
        // What a map says
        // -----------------------------------------------------------------

        [Test]
        public void FourCorners_MapsToTheExpectedText()
        {
            // 11100111 in ABC order. A = 0: BC = 00, 01, 11, 10 are rows 000, 001, 011, 010 -> 1 1 0 1.
            // A = 1: rows 100, 101, 111, 110 -> 0 1 1 1.
            const string expected =
                "   BC\n" +
                "A  00 01 11 10\n" +
                "0   1  1  0  1\n" +
                "1   0  1  1  1\n";

            Assert.AreEqual(expected, KarnaughMap.Format(Shipped("four-corners"), "out"));
            Assert.AreEqual(4, KarnaughMap.LineCount(Shipped("four-corners")));
        }

        [Test]
        public void TheColumns_RunInGrayOrder()
        {
            string[] lines = KarnaughMap.Format(Shipped("four-corners"), "out").Split('\n');

            StringAssert.EndsWith("00 01 11 10", lines[1], "the codes must change one bit at a time");
        }

        [Test]
        public void TwoInputs_MakeATwoByTwoMap()
        {
            const string expected =
                "    B\n" +
                "A   0  1\n" +
                "0   0  0\n" +
                "1   0  1\n";

            Assert.AreEqual(expected, KarnaughMap.Format(TwoInputs("0011", "0101", "0001"), "out"));
        }

        [Test]
        public void TheHalfAdder_HasAMapPerBin_InItsOwnOrder()
        {
            LevelDefinition level = Shipped("half-adder");

            CollectionAssert.AreEqual(new[] { "sum", "carry" }, KarnaughMap.Bins(level));

            string sum = KarnaughMap.Format(level, "sum");
            string carry = KarnaughMap.Format(level, "carry");

            StringAssert.EndsWith("0   0  1\n1   1  0\n", sum, "SUM is A XOR B");
            StringAssert.EndsWith("0   0  0\n1   0  1\n", carry, "CARRY is A AND B");
        }

        [Test]
        public void ABinTheLevelDoesNotGrade_HasNoMap()
        {
            Assert.AreEqual(string.Empty, KarnaughMap.Format(Shipped("half-adder"), "nothing"));
            Assert.AreEqual(string.Empty, KarnaughMap.Format(Shipped("half-adder"), "a"),
                "a source is not a bin");
        }

        [Test]
        public void ACombinationNoRowTests_IsADontCare()
        {
            // Rows 1010 to 1111 never happen. In a 4 x 4 map with rows AB and columns CD, those are
            // the whole AB = 11 row and the right half of AB = 10.
            const string expected =
                "    CD\n" +
                "AB  00 01 11 10\n" +
                "00   0  0  0  0\n" +
                "01   0  1  1  1\n" +
                "11   x  x  x  x\n" +
                "10   1  1  x  x\n";

            Assert.AreEqual(expected, KarnaughMap.Format(TenOfSixteen(), "out"));
            Assert.AreEqual(6, KarnaughMap.LineCount(TenOfSixteen()));
        }

        [Test]
        public void AnXInTheFile_StaysAnX_AndASilentVectorIsADot()
        {
            // AB = 00 -> x, 01 -> -, 10 -> 1, 11 -> 0.
            string map = KarnaughMap.Format(TwoInputs("0011", "0101", "x-10"), "out");

            StringAssert.EndsWith("0   x  .\n1   1  0\n", map);
        }

        [Test]
        public void InputsWithLongerNames_AreSpacedApart()
        {
            LevelDefinition level = LevelTestFixtures.Parse(@"{
                ""name"": ""Named inputs"",
                ""tickLimit"": 100,
                ""fixtures"": [
                    { ""id"": ""i3"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"":  2 }, ""stream"": ""0000000011111111"" },
                    { ""id"": ""i2"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"":  1 }, ""stream"": ""0000111100001111"" },
                    { ""id"": ""i1"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"": -1 }, ""stream"": ""0011001100110011"" },
                    { ""id"": ""i0"", ""kind"": ""Source"", ""cell"": { ""x"": -4, ""y"": -2 }, ""stream"": ""0101010101010101"" },
                    { ""id"": ""v"",  ""kind"": ""Sink"",   ""cell"": { ""x"":  4, ""y"":  0 } }
                ],
                ""budget"": [ { ""kind"": ""Or"", ""count"": 3 } ],
                ""expected"": [ { ""sink"": ""v"", ""values"": ""0111111111111111"" } ]
            }");

            string[] lines = KarnaughMap.Format(level, "v").Split('\n');

            Assert.AreEqual("       I1 I0", lines[0], "the column inputs, over the first code");
            Assert.AreEqual("I3 I2  00 01 11 10", lines[1]);
            Assert.AreEqual("   00   0  1  1  1", lines[2]);
        }

        // -----------------------------------------------------------------
        // Its size
        // -----------------------------------------------------------------

        /// <summary>
        /// What keeps switching between the table and a map from resizing the help panel -- and a
        /// resized panel re-frames the board, since the camera fits it into what the panel leaves.
        /// </summary>
        [Test]
        public void NoMap_IsTallerThanItsLevelsTable()
        {
            int mapped = 0;

            foreach (KeyValuePair<string, LevelDefinition> level in EveryShippedLevel())
            {
                if (!KarnaughMap.Applies(level.Value))
                    continue;

                mapped++;
                int tableLines = TruthTable.Format(level.Value).Split('\n').Length - 1;

                foreach (string bin in KarnaughMap.Bins(level.Value))
                {
                    string map = KarnaughMap.Format(level.Value, bin);

                    Assert.AreEqual(KarnaughMap.LineCount(level.Value), map.Split('\n').Length - 1,
                        $"{level.Key}/{bin}: LineCount disagrees with the map it describes");
                    Assert.LessOrEqual(KarnaughMap.LineCount(level.Value), tableLines,
                        $"{level.Key}/{bin}: the map is taller than the table");
                }
            }

            Assert.Greater(mapped, 0, "sanity: no shipped level has a map");
        }
    }
}
