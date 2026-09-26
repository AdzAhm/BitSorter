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

        /// <summary>
        /// Whether it also earns a place on the short line at the foot of the main menu.
        /// </summary>
        /// <remarks>
        /// A third rendering, and it needs its own flag for the reason the first one does: the menu
        /// is a front door, and its line names only what a key does on the menu itself -- which is
        /// the sound, and nothing else. It named H, M and Escape as well, and with the menu open the
        /// first two do nothing and Escape closes it; the menu's buttons are the way on from there.
        ///
        /// It exists at all because the menu used to draw a literal of its own. Two hand-written
        /// copies of the bindings is the drift this whole type was extracted to stop, and the copy
        /// was the only place the game ever mentioned M.
        /// </remarks>
        public readonly bool OnMenu;

        public ControlEntry(string text, bool onStatusLine, ControlKind kind, bool onMenu = false)
        {
            Text = text;
            OnStatusLine = onStatusLine;
            Kind = kind;
            OnMenu = onMenu;
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

        /// <summary>
        /// The key for the timing diagram, which the clock strip names on every level with a clock.
        /// </summary>
        /// <remarks>
        /// Kept off the controls line: it is only worth anything on a level with a clock, and the
        /// strip that says so is on screen exactly then. It used to be named nowhere at all, though
        /// its own remarks call it the notation the course uses.
        ///
        /// F2, not F3: in a browser F3 opens the page's find bar, which is what pressing it did in
        /// a playtest, 2026-09-26. F2 is the one function key browsers leave alone -- F1 is their
        /// help, F5 reloads, F6 and F10 to F12 are taken.
        /// </remarks>
        public static readonly ControlEntry TimingDiagram =
            new ControlEntry("F2 for the timing diagram", false, ControlKind.Running);

        public static IReadOnlyList<ControlEntry> All { get; } = new[]
        {
            // The number keys and redo are bound (PlacementController, SimulationInput) and were
            // named nowhere in the game -- only in the README. The card is where the rest are
            // listed, so they go there and not on the crowded line.
            new ControlEntry("1 to 7 to pick a part", false, ControlKind.Building),
            new ControlEntry("drag a port to wire", true, ControlKind.Building),
            new ControlEntry("right click to delete", true, ControlKind.Building),
            new ControlEntry("scroll a wire to re-time", true, ControlKind.Building),
            new ControlEntry("ctrl+Z to undo", true, ControlKind.Building),
            new ControlEntry("ctrl+Y to redo", false, ControlKind.Building),
            new ControlEntry("shift+R to clear", true, ControlKind.Building),

            new ControlEntry("Enter to run", false, ControlKind.Running),
            new ControlEntry("R to reset the board", false, ControlKind.Running),
            new ControlEntry("Space to pause a run", false, ControlKind.Running),
            new ControlEntry("right arrow to step one tick", false, ControlKind.Running),
            TimingDiagram,

            // H and M are off the menu's line: with the menu open neither does anything -- the level
            // list will not stack on the menu, and H is held back the same way.
            new ControlEntry("H for help", true, ControlKind.Everything),
            new ControlEntry("M for levels", true, ControlKind.Everything),
            new ControlEntry("Q and E to change level", false, ControlKind.Everything),
            new ControlEntry("N to mute", true, ControlKind.Everything, onMenu: true),

            // On the status line, though that row is the crowded one. It was left off it once, and
            // then the only places the key was named were the menu itself -- which a player has to
            // be on already -- and the card at the end of the tutorial, which a player who skipped it
            // never sees. A way back to the front door that is never mentioned is a dead end.
            //
            // And off the menu's own line, now that the board names it: at the foot of the main
            // menu it offered the screen the player was on, and the key there closes it. It was M
            // until a playtest swapped M and Escape, 2026-09-26.
            new ControlEntry("ESC for the main menu", true, ControlKind.Everything),
        };

        /// <summary>The groups in the order the card lays them out.</summary>
        /// <remarks>
        /// Built from <see cref="All"/> rather than written out again, so a control cannot be in the
        /// list and missing from the card, or on the card twice.
        /// </remarks>
        public static IReadOnlyList<ControlGroup> Groups { get; } = BuildGroups();

        /// <summary>The short line at the foot of the main menu.</summary>
        /// <remarks>
        /// Derived, like <see cref="Line"/> and <see cref="Groups"/>. The menu used to draw its own
        /// literal -- "M menu     ESC levels     H help     N mute" -- which was a second
        /// hand-written copy of the bindings and the only place the menu's key was ever mentioned.
        /// </remarks>
        public static string MenuLine => Joined(entry => entry.OnMenu);

        /// <summary>The one-line strip along the bottom of the board.</summary>
        public static string Line => Joined(entry => entry.OnStatusLine);

        /// <summary>Every entry the predicate accepts, in list order, on one line.</summary>
        private static string Joined(System.Predicate<ControlEntry> wanted)
        {
            var text = new StringBuilder();

            foreach (ControlEntry entry in All)
            {
                if (!wanted(entry))
                    continue;

                if (text.Length > 0)
                    text.Append(LineSeparator);

                text.Append(entry.Text);
            }

            return text.ToString();
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
