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

        // The two files that sit beside a save: the write in progress, and a copy of a save that
        // could not be read.
        private string TempPath => _path + ".tmp";
        private string UnreadablePath => _path + ".unreadable";

        private void Delete()
        {
            foreach (string path in new[] { _path, TempPath, UnreadablePath })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
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

        /// <summary>
        /// An exercise's best (Part 15d): the fewest gates and the quickest settle, each kept on its own, through a restart --
        /// and kept for an exercise of registers alone, which has no settle to reckon (the #88 review: it kept none).
        /// </summary>
        [Test]
        public void AnExercisesBest_IsKeptPartByPart_ThroughARestart_SettleOrNot()
        {
            var first = new ProgressStore(_path);
            first.Load();
            Assert.AreEqual((true, true), first.RecordExerciseBest("half-adder", 3, 2));
            Assert.AreEqual((true, false), first.RecordExerciseBest("half-adder", 2, 3), "fewer gates, slower");
            Assert.AreEqual((false, true), first.RecordExerciseBest("half-adder", 4, 1), "quicker, more gates");
            Assert.AreEqual((true, false), first.RecordExerciseBest("one-clock-late", 0, null), "no settle: the gates kept all the same");
            first.Save();

            var second = new ProgressStore(_path);
            second.Load();
            Assert.IsTrue(second.TryGetExerciseBest("half-adder", out int gates, out long? settle));
            Assert.AreEqual(2, gates);
            Assert.AreEqual(1L, settle);
            Assert.IsTrue(second.TryGetExerciseBest("one-clock-late", out int registers, out long? none));
            Assert.AreEqual(0, registers);
            Assert.IsNull(none);
            Assert.IsFalse(second.TryGetExerciseBest("odd-one-out", out _, out _));
        }

        /// <summary>
        /// The student's circuits (Part 15a): the ones last opened, latest first, survive a restart; a circuit renamed keeps
        /// its document and its place among them, and refuses a name another has; one deleted is gone from both.
        /// </summary>
        [Test]
        public void TheCircuitsLastOpened_ARenameAndADelete_SurviveARestart()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.SetProject("doc/adder", BitSorter.Board.Project.New("adder"));
            store.SetProject("doc/alu", BitSorter.Board.Project.New("alu"));
            store.Touch("doc/adder");
            store.Touch("doc/alu");
            store.LastOpen = "doc/adder";
            Assert.IsFalse(store.RenameProject("doc/adder", "doc/alu"), "a name another has");
            Assert.IsTrue(store.RenameProject("doc/adder", "doc/half adder"));
            store.Save();

            var reloaded = new ProgressStore(_path);
            reloaded.Load();
            CollectionAssert.AreEqual(new[] { "doc/alu", "doc/half adder" }, reloaded.Recent, "the latest first, the renamed one in its place");
            Assert.IsTrue(reloaded.TryGetProject("doc/half adder", out BitSorter.Board.Project renamed, null));
            Assert.AreEqual("adder", renamed.Top, "the same document under its new name");
            Assert.AreEqual("doc/half adder", reloaded.LastOpen, "the last thing open, followed through the rename (Part 15a)");

            reloaded.RemoveProject("doc/alu");
            reloaded.Save();
            var again = new ProgressStore(_path);
            again.Load();
            Assert.IsFalse(again.HasProject("doc/alu"));
            CollectionAssert.AreEqual(new[] { "doc/half adder" }, again.Recent);
        }

        /// <summary>
        /// A circuit locked against change (Part 15h-4d) stays locked over a restart, follows a rename, goes with a delete; a
        /// file from before the lock reads as nothing locked.
        /// </summary>
        [Test]
        public void ALockedCircuit_StaysLockedOverARestart_AndFollowsARenameAndADelete()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.SetProject("doc/adder", BitSorter.Board.Project.New("adder"));
            store.SetProject("doc/alu", BitSorter.Board.Project.New("alu"));
            Assert.IsFalse(store.IsLocked("doc/adder"), "nothing locked to begin with");
            store.SetLocked("doc/adder", true);
            store.SetLocked("doc/adder", true);
            Assert.IsTrue(store.RenameProject("doc/adder", "doc/half adder"));
            store.Save();

            var reloaded = new ProgressStore(_path);
            reloaded.Load();
            Assert.IsTrue(reloaded.IsLocked("doc/half adder"), "locked under its new name, over a restart");
            Assert.IsFalse(reloaded.IsLocked("doc/adder"));
            Assert.IsFalse(reloaded.IsLocked("doc/alu"));

            reloaded.SetLocked("doc/alu", true);
            reloaded.SetLocked("doc/half adder", false);
            reloaded.RemoveProject("doc/alu");
            Assert.IsFalse(reloaded.IsLocked("doc/half adder"), "unlocked");
            Assert.IsFalse(reloaded.IsLocked("doc/alu"), "and a circuit deleted takes its lock with it");

            // An import kept as it came in follows the circuit through a rename and a restart, and goes with a delete.
            reloaded.SetProject("doc/mux", BitSorter.Board.Project.New("mux"));
            reloaded.SetImported("doc/mux", BitSorter.Board.Project.New("mux"));
            reloaded.SetProject("doc/mux", BitSorter.Board.Project.New("changed"));   // the circuit changed: its copy is not
            Assert.IsTrue(reloaded.RenameProject("doc/mux", "doc/mux2"));
            reloaded.Save();
            var restarted = new ProgressStore(_path);
            restarted.Load();
            Assert.IsTrue(restarted.HasImported("doc/mux2"), "as it came in, under its new name, over a restart");
            Assert.IsTrue(restarted.TryGetImported("doc/mux2", out BitSorter.Board.Project original, null));
            Assert.AreEqual("mux", original.Top);
            Assert.IsFalse(restarted.HasImported("doc/mux"));
            restarted.RemoveProject("doc/mux2");
            Assert.IsFalse(restarted.HasImported("doc/mux2"), "and gone with the circuit");

            // A file from before the lock has no `locked`: nothing in it is locked.
            System.IO.File.WriteAllText(_path, "{\"format\":5,\"projects\":[]}");
            var older = new ProgressStore(_path);
            older.Load();
            Assert.IsFalse(older.IsLocked("doc/half adder"), "a file from before the lock locks nothing");
            Assert.IsFalse(older.HasImported("doc/half adder"), "and keeps nothing to go back to");
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
        // A save that dies half way
        // -----------------------------------------------------------------

        [Test]
        public void ASaveInterruptedBeforeItLanded_IsRecovered()
        {
            // A save is written beside the real file and then moved over it. Dying between the two
            // leaves the complete new save as the temporary file and no real file at all -- and
            // reading that as a fresh install would throw away everything the new save holds.
            File.WriteAllText(TempPath, "{\"completed\":[\"route-the-bit\"]}");

            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsTrue(store.IsComplete("route-the-bit"),
                "the finished write was sitting beside the save and was not read");
        }

        [Test]
        public void ASaveThatLanded_LeavesNothingBesideIt()
        {
            // The temporary file is only ever a save in flight. Left behind by a save that finished,
            // it would be read the next time the real file went missing -- and it would be stale.
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkComplete("route-the-bit");
            store.MarkComplete("half-adder");

            Assert.IsTrue(File.Exists(_path), "the save did not land");
            Assert.IsFalse(File.Exists(TempPath), "the write in progress was left behind");

            var reread = new ProgressStore(_path);
            reread.Load();

            Assert.IsTrue(reread.IsComplete("half-adder"), "the second save did not replace the first");
        }

        [Test]
        public void AnUnreadableSave_IsKeptForRecovery()
        {
            // An unreadable save starts the game fresh, which is right. The next write replacing the
            // unreadable file is not: whatever was still in it can only be recovered by hand, and
            // only if it still exists.
            const string Damaged = "{\"completed\":[\"route-the-bit\",\"half-add";
            File.WriteAllText(_path, Damaged);

            var store = new ProgressStore(_path);
            store.Load();

            Assert.IsNotNull(store.LastError, "sanity: the damaged file should not have parsed");

            store.MarkComplete("the-long-way-round");   // any write at all

            Assert.IsTrue(File.Exists(UnreadablePath),
                "the damaged save was overwritten without a copy being kept");
            Assert.AreEqual(Damaged, File.ReadAllText(UnreadablePath),
                "the kept copy should be the damaged file exactly as it was");
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
