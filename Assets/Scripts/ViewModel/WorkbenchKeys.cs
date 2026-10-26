using System;
using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>What a key does on the workbench.</summary>
    public enum WorkbenchCommand
    {
        PlayPause,
        Restart,
        NextSample,
        NextEdge,
        PreviousEdge,
        Slower,
        Faster,
        StepForward,
        StepBack,
        StepForwardFine,
        StepBackFine,
        ZoomIn,
        ZoomOut,
        Fit,
        PageUp,
        PageDown,
        AddSignal,
        Group,
        Radix,
        Names,
        ClearCursor,
        RemoveRows,
        Unit,
        Undo,
        Redo,
        Export,

        /// <summary>The selection on the canvas turned a quarter, counter-clockwise (Part 10a).</summary>
        TurnLeft,

        /// <summary>The selection turned a quarter back, clockwise.</summary>
        TurnRight,

        /// <summary>The selection mirrored left to right.</summary>
        Mirror,

        /// <summary>Every part and wire on the canvas selected.</summary>
        SelectAll,

        /// <summary>The inspector of the one part or wire selected on the canvas (Part 10c).</summary>
        Inspect,

        /// <summary>The parts selected, and the wires between them, copied (Part 10d).</summary>
        Copy,

        /// <summary>The selection copied and taken away.</summary>
        Cut,

        /// <summary>What was copied made again, a step on.</summary>
        Paste,

        /// <summary>The keyboard map: every key and gesture, grouped (Part 11d).</summary>
        Keys,
    }

    /// <summary>One key and what it does: the key by its Input System name, the modifiers held, and the words for it.</summary>
    public readonly struct KeyBinding
    {
        public KeyBinding(string key, WorkbenchCommand command, string cap, string says, bool shift = false, bool ctrl = false)
        {
            Key = key;
            Command = command;
            Cap = cap;
            Says = says;
            Shift = shift;
            Ctrl = ctrl;
        }

        /// <summary>The key, as the Input System's <c>Key</c> names it: <c>Space</c>, <c>RightArrow</c>, <c>A</c>.</summary>
        public string Key { get; }

        public WorkbenchCommand Command { get; }

        /// <summary>What the key's cap over its button says: <c>SPACE</c>, <c>A</c>.</summary>
        public string Cap { get; }

        /// <summary>What it does, for a list of keys.</summary>
        public string Says { get; }

        public bool Shift { get; }
        public bool Ctrl { get; }

        public override string ToString() => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + Key + " " + Command;
    }

    /// <summary>
    /// Every key the workbench answers, as data: the panel reads its keys from this table, and Part 11's
    /// keyboard map can list them. Keys the rest of the game or a browser already own are refused.
    /// </summary>
    /// <remarks>
    /// **Reserved for the whole game:** N (sound), M (the level list), H (help), F2 and F8 (the clock
    /// diagram and the timing strip), and Escape, which closes what is on top -- on the workbench, its
    /// popups first and then the workbench (<c>UiEscape</c>). **Reserved by browsers:** Ctrl with R, W,
    /// S, T, N or L (reload, close, save, new tab, new window, address bar), which a web page cannot
    /// take back, and every function key but F2 and F8, as the 4.x controls found (CLAUDE.md, F8).
    /// </remarks>
    public static class WorkbenchKeys
    {
        /// <summary>The keys the rest of the game owns.</summary>
        public static readonly IReadOnlyList<string> GameKeys = Array.AsReadOnly(new[] { "N", "M", "H", "F2", "F8", "Escape" });

        /// <summary>The letters a browser keeps with Ctrl.</summary>
        public static readonly IReadOnlyList<string> BrowserCtrlKeys = Array.AsReadOnly(new[] { "R", "W", "S", "T", "N", "L" });

        /// <summary>Every binding, in the order a list of keys shows them.</summary>
        public static readonly IReadOnlyList<KeyBinding> All = Array.AsReadOnly(new[]
        {
            new KeyBinding("Space", WorkbenchCommand.PlayPause, "SPACE", "play or pause"),
            new KeyBinding("R", WorkbenchCommand.Restart, "R", "play again from the start"),
            new KeyBinding("Period", WorkbenchCommand.NextEdge, ".", "to the next edge of the selected rows"),
            new KeyBinding("Comma", WorkbenchCommand.PreviousEdge, ",", "to the edge before, on the selected rows"),
            new KeyBinding("LeftBracket", WorkbenchCommand.Slower, "[", "play slower"),
            new KeyBinding("RightBracket", WorkbenchCommand.Faster, "]", "play faster"),
            new KeyBinding("S", WorkbenchCommand.NextSample, "S", "show the next sample"),
            new KeyBinding("RightArrow", WorkbenchCommand.StepForward, "RIGHT", "a unit later"),
            new KeyBinding("LeftArrow", WorkbenchCommand.StepBack, "LEFT", "a unit earlier"),
            new KeyBinding("RightArrow", WorkbenchCommand.StepForwardFine, "SHIFT RIGHT", "a tenth of a unit later", shift: true),
            new KeyBinding("LeftArrow", WorkbenchCommand.StepBackFine, "SHIFT LEFT", "a tenth of a unit earlier", shift: true),
            new KeyBinding("Equals", WorkbenchCommand.ZoomIn, "+", "zoom the circuit in"),
            new KeyBinding("NumpadPlus", WorkbenchCommand.ZoomIn, "+", "zoom the circuit in"),
            new KeyBinding("Minus", WorkbenchCommand.ZoomOut, "-", "zoom the circuit out"),
            new KeyBinding("NumpadMinus", WorkbenchCommand.ZoomOut, "-", "zoom the circuit out"),
            new KeyBinding("F", WorkbenchCommand.Fit, "F", "show the whole circuit"),
            new KeyBinding("UpArrow", WorkbenchCommand.PageUp, "UP", "scroll the waves up"),
            new KeyBinding("PageUp", WorkbenchCommand.PageUp, "PG UP", "scroll the waves up"),
            new KeyBinding("DownArrow", WorkbenchCommand.PageDown, "DOWN", "scroll the waves down"),
            new KeyBinding("PageDown", WorkbenchCommand.PageDown, "PG DN", "scroll the waves down"),
            new KeyBinding("A", WorkbenchCommand.AddSignal, "A", "add a signal to the waveform"),
            new KeyBinding("G", WorkbenchCommand.Group, "G", "group the selected rows"),
            new KeyBinding("X", WorkbenchCommand.Radix, "X", "the selected rows in the next radix"),
            new KeyBinding("L", WorkbenchCommand.Names, "L", "short or full names"),
            new KeyBinding("C", WorkbenchCommand.ClearCursor, "C", "take the second cursor away"),
            new KeyBinding("Delete", WorkbenchCommand.RemoveRows, "DEL", "take away what is selected: parts on the circuit, or rows"),
            new KeyBinding("Backspace", WorkbenchCommand.RemoveRows, "DEL", "take away what is selected: parts on the circuit, or rows"),
            new KeyBinding("Q", WorkbenchCommand.TurnLeft, "Q", "turn the selected parts a quarter"),
            new KeyBinding("Q", WorkbenchCommand.TurnRight, "SHIFT Q", "turn them back a quarter", shift: true),
            new KeyBinding("V", WorkbenchCommand.Mirror, "V", "mirror the selected parts left to right"),
            new KeyBinding("A", WorkbenchCommand.SelectAll, "CTRL A", "select every part and wire", ctrl: true),
            new KeyBinding("Enter", WorkbenchCommand.Inspect, "ENTER", "inspect the part or wire selected: its delays, width, name"),
            new KeyBinding("C", WorkbenchCommand.Copy, "CTRL C", "copy the parts selected, and the wires between them", ctrl: true),
            new KeyBinding("X", WorkbenchCommand.Cut, "CTRL X", "cut the parts selected", ctrl: true),
            new KeyBinding("V", WorkbenchCommand.Paste, "CTRL V", "paste what was copied, a cell on", ctrl: true),
            new KeyBinding("U", WorkbenchCommand.Unit, "U", "count time in the next unit"),
            new KeyBinding("E", WorkbenchCommand.Export, "E", "export the run: a VCD, or the waveform as a PNG"),
            new KeyBinding("Z", WorkbenchCommand.Undo, "CTRL Z", "undo the last edit", ctrl: true),
            new KeyBinding("Y", WorkbenchCommand.Redo, "CTRL Y", "do it again", ctrl: true),
            new KeyBinding("Z", WorkbenchCommand.Redo, "CTRL SHIFT Z", "do it again", shift: true, ctrl: true),
            new KeyBinding("Slash", WorkbenchCommand.Keys, "?", "every key and gesture"),
        });

        /// <summary>The first binding for <paramref name="command"/>: the one its button's cap shows.</summary>
        public static KeyBinding For(WorkbenchCommand command)
        {
            foreach (KeyBinding binding in All)
                if (binding.Command == command)
                    return binding;
            throw new ArgumentOutOfRangeException(nameof(command), command, "No key does that.");
        }

        /// <summary>Why a binding may not be used -- a key the game or a browser owns -- or null when it may.</summary>
        public static string WhyRefused(KeyBinding binding)
        {
            if (!binding.Ctrl)
            {
                foreach (string key in GameKeys)
                    if (binding.Key == key)
                        return $"{key} is the game's, whatever is on screen";
            }
            else
            {
                foreach (string key in BrowserCtrlKeys)
                    if (binding.Key == key)
                        return $"Ctrl+{key} is the browser's, and a page cannot take it back";
            }

            if (binding.Key.Length >= 2 && binding.Key[0] == 'F' && int.TryParse(binding.Key.Substring(1), out int f) && f >= 1 && f <= 12)
                return $"F{f} is the browser's or the game's";

            return null;
        }
    }
}
