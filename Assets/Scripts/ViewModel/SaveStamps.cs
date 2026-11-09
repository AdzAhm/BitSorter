using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Which stamps of the save are one window's own (browser-1): the stamp it read or last wrote, and the stamp of every write
    /// of its own since that did not land -- a write that died after the old file went and before the new one was moved in
    /// leaves it as the temporary file, which is this window's, never another's, and a write after it that failed outright
    /// leaves that file as it was. Any other stamp the save holds now is another window's, written since this one read it, and
    /// this one writes nothing over it until it reads it again.
    /// </summary>
    public sealed class SaveStamps
    {
        /// <summary>Every write begun since the last that landed: one a failed save, cleared as a write lands or the save is read.</summary>
        private readonly HashSet<string> _tried = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The stamp of the save as this window last read or wrote it; null for none.</summary>
        public string Known { get; private set; }

        /// <summary>The save is read, its stamp <paramref name="stamp"/>: what every later write is held to.</summary>
        public void Read(string stamp)
        {
            Known = stamp;
            _tried.Clear();
        }

        /// <summary>A write of <paramref name="stamp"/> begins: from here it may land, in part or whole, whether it says so or not.</summary>
        public void Trying(string stamp)
        {
            if (stamp != null)
                _tried.Add(stamp);
        }

        /// <summary>The write of <paramref name="stamp"/> landed: the save is this window's, as it wrote it, and nothing tried before it is left.</summary>
        public void Wrote(string stamp)
        {
            Known = stamp;
            _tried.Clear();
        }

        /// <summary>Whether <paramref name="now"/>, the stamp the save holds now, is another window's.</summary>
        public bool IsAnothers(string now) =>
            !string.Equals(now, Known, StringComparison.Ordinal) && !(now != null && _tried.Contains(now));
    }
}
