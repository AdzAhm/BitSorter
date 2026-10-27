using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>One line of the keyboard map: a key's cap, or a gesture's, and what it does.</summary>
    public readonly struct KeyMapEntry
    {
        public KeyMapEntry(string cap, string says)
        {
            Cap = cap;
            Says = says;
        }

        /// <summary>What its cap says: <c>SPACE</c>, <c>CTRL Z</c>, or a gesture's <c>DRAG</c>.</summary>
        public string Cap { get; }

        public string Says { get; }

        public override string ToString() => Cap + " " + Says;
    }

    /// <summary>A heading of the keyboard map, and its lines.</summary>
    public sealed class KeyMapGroup
    {
        public KeyMapGroup(string title, IReadOnlyList<KeyMapEntry> entries)
        {
            Title = title;
            Entries = entries;
        }

        public string Title { get; }

        public IReadOnlyList<KeyMapEntry> Entries { get; }
    }

    /// <summary>Where the pointer is, as the status line names its gestures.</summary>
    public enum PointerArea { Elsewhere, Circuit, Waves, Blocks }

    /// <summary>
    /// The keyboard map (Part 11d), Unity-free: every key the workbench answers, from <see cref="WorkbenchKeys"/> alone, under
    /// RUN, CIRCUIT, WAVES and VIEW, and the mouse's gestures on the circuit, the waves and in PARTS -- what <c>?</c> shows;
    /// and the status line's one line for where the pointer is.
    /// </summary>
    /// <remarks>
    /// **No second copy of a key**: a key's lines are made from its binding, so the map, the caps over the buttons and the
    /// keys the panel answers cannot disagree. Two keys that do one thing in one cap -- = and the keypad's + -- are one line.
    /// **The gestures are the one thing written here**, since no table holds the mouse; they replaced the two lines of help
    /// along the bottom (Parts 10a to 11c), which were a second list of them.
    /// </remarks>
    public static class KeyMap
    {
        /// <summary>The gestures on the circuit, as the map lists them.</summary>
        public static readonly IReadOnlyList<KeyMapEntry> CircuitGestures = new[]
        {
            new KeyMapEntry("CLICK", "a part on the left, then the circuit, to place it"),
            new KeyMapEntry("DRAG", "from a pin to wire it; a part to move it; nothing to select"),
            new KeyMapEntry("DOUBLE-CLICK", "a block to step inside it; any other part, or a wire, to inspect it"),
            new KeyMapEntry("RIGHT-CLICK", "the circuit's menu: align, turn, copy, delete"),
            new KeyMapEntry("MIDDLE-DRAG", "pan; or SPACE and drag"),
            new KeyMapEntry("WHEEL", "zoom about the pointer"),
            new KeyMapEntry("ALT-CLICK", "a wire to add its signal to the waveform"),
        };

        /// <summary>
        /// The gestures in PARTS, as the map lists them (Part 14g): deleting a block leaves its module, the player's work, and
        /// this is how a module goes.
        /// </summary>
        public static readonly IReadOnlyList<KeyMapEntry> PartsGestures = new[]
        {
            new KeyMapEntry("RIGHT-CLICK", "a block's row to delete its module, once nothing uses it"),
        };

        /// <summary>The gestures on the waves, as the map lists them.</summary>
        public static readonly IReadOnlyList<KeyMapEntry> WavesGestures = new[]
        {
            new KeyMapEntry("CLICK", "the waves to seek there; drag to scrub"),
            new KeyMapEntry("SHIFT-CLICK", "a second cursor"),
            new KeyMapEntry("WHEEL", "zoom time about the pointer; SHIFT and the wheel moves through it"),
            new KeyMapEntry("RIGHT-CLICK", "a row's menu, or a moment's"),
            new KeyMapEntry("DOUBLE-CLICK", "an input's wave to set its value"),
            new KeyMapEntry("CLICK A NAME", "select its row; SHIFT or CTRL for more"),
        };

        /// <summary>
        /// The keys of a written block's code while it has the keyboard (Part 14g): the editor's own, which no key of the
        /// workbench takes from it -- every one of those is dead while anything is typed in (<c>UiText</c>). Ctrl+V pastes
        /// text there and parts only on the circuit. The browser's editor answers all of them; the desktop build's answers all
        /// but the three said to be a browser's.
        /// </summary>
        public static readonly IReadOnlyList<KeyMapEntry> CodeKeys = new[]
        {
            new KeyMapEntry("CTRL ENTER", "apply the code now; it applies itself when typing pauses"),
            new KeyMapEntry("CTRL S", "apply it now, as saving would"),
            new KeyMapEntry("CTRL Z", "undo the typing; CTRL Y or CTRL SHIFT Z redoes it"),
            new KeyMapEntry("TAB", "indent the lines by four spaces; SHIFT TAB outdents"),
            new KeyMapEntry("CTRL /", "comment the lines out, or back in"),
            new KeyMapEntry("ALT UP", "move the lines up, ALT DOWN down: in a browser"),
            new KeyMapEntry("CTRL F", "find and replace: in a browser"),
            new KeyMapEntry("CTRL D", "select the next match: in a browser"),
            new KeyMapEntry("ESC", "close find, then let the code go; the next Escape closes the workbench"),
        };

        /// <summary>
        /// Which headings the map draws in each of its three columns, top to bottom (Part 14g): every heading in exactly one,
        /// which <c>KeyMapTests</c> holds, laid out so no column is much taller than the rest.
        /// </summary>
        public static readonly IReadOnlyList<IReadOnlyList<string>> Columns = new IReadOnlyList<string>[]
        {
            new[] { "RUN", "VIEW", "IN PARTS" },
            new[] { "CIRCUIT", "WAVES" },
            new[] { "ON THE CIRCUIT", "ON THE WAVES", "IN THE CODE" },
        };

        /// <summary>The keys' headings, in order: set before <see cref="Groups"/>, which is made from them.</summary>
        private static readonly string[] KeyGroups = { "RUN", "CIRCUIT", "WAVES", "VIEW" };

        /// <summary>Every heading of the map, in order: the keys under theirs, then the gestures.</summary>
        public static readonly IReadOnlyList<KeyMapGroup> Groups = Build();

        /// <summary>The heading a key is listed under.</summary>
        public static string GroupOf(WorkbenchCommand command)
        {
            switch (command)
            {
                case WorkbenchCommand.PlayPause:
                case WorkbenchCommand.Restart:
                case WorkbenchCommand.NextEdge:
                case WorkbenchCommand.PreviousEdge:
                case WorkbenchCommand.Slower:
                case WorkbenchCommand.Faster:
                case WorkbenchCommand.StepForward:
                case WorkbenchCommand.StepBack:
                case WorkbenchCommand.StepForwardFine:
                case WorkbenchCommand.StepBackFine:
                case WorkbenchCommand.NextSample:
                case WorkbenchCommand.Unit:
                case WorkbenchCommand.Export:
                    return "RUN";

                case WorkbenchCommand.AddSignal:
                case WorkbenchCommand.Group:
                case WorkbenchCommand.Radix:
                case WorkbenchCommand.Names:
                case WorkbenchCommand.ClearCursor:
                case WorkbenchCommand.PageUp:
                case WorkbenchCommand.PageDown:
                    return "WAVES";

                case WorkbenchCommand.ZoomIn:
                case WorkbenchCommand.ZoomOut:
                case WorkbenchCommand.Fit:
                case WorkbenchCommand.Keys:
                    return "VIEW";

                default:
                    return "CIRCUIT";
            }
        }

        private static IReadOnlyList<KeyMapGroup> Build()
        {
            var groups = new List<KeyMapGroup>();
            foreach (string title in KeyGroups)
            {
                var entries = new List<KeyMapEntry>();
                foreach (KeyBinding binding in WorkbenchKeys.All)
                {
                    if (GroupOf(binding.Command) != title)
                        continue;
                    var entry = new KeyMapEntry(binding.Cap, binding.Says);
                    if (!entries.Contains(entry))
                        entries.Add(entry);
                }

                groups.Add(new KeyMapGroup(title, entries.AsReadOnly()));
            }

            groups.Add(new KeyMapGroup("ON THE CIRCUIT", CircuitGestures));
            groups.Add(new KeyMapGroup("ON THE WAVES", WavesGestures));
            groups.Add(new KeyMapGroup("IN PARTS", PartsGestures));
            groups.Add(new KeyMapGroup("IN THE CODE", CodeKeys));
            return groups.AsReadOnly();
        }

        /// <summary>
        /// The status line for where the pointer is: the gestures there, short, and how to see every key -- one line,
        /// where two lines of help used to name every gesture at once.
        /// </summary>
        public static string StatusFor(PointerArea area)
        {
            switch (area)
            {
                case PointerArea.Circuit:
                    return "circuit:  drag from a pin to wire  ·  double-click to inspect  ·  right-click for its menu  ·  wheel to zoom  ·  ? every key";
                case PointerArea.Waves:
                    return "waves:  click to seek  ·  shift-click a second cursor  ·  wheel to zoom time  ·  right-click for a menu  ·  ? every key";
                case PointerArea.Blocks:
                    // A block's module stays when its last block goes, since it is the player's work: this is how it goes.
                    return "blocks:  click to take one  ·  right-click to delete its module  ·  ? every key";
                default:
                    return "? shows every key and gesture";
            }
        }
    }
}
