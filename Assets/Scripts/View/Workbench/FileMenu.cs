using System;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>What the FILE menu does (Part 13).</summary>
    public enum FileAction
    {
        /// <summary>The project as a zip ModelSim opens: its files and a run.do (Part 13a).</summary>
        ExportProject,

        /// <summary>A project from the Import folder, into MY CIRCUIT (Part 13d; a desktop and the editor).</summary>
        ImportFolder,

        /// <summary>The Import folder opened in the system's file browser (Part 13d; a desktop and the editor).</summary>
        OpenImportFolder,

        /// <summary>A project from files the player picks, into MY CIRCUIT (Part 13d; a browser).</summary>
        ImportFiles,

        /// <summary>A project from a folder the player picks, into MY CIRCUIT (Part 13d; a browser).</summary>
        ImportPickedFolder,

        /// <summary>The written testbench turned into the stimulus, so the inputs are set on the waves (Part 13c, 13d).</summary>
        ConvertTestbench,
    }

    /// <summary>
    /// The FILE menu, from the chip beside SAMPLE (Part 13): what goes out of the workbench and what comes in. A shell menu
    /// like the waves' EXPORT, holding Escape while it is open. What it offers depends on where the game runs -- a desktop
    /// reads the Import folder, a browser picks files -- and CONVERT only while a testbench drives the inputs.
    /// </summary>
    public sealed class FileMenu : WaveMenu
    {
        /// <summary>The menu's name, and an item's: this, and its action.</summary>
        public const string RootName = "Workbench file menu", ActionItem = "Workbench file ";

        private const float ItemWidth = 420f;

        private ShellButton _importFolder, _openFolder, _importFiles, _importPicked, _convert;

        public static FileMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<FileAction> onChoose)
        {
            FileMenu menu = Make<FileMenu>(shell, owner, RootName, palette, ItemWidth);
            menu.Item(ActionItem + FileAction.ExportProject, "EXPORT PROJECT: .SV FILES AND RUN.DO", () => onChoose?.Invoke(FileAction.ExportProject));
            menu._importFolder = menu.Item(ActionItem + FileAction.ImportFolder, "IMPORT A PROJECT FROM THE IMPORT FOLDER",
                () => onChoose?.Invoke(FileAction.ImportFolder), breakBefore: true);
            menu._openFolder = menu.Item(ActionItem + FileAction.OpenImportFolder, "OPEN THE IMPORT FOLDER", () => onChoose?.Invoke(FileAction.OpenImportFolder));
            menu._importFiles = menu.Item(ActionItem + FileAction.ImportFiles, "IMPORT A PROJECT'S FILES", () => onChoose?.Invoke(FileAction.ImportFiles),
                breakBefore: true);
            menu._importPicked = menu.Item(ActionItem + FileAction.ImportPickedFolder, "IMPORT A PROJECT'S FOLDER", () => onChoose?.Invoke(FileAction.ImportPickedFolder));
            menu._convert = menu.Item(ActionItem + FileAction.ConvertTestbench, "CONVERT THE TESTBENCH TO STEPS ON THE WAVES",
                () => onChoose?.Invoke(FileAction.ConvertTestbench), breakBefore: true);
            return menu;
        }

        /// <summary>Whether <paramref name="action"/> was on the menu when it last opened.</summary>
        public bool Offers(FileAction action)
        {
            ShellButton item = action switch
            {
                FileAction.ImportFolder => _importFolder,
                FileAction.OpenImportFolder => _openFolder,
                FileAction.ImportFiles => _importFiles,
                FileAction.ImportPickedFolder => _importPicked,
                FileAction.ConvertTestbench => _convert,
                _ => null,
            };
            return item == null || item.style.display != UnityEngine.UIElements.DisplayStyle.None;
        }

        /// <summary>Puts the menu up at <paramref name="screenPoint"/>: CONVERT only while <paramref name="hasTestbench"/>.</summary>
        public void Open(Vector2 screenPoint, bool hasTestbench)
        {
            bool picks = FileImport.Picks;
            Offer(_importFolder, !picks);
            Offer(_openFolder, !picks);
            Offer(_importFiles, picks);
            Offer(_importPicked, picks);
            Offer(_convert, hasTestbench);
            Show(screenPoint);
        }
    }
}
