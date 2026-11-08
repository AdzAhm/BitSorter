using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The dump COMPARE WITH MODELSIM'S RUN holds the run to, among the files a browser's page read (<see cref="DumpPick"/>,
    /// Part 15i-5): the modelsim.vcd read, or why there is none, in words a student can act on.
    /// </summary>
    public class DumpPickTests
    {
        [Test]
        public void TheDumpRead_IsTheOneComparedWith()
        {
            var files = new[] { new SourceFile("run.do", "vsim tb"), new SourceFile("modelsim.vcd", "$date $end") };

            Assert.IsTrue(DumpPick.TryChoose(files, out SourceFile dump, out string why));
            Assert.AreEqual("modelsim.vcd", dump.Path);
            Assert.IsNull(why);
        }

        /// <summary>
        /// A dump the page left unread says why, as the desktop's COMPARE does: past the 16 MB COMPARE reads, how large it is
        /// and how to make it smaller; picked with other files, that COMPARE reads one. Both were told to pick the very file
        /// just picked, and picked it again to the same answer (browser-6).
        /// </summary>
        [Test]
        public void ADumpLeftUnread_SaysWhy_TooLargeOrPickedWithOthers()
        {
            Assert.IsFalse(DumpPick.TryChoose(new[] { SourceFile.Unread("modelsim.vcd", 20L << 20) }, out _, out string why));
            StringAssert.Contains("modelsim.vcd is 20 MB", why);
            StringAssert.Contains("16 MB", why);
            StringAssert.Contains("dump fewer signals", why);

            Assert.IsFalse(DumpPick.TryChoose(new[] { SourceFile.Unread("modelsim.vcd", 400), SourceFile.Unread("run.do", 50) }, out _, out why));
            StringAssert.Contains("one file", why);

            Assert.IsFalse(DumpPick.TryChoose(new[] { new SourceFile("run.do", "vsim tb") }, out _, out why));
            StringAssert.Contains("pick the modelsim.vcd", why, "a pick with no dump in it asks for the dump");
        }
    }
}
