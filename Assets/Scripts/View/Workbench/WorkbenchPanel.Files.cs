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

            _fileMenu.Open(at);
        }

        private void FileChosen(FileAction action)
        {
            switch (action)
            {
                case FileAction.ExportProject:
                    ExportProject();
                    break;
            }
        }

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
