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

        /// <summary>
        /// The name a player sees. Falls back to the file name for a level that would not parse, so
        /// a broken level is still listed and still selectable rather than silently missing.
        /// </summary>
        public readonly string DisplayName;

        /// <summary>
        /// Whether this level is in the sequential half of the run.
        /// </summary>
        /// <remarks>
        /// Carried here because the catalogue is built where the files are already parsed, and the
        /// level list is not: without it the list would have to load all seventeen again to find
        /// out where the chapters divide. <see cref="LevelCatalog.IsSequential"/> is what decides.
        /// </remarks>
        public readonly bool IsSequential;

        public LevelEntry(string fileName, int order) : this(fileName, order, fileName, false)
        {
        }

        public LevelEntry(string fileName, int order, string displayName)
            : this(fileName, order, displayName, false)
        {
        }

        public LevelEntry(string fileName, int order, string displayName, bool isSequential)
        {
            FileName = fileName;
            Order = order;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? fileName : displayName;
            IsSequential = isSequential;
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
        /// Whether a key names something that is not one of the levels in the run.
        /// </summary>
        /// <remarks>
        /// Free play and the guided tutorial both reach the board through
        /// <see cref="LevelSession.Adopt"/> under a key that is not a file name, and neither is in
        /// this catalogue. Anything that counts levels, ranks them or reports them has to say so, or
        /// nine levels start looking like eleven.
        ///
        /// One predicate rather than a key comparison repeated at each site: there were two such
        /// comparisons and a third was needed, which is the point at which they start disagreeing.
        /// The tutorial being *graded* is what made this bite -- the sandbox never passes, so it
        /// never reached the code that records a solve, and the tutorial does.
        /// </remarks>
        public static bool IsOffCatalogue(string key) =>
            key == SandboxLevel.Key || key == TutorialLevel.Key;

        /// <summary>The two halves of the run, named.</summary>
        /// <remarks>
        /// The second name is the chapter card's own title, and the card reads it from here, so the
        /// list and the card cannot end up calling the same boundary two things. The first is the
        /// pair to it, and comes from the card's own words for what changes: everything before it
        /// forgets each bit the moment it has used it.
        /// </remarks>
        public const string CombinationalChapter = "CIRCUITS THAT FORGET";

        /// <inheritdoc cref="CombinationalChapter"/>
        public const string SequentialChapter = "CIRCUITS THAT REMEMBER";

        /// <summary>What each chapter is called in the subject it is teaching.</summary>
        /// <remarks>
        /// The names above say what changes; these say what it is called in a textbook, which is
        /// what a player looking for the right chapter of their course is searching for. Shown
        /// beside the heading rather than instead of it, because one of them is the reason to care
        /// and the other is the word to look up.
        /// </remarks>
        public const string CombinationalSubject = "combinational logic";

        /// <inheritdoc cref="CombinationalSubject"/>
        public const string SequentialSubject = "sequential logic";

        /// <summary>A chapter's heading, as the level list prints it.</summary>
        public static string HeadingFor(bool sequential) =>
            sequential
                ? $"{SequentialChapter}   ({SequentialSubject})"
                : $"{CombinationalChapter}   ({CombinationalSubject})";

        /// <summary>
        /// Whether a level belongs to the sequential half of the run.
        /// </summary>
        /// <remarks>
        /// Decided by what the level stocks, not by its number, for the reason the chapter card is:
        /// inserting or reordering levels cannot then put the boundary in the wrong place. Two
        /// things ask -- the card, to know when to show itself, and the level list, to know where
        /// to draw the break -- and a second copy of this rule is a second thing to drift.
        /// </remarks>
        public static bool IsSequential(LevelDefinition level)
        {
            if (level == null)
                return false;

            for (int i = 0; i < level.Budget.Count; i++)
            {
                if (level.Budget[i].Kind == GateKind.Register)
                    return true;
            }

            return false;
        }

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
