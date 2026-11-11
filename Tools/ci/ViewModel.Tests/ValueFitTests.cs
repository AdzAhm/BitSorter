using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A bus's value fitted to the room it has (5.1, Ahmad: a value read only zoomed far in): cut over a box too narrow for
    /// the whole of it, and the tip's words.
    /// </summary>
    public class ValueFitTests
    {
        /// <summary>A box as wide as a value needs shows it whole, as before; a narrower one as much as fits and an ellipsis.</summary>
        [Test]
        public void ABoxShowsTheWholeValue_WhereItFits_ElseAsMuchAsFitsAndAnEllipsis()
        {
            Assert.AreEqual(9, ValueFit.Kept(9, ValueFit.Room(9) + 0.1f, 1, out bool cut));
            Assert.IsFalse(cut, "whole");
            Assert.AreEqual(9, ValueFit.Kept(9, 400f, 1, out cut));
            Assert.IsFalse(cut);

            // Exactly as wide as nine need is not wider: eight fit, seven of the value and the ellipsis.
            Assert.AreEqual(7, ValueFit.Kept(9, ValueFit.Room(9), 1, out cut));
            Assert.IsTrue(cut, "cut");
            Assert.AreEqual(3, ValueFit.Kept(9, 50f, 1, out cut), "four fit in 50 units: three and the ellipsis");
            Assert.IsTrue(cut);

            Assert.AreEqual(1, ValueFit.Kept(9, ValueFit.Room(3) + 0.1f, 2, out cut), "an ellipsis of three dots takes two characters' room");
        }

        /// <summary>Fewer than three characters say nothing a reader can use: the box stays empty, as it was.</summary>
        [Test]
        public void ABoxWithRoomForFewerThanThree_ShowsNothing()
        {
            Assert.AreEqual(2, ValueFit.Kept(9, ValueFit.Room(3) + 0.1f, 1, out bool cut), "three fit: two and the ellipsis");
            Assert.IsTrue(cut);
            Assert.AreEqual(0, ValueFit.Kept(9, ValueFit.Room(3), 1, out cut), "two fit: nothing");
            Assert.IsFalse(cut);
            Assert.AreEqual(0, ValueFit.Kept(9, 10f, 1, out cut));
            Assert.AreEqual(0, ValueFit.Kept(0, 400f, 1, out cut), "no value, nothing");

            Assert.AreEqual(2, ValueFit.Kept(2, ValueFit.Room(2) + 0.1f, 1, out cut), "a short value whole however narrow its box");
            Assert.IsFalse(cut);
        }

        /// <summary>
        /// A box too narrow for a value leaves out its leading zeros first, in binary, octal and hex: a 32-bit address read
        /// "000..." in every box, which says nothing. Still too long, it is cut from its first digit that is not a zero; and a
        /// box with room writes the value whole, zeros and all, as every other place does.
        /// </summary>
        [Test]
        public void ABoxTooNarrow_LeavesOutTheLeadingZerosFirst_ThenCutsFromTheFirstDigitLeft()
        {
            Assert.AreEqual(8, ValueFit.Fit("0000006C", true, ValueFit.Room(8) + 0.1f, 1, out int start, out bool cut), "room: whole");
            Assert.AreEqual(0, start);
            Assert.IsFalse(cut);

            Assert.AreEqual(2, ValueFit.Fit("0000006C", true, ValueFit.Room(3) + 0.1f, 1, out start, out cut), "6C, whole, in room for three");
            Assert.AreEqual(6, start, "from the 6");
            Assert.IsFalse(cut);

            Assert.AreEqual(3, ValueFit.Fit("00FE6A1C", true, 50f, 1, out start, out cut), "four fit: FE6 and the ellipsis");
            Assert.AreEqual(2, start);
            Assert.IsTrue(cut);

            Assert.AreEqual(3, ValueFit.Fit("0000006C", false, 50f, 1, out start, out cut), "a decimal keeps its characters: cut from the first");
            Assert.AreEqual(0, start);
            Assert.IsTrue(cut);

            Assert.AreEqual(1, ValueFit.Fit("00000000", true, ValueFit.Room(3) + 0.1f, 1, out start, out cut), "zero is 0");
            Assert.AreEqual(7, start);
            Assert.AreEqual(0, ValueFit.LeadingZeros("XXXX"));
            Assert.AreEqual(3, ValueFit.LeadingZeros("0000"));
            Assert.AreEqual(0, ValueFit.LeadingZeros(""));
        }

        [Test]
        public void TheTip_SaysTheName_TheWidth_TheWholeValue_AndItsStretchOfTime()
        {
            Assert.AreEqual("pc[31:0], 32 bits\n00000024h\n1166 to 1500 ps", ValueFit.Tip("pc[31:0]", 32, "00000024h", "1166", "1500 ps"));
            StringAssert.StartsWith("en, 1 bit\n", ValueFit.Tip("en", 1, "1", "0", "5 ns"));
        }
    }
}
