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
