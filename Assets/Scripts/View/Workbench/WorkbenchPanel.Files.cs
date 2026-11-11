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
                _sample == 0 && Documents.IsOwn(_documentKey), circuitShown: !PageShowing, imported: CameIn(), held: Held() != SaveHold.None);
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
                case FileAction.ReloadSave:
                    ReloadTheSave();
                    break;
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
                case FileAction.BackToImported:
                    AskBackToImported();
                    break;
                case FileAction.ExportProject:
                    ExportProject();
                    break;
                case FileAction.ExportSubmission:
                    ExportSubmission();
                    break;
                case FileAction.ImportFiles:
                case FileAction.ImportPickedFolder:
                    ChooseAndImport(action == FileAction.ImportPickedFolder);
                    break;
                case FileAction.ConvertTestbench:
                    AskConvert();
                    break;
                case FileAction.CompareModelSim:
                    CompareWithModelSim();
                    break;
            }
        }

        // -----------------------------------------------------------------
        // Compare with ModelSim (Part 15i-5)
        // -----------------------------------------------------------------

        /// <summary>Whether the files the page is reading are a dump to compare with, not a project to import.</summary>
        private bool _comparing;

        /// <summary>
        /// FILE, COMPARE WITH MODELSIM'S RUN: the dump an exported run.do has ModelSim write (<see cref="ProjectExport.ModelSimDump"/>)
        /// -- the newest .vcd in the Import folder on a desktop, one the player picks in a browser -- held to this run.
        /// </summary>
        public void CompareWithModelSim()
        {
            if (_session == null)
                return;
            if (FileImport.Picks)
            {
                _comparing = true;
                FileImport.PickDump();
                return;
            }

            string path;
            long size;
            string text;
            try
            {
                path = FileImport.NewestDump();
                if (path == null)
                {
                    // Opened, as OPEN THE IMPORT FOLDER did before 5.1, when IMPORT read only that folder.
                    FileImport.OpenFolder();
                    Notice("put the " + ProjectExport.ModelSimDump + " ModelSim wrote in " + FileImport.Folder + ", opened for you, then FILE, COMPARE WITH MODELSIM'S RUN");
                    return;
                }
                size = new System.IO.FileInfo(path).Length;
                if (size > FileImport.MaxDumpSize)
                {
                    Notice(DumpPick.TooLarge(System.IO.Path.GetFileName(path), size));
                    return;
                }
                text = System.IO.File.ReadAllText(path);
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                Notice("the dump could not be read: " + e.Message);
                return;
            }
            Compare(text, System.IO.Path.GetFileName(path));
        }

        /// <summary>
        /// <paramref name="dump"/>, a VCD, held to this run (<see cref="RunComparison"/>): the verdict on the chip, the first
        /// difference at the TRANSCRIPT's head at its moment, and the run taken there -- or the agreement said.
        /// </summary>
        public void Compare(string dump, string name = null)
        {
            if (_session == null || dump == null)
                return;

            // Whatever a file made to break the reader throws is the file's, never the workbench's (the 15i-5 review).
            Dictionary<string, BitSorter.Sim.VcdTrace> theirs;
            try
            {
                theirs = BitSorter.Sim.VcdReader.Read(new System.IO.StringReader(dump));
            }
            catch (System.Exception e) when (!(e is System.OutOfMemoryException))
            {
                Notice("that is not a VCD ModelSim wrote: " + e.Message);
                return;
            }

            _session.Finish();   // the whole run, to be held to the whole dump
            WorkbenchModel model = _session.Run.Model;
            RunComparison comparison = RunComparison.Of(model.Session.Design, model.Session.History, model.End, theirs);
            LastComparison = comparison;
            if (comparison.Compared == 0)
            {
                Notice("ModelSim's dump names none of this run's signals: export this circuit, run its run.do, and compare its " +
                    ProjectExport.ModelSimDump);
                return;
            }

            string unit = _session.Unit;
            _transcript.Notes = comparison.Lines(unit);
            _notesAbout = _session.Project;
            _transcriptDrawn = -1;
            Notice(comparison.Verdict(unit) + (name != null ? " (" + name + ")" : ""));
            OpenDock();   // the comparison is said in the TRANSCRIPT (Part 16d)
            ShowDockTab(DockTab.Transcript);
            if (!comparison.Agree)
            {
                Pause();
                Seek(comparison.Time.Value);
            }
        }

        /// <summary>The last comparison made, for the tests.</summary>
        public RunComparison LastComparison { get; private set; }

        /// <summary>Files the page has read: a dump to compare with when COMPARE asked for them, else a project to import.</summary>
        private void Picked(List<SourceFile> files)
        {
            if (!_comparing)
            {
                Import(files);
                return;
            }

            _comparing = false;
            if (DumpPick.TryChoose(files, out SourceFile dump, out string why))
                Compare(dump.Text, dump.Path);
            else
                Notice(why);
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

        /// <summary>
        /// IMPORT A PROJECT'S FOLDER, or its FILES: in a browser the page's own picker, whose files come a frame or a few later
        /// (<see cref="Picked"/>); on a desktop, since 5.1, Windows' Open dialog (<see cref="FileImport.Choose"/>), the folder
        /// or the files chosen imported as it closes, and Cancel changing nothing.
        /// </summary>
        private void ChooseAndImport(bool folder)
        {
            _comparing = false;   // a COMPARE whose picker was cancelled never said so (the 15i-5 review)
            if (FileImport.Picks)
            {
                FileImport.Pick(folder);
                return;
            }

            string[] chosen = FileImport.Choose(folder);
            if (chosen == null || chosen.Length == 0)
                return;   // cancelled
            if (folder)
            {
                ImportFrom(chosen[0]);
                return;
            }

            List<SourceFile> files = FileImport.ReadFiles(chosen);
            if (files.Count == 0)
            {
                Notice("nothing to import: a hidden file, whose name starts with a dot, is never a project's");
                return;
            }
            Import(files);
        }

        /// <summary>
        /// A project from every file in <paramref name="folder"/> and the folders in it (<see cref="FileImport.ReadFolder"/>),
        /// as a circuit of its own -- refused, saying why, for a folder that cannot be read, holds nothing, or holds more files
        /// than an import takes, which is a folder chosen a level too high.
        /// </summary>
        public void ImportFrom(string folder)
        {
            string name = System.IO.Path.GetFileName((folder ?? "").TrimEnd('/', '\\'));
            if (name.Length == 0)
                name = folder;
            List<SourceFile> files;
            try
            {
                files = FileImport.ReadFolder(folder);
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                Notice(name + " could not be read: " + e.Message);
                return;
            }

            if (files.Count == 0)
                Notice(name + " is empty: choose the folder that holds a project's .sv files");
            else if (files.Count > ProjectImporter.MaxFiles)
                Notice(name + " holds more than " + ProjectImporter.MaxFiles.ToString(CultureInfo.InvariantCulture) +
                    " files, more than an import takes: choose the folder that holds just the project");
            else
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
            LeaveTheTypedCode();   // the code on screen kept with the circuit before the import opens (Part 16d's sweep)

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
            {
                store.SetProject(key, import.Project);
                store.SetImported(key, import.Project);   // what FILE, BACK TO AS IMPORTED puts back (Part 15h-4d)
                // Locked, so a look round it cannot change it by accident (Part 15h-4d, Ahmad, 2026-10-29) -- but for a course
                // template, which is imported to be written into.
                store.SetLocked(key, FromATemplate(import.Project) == 0);
            }
            OpenDocument(key);
            foreach (Module module in import.Project.Modules)
                if (module.Template != null)
                    _session?.ShowCodeOf(module.Name);   // a course template is written into: its code first (the review of #111)

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
                Undriven(project) +
                (import.Notes.Count > 0 ? ". The TRANSCRIPT has what the import said" : ""));
        }

        /// <summary>
        /// What the notice adds for inputs nothing drives (Part 15g, <see cref="InputTable.Undriven"/>): which, that they read z
        /// and what they feed x, and the ways to drive them. A design imported without its testbench showed grey dashes and
        /// red hatching, and nothing said why (Ahmad's import, 2026-10-28).
        /// </summary>
        private static string Undriven(Project project)
        {
            IReadOnlyList<string> undriven = InputTable.Undriven(project);
            if (undriven.Count == 0)
                return "";
            string names = undriven.Count <= 4
                ? string.Join(", ", undriven)
                : string.Join(", ", new List<string>(undriven).GetRange(0, 3)) + " and " + (undriven.Count - 3).ToString(CultureInfo.InvariantCulture) + " more";
            return ". Nothing drives " + names + " yet, so " + (undriven.Count == 1 ? "it reads" : "they read") +
                " z and what they feed x: give them values in INPUTS, or import the testbench too";
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

        /// <summary>Asks before CONVERT, under the FILE chip, saying what the stimulus will not keep; says why not when it cannot be.</summary>
        public void AskConvert() => AskConvert(UnderTheFileChip());

        /// <summary>Asks before CONVERT, the question going up at <paramref name="at"/>.</summary>
        private void AskConvert(Vector2 at)
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
            _ask.Ask(at,
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
        /// Code typed into a written module and not yet applied -- on screen, kept while its drawing is up (the review of #111),
        /// or kept since it was stepped out of (the 16d sweep) -- applied now as its editor would after the pause, so that what
        /// an export holds, or the lock keeps, is what the student typed (<see cref="WorkbenchSession.WhyTypingStops"/>; CHECK
        /// asks it through <see cref="ExerciseRun.Check"/>). The editor keeps typed text as its own until the pause or
        /// Ctrl+Enter, and text with an error as its own for good, while the document holds the last text that applied:
        /// exporting the document alone hands over older code without a word. Code that cannot apply is refused on the chip with
        /// what is wrong with it, <paramref name="outcome"/> saying what then does not happen ("exported", "handed in",
        /// "locked"), and false is returned: the export stops. True when nothing typed is left.
        /// </summary>
        private bool ApplyTypedCode(string outcome)
        {
            string refused = _session.WhyTypingStops(outcome);
            if (refused == null)
                return true;
            Notice(refused);
            return false;
        }

        /// <summary>
        /// The code typed into the circuit open, as it is left for another circuit, exercise or sample (Part 16d's sweep): what
        /// applies is applied, as its editor would after the pause, so the circuit is kept with it; what cannot apply stays the
        /// draft of its module it is (<see cref="WorkbenchSession.Drafts"/>), kept with the circuit and shown back, marked not
        /// applied, as it opens again -- Ahmad's decision (2026-11-03): code typed with an error is kept as a draft, never
        /// lost, and nothing is asked. A sample keeps nothing of its own. Leaving replaced the session with the typed text in
        /// it, without a word.
        /// </summary>
        private void LeaveTheTypedCode()
        {
            // An exercise RESET PROGRESS let go while the workbench was out of sight is not the circuit under the key the
            // stage keeps under, and is left as it is (the review of Part 16d's sweep).
            if (_session == null || _sample != 0 || _forgotten)
                return;
            _session.WhyTypingStops("kept");   // what cannot apply is the draft kept: nothing to say on the chip of the next
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
            return SaveExport(name, bytes, "application/zip", ": unzip it, and in ModelSim type do run.do");
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
            return SaveExport(name, submission.Zip, "application/zip", ": " + string.Join(", ", submission.Files) +
                " as the course gave " + (submission.Files.Count == 1 ? "it" : "them") + ", with your code between the markers" + uses);
        }
    }
}
