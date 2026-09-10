using System.IO;
using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Completion surviving a restart, and every way a save file can be unreadable.
    /// </summary>
    /// <remarks>
    /// Driven against a scratch file rather than the real save, so these never touch whatever
    /// progress is sitting in the player's own data folder.
    ///
    /// Most of what is here is about failure. A save file is the one piece of state the game cannot
    /// recreate, and also the one most likely to be truncated by a crash or edited by hand -- and
    /// losing a session to a stack trace on startup is far worse than losing the record.
    /// </remarks>
    public class ProgressStoreTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "bitsorter-progress-test.json");
            Delete();
        }

        [TearDown]
        public void TearDown() => Delete();

        private void Delete()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        // -----------------------------------------------------------------
        // The point of the whole thing
        // -----------------------------------------------------------------

        [Test]
        public void CompletionSurvivesARestart()
        {
            var first = new ProgressStore(_path);
            first.Load();
            first.MarkComplete("route-the-bit");

            // A second store reading the same file is what "restarting the game" means here.
            var second = new ProgressStore(_path);
            second.Load();

            Assert.IsTrue(second.IsComplete("route-the-bit"),
                "a level solved in one session must still be solved in the next");
        }

        [Test]
        public void OnlyTheLevelsActuallySolvedAreRemembered()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkComplete("half-adder");
            store.MarkComplete("four-corners");

            var reloaded = new ProgressStore(_path);
            reloaded.Load();

            Assert.IsTrue(reloaded.IsComplete("half-adder"));
            Assert.IsTrue(reloaded.IsComplete("four-corners"));
            Assert.IsFalse(reloaded.IsComplete("carry-the-one"), "never solved");
            Assert.AreEqual(2, reloaded.CompletedCount);
        }

        [Test]
        public void MarkingTheSameLevelTwice_ChangesNothing()
        {
            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsTrue(store.MarkComplete("half-adder"), "the first time is news");
            Assert.IsFalse(store.MarkComplete("half-adder"), "the second is not");

            Assert.AreEqual(1, store.CompletedCount, "replaying a level must not duplicate it");
        }

        [Test]
        public void ClearingForgetsEverything_OnDiskToo()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkComplete("route-the-bit");
            store.Clear();

            var reloaded = new ProgressStore(_path);
            reloaded.Load();

            Assert.AreEqual(0, reloaded.CompletedCount);
        }

        // -----------------------------------------------------------------
        // Every way the file can be unusable
        // -----------------------------------------------------------------

        [Test]
        public void NoFileAtAll_IsAFreshStartRatherThanAnError()
        {
            var store = new ProgressStore(_path);
            store.Load();

            Assert.AreEqual(0, store.CompletedCount);
            Assert.IsFalse(store.IsComplete("route-the-bit"));
            Assert.IsNull(store.LastError, "a first run is not a failure");
        }

        [Test]
        public void ATruncatedFile_LosesTheRecordRatherThanTheSession()
        {
            File.WriteAllText(_path, "{\"completed\":[\"route-the-b");

            var store = new ProgressStore(_path);

            Assert.DoesNotThrow(() => store.Load(), "a half-written save must not crash the game");
            Assert.AreEqual(0, store.CompletedCount);
            Assert.IsNotNull(store.LastError, "but it should say so somewhere");
        }

        [Test]
        public void CompleteNonsense_IsToleratedToo()
        {
            File.WriteAllText(_path, "this is not json at all");

            var store = new ProgressStore(_path);

            Assert.DoesNotThrow(() => store.Load());
            Assert.AreEqual(0, store.CompletedCount);
        }

        [Test]
        public void AFileWithNoCompletedKey_ReadsAsNobodyRatherThanNull()
        {
            // The JsonUtility trap this format was shaped around: a missing key comes back as a null
            // array, not an empty one. Anything walking it without a guard would throw on startup.
            File.WriteAllText(_path, "{}");

            var store = new ProgressStore(_path);

            Assert.DoesNotThrow(() => store.Load());
            Assert.AreEqual(0, store.CompletedCount);
            Assert.IsFalse(store.IsComplete("route-the-bit"));
        }

        [Test]
        public void AnEmptyFile_IsAFreshStart()
        {
            File.WriteAllText(_path, string.Empty);

            var store = new ProgressStore(_path);

            Assert.DoesNotThrow(() => store.Load());
            Assert.AreEqual(0, store.CompletedCount);
        }

        [Test]
        public void ANullEntryInTheList_DoesNotBecomeACompletedLevel()
        {
            File.WriteAllText(_path, "{\"completed\":[\"half-adder\",null,\"\"]}");

            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsTrue(store.IsComplete("half-adder"), "the real entry survives");
            Assert.AreEqual(1, store.CompletedCount, "the empty ones are not levels");
            Assert.IsFalse(store.IsComplete(null));
            Assert.IsFalse(store.IsComplete(string.Empty));
        }

        // -----------------------------------------------------------------
        // First-time hints
        // -----------------------------------------------------------------

        [Test]
        public void AHintShownOnce_IsNeverShownAgain()
        {
            var first = new ProgressStore(_path);
            first.Load();

            Assert.IsFalse(first.HasSeenHint(HintRules.Stalled), "nobody has seen anything yet");
            Assert.IsTrue(first.MarkHintSeen(HintRules.Stalled), "the first time is the first time");
            Assert.IsFalse(first.MarkHintSeen(HintRules.Stalled), "and only once");

            var second = new ProgressStore(_path);
            second.Load();

            Assert.IsTrue(second.HasSeenHint(HintRules.Stalled),
                "a hint shown in one session must not come back in the next");
        }

        [Test]
        public void HintsAreTrackedSeparatelyFromEachOther()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkHintSeen(HintRules.Collision);

            Assert.IsTrue(store.HasSeenHint(HintRules.Collision));
            Assert.IsFalse(store.HasSeenHint(HintRules.Stalled), "one seen is not all seen");
        }

        [Test]
        public void ASaveWrittenBeforeHintsExisted_StillLoads()
        {
            // The array is absent, not empty. JsonUtility cannot tell those apart for a value type,
            // which is the trap this whole file format was shaped around -- so each array is guarded
            // on its own and an older save keeps its completions.
            File.WriteAllText(_path, "{\"completed\":[\"route-the-bit\"]}");

            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsNull(store.LastError, "an older save is not a broken one");
            Assert.IsTrue(store.IsComplete("route-the-bit"), "completions survive");
            Assert.IsFalse(store.HasSeenHint(HintRules.Stalled), "and no hint counts as shown");
        }

        [Test]
        public void AnEmptyHintId_IsNeverSeenAndNeverRecorded()
        {
            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsFalse(store.MarkHintSeen(null));
            Assert.IsFalse(store.MarkHintSeen(string.Empty));
            Assert.IsFalse(store.HasSeenHint(null));
            Assert.IsFalse(store.HasSeenHint(string.Empty));
        }

        [Test]
        public void ClearingProgressAlsoForgetsTheHints()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkComplete("route-the-bit");
            store.MarkHintSeen(HintRules.WireDelay);

            store.Clear();

            Assert.IsFalse(store.HasSeenHint(HintRules.WireDelay),
                "starting over means meeting the game again");

            var reloaded = new ProgressStore(_path);
            reloaded.Load();

            Assert.IsFalse(reloaded.HasSeenHint(HintRules.WireDelay), "and on disk too");
        }

        // -----------------------------------------------------------------
        // Where it lives
        // -----------------------------------------------------------------

        [Test]
        public void TheRealSaveLivesInThePlayersDataFolder()
        {
            // Not in Assets, which would put one player's progress in the repository, and not in the
            // registry, which cannot be inspected or deleted by hand when something goes wrong.
            Assert.IsNotEmpty(ProgressStore.DefaultPath);
            StringAssert.EndsWith(".json", ProgressStore.DefaultPath);
        }
    }
}
