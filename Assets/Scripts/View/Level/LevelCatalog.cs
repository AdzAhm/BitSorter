using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>One level as the catalogue sees it: which file it is, and where it sits in the run.</summary>
    public readonly struct LevelEntry
    {
        /// <summary>File name without extension, as <see cref="LevelLoader.Load"/> takes it.</summary>
        public readonly string FileName;

        /// <summary>Position in the run, or zero for a level that names none.</summary>
        public readonly int Order;

        public LevelEntry(string fileName, int order)
        {
            FileName = fileName;
            Order = order;
        }

        public bool HasOrder => Order > 0;

        public override string ToString() => HasOrder ? $"{FileName} ({Order})" : $"{FileName} (unplaced)";
    }

    /// <summary>
    /// Puts the level files into play order, and refuses to be quiet about two of them claiming the
    /// same seat.
    /// </summary>
    /// <remarks>
    /// This exists because the rule it enforces is the one rule no level file can check on its own.
    /// <see cref="LevelLoader.Validate"/> sees a single file and can say whether its order is a
    /// sensible number; only something holding every file can say whether it is unique.
    ///
    /// Pure, and takes entries rather than reading Resources, for the same reason LevelLoader splits
    /// Load from Parse from Validate: a duplicate order can then be tested without shipping two
    /// broken levels to provoke one.
    /// </remarks>
    public static class LevelCatalog
    {
        /// <summary>
        /// The levels in the order they should be played. <paramref name="error"/> is null when all is
        /// well, otherwise one line naming the clash.
        /// </summary>
        /// <remarks>
        /// A clash is reported but not fatal, and the returned order is still complete and stable. The
        /// game has to start either way, and it has to start the same way twice -- an order that
        /// depended on the file system's enumeration would reproduce differently on another machine,
        /// which is the failure this whole type exists to prevent.
        ///
        /// Unordered levels sort to the end rather than the beginning. A level dropped into Resources
        /// without an order still has to appear somewhere, and the end is the only place that cannot
        /// silently displace an authored sequence.
        ///
        /// Only the first clash is named. A level author fixes one thing at a time, which is the same
        /// call LevelLoader.Validate makes.
        /// </remarks>
        public static IReadOnlyList<LevelEntry> Sort(IReadOnlyList<LevelEntry> entries, out string error)
        {
            error = null;

            if (entries == null || entries.Count == 0)
                return new List<LevelEntry>();

            var sorted = new List<LevelEntry>(entries);

            sorted.Sort((a, b) =>
            {
                // Unordered levels go last, whatever their names.
                if (a.HasOrder != b.HasOrder)
                    return a.HasOrder ? -1 : 1;

                if (a.HasOrder && a.Order != b.Order)
                    return a.Order.CompareTo(b.Order);

                // Same seat, or both unplaced: file name decides, so the result is reproducible.
                return string.CompareOrdinal(a.FileName, b.FileName);
            });

            error = FirstClash(sorted);
            return sorted;
        }

        /// <summary>The first pair sharing an order value, worded for a level author, or null.</summary>
        private static string FirstClash(List<LevelEntry> sorted)
        {
            for (int i = 1; i < sorted.Count; i++)
            {
                LevelEntry previous = sorted[i - 1];
                LevelEntry current = sorted[i];

                if (previous.HasOrder && current.HasOrder && previous.Order == current.Order)
                {
                    return $"'{previous.FileName}' and '{current.FileName}' both claim order " +
                           $"{current.Order}; every level needs its own place in the run";
                }
            }

            return null;
        }
    }
}
