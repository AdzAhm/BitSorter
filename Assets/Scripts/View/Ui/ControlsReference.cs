using System.Collections.Generic;
using System.Text;

namespace BitSorter.View
{
    /// <summary>Which column of the tutorial's card a control belongs in.</summary>
    public enum ControlKind
    {
        /// <summary>Putting a circuit together, and taking it apart again.</summary>
        Building,

        /// <summary>Driving a run once there is something to run.</summary>
        Running,

        /// <summary>Everything that is neither: help, levels, sound.</summary>
        Everything,
    }

    /// <summary>One control the player can use, and where it is worth saying so.</summary>
    public readonly struct ControlEntry
    {
        /// <summary>The whole phrase, input and effect together: "ctrl+Z to undo".</summary>
        public readonly string Text;

        /// <summary>
        /// Whether it also earns a place on the one-line strip along the bottom of the board.
        /// </summary>
        /// <remarks>
        /// Not everything does. That line is a single row a thousand pixels wide and it is on screen
        /// permanently, so it carries the controls a player reaches for mid-build; the rest are
        /// listed once, on the tutorial's card, where there is room for them.
        /// </remarks>
        public readonly bool OnStatusLine;

        /// <summary>Which heading it sits under on the card.</summary>
        public readonly ControlKind Kind;

        public ControlEntry(string text, bool onStatusLine, ControlKind kind)
        {
            Text = text;
            OnStatusLine = onStatusLine;
            Kind = kind;
        }

        public override string ToString() => Text;
    }

    /// <summary>A heading on the tutorial's card, and the controls under it.</summary>
    public readonly struct ControlGroup
    {
        public readonly ControlKind Kind;
        public readonly string Name;
        public readonly IReadOnlyList<ControlEntry> Entries;

        public ControlGroup(ControlKind kind, string name, IReadOnlyList<ControlEntry> entries)
        {
            Kind = kind;
            Name = name;
            Entries = entries;
        }

        public override string ToString() => $"{Name} ({Entries.Count})";
    }

    /// <summary>
    /// Every control the game has, in one place, rendered two ways.
    /// </summary>
    /// <remarks>
    /// The list used to live inline in <see cref="RunControls"/>. Once the tutorial gained a card
    /// listing the same controls there were two copies, and a changed binding would have left two
    /// different answers on screen -- the drift CLAUDE.md's "derived, never restated" rule exists to
    /// stop, and the same move <see cref="BitVisuals"/> got for the bit colours once a bit was drawn
    /// in two places.
    ///
    /// Adding a binding here reaches both renderings with no second edit: the status line if it is
    /// flagged for it, and the card's correct column by its kind. That is the property
    /// ControlsReferenceTests pins, in both directions -- nothing missing, and nothing shown that is
    /// not on this list.
    ///
    /// The card is handed <see cref="Groups"/> rather than a formatted string, because a panel given
    /// rows can lay them out in columns and a panel given a string can only print it. That is what
    /// the flat list on the old card was.
    /// </remarks>
    public static class ControlsReference
    {
        /// <summary>Separator on the status line. Wide, because it is the only thing dividing them.</summary>
        public const string LineSeparator = "     ";

        public static IReadOnlyList<ControlEntry> All { get; } = new[]
        {
            new ControlEntry("drag a port to wire", true, ControlKind.Building),
            new ControlEntry("right click to delete", true, ControlKind.Building),
            new ControlEntry("scroll a wire to re-time", true, ControlKind.Building),
            new ControlEntry("ctrl+Z to undo", true, ControlKind.Building),
            new ControlEntry("shift+R to clear", true, ControlKind.Building),

            new ControlEntry("Enter to run", false, ControlKind.Running),
            new ControlEntry("R to reset the board", false, ControlKind.Running),
            new ControlEntry("Space to pause a run", false, ControlKind.Running),
            new ControlEntry("right arrow to step one tick", false, ControlKind.Running),

            new ControlEntry("H for help", true, ControlKind.Everything),
            new ControlEntry("ESC for levels", true, ControlKind.Everything),
            new ControlEntry("Q and E to change level", false, ControlKind.Everything),
            new ControlEntry("N to mute", true, ControlKind.Everything),
        };

        /// <summary>The groups in the order the card lays them out.</summary>
        /// <remarks>
        /// Built from <see cref="All"/> rather than written out again, so a control cannot be in the
        /// list and missing from the card, or on the card twice.
        /// </remarks>
        public static IReadOnlyList<ControlGroup> Groups { get; } = BuildGroups();

        /// <summary>The one-line strip along the bottom of the board.</summary>
        public static string Line
        {
            get
            {
                var text = new StringBuilder();

                foreach (ControlEntry entry in All)
                {
                    if (!entry.OnStatusLine)
                        continue;

                    if (text.Length > 0)
                        text.Append(LineSeparator);

                    text.Append(entry.Text);
                }

                return text.ToString();
            }
        }

        private static IReadOnlyList<ControlGroup> BuildGroups()
        {
            var order = new[]
            {
                new KeyValuePair<ControlKind, string>(ControlKind.Building, "BUILDING"),
                new KeyValuePair<ControlKind, string>(ControlKind.Running, "RUNNING"),
                new KeyValuePair<ControlKind, string>(ControlKind.Everything, "EVERYTHING ELSE"),
            };

            var groups = new List<ControlGroup>(order.Length);

            foreach (KeyValuePair<ControlKind, string> heading in order)
            {
                var entries = new List<ControlEntry>();

                foreach (ControlEntry entry in All)
                {
                    if (entry.Kind == heading.Key)
                        entries.Add(entry);
                }

                groups.Add(new ControlGroup(heading.Key, heading.Value, entries));
            }

            return groups;
        }
    }
}
