using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Where each level sits in the run, and what happens when two claim the same seat.
    /// </summary>
    /// <remarks>
    /// Play order used to be the ordinal sort of the file names, which put the NAND puzzle before the
    /// half adder and the tutorial seventh. An explicit order field fixes that, but it introduces a
    /// rule no single file can check: two levels must not share a value. <see cref="LevelLoader"/>
    /// only ever sees one file, so the check lives in <see cref="LevelCatalog"/>, which sees them all.
    ///
    /// The sort is a pure function over entries rather than something that reads Resources, for the
    /// same reason LevelLoader splits Load from Parse from Validate: a duplicate can then be tested
    /// without shipping two broken levels to provoke it.
    /// </remarks>
    public class LevelOrderTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        private const string SourceIn =
            @"{ ""id"": ""in"", ""kind"": ""Source"", ""cell"": { ""x"": -3, ""y"": 0 }, ""stream"": ""0"" }";

        private const string BinOne =
            @"{ ""id"": ""binOne"", ""kind"": ""Sink"", ""cell"": { ""x"": 3, ""y"": 1 } }";

        private const string ExpectOne = @"{ ""sink"": ""binOne"", ""values"": ""1"" }";

        /// <summary>A minimal valid level, with whatever order clause the test is varying.</summary>
        private static string Json(string orderClause) =>
            @"{ ""name"": ""Ordered"", ""tickLimit"": 100, " + orderClause +
            $@" ""fixtures"": [{SourceIn}, {BinOne}]," +
            $@" ""expected"": [{ExpectOne}] }}";

        // -----------------------------------------------------------------
        // Parsing
        // -----------------------------------------------------------------

        [Test]
        public void AnOrder_IsCarriedThrough()
        {
            LevelLoadResult result = LevelLoader.Parse(Json(@"""order"": 30,"), Board);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(30, result.Level.Order);
            Assert.IsTrue(result.Level.HasOrder);
        }

        [Test]
        public void AnOmittedOrder_LeavesTheLevelUnplaced()
        {
            // Zero has to keep meaning "unspecified": JsonUtility cannot tell a missing key from an
            // explicit 0, the same reason tickLimit, maxWireDelay and maxLatency all work this way.
            LevelLoadResult result = LevelLoader.Parse(Json(string.Empty), Board);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(0, result.Level.Order);
            Assert.IsFalse(result.Level.HasOrder);
        }

        [Test]
        public void ANegativeOrder_IsRefused()
        {
            LevelLoadResult result = LevelLoader.Parse(Json(@"""order"": -1,"), Board);

            Assert.IsFalse(result.IsValid, "expected a refusal");
            Assert.IsNotNull(result.Error);
        }

        // -----------------------------------------------------------------
        // Sorting
        // -----------------------------------------------------------------

        private static LevelEntry Entry(string file, int order) => new LevelEntry(file, order);

        private static string[] Names(IReadOnlyList<LevelEntry> entries)
        {
            var names = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                names[i] = entries[i].FileName;

            return names;
        }

        [Test]
        public void LevelsSortByTheirOrderField_NotByFileName()
        {
            // Deliberately reverse-alphabetical against the intended order, so a sort that quietly
            // fell back to file names would produce exactly the wrong answer rather than a plausible one.
            var entries = new List<LevelEntry>
            {
                Entry("zebra", 10),
                Entry("apple", 20),
                Entry("mango", 30),
            };

            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(entries, out string error);

            Assert.IsNull(error, error);
            CollectionAssert.AreEqual(new[] { "zebra", "apple", "mango" }, Names(sorted));
        }

        [Test]
        public void LevelsWithNoOrder_FallBackToOrdinalFileName()
        {
            var entries = new List<LevelEntry>
            {
                Entry("charlie", 0),
                Entry("alpha", 0),
                Entry("bravo", 0),
            };

            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(entries, out string error);

            Assert.IsNull(error, error);
            CollectionAssert.AreEqual(new[] { "alpha", "bravo", "charlie" }, Names(sorted));
        }

        [Test]
        public void UnorderedLevels_ComeAfterOrderedOnes()
        {
            // A level dropped into Resources without an order still has to appear somewhere, and the
            // end is the only place that cannot silently displace an authored sequence.
            var entries = new List<LevelEntry>
            {
                Entry("unplaced", 0),
                Entry("second", 20),
                Entry("first", 10),
            };

            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(entries, out string error);

            Assert.IsNull(error, error);
            CollectionAssert.AreEqual(new[] { "first", "second", "unplaced" }, Names(sorted));
        }

        [Test]
        public void EveryLevelSurvivesTheSort()
        {
            var entries = new List<LevelEntry>
            {
                Entry("a", 10), Entry("b", 0), Entry("c", 20), Entry("d", 0),
            };

            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(entries, out _);

            Assert.AreEqual(entries.Count, sorted.Count, "a sort must not lose or duplicate a level");
            CollectionAssert.AreEquivalent(Names(entries), Names(sorted));
        }

        // -----------------------------------------------------------------
        // Duplicates
        // -----------------------------------------------------------------

        [Test]
        public void TwoLevelsSharingAnOrder_FailLoudly()
        {
            // The whole reason the catalogue exists. Silently picking one would give a play order that
            // changes with the file system's enumeration order -- different on someone else's machine,
            // and impossible to reproduce from the level files alone.
            var entries = new List<LevelEntry>
            {
                Entry("first", 10),
                Entry("clash-a", 20),
                Entry("clash-b", 20),
            };

            LevelCatalog.Sort(entries, out string error);

            Assert.IsNotNull(error, "a shared order must be reported, not absorbed");
            StringAssert.Contains("20", error, "the reason should name the value that clashes");
            StringAssert.Contains("clash-a", error, "and both files that claim it");
            StringAssert.Contains("clash-b", error);
        }

        [Test]
        public void ADuplicate_StillReturnsAStableCompleteOrder()
        {
            // Reported, but not fatal. The game still has to start, and it must start the same way
            // twice -- otherwise the bug reproduces differently every run.
            var entries = new List<LevelEntry>
            {
                Entry("clash-b", 20),
                Entry("clash-a", 20),
                Entry("first", 10),
            };

            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(entries, out string error);

            Assert.IsNotNull(error);
            Assert.AreEqual(3, sorted.Count, "no level may be dropped because of a clash");
            CollectionAssert.AreEqual(new[] { "first", "clash-a", "clash-b" }, Names(sorted),
                "a clash breaks the tie on file name, so the order is at least reproducible");
        }

        [Test]
        public void AnEmptyCatalogue_IsNotAnError()
        {
            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(new List<LevelEntry>(), out string error);

            Assert.IsNull(error);
            Assert.AreEqual(0, sorted.Count);
        }

        // -----------------------------------------------------------------
        // The shipped set
        // -----------------------------------------------------------------

        [Test]
        public void EveryShippedLevel_NamesAnOrder_AndNoTwoShareOne()
        {
            // The guard that matters in practice. Adding a level without an order, or reusing a
            // value, is caught here rather than by someone noticing the tutorial moved.
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);
            Assert.Greater(assets.Length, 0, "no level files found");

            var entries = new List<LevelEntry>(assets.Length);

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, Board);
                Assert.IsTrue(parsed.IsValid, $"{asset.name}: {parsed.Error}");

                Assert.IsTrue(parsed.Level.HasOrder,
                    $"{asset.name}.json names no order, so it would sort to the end of the run");

                entries.Add(new LevelEntry(asset.name, parsed.Level.Order));
            }

            LevelCatalog.Sort(entries, out string error);
            Assert.IsNull(error, error);
        }

        [Test]
        public void TheTutorialComesFirst()
        {
            // route-the-bit teaches what a bin is. If anything ever sorts ahead of it, a new player
            // meets a gate before they have been told bits fall into bins.
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);
            var entries = new List<LevelEntry>(assets.Length);

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, Board);
                entries.Add(new LevelEntry(asset.name, parsed.IsValid ? parsed.Level.Order : 0));
            }

            IReadOnlyList<LevelEntry> sorted = LevelCatalog.Sort(entries, out _);

            Assert.AreEqual("route-the-bit", sorted[0].FileName);
        }
    }
}
