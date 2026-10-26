using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's files (Part 13): the FILE chip beside SAMPLE and its menu. 13a: EXPORT PROJECT, the document as a zip
    /// ModelSim opens -- every file it prints as and a run.do -- saved as every export is (<see cref="FileSaver"/>): a
    /// download in a browser, a file in the Exports folder elsewhere, the chip saying where.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The FILE chip's name.</summary>
        public const string FileButton = "Workbench file";

        private const float FileWidth = 96f;

        private FileMenu _fileMenu;

        /// <summary>The FILE menu.</summary>
        public FileMenu FileMenu => _fileMenu;

        /// <summary>FILE, left of SAMPLE's key, in the header's row.</summary>
        private void BuildFileChip(VisualElement header, SchematicPalette palette, float right)
        {
            ShellButton file = ShellTheme.Button(FileButton, "FILE", palette, OpenFileMenu);
            ShellTheme.PlaceRight(file, right, (HeaderHeight - SampleHeight) * 0.5f, FileWidth, SampleHeight);
            header.Add(file);
        }

        /// <summary>Opens the FILE menu under its chip.</summary>
        public void OpenFileMenu()
        {
            if (_fileMenu == null || _shell == null)
                return;

            Vector2 at = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            VisualElement button = UQueryExtensions.Q(_shell.Root, FileButton);
            if (button != null && button.panel != null)
            {
                Rect chip = ShellPanel.ScreenRectOf(button);
                at = new Vector2(chip.xMin, chip.yMin);
            }

            _fileMenu.Open(at, _session != null && _session.Project.Bench != null);
        }

        private void FileChosen(FileAction action)
        {
            switch (action)
            {
                case FileAction.ExportProject:
                    ExportProject();
                    break;
                case FileAction.ImportFolder:
                    ImportFromFolder();
                    break;
                case FileAction.OpenImportFolder:
                    FileImport.OpenFolder();
                    Notice("put a project's files in " + FileImport.Folder + ", then FILE, IMPORT");
                    break;
                case FileAction.ImportFiles:
                case FileAction.ImportPickedFolder:
                    FileImport.Pick(action == FileAction.ImportPickedFolder);
                    break;
                case FileAction.ConvertTestbench:
                    AskConvert();
                    break;
            }
        }

        // -----------------------------------------------------------------
        // Import (Part 13d)
        // -----------------------------------------------------------------

        private AskMenu _ask;

        /// <summary>The question before an import replaces MY CIRCUIT, or before CONVERT.</summary>
        public AskMenu Ask => _ask;

        /// <summary>Where a question goes up: under the FILE chip.</summary>
        private Vector2 UnderTheFileChip()
        {
            VisualElement button = _shell != null ? UQueryExtensions.Q(_shell.Root, FileButton) : null;
            if (button != null && button.panel != null)
            {
                Rect chip = ShellPanel.ScreenRectOf(button);
                return new Vector2(chip.xMin, chip.yMin);
            }

            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        }

        /// <summary>A project from every file in the Import folder (<see cref="FileImport.ReadFolder"/>), into MY CIRCUIT; the folder made if it is not there.</summary>
        public void ImportFromFolder()
        {
            List<SourceFile> files = FileImport.ReadFolder();
            if (files.Count == 0)
            {
                Notice("the Import folder is empty: put a project's files in " + FileImport.Folder + ", then FILE, IMPORT");
                return;
            }

            Import(files);
        }

        /// <summary>
        /// <paramref name="files"/> imported (<see cref="ProjectImporter"/>) into MY CIRCUIT, asking first when MY CIRCUIT has a
        /// circuit in it. What the import said is listed in the TRANSCRIPT, each with its file and line; it is refused only
        /// when nothing at all could be made of the files.
        /// </summary>
        public void Import(IReadOnlyList<SourceFile> files)
        {
            if (_session == null)
                return;

            ProjectImport import = ProjectImporter.Import(files);
            if (!import.Succeeded)
            {
                SayInTheTranscript(import);
                ShowTranscript();
                Notice("nothing could be made of the " + Count(files.Count, "file") + ": the TRANSCRIPT says why");
                return;
            }

            KeepMyCircuit(now: true);
            if (MyCircuitHasAnything())
                _ask.Ask(UnderTheFileChip(),
                    "MY CIRCUIT has a circuit in it. Replace it with " + import.Project.Name + ", imported from " + Count(files.Count, "file") + "?",
                    "REPLACE MY CIRCUIT", () => Adopt(import));
            else
                Adopt(import);
        }

        /// <summary>The import made MY CIRCUIT and opened, with what it said in the TRANSCRIPT.</summary>
        private void Adopt(ProjectImport import)
        {
            ProgressStore store = Store();
            if (store != null)
            {
                store.SetProject(WorkbenchSamples.MyCircuitKey, import.Project);
                store.Save();
            }

            _keptProject = _session.Project;   // MY CIRCUIT is the import now: nothing left to keep of the session's
            _keepAt = -1;
            ShowSample(0);

            SayInTheTranscript(import);
            Project project = import.Project;
            int written = 0;
            foreach (Module module in project.Modules)
                if (module.IsWritten && module.Name != project.Bench)
                    written++;
            Notice("imported " + project.Name + ": " + Count(project.Modules.Count - (project.Bench != null ? 1 : 0), "module") +
                (written > 0 ? ", " + written.ToString(CultureInfo.InvariantCulture) + " of them written" : "") +
                (project.Bench != null ? ", run by its testbench " + project.Bench : "") +
                (import.Notes.Count > 0 ? ". The TRANSCRIPT has what the import said" : ""));
        }

        /// <summary>The project what the transcript's notes say was said about; null for none.</summary>
        private Project _notesAbout;

        /// <summary>
        /// What an import said, at the head of the TRANSCRIPT while the project shown is the one it was said about: the first
        /// edit, or another document, takes it away (<see cref="RefreshTranscript"/>), so a refused import's reasons do not
        /// stay on a circuit they are not about.
        /// </summary>
        private void SayInTheTranscript(ProjectImport import)
        {
            _transcript.Notes = TranscriptView.LinesOf(import.Notes);
            _notesAbout = _session.Project;
            _transcriptDrawn = -1;
        }

        /// <summary>Asks before CONVERT, saying what the stimulus will not keep; says why not when it cannot be.</summary>
        public void AskConvert()
        {
            if (_session == null || _session.Project.Bench == null)
                return;

            Conversion conversion = BenchConverter.Convert(_session.Project);
            if (!conversion.Succeeded)
            {
                Notice(conversion.Refusal);
                return;
            }

            string bench = _session.Project.Bench;
            string lost = conversion.Lost.Count == 0 ? "" : "\nIt keeps nothing else " + bench + " does: " + string.Join("; ", conversion.Lost) + ".";
            _ask.Ask(UnderTheFileChip(),
                "CONVERT records what " + bench + " drives into the inputs as steps you can set on the waves, and takes " + bench +
                " out. Ctrl+Z puts it back." + lost,
                "CONVERT", ConvertTestbench);
        }

        /// <summary>The testbench turned into the stimulus (<see cref="WorkbenchSession.ConvertBench"/>), and run again.</summary>
        public void ConvertTestbench()
        {
            if (_session == null)
                return;
            EditResult converted = _session.ConvertBench();
            Said(converted);
            if (converted.Done)
                Notice("converted: the inputs are steps on the waves now");
        }

        private static string Count(int n, string thing) => n.ToString(CultureInfo.InvariantCulture) + " " + thing + (n == 1 ? "" : "s");

        /// <summary>
        /// Saves the document as a zip ModelSim opens (<see cref="ProjectExport"/>), dated now: a download in a browser, a file
        /// in the Exports folder elsewhere. The chip says what was saved and where. Returns the file's name, or null.
        /// </summary>
        public string ExportProject()
        {
            if (_session == null)
                return null;

            byte[] bytes = ProjectExport.Zip(_session.Project, System.DateTime.Now);
            if (bytes == null)
            {
                Notice("the circuit does not compile, so there is nothing to export");
                return null;
            }

            string name = WaveExport.FileName(WorkbenchSamples.Chip[_sample].Name, "zip");
            Notice("saved " + name + " " + FileSaver.Save(name, bytes, "application/zip") + ": unzip it, and in ModelSim type do run.do");
            return name;
        }
    }
}
