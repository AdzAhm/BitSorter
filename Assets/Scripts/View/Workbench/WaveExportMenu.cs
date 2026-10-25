using System;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>What the export menu saves.</summary>
    public enum WaveExportKind
    {
        /// <summary>The run as a VCD: every signal, for GTKWave or anything else that reads one.</summary>
        Vcd,

        /// <summary>The waveform as a picture, in Schematic Light, for a report.</summary>
        Png,
    }

    /// <summary>
    /// The menu EXPORT, or E, opens (Part 9b-4): the run as a VCD, or the waveform as a PNG. Everything a
    /// workbench menu does besides is <see cref="WaveMenu"/>'s.
    /// </summary>
    public sealed class WaveExportMenu : WaveMenu
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench export menu";

        /// <summary>An item's name: this, and what it saves.</summary>
        public const string KindItem = "Workbench export ";

        private const float ItemWidth = 300f;

        /// <summary>The menu on <paramref name="shell"/>, shut, calling <paramref name="onChoose"/> as an item is chosen.</summary>
        public static WaveExportMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<WaveExportKind> onChoose)
        {
            WaveExportMenu menu = Make<WaveExportMenu>(shell, owner, RootName, palette, ItemWidth);
            menu.Item(KindItem + WaveExportKind.Vcd, "VCD: EVERY SIGNAL", () => onChoose?.Invoke(WaveExportKind.Vcd));
            menu.Item(KindItem + WaveExportKind.Png, "PNG: THE WAVEFORM", () => onChoose?.Invoke(WaveExportKind.Png));
            return menu;
        }

        /// <summary>Opens the menu at <paramref name="screenPoint"/>.</summary>
        public void Open(Vector2 screenPoint) => Show(screenPoint);
    }
}
