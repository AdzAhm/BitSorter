using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// A written block's code in the circuit's window (Part 12d): stepping inside a block written as SystemVerilog shows
    /// its text where the circuit is drawn, the dock with the waves still under it -- edit, run, and watch the waves in
    /// one view, the course's loop.
    /// </summary>
    /// <remarks>
    /// - **What it edits is a <see cref="CodeDocument"/>**: typing applies itself once it pauses, and Ctrl+Enter at once;
    ///   a fault is marked beside its line number and listed under the text, each a click to its place; a header that
    ///   would leave a wire without its pin is said there too, in the document's words.
    /// - **The field holds Escape while it has the keyboard** (<see cref="IHoldsEscape"/>): the first Escape lets it go,
    ///   and only a second closes the workbench, as RUN TO's does.
    /// - **Its letters are a monospaced face the computer already has** (<see cref="CodeFont"/>): code's columns line up
    ///   only in one, and none ships with the game. In a browser, CodeMirror takes this window's place (Part 12f).
    /// </remarks>
    public sealed class CodeEditorView : MonoBehaviour, IHoldsEscape, IScrollHandler
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench code", FieldName = "Workbench code field", RevertName = "Workbench code revert",
            ProblemName = "Workbench code problem ";

        /// <summary>Room left along the top for the breadcrumb, which the shell draws over the window's top-left corner.</summary>
        private const float TopStrip = 46f;

        private const float GutterWidth = 64f, Pad = 10f, ProblemHeight = 26f, RevertWidth = 110f, ButtonHeight = 32f;

        /// <summary>How many faults are listed under the text; the rest are counted, so the list never takes the code's room.</summary>
        public const int ListedProblems = 3;

        private RectTransform _root, _body, _gutterView, _gutterText, _problems;
        private TMP_InputField _field;
        private TextMeshProUGUI _gutter, _status;
        private Button _revert;
        private readonly List<TextMeshProUGUI> _problemLabels = new List<TextMeshProUGUI>();
        private readonly List<CodeMark> _listed = new List<CodeMark>();
        private SchematicPalette _palette;

        private CodeDocument _code;
        private int _shownVersion = -1, _shownLines = -1, _letGoFrame = -1;
        private bool _applyAsked;

        /// <summary>The code shown, or null.</summary>
        public CodeDocument Code => _code;

        /// <summary>The field the code is typed in.</summary>
        public TMP_InputField Field => _field;

        /// <summary>What the line over the code says: applied, being typed, or what keeps it from applying.</summary>
        public string Status => _status != null ? _status.text : null;

        /// <summary>The faults listed under the code, as they read.</summary>
        public IEnumerable<string> ListedText
        {
            get
            {
                foreach (TextMeshProUGUI label in _problemLabels)
                    if (label.gameObject.activeSelf)
                        yield return label.text;
            }
        }

        /// <inheritdoc/>
        public bool HoldsEscapeNow => (_field != null && _field.isFocused) || _letGoFrame == Time.frameCount;

        /// <summary>The wheel over the line numbers or the margins scrolls the code, as it does over the code itself.</summary>
        public void OnScroll(PointerEventData eventData) => _field?.OnScroll(eventData);

        /// <summary>The editor built under <paramref name="parent"/>, hidden until it is given code.</summary>
        public static CodeEditorView Build(RectTransform parent, SchematicPalette palette)
        {
            Image ground = UiTheme.Panel_(RootName, parent, palette.Ground);
            ground.sprite = null;
            var view = ground.gameObject.AddComponent<CodeEditorView>();
            view._root = ground.rectTransform;
            view._palette = palette;
            view.BuildInside(palette);
            Show(ground, false);
            return view;
        }

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void BuildInside(SchematicPalette palette)
        {
            // The line over the code, right of the breadcrumb: what state the text is in, and REVERT while it is not the document's.
            _status = UiTheme.Label("status", _root, UiType.Caption, palette.TextDim, TextAlignmentOptions.MidlineRight);
            _status.raycastTarget = false;
            _revert = WorkbenchPanel.CanvasButton(RevertName, _root, "REVERT", out TextMeshProUGUI _, UiType.Body, palette);
            _revert.onClick.AddListener(() =>
            {
                _code?.Revert();
                UiTheme.Defocus();
            });

            _body = UiTheme.Rect("body", _root);

            // The line numbers, in a window of their own that follows the text up and down as it scrolls.
            _gutterView = UiTheme.Rect("gutter", _body);
            _gutterView.gameObject.AddComponent<RectMask2D>();
            _gutter = UiTheme.Label("numbers", _gutterView, UiType.Body, palette.Muted, TextAlignmentOptions.TopRight);
            _gutter.raycastTarget = false;
            _gutter.richText = true;
            _gutter.textWrappingMode = TextWrappingModes.NoWrap;
            _gutter.overflowMode = TextOverflowModes.Overflow;
            _gutterText = _gutter.rectTransform;

            Image box = UiTheme.Panel_(FieldName, _body, palette.Ground);
            box.sprite = null;
            RectTransform area = UiTheme.Rect("text area", box.rectTransform);
            UiTheme.Stretch(area, 0f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = UiTheme.Label("text", area, UiType.Body, palette.Text, TextAlignmentOptions.TopLeft);
            UiTheme.Stretch(text.rectTransform, 0f);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            if (CodeFont.Asset != null)
            {
                text.font = CodeFont.Asset;
                _gutter.font = CodeFont.Asset;
            }

            _field = box.gameObject.AddComponent<TMP_InputField>();
            _field.textViewport = area;
            _field.textComponent = text;
            _field.lineType = TMP_InputField.LineType.MultiLineNewline;
            _field.richText = false;
            _field.onFocusSelectAll = false;
            _field.resetOnDeActivation = false;
            _field.restoreOriginalTextOnEscape = false;
            _field.scrollSensitivity = 3f;
            _field.caretColor = palette.Text;
            _field.customCaretColor = true;
            _field.caretWidth = 2;
            _field.selectionColor = palette.Over(palette.Selection, 0.45f);
            _field.onValueChanged.AddListener(typed => _code?.Type(typed, Now));
            _field.onValidateInput = Validate;
            _field.onEndEdit.AddListener(_ =>
            {
                // Escape lets the field go, and holds this frame's Escape so the workbench stays open; TMP leaves a
                // finished field selected, which would keep every workbench key dead until a click elsewhere. Only its
                // own selection, though: a click into the CODE tab's listing (Part 12e) ends this edit after the listing
                // has taken the keyboard, and letting go of everything then would leave the keyboard with neither.
                if (_field.wasCanceled)
                    _letGoFrame = Time.frameCount;
                UiTheme.Defocus(_field);
            });

            _problems = UiTheme.Rect("problems", _root);
            for (int i = 0; i < ListedProblems + 1; i++)
            {
                TextMeshProUGUI label = UiTheme.Label(ProblemName + i.ToString(CultureInfo.InvariantCulture), _problems, UiType.Caption,
                    palette.Text, TextAlignmentOptions.MidlineLeft);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.raycastTarget = true;
                int index = i;
                Button go = label.gameObject.AddComponent<Button>();
                go.transition = Selectable.Transition.None;
                go.onClick.AddListener(() => GoTo(index));
                _problemLabels.Add(label);
            }
        }

        /// <summary>The game's clock, in milliseconds: a test drives it, so a pause in typing is one however loaded the machine.</summary>
        private static double Now => Time.timeAsDouble * 1000.0;

        /// <summary>Ctrl+Enter applies the code rather than typing a new line; anything else is typed as it comes.</summary>
        private char Validate(string text, int index, char added)
        {
            UnityEngine.InputSystem.Keyboard keys = UiText.FieldKeys;
            if (added == '\n' && keys != null && keys.ctrlKey.isPressed
                && (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame))
            {
                _applyAsked = true;
                return '\0';
            }

            return added;
        }

        /// <summary>Shows <paramref name="code"/>, or hides the editor for null.</summary>
        public void Bind(CodeDocument code)
        {
            if (ReferenceEquals(code, _code))
                return;
            _code = code;
            _shownVersion = _shownLines = -1;
            if (code == null && _field != null && _field.isFocused)
                UiTheme.Defocus();
            Show(_root, code != null);
            if (code != null)
                _field.SetTextWithoutNotify(code.Text);
        }

        /// <summary>The editor over the circuit's window, <paramref name="size"/> at <paramref name="corner"/>.</summary>
        public void Place(Vector2 corner, Vector2 size)
        {
            WorkbenchPanel.TopLeft(_root, corner, size);
            Lay(size);
        }

        /// <summary>
        /// One frame: Ctrl+Enter's apply, a pause in typing applied, the text following an undo, and the marks, the list and
        /// the status drawn again when they changed.
        /// </summary>
        public void Tick()
        {
            if (_code == null)
                return;

            if (_applyAsked)
            {
                _applyAsked = false;
                _code.Apply();
            }
            else
            {
                _code.Frame(Now);
            }

            if (_field.text != _code.Text)
            {
                // An undo, a redo or REVERT changed the text under the field.
                int caret = Mathf.Min(_field.stringPosition, _code.Text.Length);
                _field.SetTextWithoutNotify(_code.Text);
                _field.stringPosition = caret;
            }

            int lines = LineCount(_code.Text);
            if (_code.Version != _shownVersion || lines != _shownLines)
            {
                _shownVersion = _code.Version;
                _shownLines = lines;
                DrawMarks(lines);
            }

            // The numbers follow the text as the field scrolls it.
            RectTransform text = _field.textComponent.rectTransform;
            _gutterText.anchoredPosition = new Vector2(_gutterText.anchoredPosition.x, text.anchoredPosition.y);
        }

        /// <summary>Puts the keyboard in the code, its caret at the start of line <paramref name="line"/>, column <paramref name="column"/>.</summary>
        public void GoTo(int line, int column)
        {
            if (_code == null)
                return;
            UiTheme.Focus(_field);
            int at = _code.OffsetOf(line, column);
            _field.stringPosition = at;
            _field.selectionStringAnchorPosition = at;
            _field.selectionStringFocusPosition = at;
        }

        private void GoTo(int listed)
        {
            if (listed < _listed.Count)
                GoTo(_listed[listed].Line, _listed[listed].Column);
        }

        private void Lay(Vector2 size)
        {
            if (_root == null)
                return;

            float problems = ProblemRows() * ProblemHeight + (ProblemRows() > 0 ? Pad : 0f);
            WorkbenchPanel.TopLeft(_status.rectTransform, new Vector2(Pad, 8f), new Vector2(Mathf.Max(0f, size.x - 2f * Pad - RevertWidth - Pad), ButtonHeight));
            WorkbenchPanel.TopLeft((RectTransform)_revert.transform, new Vector2(size.x - Pad - RevertWidth, 8f), new Vector2(RevertWidth, ButtonHeight));
            WorkbenchPanel.TopLeft(_body, new Vector2(0f, TopStrip), new Vector2(size.x, Mathf.Max(0f, size.y - TopStrip - problems)));
            WorkbenchPanel.TopLeft(_gutterView, new Vector2(0f, 0f), new Vector2(GutterWidth, _body.sizeDelta.y));
            WorkbenchPanel.TopLeft(_gutterText, new Vector2(0f, 0f), new Vector2(GutterWidth - Pad, 100000f));
            WorkbenchPanel.TopLeft((RectTransform)_field.transform, new Vector2(GutterWidth + Pad, 0f),
                new Vector2(Mathf.Max(0f, size.x - GutterWidth - 2f * Pad), _body.sizeDelta.y));
            WorkbenchPanel.TopLeft(_problems, new Vector2(GutterWidth + Pad, size.y - problems), new Vector2(Mathf.Max(0f, size.x - GutterWidth - 2f * Pad), problems));
            for (int i = 0; i < _problemLabels.Count; i++)
                WorkbenchPanel.TopLeft(_problemLabels[i].rectTransform, new Vector2(0f, i * ProblemHeight), new Vector2(_problems.sizeDelta.x, ProblemHeight));
        }

        private int ProblemRows()
        {
            if (_code == null)
                return 0;
            int faults = _code.Marks.Count + _code.Elsewhere.Count;
            return faults > ListedProblems ? ListedProblems + 1 : faults;
        }

        /// <summary>The line numbers, each fault's in its colour with a dot; the faults listed; and the status line.</summary>
        private void DrawMarks(int lines)
        {
            var errors = new HashSet<int>();
            var warnings = new HashSet<int>();
            foreach (CodeMark mark in _code.Marks)
                (mark.IsError ? errors : warnings).Add(mark.Line);

            string error = ColorUtility.ToHtmlStringRGB(_palette.Unknown), warning = ColorUtility.ToHtmlStringRGB(_palette.Cursor);
            var numbers = new StringBuilder();
            for (int line = 1; line <= lines; line++)
            {
                if (line > 1)
                    numbers.Append('\n');
                string number = line.ToString(CultureInfo.InvariantCulture);
                // Marked by a letter as well as a colour, and a plain one: a glyph the face lacks would come from another
                // face, whose height could push its line out of step with the code's.
                if (errors.Contains(line))
                    numbers.Append("<color=#").Append(error).Append(">x ").Append(number).Append("</color>");
                else if (warnings.Contains(line))
                    numbers.Append("<color=#").Append(warning).Append(">! ").Append(number).Append("</color>");
                else
                    numbers.Append(number);
            }

            WorkbenchPanel.SetText(_gutter, numbers.ToString());

            _listed.Clear();
            int row = 0;
            foreach (CodeMark mark in _code.Marks)
            {
                if (row >= ListedProblems)
                    break;
                _listed.Add(mark);
                ShowProblem(row++, mark.ToString(), mark.IsError ? _palette.Unknown : _palette.Cursor);
            }

            foreach (string elsewhere in _code.Elsewhere)
            {
                if (row >= ListedProblems)
                    break;
                ShowProblem(row++, elsewhere, _palette.Unknown);
            }

            int more = _code.Marks.Count + _code.Elsewhere.Count - row;
            if (more > 0)
                ShowProblem(row++, "and " + more.ToString(CultureInfo.InvariantCulture) + " more", _palette.TextDim);
            for (; row < _problemLabels.Count; row++)
                _problemLabels[row].gameObject.SetActive(false);

            WorkbenchPanel.SetText(_status, StatusText());
            Show(_revert, _code.Pending);
            Lay(_root.sizeDelta);
        }

        private void ShowProblem(int row, string text, Color colour)
        {
            TextMeshProUGUI label = _problemLabels[row];
            label.gameObject.SetActive(true);
            label.text = text;
            label.color = colour;
        }

        private string StatusText()
        {
            if (_code.HasErrors)
                return "NOT APPLIED: the waves are the last code that worked.";
            if (_code.Pending)
                return "TYPING: applied when you pause, or now with Ctrl+Enter.";
            return "APPLIED: the waves are this code's. It applies itself as you type.";
        }

        private static int LineCount(string text)
        {
            int lines = 1;
            foreach (char c in text)
                if (c == '\n')
                    lines++;
            return lines;
        }

        private static void Show(Component part, bool shown) => UiTheme.SetShown(part, shown);
    }

    /// <summary>
    /// The monospaced face code is set in (Part 12d): one the computer already has, found by name, since none ships with
    /// the game -- Consolas on Windows, Menlo on a Mac, DejaVu Sans Mono or Liberation Mono on Linux. Null where there is
    /// none, as in a browser, and the code is set in the game's own face.
    /// </summary>
    public static class CodeFont
    {
        private static readonly string[] Families = { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" };
        private static TMP_FontAsset _asset;
        private static bool _looked, _found;

        public static TMP_FontAsset Asset
        {
            get
            {
                // A dead one is looked for again: an unload of unused objects takes a font made at runtime with it.
                if (_looked && (!_found || _asset))
                    return _found ? _asset : null;
                _looked = true;
                _found = false;
                _asset = null;
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                    return null;

                foreach (string family in Families)
                {
                    // Null, and a line in the log, for a face this computer does not have: the next is tried.
                    _asset = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (_asset == null)
                        continue;
                    _asset.hideFlags = HideFlags.HideAndDontSave;
                    if (_asset.material != null)
                        _asset.material.hideFlags = HideFlags.HideAndDontSave;
                    if (_asset.atlasTextures != null)
                        foreach (Texture2D atlas in _asset.atlasTextures)
                            if (atlas != null)
                                atlas.hideFlags = HideFlags.HideAndDontSave;
                    _asset.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
                    _found = true;
                    break;
                }

                return _asset;
            }
        }
    }
}
