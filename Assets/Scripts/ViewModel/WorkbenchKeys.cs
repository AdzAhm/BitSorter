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
            new KeyBinding("UpArrow", WorkbenchCommand.PageUp, "UP", "move the page up"),
            new KeyBinding("PageUp", WorkbenchCommand.PageUp, "PG UP", "move the page up"),
            new KeyBinding("DownArrow", WorkbenchCommand.PageDown, "DOWN", "move the page down"),
            new KeyBinding("PageDown", WorkbenchCommand.PageDown, "PG DN", "move the page down"),
            new KeyBinding("A", WorkbenchCommand.AddSignal, "A", "add a signal to the waveform"),
            new KeyBinding("G", WorkbenchCommand.Group, "G", "group the selected rows"),
            new KeyBinding("X", WorkbenchCommand.Radix, "X", "the selected rows in the next radix"),
            new KeyBinding("L", WorkbenchCommand.Names, "L", "short or full names"),
            new KeyBinding("C", WorkbenchCommand.ClearCursor, "C", "take the second cursor away"),
            new KeyBinding("Delete", WorkbenchCommand.RemoveRows, "DEL", "take the selected rows out"),
            new KeyBinding("Backspace", WorkbenchCommand.RemoveRows, "DEL", "take the selected rows out"),
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
