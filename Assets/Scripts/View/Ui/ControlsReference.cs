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

    /// <summary>Where on the board a control is shown, if anywhere.</summary>
    /// <remarks>
    /// Beside the thing it works, as near as it will go. The board's controls were one line along the
    /// bottom, in list order, so "ctrl+Z to undo" sat far from UNDO and a player had to read the whole
    /// line to find the key for the button under their hand (playtest, 2026-09-27). Now a key that
    /// works a button is shown over that button, the main menu's key under MENU and the help key
    /// under the help badge; what works nothing on screen -- the mouse, and keys with no button --
    /// sits in two short blocks either side of the buttons.
    /// </remarks>
    public enum ControlSpot
    {
        /// <summary>Nowhere on the board: the tutorial's card only.</summary>
        None,

        /// <summary>Left of the run buttons: the upper line, level with the keys over them.</summary>
        /// <remarks>
        /// The upper lines are level with the names under the board's bottom row, so each holds one
        /// short phrase and stops well short of the edge columns. Two phrases there reached the
        /// name of a bottom-left source -- "B1drag a port to wire" on Pass it on (2026-09-27).
        /// </remarks>
        LeftUpper,

        /// <summary>Left of the run buttons: the lower line, level with the buttons.</summary>
        LeftLower,

        /// <summary>Right of the run buttons, upper.</summary>
        RightUpper,

        /// <summary>Right of the run buttons, lower.</summary>
        RightLower,

        /// <summary>Its key, over RUN.</summary>
        RunButton,

        /// <summary>Its key, over RESET.</summary>
        ResetButton,

        /// <summary>Its key, over UNDO.</summary>
        UndoButton,

        /// <summary>Its key, over REDO.</summary>
        RedoButton,

        /// <summary>Its key, over CLEAR ALL -- START OVER, on a level that opens on a circuit.</summary>
        ClearButton,

        /// <summary>Its key, under the MENU button in the top-left corner.</summary>
        MenuButton,

        /// <summary>Its key, under the help badge in the top-right corner.</summary>
        HelpBadge,

        /// <summary>Its key, under the timing diagram's badge, beside the help badge.</summary>
        TimingBadge,

        /// <summary>Its key, before the level counter on the banner: the key for the level before.</summary>
        /// <remarks>
        /// Q and E were named on the tutorial's card and the level list's help line and nowhere on
        /// the board, and a playtester found neither (2026-09-28). The counter they change is the
        /// thing they sit beside.
        /// </remarks>
        LevelBefore,

        /// <summary>Its key, after the level counter on the banner: the key for the level after.</summary>
        LevelAfter,
    }

    /// <summary>One control the player can use, and where it is worth saying so.</summary>
    public readonly struct ControlEntry
    {
        /// <summary>The whole phrase, input and effect together: "ctrl+Z to undo".</summary>
        public readonly string Text;

        /// <summary>Where on the board it is shown, if it is.</summary>
        /// <remarks>
        /// Not everything is. The board's controls are on screen permanently, so they carry what a
        /// player reaches for mid-build; the rest are listed once, on the tutorial's card, where there
        /// is room for them.
        /// </remarks>
        public readonly ControlSpot Spot;

        /// <summary>Whether the board shows it anywhere.</summary>
        public bool OnBoard => Spot != ControlSpot.None;

        /// <summary>
        /// The key alone, for a spot that shows only the key: "ctrl+Z" from "ctrl+Z to undo". The
        /// phrase's first word, so the key over a button cannot say one thing and the card another.
        /// </summary>
        public string Key
        {
            get
            {
                int space = Text.IndexOf(' ');
                return space < 0 ? Text : Text.Substring(0, space);
            }
        }

        /// <summary>
        /// Whether its spot shows the key alone, on a button the words are already written on,
        /// rather than the whole phrase.
        /// </summary>
        public bool ShowsKeyOnly => Spot >= ControlSpot.RunButton;

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

        public ControlEntry(string text, ControlSpot spot, ControlKind kind, bool onMenu = false)
        {
            Text = text;
            Spot = spot;
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
        /// <summary>Between the phrases of one block. Wide, because it is the only thing dividing them.</summary>
        public const string LineSeparator = "     ";

        /// <summary>
        /// The key for the timing diagram, under its badge beside the help badge.
        /// </summary>
        /// <remarks>
        /// While the diagram showed only the clock, it was named only on the clock strip, on the five
        /// levels with a clock. Once it showed every source and bin it was worth something on every
        /// level, and a key named nowhere a player looks does not exist -- so it has a badge, and
        /// its key is under it as H is under the help's (2026-09-30).
        ///
        /// F2, not F3: in a browser F3 opens the page's find bar, which is what pressing it did in
        /// a playtest, 2026-09-26. F2 is the one function key browsers leave alone -- F1 is their
        /// help, F5 reloads, F6 and F10 to F12 are taken.
        /// </remarks>
        public static readonly ControlEntry TimingDiagram =
            new ControlEntry("F2 for the timing diagram", ControlSpot.TimingBadge, ControlKind.Running);

        public static IReadOnlyList<ControlEntry> All { get; } = new[]
        {
            // The number keys and redo are bound (PlacementController, SimulationInput) and were
            // named nowhere in the game -- only in the README. The number keys are on the card; redo
            // has a button now, so its key is over it with the others.
            new ControlEntry("1 to 7 to pick a part", ControlSpot.None, ControlKind.Building),
            // Said in full after a playtest found the controls unclear (2026-09-28): "right click to
            // delete" did not say what, and "re-time" named an idea the player had not met yet. The
            // wire's delay sentence takes a lower line, which has the room.
            new ControlEntry("drag a port to wire", ControlSpot.LeftUpper, ControlKind.Building),
            new ControlEntry("right click a part or wire to remove it", ControlSpot.LeftLower, ControlKind.Building),
            new ControlEntry("scroll a wire to change its delay", ControlSpot.RightLower, ControlKind.Building),
            new ControlEntry("ctrl+Z to undo", ControlSpot.UndoButton, ControlKind.Building),
            new ControlEntry("ctrl+Y to redo", ControlSpot.RedoButton, ControlKind.Building),
            new ControlEntry("shift+R to clear", ControlSpot.ClearButton, ControlKind.Building),

            new ControlEntry("Enter to run", ControlSpot.RunButton, ControlKind.Running),
            new ControlEntry("R to reset the board", ControlSpot.ResetButton, ControlKind.Running),
            new ControlEntry("Space to pause a run", ControlSpot.None, ControlKind.Running),
            new ControlEntry("right arrow to step one tick", ControlSpot.None, ControlKind.Running),
            TimingDiagram,

            // H and M are off the menu's line: with the menu open neither does anything -- the level
            // list will not stack on the menu, and H is held back the same way.
            new ControlEntry("H for help", ControlSpot.HelpBadge, ControlKind.Everything),
            new ControlEntry("M for levels", ControlSpot.RightUpper, ControlKind.Everything),
            new ControlEntry("Q for the level before", ControlSpot.LevelBefore, ControlKind.Everything),
            new ControlEntry("E for the level after", ControlSpot.LevelAfter, ControlKind.Everything),

            // Off the board since the controls there grew (2026-09-28): the upper line beside M has
            // room for one short phrase. Named on the main menu's own line, in Settings and on the
            // tutorial's card.
            new ControlEntry("N to mute", ControlSpot.None, ControlKind.Everything, onMenu: true),

            // On the board, under MENU. It was left off the board once, and
            // then the only places the key was named were the menu itself -- which a player has to
            // be on already -- and the card at the end of the tutorial, which a player who skipped it
            // never sees. A way back to the front door that is never mentioned is a dead end.
            //
            // And off the menu's own line, now that the board names it: at the foot of the main
            // menu it offered the screen the player was on, and the key there closes it. It was M
            // until a playtest swapped M and Escape, 2026-09-26.
            new ControlEntry("ESC for the main menu", ControlSpot.MenuButton, ControlKind.Everything),
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

        /// <summary>
        /// What one spot on the board shows: for a block beside the buttons, its phrases on one line;
        /// for a spot on a button, the key alone. Empty where nothing is shown.
        /// </summary>
        public static string At(ControlSpot spot)
        {
            if (spot == ControlSpot.None)
                return string.Empty;

            foreach (ControlEntry entry in All)
            {
                if (entry.Spot == spot && entry.ShowsKeyOnly)
                    return entry.Key;
            }

            return Joined(entry => entry.Spot == spot);
        }

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
