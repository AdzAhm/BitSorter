using System;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Which stamps of the save are one window's own (browser-1): the stamp it read or last wrote, and the stamp of a write of
    /// its own that did not land -- a write that died after the old file went and before the new one was moved in leaves it
    /// as the temporary file, which is this window's, never another's. Any other stamp the save holds now is another
    /// window's, written since this one read it, and this one writes nothing over it until it reads it again.
    /// </summary>
    public sealed class SaveStamps
    {
        private string _tried;

        /// <summary>The stamp of the save as this window last read or wrote it; null for none.</summary>
        public string Known { get; private set; }

        /// <summary>The save is read, its stamp <paramref name="stamp"/>: what every later write is held to.</summary>
        public void Read(string stamp)
        {
            Known = stamp;
            _tried = null;
        }

        /// <summary>A write of <paramref name="stamp"/> begins: from here it may land, in part or whole, whether it says so or not.</summary>
        public void Trying(string stamp) => _tried = stamp;

        /// <summary>The write of <paramref name="stamp"/> landed: the save is this window's, as it wrote it.</summary>
        public void Wrote(string stamp)
        {
            Known = stamp;
            _tried = null;
        }

        /// <summary>Whether <paramref name="now"/>, the stamp the save holds now, is another window's.</summary>
        public bool IsAnothers(string now) =>
            !string.Equals(now, Known, StringComparison.Ordinal) && !(now != null && string.Equals(now, _tried, StringComparison.Ordinal));
    }
}
