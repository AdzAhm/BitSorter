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
        public void EveryControl_ReachesTheCard()
        {
            // The card is the complete reference, so nothing may be missing from it.
            foreach (ControlEntry entry in ControlsReference.All)
            {
                StringAssert.Contains(entry.Text, ControlsReference.Card,
                    $"'{entry.Text}' never reaches the tutorial's card");
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
            // The direction the other tests cannot catch: a phrase hardcoded into the rendering
            // rather than taken from the list would still let every entry through.
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

        [Test]
        public void TheCardSaysNothingThatIsNotInTheList()
        {
            string[] shown = ControlsReference.Card.Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string piece in shown)
            {
                bool known = false;

                foreach (ControlEntry entry in ControlsReference.All)
                {
                    if (entry.Text == piece.Trim())
                        known = true;
                }

                Assert.IsTrue(known,
                    $"the card shows '{piece.Trim()}', which is not in ControlsReference.All");
            }
        }

        /// <summary>
        /// Both renderings are exactly as long as the list says they should be.
        /// </summary>
        /// <remarks>
        /// This is the test that actually fails when someone adds a binding to only one of them.
        /// The others prove every entry gets through; this one proves nothing else does, and that
        /// the count is derived rather than fixed -- so a new entry necessarily moves both numbers.
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

            int cardCount = ControlsReference.Card.Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            Assert.AreEqual(onLine, lineCount, "the status line is not the flagged entries");
            Assert.AreEqual(total, cardCount, "the card is not the whole list");
        }

        [Test]
        public void TheStatusLineIsAShortlist_NotEverything()
        {
            // Not a style preference: the line is one row on screen permanently, and putting all
            // thirteen on it is what the OnStatusLine flag exists to prevent.
            Assert.Less(ControlsReference.Line.Length, ControlsReference.Card.Length,
                "if the line carries everything, the flag has stopped meaning anything");
        }
    }
}
