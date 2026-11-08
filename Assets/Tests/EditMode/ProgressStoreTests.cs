using System.IO;
using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.EditMode.Tests
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
        /// <summary>Hint ids for the store to remember: any will do, as the store holds them as names (the 4.x game's, until Part 16a).</summary>
        private static class Hint
        {
            public const string Stalled = "stalled", Collision = "collision", WireDelay = "wire-delay";
        }

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

            foreach (string copy in NewerCopies())
                File.Delete(copy);
        }

        /// <summary>The copies kept beside the save of one a newer BitSorter wrote.</summary>
        private string[] NewerCopies() =>
            Directory.GetFiles(Path.GetDirectoryName(_path), Path.GetFileName(_path) + ".newer-version-*");

        /// <summary>The save read by a store of its own, as another window, or the next session, reads it.</summary>
        private ProgressStore Reread()
        {
            var store = new ProgressStore(_path);
            store.Load();
            return store;
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

        /// <summary>
        /// A file the 4.x game also wrote to -- its boards, free play and block library beside 5.0's keys -- still loads,
        /// with every 5.0 thing in it; the 4.x keys are not read, and the next save leaves them out (Part 16a).
        /// </summary>
        /// <remarks>
        /// The 4.x game went with Part 16a, and its boards need not carry over (Ahmad, 2026-11-02); the save must not fail
        /// on them, which until then the migration tests held.
        /// </remarks>
        [Test]
        public void AFileWithThe4xGamesKeys_StillLoads_AndTheNextSaveDropsThem()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkComplete("half-adder");
            store.MarkHintSeen(Hint.Collision);
            store.SetProject("doc/adder", BitSorter.Board.Project.New("adder"));
            store.LastOpen = "doc/adder";
            Assert.IsTrue(store.SetSampleProgram("Pipelined RISC-V", "PROGRAM", "FIBONACCI"));
            store.Save();

            string old = File.ReadAllText(_path).TrimStart();
            Assert.AreEqual('{', old[0], "sanity: the save is a JSON object");
            File.WriteAllText(_path, "{\"boards\":[{\"level\":\"half-adder\",\"gates\":[{\"kind\":\"And\",\"x\":0,\"y\":0}],"
                + "\"wires\":[]}],\"freePlay\":{\"active\":\"My board\",\"boards\":[{\"name\":\"My board\"}]},"
                + "\"blocks\":[{\"name\":\"FA\",\"gates\":[]}]," + old.Substring(1));

            var older = new ProgressStore(_path);
            older.Load();
            Assert.IsNull(older.LastError, "a file the 4.x game wrote to is not an error");
            Assert.IsNull(older.FromANewerVersion, "nor a newer BitSorter's: its keys are an older one's");
            Assert.IsTrue(older.IsComplete("half-adder"), "its solves are the exercises' names, and still read");
            Assert.IsTrue(older.HasSeenHint(Hint.Collision), "and its hints");
            Assert.IsTrue(older.HasProject("doc/adder"), "a 5.0 circuit beside the 4.x keys is kept");
            Assert.AreEqual("doc/adder", older.LastOpen);
            Assert.AreEqual("FIBONACCI", older.SamplePrograms("Pipelined RISC-V")["PROGRAM"]);

            older.Save();
            string saved = File.ReadAllText(_path);
            StringAssert.DoesNotContain("\"boards\"", saved, "the next save leaves the 4.x boards out");
            StringAssert.DoesNotContain("\"freePlay\"", saved);
            StringAssert.DoesNotContain("\"blocks\"", saved);

            var again = new ProgressStore(_path);
            again.Load();
            Assert.IsTrue(again.IsComplete("half-adder") && again.HasProject("doc/adder"), "and loses nothing of 5.0's");
        }

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

        /// <summary>
        /// A save that cannot be read just now -- held by another program as BitSorter opens, which is no damaged file -- is
        /// never written over by the empty store that load left: the first save after it replaced every circuit and solve
        /// with nothing, and the copy kept of an unreadable save failed on the same lock (save-7).
        /// </summary>
        [Test]
        public void ASaveThatCouldNotBeReadJustNow_IsNeverWrittenOver()
        {
            var first = new ProgressStore(_path);
            first.Load();
            first.MarkComplete("route-the-bit");

            var store = new ProgressStore(_path);
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
                store.Load();
            Assert.IsNotNull(store.LastError, "sanity: it could not be read");

            store.MarkHintSeen(Hint.Stalled);   // any write at all, once the lock has gone

            var reread = new ProgressStore(_path);
            reread.Load();
            Assert.IsTrue(reread.IsComplete("route-the-bit"), "the save was written over by a store that never read it");
        }

        /// <summary>
        /// Two windows of BitSorter on one save -- two tabs of the browser build, or two of the desktop's -- each read it once
        /// and wrote it whole, so the second to save put its older save over the first's: a solve made in one window was gone
        /// once the other saved anything (browser-1).
        /// </summary>
        [Test]
        public void TwoWindows_TheOneThatReadTheSaveBeforeTheOtherWroteIt_NeverWritesOverIt()
        {
            var one = new ProgressStore(_path);
            one.Load();
            var other = new ProgressStore(_path);
            other.Load();

            one.MarkComplete("half-adder");
            other.MarkHintSeen(Hint.Stalled);   // any write of the other window's, which read the save before the solve

            var reread = new ProgressStore(_path);
            reread.Load();
            Assert.IsTrue(reread.IsComplete("half-adder"), "the other window wrote its older save over the solve");
        }

        // -----------------------------------------------------------------
        // Two windows, and a newer BitSorter's save (Ahmad's decisions, 2026-11-03)
        // -----------------------------------------------------------------

        /// <summary>
        /// A window held says why, and writes nothing however often it saves, while the window that wrote goes on writing; one
        /// that began on no save while another made it is held as well, and a window that read the save after it was written
        /// writes (browser-1).
        /// </summary>
        [Test]
        public void AWindowHeld_SaysWhy_WhileTheOneThatWroteGoesOnWriting()
        {
            var one = new ProgressStore(_path);
            one.Load();
            var other = new ProgressStore(_path);
            other.Load();

            one.MarkComplete("half-adder");
            Assert.AreEqual(SaveHold.None, one.Held);

            other.MarkComplete("four-corners");
            Assert.AreEqual(SaveHold.WrittenElsewhere, other.Held, "it began on no save, while the other made one");
            StringAssert.Contains("another window", other.LastError);
            other.Save();
            Assert.AreEqual(SaveHold.WrittenElsewhere, other.Held, "and stays held until it reads the save again");

            one.MarkComplete("carry-the-one");
            Assert.IsNull(one.LastError, "the window that wrote last goes on writing");

            ProgressStore reread = Reread();
            Assert.IsTrue(reread.IsComplete("half-adder") && reread.IsComplete("carry-the-one"));
            Assert.IsFalse(reread.IsComplete("four-corners"), "the held window's solve is its own until it reloads");
            reread.MarkHintSeen(Hint.Stalled);
            Assert.IsNull(reread.LastError, "a window that read the save after it was written writes");
        }

        /// <summary>A save from before stamps is written by the window that read it, and stamped from then on.</summary>
        [Test]
        public void ASaveFromBeforeStamps_IsWrittenByTheWindowThatReadIt()
        {
            File.WriteAllText(_path, "{\"completed\":[\"route-the-bit\"],\"format\":5}");
            var store = new ProgressStore(_path);
            store.Load();

            store.MarkComplete("half-adder");

            Assert.IsNull(store.LastError);
            Assert.AreEqual(SaveHold.None, store.Held);
            StringAssert.Contains("\"stamp\"", File.ReadAllText(_path), "stamped from now on");
            Assert.IsTrue(Reread().IsComplete("route-the-bit"));
        }

        /// <summary>
        /// A save a newer BitSorter wrote -- its format past this one's -- is said to be so as it is read, and copied beside
        /// itself as it was, dated, before this BitSorter first writes over it: once, and only then (Ahmad's decision,
        /// 2026-11-03).
        /// </summary>
        [Test]
        public void ASaveFromANewerBitSorter_IsSaidSo_AndCopiedAsItWasBeforeTheFirstWrite()
        {
            const string Newer = "{\"format\":6,\"completed\":[\"route-the-bit\"],\"glow\":true}";
            File.WriteAllText(_path, Newer);
            var store = new ProgressStore(_path);
            store.Load();

            StringAssert.Contains("format is 6", store.FromANewerVersion);
            Assert.IsNull(store.NewerCopy, "nothing copied while nothing is written");
            CollectionAssert.IsEmpty(NewerCopies());
            Assert.IsTrue(store.IsComplete("route-the-bit"), "what this BitSorter knows of it is read");

            store.MarkHintSeen(Hint.Stalled);
            Assert.IsNotNull(store.NewerCopy, "copied before the first write");
            StringAssert.StartsWith(_path + ".newer-version-", store.NewerCopy, "beside it, dated");
            Assert.AreEqual(Newer, File.ReadAllText(store.NewerCopy), "as it was");
            store.MarkHintSeen(Hint.Collision);
            Assert.AreEqual(1, NewerCopies().Length, "once");

            Assert.IsNull(Reread().FromANewerVersion, "the save written is this BitSorter's own");
        }

        /// <summary>
        /// A key this BitSorter does not know, as deep in the save as a project's own fields, makes it a newer one's: the
        /// reader passes over it without a word, and the next save would drop it.
        /// </summary>
        [Test]
        public void AKeyThisBitSorterDoesNotKnow_AtAnyDepth_MakesTheSaveANewerOnes()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.SetProject("doc/adder", BitSorter.Board.Project.New("adder"));
            store.SetLocked("doc/adder", true);
            store.LastOpen = "doc/adder";
            store.Save();

            string written = File.ReadAllText(_path);
            Assert.IsNull(Reread().FromANewerVersion, "a save this BitSorter wrote is its own");
            StringAssert.Contains("\"timeUnit\"", written, "sanity: a project's field to set a newer one beside");

            File.WriteAllText(_path, written.Replace("\"timeUnit\"", "\"glow\": true, \"timeUnit\""));
            StringAssert.Contains("glow", Reread().FromANewerVersion);
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

            Assert.IsFalse(first.HasSeenHint(Hint.Stalled), "nobody has seen anything yet");
            Assert.IsTrue(first.MarkHintSeen(Hint.Stalled), "the first time is the first time");
            Assert.IsFalse(first.MarkHintSeen(Hint.Stalled), "and only once");

            var second = new ProgressStore(_path);
            second.Load();

            Assert.IsTrue(second.HasSeenHint(Hint.Stalled),
                "a hint shown in one session must not come back in the next");
        }

        [Test]
        public void HintsAreTrackedSeparatelyFromEachOther()
        {
            var store = new ProgressStore(_path);
            store.Load();
            store.MarkHintSeen(Hint.Collision);

            Assert.IsTrue(store.HasSeenHint(Hint.Collision));
            Assert.IsFalse(store.HasSeenHint(Hint.Stalled), "one seen is not all seen");
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
            Assert.IsFalse(store.HasSeenHint(Hint.Stalled), "and no hint counts as shown");
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
            store.MarkHintSeen(Hint.WireDelay);

            store.Clear();

            Assert.IsFalse(store.HasSeenHint(Hint.WireDelay),
                "starting over means meeting the game again");

            var reloaded = new ProgressStore(_path);
            reloaded.Load();

            Assert.IsFalse(reloaded.HasSeenHint(Hint.WireDelay), "and on disk too");
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
