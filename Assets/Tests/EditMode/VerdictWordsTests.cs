using System.Collections.Generic;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="LevelGrader.Words"/>: what a verdict says, and that every one fits its line.
    /// </summary>
    /// <remarks>
    /// A verdict is one line under the banner, at <see cref="StatusBanner.VerdictType"/>, as wide as
    /// the banner's text. Two verdicts -- a run that never settled, and a right answer that was too
    /// slow -- ran past it on a real board, and nothing measured them. This builds every verdict
    /// every shipped level can give, with the level's own rows, inputs and bins, and measures each.
    /// </remarks>
    public class VerdictWordsTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private static IEnumerable<LevelDefinition> EveryGradedLevel()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, Board);
                Assert.IsTrue(parsed.IsValid, asset.name + ": " + parsed.Error);
                yield return parsed.Level;
            }

            // Graded as well, though it is not in the run.
            yield return TutorialLevel.Build(Board);
        }

        [Test]
        public void EveryVerdictAShippedLevelCanGive_FitsItsLine()
        {
            var tooLong = new List<string>();
            int measured = 0;

            foreach (LevelDefinition level in EveryGradedLevel())
            {
                var lines = new List<string>
                {
                    StatusBanner.FailPrefix + LevelGrader.Words.NeverSettled(level.TickLimit),
                    StatusBanner.FailPrefix + LevelGrader.Words.Destroyed(99),
                    StatusBanner.PassPrefix + LevelGrader.Words.Passed(level.VectorCount),
                };

                foreach (LevelExpectation expectation in level.Expectations)
                {
                    string sink = expectation.SinkId;

                    lines.Add(StatusBanner.FailPrefix + LevelGrader.Words.EmptyBinFilled(sink, 99));
                    lines.Add(StatusBanner.FailPrefix + LevelGrader.Words.TooMany(sink, 99));
                    lines.Add(StatusBanner.FailPrefix + LevelGrader.Words.TooSlow(
                        sink, 999, level.HasLatencyLimit ? level.MaxLatency : 99));

                    // Every row, and the one after the last input that a register's tail lands in.
                    for (int vector = 0; vector <= level.VectorCount; vector++)
                    {
                        string row = LevelGrader.Row(level, vector);

                        lines.Add(StatusBanner.FailPrefix + LevelGrader.Words.Wrong(row, sink, 1, 0));
                        lines.Add(StatusBanner.FailPrefix + LevelGrader.Words.NothingArrived(row, sink, "a bit"));
                        lines.Add(StatusBanner.FailPrefix + LevelGrader.Words.ShouldStayEmpty(row, sink));
                    }
                }

                foreach (string line in lines)
                {
                    measured++;
                    float width = UiTheme.TextWidth(line, StatusBanner.VerdictType);

                    if (width > UiTheme.BannerTextWidth)
                        tooLong.Add($"{width:F0}px: {line}");
                }
            }

            Assert.Greater(measured, 100, "sanity: hardly any verdicts were built, so nothing was measured");
            CollectionAssert.IsEmpty(tooLong,
                $"these verdicts run past the {UiTheme.BannerTextWidth}px line under the banner:\n  " +
                string.Join("\n  ", tooLong));
        }

        /// <summary>A level with these sources, each emitting the given bits, into one bin.</summary>
        private static LevelDefinition Inputs(params (string id, string stream)[] sources)
        {
            var fixtures = new System.Text.StringBuilder();
            int y = 3;

            foreach (var source in sources)
            {
                fixtures.Append($@"{{ ""id"": ""{source.id}"", ""kind"": ""Source"", ""cell"": {{ ""x"": -6, ""y"": {y--} }}, ""stream"": ""{source.stream}"" }},");
            }

            string zeros = new string('0', sources[0].stream.Length);

            return LevelLoader.Parse($@"{{
                ""name"": ""Inputs"", ""tickLimit"": 100,
                ""fixtures"": [ {fixtures} {{ ""id"": ""out"", ""kind"": ""Sink"", ""cell"": {{ ""x"": 6, ""y"": 0 }} }} ],
                ""budget"": [ {{ ""kind"": ""And"", ""count"": 1 }} ],
                ""expected"": [ {{ ""sink"": ""out"", ""values"": ""{zeros}"" }} ]
            }}", new Vector2Int(6, 3)).Level;
        }

        [Test]
        public void UpToFourInputs_AreNamedOneByOne()
        {
            Assert.AreEqual("Row 2 (A = 1, B = 0, C = 1)",
                LevelGrader.Row(Inputs(("a", "01"), ("b", "00"), ("c", "01")), 1));

            Assert.AreEqual("Row 2 (A1 = 1, A0 = 0, B1 = 1, B0 = 1)",
                LevelGrader.Row(Inputs(("a1", "01"), ("a0", "00"), ("b1", "01"), ("b0", "01")), 1));
        }

        /// <summary>Past four, the inputs are one word, the way the table's row reads.</summary>
        [Test]
        public void MoreThanFourInputs_AreOneWord()
        {
            Assert.AreEqual("Row 2 (ABCDE = 10110)",
                LevelGrader.Row(Inputs(("a", "01"), ("b", "00"), ("c", "01"), ("d", "01"), ("e", "00")), 1));

            Assert.AreEqual("Row 2 (A2 A1 A0 B2 B1 B0 = 101100)",
                LevelGrader.Row(Inputs(("a2", "01"), ("a1", "00"), ("a0", "01"), ("b2", "01"), ("b1", "00"), ("b0", "00")), 1));
        }

        /// <summary>
        /// Six two-letter inputs as one word fit the line, where one by one they ran past it: the
        /// reason the form exists.
        /// </summary>
        [Test]
        public void SixInputs_FitTheLine()
        {
            LevelDefinition level = Inputs(
                ("a2", "1"), ("a1", "1"), ("a0", "1"), ("b2", "1"), ("b1", "1"), ("b0", "1"));

            string line = StatusBanner.FailPrefix +
                          LevelGrader.Words.NothingArrived(LevelGrader.Row(level, 0), "cout", "a bit");

            Assert.LessOrEqual(UiTheme.TextWidth(line, StatusBanner.VerdictType), UiTheme.BannerTextWidth, line);
        }

        /// <summary>A bin and an input are named as the board labels them, and a wrong bit says what was wanted.</summary>
        [Test]
        public void AVerdict_NamesThingsAsTheBoardDoes()
        {
            string wrong = LevelGrader.Words.Wrong("Row 1 (IN = 1)", "out", 1, 0);

            StringAssert.Contains("OUT should get 1", wrong);
            StringAssert.Contains("but got 0", wrong);
            StringAssert.DoesNotContain("wanted", wrong, "\"out wanted 1\" read as a verb");
        }
    }
}
