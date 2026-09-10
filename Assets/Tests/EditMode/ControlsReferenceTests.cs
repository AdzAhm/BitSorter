using System;
using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="ControlsReference"/>: the controls line and the tutorial's card come from one
    /// list, so adding a binding reaches both with no second edit.
    /// </summary>
    /// <remarks>
    /// The list used to be a string literal inside RunControls. The moment the tutorial gained a
    /// card listing the same controls there were two copies, and a changed binding would have left
    /// two different answers on screen. These tests exist to make that regression loud rather than
    /// silent -- there is no compiler error for two strings drifting apart.
    /// </remarks>
    public class ControlsReferenceTests
    {
        [Test]
        public void ThereAreControlsAtAll()
        {
            Assert.IsNotEmpty(ControlsReference.All);

            foreach (ControlEntry entry in ControlsReference.All)
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Text), "a control with no text");
        }

        [Test]
        public void EveryControl_ReachesExactlyOneGroup()
        {
            // The card is the complete reference, so nothing may be missing from it -- and nothing
            // may appear twice, which a hand-written grouping would eventually do.
            foreach (ControlEntry entry in ControlsReference.All)
            {
                int found = 0;

                foreach (ControlGroup group in ControlsReference.Groups)
                {
                    foreach (ControlEntry candidate in group.Entries)
                    {
                        if (candidate.Text == entry.Text)
                            found++;
                    }
                }

                Assert.AreEqual(1, found, $"'{entry.Text}' appears in {found} groups, not one");
            }
        }

        [Test]
        public void TheGroupsSayNothingThatIsNotInTheList()
        {
            // The direction the test above cannot catch: a row invented in the grouping rather than
            // taken from the list would still let every entry through.
            foreach (ControlGroup group in ControlsReference.Groups)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(group.Name), "a group with no heading");

                foreach (ControlEntry entry in group.Entries)
                {
                    bool known = false;

                    foreach (ControlEntry candidate in ControlsReference.All)
                    {
                        if (candidate.Text == entry.Text)
                            known = true;
                    }

                    Assert.IsTrue(known,
                        $"'{group.Name}' lists '{entry.Text}', which is not in ControlsReference.All");
                }
            }
        }

        [Test]
        public void EveryStatusLineControl_ReachesTheLine()
        {
            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (!entry.OnStatusLine)
                    continue;

                StringAssert.Contains(entry.Text, ControlsReference.Line,
                    $"'{entry.Text}' is flagged for the status line but never reaches it");
            }
        }

        [Test]
        public void TheLineSaysNothingThatIsNotInTheList()
        {
            string[] shown = ControlsReference.Line.Split(
                new[] { ControlsReference.LineSeparator }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string piece in shown)
            {
                bool known = false;

                foreach (ControlEntry entry in ControlsReference.All)
                {
                    if (entry.Text == piece.Trim())
                        known = true;
                }

                Assert.IsTrue(known,
                    $"the status line shows '{piece.Trim()}', which is not in ControlsReference.All");
            }
        }

        /// <summary>
        /// Both renderings are exactly as long as the list says they should be.
        /// </summary>
        /// <remarks>
        /// This is the test that actually fails when someone adds a binding to only one of them. The
        /// others prove every entry gets through; this one proves nothing else does, and that the
        /// counts are derived rather than fixed -- so a new entry necessarily moves both numbers.
        /// </remarks>
        [Test]
        public void AddingAControl_MovesBothRenderings()
        {
            int total = ControlsReference.All.Count;
            int onLine = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.OnStatusLine)
                    onLine++;
            }

            int lineCount = ControlsReference.Line.Split(
                new[] { ControlsReference.LineSeparator }, StringSplitOptions.RemoveEmptyEntries).Length;

            int grouped = 0;

            foreach (ControlGroup group in ControlsReference.Groups)
                grouped += group.Entries.Count;

            Assert.AreEqual(onLine, lineCount, "the status line is not the flagged entries");
            Assert.AreEqual(total, grouped, "the card's columns are not the whole list");
        }

        [Test]
        public void EveryGroupHasSomethingInIt()
        {
            // An empty column would draw a heading with nothing under it, which reads as a bug.
            foreach (ControlGroup group in ControlsReference.Groups)
                Assert.IsNotEmpty(group.Entries, $"'{group.Name}' has no controls under it");
        }

        [Test]
        public void TheStatusLineIsAShortlist_NotEverything()
        {
            // Not a style preference: the line is one row on screen permanently, and putting all
            // thirteen on it is what the OnStatusLine flag exists to prevent.
            int onLine = 0;

            foreach (ControlEntry entry in ControlsReference.All)
            {
                if (entry.OnStatusLine)
                    onLine++;
            }

            Assert.Less(onLine, ControlsReference.All.Count,
                "if the line carries everything, the flag has stopped meaning anything");
        }
    }
}
