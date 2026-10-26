using System;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>What the FILE menu does (Part 13).</summary>
    public enum FileAction
    {
        /// <summary>The project as a zip ModelSim opens: its files and a run.do (Part 13a).</summary>
        ExportProject,
    }

    /// <summary>
    /// The FILE menu, from the chip beside SAMPLE (Part 13): what goes out of the workbench and, from 13d, what comes in.
    /// A shell menu like the waves' EXPORT, holding Escape while it is open.
    /// </summary>
    public sealed class FileMenu : WaveMenu
    {
        /// <summary>The menu's name, and an item's: this, and its action.</summary>
        public const string RootName = "Workbench file menu", ActionItem = "Workbench file ";

        private const float ItemWidth = 380f;

        public static FileMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<FileAction> onChoose)
        {
            FileMenu menu = Make<FileMenu>(shell, owner, RootName, palette, ItemWidth);
            menu.Item(ActionItem + FileAction.ExportProject, "EXPORT PROJECT: .SV FILES AND RUN.DO", () => onChoose?.Invoke(FileAction.ExportProject));
            return menu;
        }

        public void Open(Vector2 screenPoint) => Show(screenPoint);
    }
}
