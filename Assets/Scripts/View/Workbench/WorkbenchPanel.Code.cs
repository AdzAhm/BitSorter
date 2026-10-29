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

        /// <summary>The switch between a written module's code and the drawing of it (Part 15h-4), in the circuit window's corner.</summary>
        public const string DrawingSwitchName = "Workbench drawing switch";

        public const string DrawingCaption = "DRAWING", CodeCaption = "CODE";

        /// <summary>The switch between a drawing of code's boxes and its gates (Part 15h-4), left of the drawing switch over a drawing.</summary>
        public const string GatesSwitchName = "Workbench gates switch";

        public const string GatesCaption = "GATES", BoxesCaption = "BOXES";

        /// <summary>What GATES says when it opens nothing: every operator past four bits.</summary>
        public const string NothingOpens = "Nothing here opens into gates: past 4 bits an operator stays a box.";

        private const float GatesWidth = 104f;

        private CodeEditorView _codeView;
        private WaveRenameField _codeName;

        /// <summary>The code editor in the circuit's window, shown while the module shown is written.</summary>
        public CodeEditorView CodeView => _codeView;

        /// <summary>The field a new code block's name is asked in.</summary>
        public WaveRenameField CodeNameField => _codeName;

        /// <summary>The editor over the circuit's window, hidden until a written block is stepped into.</summary>
        private UnityEngine.UI.Button _drawingSwitch;
        private TMPro.TextMeshProUGUI _drawingCaption;

        /// <summary>DRAWING over a written module's code, CODE over its drawing: which the circuit's window shows (Part 15h-4).</summary>
        public UnityEngine.UI.Button DrawingSwitch => _drawingSwitch;

        private UnityEngine.UI.Button _gatesSwitch;
        private TMPro.TextMeshProUGUI _gatesCaption;

        /// <summary>GATES over a drawing of code's boxes, BOXES over its gates: whether its small operators and muxes are drawn as their gates.</summary>
        public UnityEngine.UI.Button GatesSwitch => _gatesSwitch;

        private void BuildCodeView(SchematicPalette palette)
        {
            _codeView = CodeEditorView.Build(Root, palette);

            // Over the code and over the drawing alike, at the window's top right: in a browser the code is a box the page lays
            // over everything under the code's top strip, and a switch in the lower corner was under it (the 15h-4 browser check).
            _drawingSwitch = CanvasButton(DrawingSwitchName, Root, DrawingCaption, out _drawingCaption, UiType.Body, palette);
            _drawingSwitch.onClick.AddListener(() =>
            {
                if (_session != null)
                    _session.DrawCode(!_session.ShowsDrawingOfCode);
                UiTheme.Defocus();
            });
            Show(_drawingSwitch, false);
            _gatesSwitch = CanvasButton(GatesSwitchName, Root, GatesCaption, out _gatesCaption, UiType.Body, palette);
            _gatesSwitch.onClick.AddListener(() =>
            {
                if (_session != null)
                {
                    _session.DrawGates(!_session.ShowsGatesOfCode);
                    if (_session.ShowsGatesOfCode && _session.GatesOpened == 0)
                        Notice(NothingOpens);
                }
                UiTheme.Defocus();
            });
            Show(_gatesSwitch, false);
            PlaceCodeView();
            _codeName = WaveRenameField.Build(_shell, transform, palette, NewCodeBlockNamed, WaveRenameField.CodeNameName,
                "NAME THE NEW CODE BLOCK", "a module's name, add4");
        }

        /// <summary>
        /// The editor over the circuit's window, and above the strip along its foot while a hint or the tutorial is up
        /// (Part 15g): the browser's editor is a box the page lays over the canvas, and it covered the strip's words, leaving
        /// GOT IT showing beside them (Ahmad, 2026-10-28).
        /// </summary>
        private void PlaceCodeView()
        {
            if (_codeView == null)
                return;
            _codeInset = LessonInset();
            Vector2 size = SizeOf(_regions.Circuit);
            size.y = Mathf.Max(0f, size.y - _codeInset);
            _codeView.Place(CornerOf(_regions.Circuit), size);
            if (_drawingSwitch != null)
            {
                var switchSize = new Vector2(124f, 32f);
                TopLeft(_drawingSwitch.GetComponent<RectTransform>(), CornerOf(_regions.Circuit) + new Vector2(size.x - 10f - switchSize.x, 8f), switchSize);
                if (_gatesSwitch != null)
                    TopLeft(_gatesSwitch.GetComponent<RectTransform>(),
                        CornerOf(_regions.Circuit) + new Vector2(size.x - 10f - switchSize.x - 8f - GatesWidth, 8f), new Vector2(GatesWidth, 32f));
            }
        }

        private bool _drawingSwitchShown, _gatesSwitchShown;

        /// <summary>The switch shown over a written module, saying what it turns to, and kept over the code's editor.</summary>
        private void TickDrawingSwitch()
        {
            if (_drawingSwitch == null)
                return;
            bool shown = _session != null && _session.ShowsWritten && !PageShowing;
            if (shown != _drawingSwitchShown)
            {
                Show(_drawingSwitch, shown);
                if (shown)
                    _drawingSwitch.transform.SetAsLastSibling();
                _drawingSwitchShown = shown;
            }

            // GATES beside it over a drawing of code, never over the code itself.
            bool gates = shown && _session.ShowsDrawingOfCode;
            if (gates != _gatesSwitchShown && _gatesSwitch != null)
            {
                Show(_gatesSwitch, gates);
                if (gates)
                    _gatesSwitch.transform.SetAsLastSibling();
                _gatesSwitchShown = gates;
            }

            // FIT steps left of the switches while they are up.
            if (_fit != null)
            {
                float right = shown ? -(8f + CodeEditorView.SwitchRoom + (gates ? GatesWidth + 8f : 0f)) : -8f;
                var fit = _fit.GetComponent<RectTransform>();
                if (!Mathf.Approximately(fit.anchoredPosition.x, right))
                    TopRight(fit, new Vector2(right, 8f), new Vector2(72f, 32f));
            }
            if (!shown)
                return;
            string caption = _session.ShowsDrawingOfCode ? CodeCaption : DrawingCaption;
            if (_drawingCaption.text != caption)
                _drawingCaption.text = caption;
            string gatesCaption = _session.ShowsGatesOfCode ? BoxesCaption : GatesCaption;
            if (gates && _gatesCaption.text != gatesCaption)
                _gatesCaption.text = gatesCaption;
        }

        /// <summary>How much of the circuit's window's foot the strip takes, with its margin; 0 while it is down.</summary>
        private float LessonInset()
        {
            if (LessonText == null)
                return 0f;
            float height = _lesson.resolvedStyle.height;
            return float.IsNaN(height) ? 0f : height + 2f * 12f;
        }

        private float _codeInset;

        /// <summary>The editor given the code shown, if any, and its frame: a pause in typing applied, an undo followed.</summary>
        private void TickCode()
        {
            if (_codeView == null)
                return;
            if (!Mathf.Approximately(LessonInset(), _codeInset))
                PlaceCodeView();
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
