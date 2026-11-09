using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Which stamps of the save are a window's own (<see cref="SaveStamps"/>, browser-1): what it read or last wrote, and what
    /// it tried to write and may have left behind -- any other is another window's, and holds this one's writes.
    /// </summary>
    public class SaveStampsTests
    {
        [Test]
        public void TheStampReadOrWritten_IsOnesOwn_AndAnyOtherIsAnothers()
        {
            var stamps = new SaveStamps();
            stamps.Read("read");
            Assert.IsFalse(stamps.IsAnothers("read"));
            Assert.IsTrue(stamps.IsAnothers("other"));
            Assert.IsTrue(stamps.IsAnothers(null), "a save from before stamps, written since one with a stamp was read");

            stamps.Trying("mine");
            stamps.Wrote("mine");
            Assert.IsFalse(stamps.IsAnothers("mine"));
            Assert.IsTrue(stamps.IsAnothers("read"), "what it read is gone once it has written");

            var fresh = new SaveStamps();
            fresh.Read(null);
            Assert.IsFalse(fresh.IsAnothers(null), "no save, and none since");
            Assert.IsTrue(fresh.IsAnothers("other"), "one made since by a window that began with none");
        }

        /// <summary>
        /// A write that landed as the temporary file and was never moved in -- the old file gone, the move refused by a scan
        /// holding the new one -- leaves this window's stamp there; a write after it that failed on the same lock left that
        /// file as it was. Both are this window's own: only the last write's stamp was, and the next save took the first for
        /// another window's, told the student another BitSorter had saved, and offered a reload that would throw their work
        /// away (the browser-1 fix's review).
        /// </summary>
        [Test]
        public void EveryWriteOfOnesOwnNotLanded_IsOnesOwn_NotOnlyTheLast()
        {
            var stamps = new SaveStamps();
            stamps.Read("read");

            stamps.Trying("first");    // written as the temporary file; the move failed
            stamps.Trying("second");   // its write of the temporary file failed: the file still holds "first"

            Assert.IsFalse(stamps.IsAnothers("first"), "the first write, left behind, is this window's own");
            Assert.IsFalse(stamps.IsAnothers("second"));
            Assert.IsFalse(stamps.IsAnothers("read"));
            Assert.IsTrue(stamps.IsAnothers("other"), "and another window's still is");

            stamps.Wrote("third");
            Assert.IsTrue(stamps.IsAnothers("first"), "once a write lands, nothing before it is in the file");
            Assert.IsFalse(stamps.IsAnothers("third"));
        }
    }
}
