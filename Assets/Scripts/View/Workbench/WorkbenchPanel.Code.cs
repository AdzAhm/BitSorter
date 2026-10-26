using BitSorter.Board;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BitSorter.View
{
    /// <summary>
    /// Written blocks (Part 12d): a block whose inside is SystemVerilog. Stepping into one shows its code in the circuit's
    /// window (<see cref="CodeEditorView"/>), the dock with the waves still under it; + CODE in PARTS' MORE
    /// asks for a name, makes the module from a template header, and puts a block of it in hand to place.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>+ CODE in PARTS, and the note PARTS shows inside written code.</summary>
        public const string NewCodeBlockName = "Workbench new code block", CodeNoteName = "Workbench code note";

        private CodeEditorView _codeView;
        private WaveRenameField _codeName;

        /// <summary>The code editor in the circuit's window, shown while the module shown is written.</summary>
        public CodeEditorView CodeView => _codeView;

        /// <summary>The field a new code block's name is asked in.</summary>
        public WaveRenameField CodeNameField => _codeName;

        /// <summary>The editor over the circuit's window, hidden until a written block is stepped into.</summary>
        private void BuildCodeView(SchematicPalette palette)
        {
            _codeView = CodeEditorView.Build(Root, palette);
            PlaceCodeView();
            _codeName = WaveRenameField.Build(_shell, transform, palette, NewCodeBlockNamed, WaveRenameField.CodeNameName,
                "NAME THE NEW CODE BLOCK", "a module's name, add4");
        }

        private void PlaceCodeView() => _codeView?.Place(CornerOf(_regions.Circuit), SizeOf(_regions.Circuit));

        /// <summary>The editor given the code shown, if any, and its frame: a pause in typing applied, an undo followed.</summary>
        private void TickCode()
        {
            if (_codeView == null)
                return;
            _codeView.Bind(_session?.Code);
            _codeView.Tick();
        }

        /// <summary>+ CODE: the new block's name asked for beside the pointer, or at <paramref name="screenPoint"/>, a free one offered.</summary>
        public void AskCodeBlockName(Vector2? screenPoint = null)
        {
            if (_session == null || _session.ShowsCode)
                return;
            Vector2 at = screenPoint ?? (Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            _codeName.Open(FreeCodeName(), at);
        }

        /// <summary><c>code1</c>, <c>code2</c>...: the first no module has.</summary>
        private string FreeCodeName()
        {
            for (int n = 1; ; n++)
            {
                string name = "code" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_session.Project.Find(name) == null)
                    return name;
            }
        }

        /// <summary>The name given: the written module made from the template, and a block of it in hand -- or refused in the bar's line.</summary>
        private void NewCodeBlockNamed(string name)
        {
            if (_session == null || _editor == null)
                return;
            string chosen = string.IsNullOrWhiteSpace(name) ? FreeCodeName() : name.Trim();
            EditResult made = _session.AddWrittenModule(chosen);
            if (!made.Done)
            {
                CanvasEdit(made);
                return;
            }

            foreach (CatalogEntry entry in PartsCatalog.For(_session.Project, _session.Module))
            {
                if (entry.Module != chosen)
                    continue;
                _editor.Take(entry);
                break;
            }
        }
    }
}
