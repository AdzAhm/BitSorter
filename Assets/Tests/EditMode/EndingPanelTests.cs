using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// When finishing a level ends the game rather than showing the ordinary win panel.
    /// </summary>
    /// <remarks>
    /// The condition is asserted here rather than left to play testing because two panels read it:
    /// EndingPanel to show itself and WinPanel to stand aside. Getting it wrong shows both at once
    /// or neither, and neither failure is visible until someone has solved all nine levels.
    ///
    /// Driven against a scratch save, so these never touch the player's own progress.
    /// </remarks>
    public class EndingPanelTests
    {
        private string _path;
        private ProgressStore _store;

        private static readonly IReadOnlyList<LevelEntry> Three = new[]
        {
            new LevelEntry("first", 10, "First"),
            new LevelEntry("middle", 20, "Middle"),
            new LevelEntry("last", 30, "Last"),
        };

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "bitsorter-ending-test.json");
            Delete();

            _store = new ProgressStore(_path);
            _store.Load();
        }

        [TearDown]
        public void TearDown() => Delete();

        private void Delete()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        private bool End(int index, string level) =>
            EndingPanel.IsTheEnd(Three, index, level, _store);

        /// <summary>The shipped levels in play order, built the way the game builds them.</summary>
        private static IReadOnlyList<LevelEntry> ShippedCatalogue()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);
            var entries = new List<LevelEntry>(assets.Length);

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, LevelTestFixtures.Board);
                Assert.IsTrue(parsed.IsValid, $"{asset.name}: {parsed.Error}");

                entries.Add(new LevelEntry(asset.name, parsed.Level.Order, parsed.Level.Name));
            }

            IReadOnlyList<LevelEntry> ordered = LevelCatalog.Sort(entries, out string clash);
            Assert.IsNull(clash, clash);

            return ordered;
        }

        // -----------------------------------------------------------------

        [Test]
        public void TheLastLevelWithEverythingElseSolved_IsTheEnd()
        {
            _store.MarkComplete("first");
            _store.MarkComplete("middle");

            Assert.IsTrue(End(2, "last"));
        }

        /// <summary>
        /// The level just passed is not consulted in the store.
        /// </summary>
        /// <remarks>
        /// Whether the tracker has recorded it yet depends on which component's Update ran first,
        /// and an ending that appears or does not depending on component order would be the worst
        /// kind of intermittent.
        /// </remarks>
        [Test]
        public void TheCurrentLevelNeedNotBeRecordedYet()
        {
            _store.MarkComplete("first");
            _store.MarkComplete("middle");

            Assert.IsFalse(_store.IsComplete("last"), "precondition: not recorded yet");
            Assert.IsTrue(End(2, "last"), "the pass that triggered this counts without being saved");
        }

        [Test]
        public void TheLastLevelWithAnEarlierOneUnsolved_IsNotTheEnd()
        {
            _store.MarkComplete("first");

            Assert.IsFalse(End(2, "last"),
                "skipping to the last level from the list must not end the game");
        }

        [Test]
        public void SolvingEverythingButFinishingElsewhere_IsNotTheEnd()
        {
            _store.MarkComplete("first");
            _store.MarkComplete("middle");
            _store.MarkComplete("last");

            Assert.IsFalse(End(1, "middle"),
                "replaying a middle level after finishing must not replay the ending");
        }

        [Test]
        public void AFreshSaveOnTheLastLevel_IsNotTheEnd()
        {
            Assert.IsFalse(End(2, "last"));
        }

        [Test]
        public void NoLevelLoaded_IsNotTheEnd()
        {
            Assert.IsFalse(EndingPanel.IsTheEnd(Three, -1, null, _store));
            Assert.IsFalse(EndingPanel.IsTheEnd(new LevelEntry[0], 0, "x", _store));
            Assert.IsFalse(EndingPanel.IsTheEnd(Three, 2, "last", null));
        }

        /// <summary>
        /// The real catalogue, so the ending cannot be wired to a level count that no longer
        /// matches what ships.
        /// </summary>
        [Test]
        public void OnTheShippedCatalogue_OnlyTheFinalLevelEnds()
        {
            IReadOnlyList<LevelEntry> catalogue = ShippedCatalogue();

            Assert.Greater(catalogue.Count, 1, "there should be several shipped levels");

            for (int i = 0; i < catalogue.Count - 1; i++)
                _store.MarkComplete(catalogue[i].FileName);

            int last = catalogue.Count - 1;

            Assert.IsTrue(
                EndingPanel.IsTheEnd(catalogue, last, catalogue[last].FileName, _store),
                $"solving {catalogue[last].DisplayName} last should end the game");

            Assert.IsFalse(
                EndingPanel.IsTheEnd(catalogue, 0, catalogue[0].FileName, _store),
                "the first level must never end the game");
        }
    }
}
