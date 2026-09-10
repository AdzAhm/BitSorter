using System.Collections.Generic;
using System.Text;

namespace BitSorter.View
{
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

        public ControlEntry(string text, bool onStatusLine)
        {
            Text = text;
            OnStatusLine = onStatusLine;
        }

        public override string ToString() => Text;
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
    /// Adding a binding here reaches both renderings with no second edit. That is the property
    /// ControlsReferenceTests pins.
    /// </remarks>
    public static class ControlsReference
    {
        /// <summary>Separator on the status line. Wide, because it is the only thing dividing them.</summary>
        public const string LineSeparator = "     ";

        public static IReadOnlyList<ControlEntry> All { get; } = new[]
        {
            // The build loop, and the reason the status line exists.
            new ControlEntry("drag a port to wire", true),
            new ControlEntry("right click to delete", true),
            new ControlEntry("scroll a wire to re-time", true),
            new ControlEntry("ctrl+Z to undo", true),
            new ControlEntry("shift+R to clear", true),
            new ControlEntry("H for help", true),
            new ControlEntry("ESC for levels", true),
            new ControlEntry("N to mute", true),

            // Bound in SimulationInput, and off the status line only for want of room.
            new ControlEntry("Enter to run", false),
            new ControlEntry("R to reset the board", false),
            new ControlEntry("Space to pause a run", false),
            new ControlEntry("right arrow to step one tick", false),
            new ControlEntry("Q and E to change level", false),
        };

        /// <summary>The one-line strip along the bottom of the board.</summary>
        public static string Line => Join(LineSeparator, true);

        /// <summary>Everything, one per line, for the tutorial's card and the help panel.</summary>
        public static string Card => Join("\n", false);

        private static string Join(string separator, bool statusLineOnly)
        {
            var text = new StringBuilder();

            foreach (ControlEntry entry in All)
            {
                if (statusLineOnly && !entry.OnStatusLine)
                    continue;

                if (text.Length > 0)
                    text.Append(separator);

                text.Append(entry.Text);
            }

            return text.ToString();
        }
    }
}
