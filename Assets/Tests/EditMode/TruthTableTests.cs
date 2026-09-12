using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The level's own vectors, shown as a table.
    /// </summary>
    /// <remarks>
    /// Exists because four-corners was unsolvable in practice: its goal had to describe an eight-row
    /// function in prose, which nobody can hold in their head while wiring. The data was always
    /// there. What these tests protect is that the table is *derived* -- if it ever stopped agreeing
    /// with the streams and expectations the grader uses, it would confidently mislead.
    /// </remarks>
    public class TruthTableTests
    {
        private static LevelDefinition Load(string name)
        {
            LevelLoadResult result = LevelLoader.Load(name, LevelTestFixtures.Board);
            Assert.IsTrue(result.IsValid, result.Error);
            return result.Level;
        }

        private static string[] Lines(string table) =>
            table.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        [Test]
        public void ATableHasAHeaderARuleAndOneRowPerVector()
        {
            LevelDefinition level = Load("four-corners");
            string[] lines = Lines(TruthTable.Format(level));

            Assert.AreEqual(level.VectorCount + 2, lines.Length,
                "header, rule, then eight rows");
        }

        [Test]
        public void TheHeaderNamesEverySourceAndEverySink()
        {
            string[] lines = Lines(TruthTable.Format(Load("carry-the-one")));
            string header = lines[0];

            foreach (string id in new[] { "a", "b", "cin", "sum", "cou" })
                StringAssert.Contains(id, header, $"header should name {id}");
        }

        [Test]
        public void EveryRowMatchesTheStreamsTheGraderActuallyUses()
        {
            // The assertion that makes this a derivation rather than a second copy of the function.
            LevelDefinition level = Load("four-corners");
            string[] lines = Lines(TruthTable.Format(level));

            LevelFixture a = level.FixtureById("a");
            LevelFixture b = level.FixtureById("b");
            LevelFixture c = level.FixtureById("c");
            string expected = level.Expectations[0].Values;

            for (int vector = 0; vector < level.VectorCount; vector++)
            {
                string row = lines[vector + 2];
                string digits = row.Replace(" ", string.Empty).Replace("|", string.Empty);

                string want =
                    $"{(int)a.Stream[vector]}{(int)b.Stream[vector]}{(int)c.Stream[vector]}" +
                    expected[vector];

                Assert.AreEqual(want, digits, $"row {vector}");
            }
        }

        [Test]
        public void ASilentVectorShowsAsAGapRatherThanADash()
        {
            // route-the-bit expects nothing in one of its bins. A dash beside a column of noughts
            // reads as a minus sign, which is why it is a dot.
            string table = TruthTable.Format(Load("route-the-bit"));

            StringAssert.Contains(".", table);
        }

        [Test]
        public void BothSinksGetAColumn()
        {
            string[] lines = Lines(TruthTable.Format(Load("half-adder")));

            // The rule under the header carries a '+' at the crossing rather than a bar, the way an
            // ASCII table does, so it is checked separately rather than loosening the rule for all.
            Assert.AreEqual(1, CountOf(lines[1], '+'), "the rule should cross with a plus");

            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 1)
                    continue;

                Assert.AreEqual(1, CountOf(lines[i], '|'),
                    $"one bar between inputs and outputs, on line {i}");
            }

            string header = lines[0];
            StringAssert.Contains("sum", header);
            StringAssert.Contains("car", header);
        }

        /// <summary>
        /// No two columns share a heading, on any shipped level.
        /// </summary>
        /// <remarks>
        /// Columns were padded to a fixed three characters and anything longer was truncated, which
        /// collided the moment two names shared a prefix. route-the-bit grades `binOne` and
        /// `binZero`, and both render as "bin" -- so the very first level, whose whole lesson is that
        /// one bin must stay empty, showed two output columns with identical headings and no way to
        /// tell which was which.
        ///
        /// Checked across every level rather than that one, because the next colliding pair will be
        /// somewhere else.
        /// </remarks>
        [Test]
        public void NoTwoColumnsShareAHeading()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);

                string header = Lines(TruthTable.Format(parsed.Level))[0];

                string[] headings = header.Split(
                    new[] { ' ', '|' }, System.StringSplitOptions.RemoveEmptyEntries);

                var seen = new System.Collections.Generic.HashSet<string>();

                foreach (string heading in headings)
                {
                    Assert.IsTrue(seen.Add(heading),
                        $"{asset.name}: two columns are both headed '{heading}', so the table " +
                        $"cannot be read. Header: {header.Trim()}");
                }
            }
        }

        /// <summary>
        /// A heading is the fixture's whole name, so the table and the goal quote the same string.
        /// </summary>
        /// <remarks>
        /// The pair to the test above: distinctness alone could be had by numbering the columns,
        /// which would be distinct and meaningless. A level's goal names its bins -- "leave the
        /// other bin empty" is about `binZero` -- so the table has to use the name the player is
        /// reading elsewhere.
        /// </remarks>
        [Test]
        public void AHeadingIsTheWholeFixtureName()
        {
            LevelDefinition level = Load("route-the-bit");
            string header = Lines(TruthTable.Format(level))[0];

            foreach (string id in new[] { "in", "binOne", "binZero" })
            {
                StringAssert.Contains(id, header,
                    $"the header should name '{id}' in full, not an abbreviation of it");
            }
        }

        [Test]
        public void EveryShippedLevelProducesATable()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);

                string table = TruthTable.Format(parsed.Level);

                Assert.IsNotEmpty(table, $"{asset.name} produced no table");
                Assert.AreEqual(parsed.Level.VectorCount + 2, Lines(table).Length, asset.name);
            }
        }

        [Test]
        public void ANullLevelIsAnEmptyTableRatherThanACrash()
        {
            Assert.IsEmpty(TruthTable.Format(null));
        }

        // -----------------------------------------------------------------
        // Measuring a table, so a panel can fit it
        // -----------------------------------------------------------------

        /// <summary>
        /// The measurement <see cref="HelpPanel"/> sizes itself from.
        /// </summary>
        /// <remarks>
        /// Columns are as wide as the longest fixture name now that nothing is truncated, so the
        /// panel can no longer assume a fixed width -- carry-the-one needs more room than four
        /// corners does. Measured off the finished table so the panel and the text inside it cannot
        /// disagree, which means this function is the seam and is worth pinning.
        /// </remarks>
        [Test]
        public void WidestLine_IsTheLongestRow()
        {
            Assert.AreEqual(0, TruthTable.WidestLine(null), "nothing to measure");
            Assert.AreEqual(0, TruthTable.WidestLine(string.Empty));

            Assert.AreEqual(5, TruthTable.WidestLine("abc\nabcde\na\n"),
                "the longest row decides, wherever it sits");

            Assert.AreEqual(4, TruthTable.WidestLine("ab\nabcd"),
                "a table with no trailing newline still counts its last row");
        }

        [Test]
        public void WidestLine_MatchesEveryRowOfEveryShippedTable()
        {
            // Every row is padded to the same width, so the measurement is also the width of each
            // individual line. If those two ever diverge the table has gone ragged.
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, asset.name);

                string table = TruthTable.Format(parsed.Level);
                int widest = TruthTable.WidestLine(table);

                Assert.Greater(widest, 0, $"{asset.name} measured as no width at all");

                foreach (string line in Lines(table))
                {
                    Assert.AreEqual(widest, line.Length,
                        $"{asset.name}: '{line}' is not the width the panel will be sized to");
                }
            }
        }

        [Test]
        public void ColumnsLineUpAcrossEveryRow()
        {
            // A table whose columns wander is worse than no table. Every line has to be the same
            // width for the monospaced block to read as a grid.
            foreach (string name in new[] { "four-corners", "carry-the-one", "route-the-bit" })
            {
                string[] lines = Lines(TruthTable.Format(Load(name)));
                int width = lines[0].Length;

                foreach (string line in lines)
                    Assert.AreEqual(width, line.Length, $"{name}: ragged row '{line}'");
            }
        }

        private static int CountOf(string text, char c)
        {
            int count = 0;
            foreach (char x in text)
            {
                if (x == c)
                    count++;
            }

            return count;
        }
    }
}
