using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// A block made from a selection (Part 12b): MAKE BLOCK in the canvas's menu asks for the new module's name first
    /// (Ahmad's choice, 2026-10-26), offering <c>block1</c>, <c>block2</c>..., and Enter makes it -- the parts selected and
    /// the wires between them moved into the new module, a port for each net that crosses the selection's edge, and one
    /// instance standing where they were, in one undo step, the circuit doing exactly what it did (<c>BlockMaker</c>).
    /// Escape asks nothing and makes nothing. A refusal -- a port selected, a net driven both inside and out, a name
    /// taken -- is said in the bar's line, in the document's words.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        private WaveRenameField _blockName;

        /// <summary>The field the new block's name is asked in.</summary>
        public WaveRenameField BlockNameField => _blockName;

        /// <summary>MAKE BLOCK: the new block's name asked for at <paramref name="screenPoint"/>, or beside the pointer, its free name offered.</summary>
        public void AskBlockName(Vector2? screenPoint = null)
        {
            if (_editor == null || _editor.Selection.Parts.Count == 0)
                return;
            if (_session?.WhyNotChange is string why)
            {
                Notice(why);   // the drawing's or the lock's reason before the name, never after it (the review of the lock)
                return;
            }
            Vector2 at = screenPoint ?? (Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            _blockName.Open(_editor.FreeBlockName, at);
        }

        /// <summary>The name given: the selection made into a block of that name, or refused in the bar's line.</summary>
        private void MakeBlockNamed(string name)
        {
            if (_editor == null)
                return;
            CanvasEdit(_editor.MakeBlock(string.IsNullOrWhiteSpace(name) ? _editor.FreeBlockName : name.Trim()));
        }

        // The document and its modules PARTS was last made for.
        private Board.Project _partsProject;
        private string _partsModules;

        /// <summary>The gate library PARTS was made under (Part 14d): a library changed changes what it offers.</summary>
        private Board.GateLibrary _partsLibrary;

        /// <summary>
        /// PARTS made again when the document's modules have changed -- a block made, or an undo taking one away -- so its
        /// BLOCKS are the modules there are. Asked every frame; only a new document is looked at, and PARTS made for these
        /// modules already is not made twice (it would be laid out afresh in the frame something reads where it is).
        /// </summary>
        private void RefreshPartsPanel()
        {
            if (_session == null || (ReferenceEquals(_partsProject, _session.Project) && _session.ShowsDrawingOfCode == _partsDrawing))
                return;
            _partsProject = _session.Project;
            if (ModulesOf(_partsProject) != _partsModules || !ReferenceEquals(_partsProject.Settings?.Library, _partsLibrary)
                || _session.ShowsCode != _partsShowCode || _session.ShowsDrawingOfCode != _partsDrawing)
            {
                BuildPartsPanel(SchematicPalette.Current);
                return;
            }

            // How many are left of each, where the library counts them (Part 15d): every placement or deletion changes it.
            foreach ((CatalogEntry entry, UnityEngine.UIElements.Button button) in _partButtons)
                SetText(button, PartsCatalog.Caption(_partsProject, entry));
        }

        /// <summary>PARTS made for the document as it is now (<see cref="BuildPartsPanel"/>).</summary>
        private void NotePartsBuilt()
        {
            _partsProject = _session?.Project;
            _partsModules = ModulesOf(_partsProject);
            _partsLibrary = _partsProject?.Settings?.Library;
            _partsShowCode = _session != null && _session.ShowsCode;
            _partsDrawing = _session != null && _session.ShowsDrawingOfCode;
        }

        /// <summary>Whether PARTS was made for the drawing of written code (Part 15h-4), which says so.</summary>
        private bool _partsDrawing;

        /// <summary>Whether PARTS was made for written code, which has nothing to place: an exercise's top written or drawn again turns it (Part 15e).</summary>
        private bool _partsShowCode;

        private static string ModulesOf(Board.Project project)
        {
            if (project == null)
                return null;
            var names = new System.Text.StringBuilder();
            foreach (Board.Module module in project.Modules)
                names.Append(module.Name).Append(',');
            return names.ToString();
        }
    }
}
