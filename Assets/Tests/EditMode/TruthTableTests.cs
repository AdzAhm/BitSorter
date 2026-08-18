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
