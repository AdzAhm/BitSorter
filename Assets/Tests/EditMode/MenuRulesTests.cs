using System;
using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Where the main menu's Continue goes, and what the line under it promises.
    /// </summary>
    /// <remarks>
    /// The regression this file exists for: the walk ended in
    /// <c>catalogue[catalogue.Count - 1]</c> with nothing above it checking the catalogue was not
    /// empty. Continue guarded the empty case itself; the menu's own refresh did not, and it runs
    /// every frame the menu is open -- so a catalogue that discovered no levels threw once per frame
    /// on the first screen of the game, with no way past it.
    /// </remarks>
    public class MenuRulesTests
    {
        private static IReadOnlyList<LevelEntry> Catalogue(params string[] names)
        {
            var entries = new List<LevelEntry>();

            for (int i = 0; i < names.Length; i++)
                entries.Add(new LevelEntry(names[i], i + 1, names[i]));

            return entries;
        }

        private static Predicate<string> Solved(params string[] names)
        {
            var done = new HashSet<string>(names);
            return name => done.Contains(name);
        }

        // -----------------------------------------------------------------
        // The bug
        // -----------------------------------------------------------------

        [Test]
        public void AnEmptyCatalogueHasNoNextLevel()
        {
            // Must answer, not throw. This is the assertion the old walk could not reach: it indexed
            // [-1] instead of returning.
            Assert.IsFalse(MenuRules.TryNextUp(Catalogue(), Solved(), out LevelEntry next));
            Assert.IsNull(next.FileName, "nothing was found, so nothing should be named");
        }

        [Test]
        public void ANullCatalogueHasNoNextLevel()
        {
            Assert.IsFalse(MenuRules.TryNextUp(null, Solved(), out LevelEntry _));
        }

        [Test]
        public void AnEmptyCatalogueIsNotAFinishedRun()
        {
            // The other half of the same failure. Zero of zero is arithmetically "all of them", so a
            // bare count comparison congratulates the player for levels that never loaded.
            Assert.IsFalse(MenuRules.AllSolved(0, 0));
            Assert.AreEqual("no levels found", MenuRules.DescribeProgress(0, 0));
        }

        // -----------------------------------------------------------------
        // Where Continue goes
        // -----------------------------------------------------------------

        [Test]
        public void AFreshSaveOpensTheFirstLevel()
        {
            Assert.IsTrue(MenuRules.TryNextUp(
                Catalogue("one", "two", "three"), Solved(), out LevelEntry next));

            Assert.AreEqual("one", next.FileName);
        }

        [Test]
        public void ItGoesToTheFurthestUnsolvedRatherThanTheLastPlayed()
        {
            // The two differ exactly when it matters: a player who wandered into a late level from the
            // list and quit should come back to the run, not to wherever they were browsing.
            Assert.IsTrue(MenuRules.TryNextUp(
                Catalogue("one", "two", "three"), Solved("one"), out LevelEntry next));

            Assert.AreEqual("two", next.FileName);
        }

        [Test]
        public void AGapInTheRunIsWhereItGoes()
        {
            // Solved out of order -- the first hole is the frontier, not the highest solved level.
            Assert.IsTrue(MenuRules.TryNextUp(
                Catalogue("one", "two", "three"), Solved("one", "three"), out LevelEntry next));

            Assert.AreEqual("two", next.FileName);
        }

        [Test]
        public void AFinishedRunStillGoesSomewhere()
        {
            // Everything solved leaves no frontier. Continue lands on the last level rather than going
            // dead, so the button always means something.
            Assert.IsTrue(MenuRules.TryNextUp(
                Catalogue("one", "two"), Solved("one", "two"), out LevelEntry next));

            Assert.AreEqual("two", next.FileName);
            Assert.IsTrue(MenuRules.AllSolved(2, 2));
        }

        [Test]
        public void WithoutAProgressFileNothingCountsAsSolved()
        {
            // A null predicate is the no-progress-tracker case, which must read as "nothing done"
            // rather than as "everything done".
            Assert.IsTrue(MenuRules.TryNextUp(Catalogue("one", "two"), null, out LevelEntry next));

            Assert.AreEqual("one", next.FileName);
        }

        // -----------------------------------------------------------------
        // What the line says
        // -----------------------------------------------------------------

        [Test]
        public void ProgressReadsAsACountBeforeAnythingIsSolved()
        {
            Assert.AreEqual("9 levels", MenuRules.DescribeProgress(0, 9));
            Assert.AreEqual("1 of 9 solved", MenuRules.DescribeProgress(1, 9));
            Assert.AreEqual("9 of 9 solved", MenuRules.DescribeProgress(9, 9));
        }

        [Test]
        public void AllSolvedNeedsTheWholeRun()
        {
            Assert.IsFalse(MenuRules.AllSolved(8, 9));
            Assert.IsTrue(MenuRules.AllSolved(9, 9));
        }
    }
}
