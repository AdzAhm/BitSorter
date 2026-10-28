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
    /// download in a browser, a file in the Exports folder elsewhere, the chip saying where. 13d: IMPORT and CONVERT. 13e:
    /// EXPORT FOR SUBMISSION, the course's template files handed back filled.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The FILE chip's name.</summary>
        public const string FileButton = "Workbench file";

        private FileMenu _fileMenu;

        /// <summary>The FILE menu.</summary>
        public FileMenu FileMenu => _fileMenu;

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

            _fileMenu.Open(at, _session != null && _session.Project.Bench != null, _session != null && FromATemplate(_session.Project) > 0,
                _sample == 0 && Documents.IsOwn(_documentKey), circuitShown: !PageShowing);
        }

        /// <summary>How many of <paramref name="project"/>'s modules came from a course template (Part 13e).</summary>
        private static int FromATemplate(Project project)
        {
            int n = 0;
            foreach (Module module in project.Modules)
                if (module.Template != null)
                    n++;
            return n;
        }

        private void FileChosen(FileAction action)
        {
            switch (action)
            {
                case FileAction.NewCircuit:
                    AskNewCircuit();
                    break;
                case FileAction.OpenCircuit:
                    OpenOpenMenu();
                    break;
                case FileAction.RenameCircuit:
                    AskRenameCircuit();
                    break;
                case FileAction.DeleteCircuit:
                    AskDeleteCircuit();
                    break;
                case FileAction.ExportProject:
                    ExportProject();
                    break;
                case FileAction.ExportSubmission:
                    ExportSubmission();
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

        /// <summary>The question before CONVERT, or before a circuit or a module is deleted.</summary>
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

        /// <summary>A project from every file in the Import folder (<see cref="FileImport.ReadFolder"/>), as a circuit of its own; the folder made if it is not there.</summary>
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
        /// <paramref name="files"/> imported (<see cref="ProjectImporter"/>) as a circuit of its own, named after the project
        /// (Part 15a: it asked before replacing MY CIRCUIT, which it no longer touches). What the import said is listed in the
        /// TRANSCRIPT, each with its file and line; it is refused only when nothing at all could be made of the files.
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

            Adopt(import);
        }

        /// <summary>The import made a circuit of its own, named after its project, and opened, with what it said in the TRANSCRIPT.</summary>
        private void Adopt(ProjectImport import)
        {
            string key = Documents.KeyOf(Documents.FreeName(DocumentKeys(), import.Project.Name));
            ProgressStore store = Store();
            if (store != null)
                store.SetProject(key, import.Project);
            OpenDocument(key);

            SayInTheTranscript(import);
            Project project = import.Project;
            int written = 0;
            foreach (Module module in project.Modules)
                if (module.IsWritten && module.Name != project.Bench)
                    written++;
            int templates = FromATemplate(project);
            Notice("imported " + project.Name + ": " + Count(project.Modules.Count - (project.Bench != null ? 1 : 0), "module") +
                (written > 0 ? ", " + written.ToString(CultureInfo.InvariantCulture) + " of them written" : "") +
                (project.Bench != null ? ", run by its testbench " + project.Bench : "") +
                (templates > 0 ? ". " + Count(templates, "course template") + ": write between the markers, then FILE, EXPORT FOR SUBMISSION" : "") +
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
        /// Code typed into the written module on screen and not yet applied, applied now as its editor would after the
        /// pause, so that what an export holds is what the student is looking at. The editor keeps typed text as its own until
        /// the pause or Ctrl+Enter, and text with an error as its own for good, while the document holds the last text that
        /// applied: exporting the document alone hands over older code without a word. Code that cannot apply is refused with
        /// what is wrong with it, <paramref name="outcome"/> saying what then does not happen ("exported", "handed in"), and
        /// false is returned: the export stops. True when what is on screen is the document's, whether or not it had to be applied.
        /// </summary>
        private bool ApplyTypedCode(string outcome)
        {
            if (!_session.ShowsCode || !(_session.Code is CodeDocument code) || !code.Pending)
                return true;

            EditResult applied = code.Apply();
            if (applied.Done)
                return true;

            Notice(code.Module + "'s code on screen does not apply, so it is not " + outcome + ": " + applied.Refusal);
            return false;
        }

        /// <summary>
        /// Saves the document as a zip ModelSim opens (<see cref="ProjectExport"/>), dated now: a download in a browser, a file
        /// in the Exports folder elsewhere. The chip says what was saved and where. Code typed and not yet applied is applied
        /// first, and code that cannot apply is refused (<see cref="ApplyTypedCode"/>). Returns the file's name, or null.
        /// </summary>
        public string ExportProject()
        {
            if (_session == null || !ApplyTypedCode("exported"))
                return null;

            byte[] bytes = ProjectExport.Zip(_session.Project, System.DateTime.Now);
            if (bytes == null)
            {
                Notice("the circuit does not compile, so there is nothing to export");
                return null;
            }

            string name = WaveExport.FileName(ExportName(), "zip");
            Notice("saved " + name + " " + FileSaver.Save(name, bytes, "application/zip") + ": unzip it, and in ModelSim type do run.do");
            return name;
        }

        /// <summary>
        /// Saves every course template the document came from with the student's code between its markers
        /// (<see cref="SubmissionExport"/>), as EXPORT PROJECT saves: the chip says what was filled and where it went, or why
        /// it was refused. Code typed and not yet applied is applied first, so what is handed in is what is on screen, and
        /// code that cannot apply is refused with what is wrong with it, never handed in as the last text that worked
        /// (<see cref="ApplyTypedCode"/>). Returns the file's name, or null.
        /// </summary>
        public string ExportSubmission()
        {
            if (_session == null || !ApplyTypedCode("handed in"))
                return null;

            Submission submission = SubmissionExport.Build(_session.Project, System.DateTime.Now);
            if (!submission.Succeeded)
            {
                Notice(submission.Refusal);
                return null;
            }

            string name = WaveExport.FileName(ExportName() + " submission", "zip");
            string uses = submission.NotHandedIn.Count == 0 ? ""
                : ". It uses " + string.Join(", ", submission.NotHandedIn) + ", not in it: anything the course did not give you goes between the markers";
            Notice("saved " + name + " " + FileSaver.Save(name, submission.Zip, "application/zip") + ": " + string.Join(", ", submission.Files) +
                " as the course gave " + (submission.Files.Count == 1 ? "it" : "them") + ", with your code between the markers" + uses);
            return name;
        }
    }
}
