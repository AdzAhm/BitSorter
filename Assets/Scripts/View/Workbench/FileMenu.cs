using System;
using UnityEngine;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>What the FILE menu does (Part 13).</summary>
    public enum FileAction
    {
        /// <summary>A new, blank circuit, named (Part 15a).</summary>
        NewCircuit,

        /// <summary>Another of the student's circuits (Part 15a).</summary>
        OpenCircuit,

        /// <summary>The circuit open, renamed (Part 15a); never MY CIRCUIT.</summary>
        RenameCircuit,

        /// <summary>The circuit open, deleted after asking (Part 15a); never MY CIRCUIT.</summary>
        DeleteCircuit,

        /// <summary>An imported circuit put back as it came in, after asking, and locked again (Part 15h-4d).</summary>
        BackToImported,

        /// <summary>The project as a zip ModelSim opens: its files and a run.do (Part 13a).</summary>
        ExportProject,

        /// <summary>The course's template files, each with the student's code between its markers (Part 13e).</summary>
        ExportSubmission,

        /// <summary>A project from the Import folder, as a circuit of its own (Part 13d, 15a; a desktop and the editor).</summary>
        ImportFolder,

        /// <summary>The Import folder opened in the system's file browser (Part 13d; a desktop and the editor).</summary>
        OpenImportFolder,

        /// <summary>A project from files the player picks, as a circuit of its own (Part 13d, 15a; a browser).</summary>
        ImportFiles,

        /// <summary>A project from a folder the player picks, as a circuit of its own (Part 13d, 15a; a browser).</summary>
        ImportPickedFolder,

        /// <summary>The written testbench turned into the stimulus, so the inputs are set on the waves (Part 13c, 13d).</summary>
        ConvertTestbench,
    }

    /// <summary>
    /// The FILE menu, from the chip beside SAMPLE (Part 13): the student's circuits -- NEW, OPEN, and RENAME and DELETE for
    /// one of their own (Part 15a) -- then what goes out of the workbench and what comes in. A shell menu like the waves'
    /// EXPORT, holding Escape while it is open. What it offers depends on where the game runs -- a desktop reads the Import
    /// folder, a browser picks files -- CONVERT only while a testbench drives the inputs, and EXPORT FOR SUBMISSION only
    /// while a module came from a course template.
    /// </summary>
    public sealed class FileMenu : WaveMenu
    {
        /// <summary>The menu's name, and an item's: this, and its action.</summary>
        public const string RootName = "Workbench file menu", ActionItem = "Workbench file ";

        private const float ItemWidth = 420f;

        private ShellButton _rename, _delete, _backToImported, _export, _submission, _importFolder, _openFolder, _importFiles, _importPicked, _convert;

        public static FileMenu Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<FileAction> onChoose)
        {
            FileMenu menu = Make<FileMenu>(shell, owner, RootName, palette, ItemWidth);
            menu.Item(ActionItem + FileAction.NewCircuit, "NEW CIRCUIT", () => onChoose?.Invoke(FileAction.NewCircuit));
            menu.Item(ActionItem + FileAction.OpenCircuit, "OPEN A CIRCUIT", () => onChoose?.Invoke(FileAction.OpenCircuit));
            menu._rename = menu.Item(ActionItem + FileAction.RenameCircuit, "RENAME THIS CIRCUIT", () => onChoose?.Invoke(FileAction.RenameCircuit));
            menu._delete = menu.Item(ActionItem + FileAction.DeleteCircuit, "DELETE THIS CIRCUIT", () => onChoose?.Invoke(FileAction.DeleteCircuit));
            menu._backToImported = menu.Item(ActionItem + FileAction.BackToImported, "BACK TO AS IMPORTED", () => onChoose?.Invoke(FileAction.BackToImported));
            menu._export = menu.Item(ActionItem + FileAction.ExportProject, "EXPORT PROJECT: .SV FILES AND RUN.DO",
                () => onChoose?.Invoke(FileAction.ExportProject), breakBefore: true);
            menu._submission = menu.Item(ActionItem + FileAction.ExportSubmission, "EXPORT FOR SUBMISSION",
                () => onChoose?.Invoke(FileAction.ExportSubmission));
            menu._importFolder = menu.Item(ActionItem + FileAction.ImportFolder, "IMPORT A PROJECT FROM THE IMPORT FOLDER",
                () => onChoose?.Invoke(FileAction.ImportFolder), breakBefore: true);
            menu._openFolder = menu.Item(ActionItem + FileAction.OpenImportFolder, "OPEN THE IMPORT FOLDER", () => onChoose?.Invoke(FileAction.OpenImportFolder));
            menu._importFiles = menu.Item(ActionItem + FileAction.ImportFiles, "IMPORT A PROJECT'S FILES", () => onChoose?.Invoke(FileAction.ImportFiles),
                breakBefore: true);
            menu._importPicked = menu.Item(ActionItem + FileAction.ImportPickedFolder, "IMPORT A PROJECT'S FOLDER", () => onChoose?.Invoke(FileAction.ImportPickedFolder));
            menu._convert = menu.Item(ActionItem + FileAction.ConvertTestbench, "CONVERT TESTBENCH TO STEPS ON THE WAVES",
                () => onChoose?.Invoke(FileAction.ConvertTestbench), breakBefore: true);
            return menu;
        }

        /// <summary>Whether <paramref name="action"/> was on the menu when it last opened.</summary>
        public bool Offers(FileAction action)
        {
            ShellButton item = action switch
            {
                FileAction.RenameCircuit => _rename,
                FileAction.DeleteCircuit => _delete,
                FileAction.BackToImported => _backToImported,
                FileAction.ExportProject => _export,
                FileAction.ExportSubmission => _submission,
                FileAction.ImportFolder => _importFolder,
                FileAction.OpenImportFolder => _openFolder,
                FileAction.ImportFiles => _importFiles,
                FileAction.ImportPickedFolder => _importPicked,
                FileAction.ConvertTestbench => _convert,
                _ => null,
            };
            return item == null || item.style.display != UnityEngine.UIElements.DisplayStyle.None;
        }

        /// <summary>
        /// Puts the menu up at <paramref name="screenPoint"/>: RENAME and DELETE only while <paramref name="ownCircuit"/> is open
        /// -- a circuit of the student's other than MY CIRCUIT -- CONVERT only while <paramref name="hasTestbench"/>, and EXPORT
        /// FOR SUBMISSION only while <paramref name="hasTemplate"/>, and BACK TO AS IMPORTED only while <paramref name="imported"/>.
        /// None of those, nor EXPORT PROJECT, while the start page hides the circuit they would act on
        /// (<paramref name="circuitShown"/> false; the #84 review).
        /// </summary>
        public void Open(Vector2 screenPoint, bool hasTestbench, bool hasTemplate, bool ownCircuit = false, bool circuitShown = true,
            bool imported = false)
        {
            Offer(_backToImported, imported && circuitShown);
            bool picks = FileImport.Picks;
            ownCircuit &= circuitShown;
            hasTestbench &= circuitShown;
            hasTemplate &= circuitShown;
            Offer(_rename, ownCircuit);
            Offer(_delete, ownCircuit);
            Offer(_export, circuitShown);
            Offer(_submission, hasTemplate);
            Offer(_importFolder, !picks);
            Offer(_openFolder, !picks);
            Offer(_importFiles, picks);
            Offer(_importPicked, picks);
            Offer(_convert, hasTestbench);
            Show(screenPoint);
        }
    }
}
