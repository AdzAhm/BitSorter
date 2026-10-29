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
    /// - **It answers the keys a writer reaches for, as the browser's editor does** (Part 14g): Ctrl+Z and Ctrl+Y step
    ///   through the typing (<see cref="CodeDocument.UndoTyping"/>), Tab and Shift+Tab indent and outdent the lines by four
    ///   spaces and Ctrl+/ comments them (<see cref="CodeEdits"/>), and Ctrl+S applies as Ctrl+Enter does. TextMesh Pro's
    ///   field had none of them -- Tab typed a tab character, Ctrl+Z did nothing -- and it keeps what it has: Ctrl+A, C, V
    ///   and X. No key of the workbench's reaches it while it has the keyboard (<see cref="UiText"/>).
    /// </remarks>
    public sealed class CodeEditorView : MonoBehaviour, IHoldsEscape, IScrollHandler
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench code", FieldName = "Workbench code field", RevertName = "Workbench code revert",
            ProblemName = "Workbench code problem ";

        /// <summary>Room left along the top for the breadcrumb, which the shell draws over the window's top-left corner.</summary>
        private const float TopStrip = 46f;

        private const float GutterWidth = 64f, Pad = 10f, ProblemHeight = 26f, RevertWidth = 110f, ButtonHeight = 32f;

        /// <summary>
        /// The room kept at the top strip's right end for the panel's DRAWING switch (Part 15h-4): over the strip, not the code,
        /// since in a browser the code is a box the page lays over everything below it.
        /// </summary>
        public const float SwitchRoom = 124f + Pad;

        /// <summary>Room the strip keeps left of the switch for the padlock (Part 15h-4d): its width and a gap.</summary>
        public const float LockRoom = 48f;

        /// <summary>
        /// Whether the padlock is up beside the switch, so the strip keeps it room: a sample's code has none, and its strip
        /// showed a gap where nothing stood.
        /// </summary>
        public bool KeepsLockRoom
        {
            get => _keepsLockRoom;
            set
            {
                if (_keepsLockRoom == value)
                    return;
                _keepsLockRoom = value;
                if (_root != null)
                    Lay(_root.sizeDelta);
            }
        }

        private bool _keepsLockRoom;

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

        /// <summary>What Tab asked of the lines, taken from the field as it typed it: +1 indent, -1 outdent, 0 nothing.</summary>
        private int _indentAsked;

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
        public bool HoldsEscapeNow => (_field != null && _field.isFocused) || _letGoFrame == Time.frameCount
            || (_browser && CodeMirrorBridge.Focused);

        /// <summary>
        /// Read only while the document is locked (Part 15h-4d): the text selected, copied and searched, never typed into --
        /// the field's own switch here, CodeMirror's in a browser.
        /// </summary>
        public bool ReadOnly
        {
            get => _readOnly;
            set
            {
                if (_readOnly == value)
                    return;
                _readOnly = value;
                _shownVersion = -1;   // the strip says it
                if (_field != null)
                    _field.readOnly = value;
                if (_browser)
                    CodeMirrorBridge.SetReadOnly(value);
            }
        }

        private bool _readOnly;

        // In a browser (Part 12f): CodeMirror over the body, the text it was last given or read, and where it was laid.
        private bool _browser, _browserShown;
        private string _browserText;
        private Rect _browserAt;
        private float _browserFont;

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

            // In a browser, CodeMirror takes the body: TextMesh Pro's field and its numbers stand down (Part 12f).
            if (CodeMirrorBridge.Available)
            {
                view._browser = true;
                CodeMirrorBridge.Create(ColoursOf(palette));
                CodeMirrorBridge.SetReadOnly(false);   // the page's editor outlives a change of style, read only or not (the review of the lock)
                view._gutterView.gameObject.SetActive(false);
                view._field.gameObject.SetActive(false);
            }
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

        /// <summary>
        /// Ctrl+Enter applies the code rather than typing a new line; Tab and Shift+Tab indent and outdent the lines rather
        /// than typing a tab (Part 14g); a key the editor answers with Ctrl types nothing as well; anything else is typed as it
        /// comes -- a paste included, which the field types a character at a time with Ctrl held.
        /// </summary>
        private char Validate(string text, int index, char added)
        {
            UnityEngine.InputSystem.Keyboard keys = UiText.FieldKeys;
            if (keys == null)
                return added;

            if (added == '\n' && keys.ctrlKey.isPressed
                && (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame))
            {
                _applyAsked = true;
                return '\0';
            }

            // Held, not pressed this frame: the field may hand a key over the frame after it went down. A tab in a paste,
            // with no Tab held, is typed as it comes.
            if (added == '\t' && keys.tabKey.isPressed)
            {
                _indentAsked = keys.shiftKey.isPressed ? -1 : 1;
                return '\0';
            }

            // Alt with Ctrl is AltGr on many keyboards, which types characters: those are typed.
            if (Command(keys) && !keys.altKey.isPressed
                && (keys.zKey.isPressed || keys.yKey.isPressed || keys.sKey.isPressed || keys.slashKey.isPressed))
                return '\0';

            return added;
        }

        /// <summary>
        /// The keys the editor answers while it has the keyboard (Part 14g), read as a field may read keys it answers itself
        /// (<see cref="UiText.FieldKeys"/>): Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z through the typing, Ctrl+S to apply, Ctrl+/ to
        /// comment the lines, and the Tab the field handed over.
        /// </summary>
        private void TakeKeys()
        {
            int indent = _indentAsked;
            _indentAsked = 0;
            if (_readOnly)
                return;   // locked: these keys type too -- a comment, a step of the typing (the review of the lock)
            UnityEngine.InputSystem.Keyboard keys = UiText.FieldKeys;
            if (keys != null && Command(keys) && !keys.altKey.isPressed)
            {
                bool shift = keys.shiftKey.isPressed;
                if (keys.zKey.wasPressedThisFrame)
                    Stepped(shift ? _code.RedoTyping(Now) : _code.UndoTyping(Now));
                else if (keys.yKey.wasPressedThisFrame)
                    Stepped(_code.RedoTyping(Now));
                else if (keys.sKey.wasPressedThisFrame)
                    _applyAsked = true;
                else if (keys.slashKey.wasPressedThisFrame)
                    EditLines(CodeEdits.ToggleComment);
            }

            if (indent > 0)
                EditLines(CodeEdits.Indent);
            else if (indent < 0)
                EditLines(CodeEdits.Outdent);
        }

        /// <summary>Ctrl, or Command on a Mac, where TextMesh Pro's own Ctrl+C and Ctrl+V are Command too.</summary>
        private static bool Command(UnityEngine.InputSystem.Keyboard keys) =>
            keys.ctrlKey.isPressed || (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
                && (keys.leftMetaKey.isPressed || keys.rightMetaKey.isPressed);

        /// <summary>The field given the text a step of the typing came to, its caret where the two first differ.</summary>
        private void Stepped(bool stepped)
        {
            if (!stepped)
                return;
            string was = _field.text;
            int at = 0;
            while (at < was.Length && at < _code.Text.Length && was[at] == _code.Text[at])
                at++;
            _field.SetTextWithoutNotify(_code.Text);
            _field.stringPosition = at;
        }

        /// <summary>The lines the selection touches edited by <paramref name="edit"/> -- one step of the typing -- the selection kept on them.</summary>
        private void EditLines(System.Func<string, int, int, CodeEdit> edit)
        {
            int anchor = _field.selectionStringAnchorPosition, focus = _field.selectionStringFocusPosition;
            CodeEdit edited = edit(_field.text, anchor, focus);
            if (edited.Text == _field.text)
                return;
            _code.Edit(edited.Text, Now);
            _field.SetTextWithoutNotify(edited.Text);
            _field.selectionStringAnchorPosition = edited.Start;
            _field.selectionStringFocusPosition = edited.End;
        }

        /// <summary>Shows <paramref name="code"/>, or hides the editor for null.</summary>
        public void Bind(CodeDocument code)
        {
            if (ReferenceEquals(code, _code))
                return;
            _code = code;
            _shownVersion = _shownLines = -1;
            _indentAsked = 0;   // a Tab asked of the code before is not this code's
            if (code == null && _field != null && _field.isFocused)
                UiTheme.Defocus();
            Show(_root, code != null);
            if (code != null)
                _field.SetTextWithoutNotify(code.Text);

            if (_browser)
            {
                if (code == null)
                {
                    CodeMirrorBridge.Blur();
                    ShowBrowser(false);
                }
                else
                {
                    // Afresh: Ctrl+Z once took the code itself out, the putting in of it a step of the editor's undo.
                    CodeMirrorBridge.Load(code.Text);
                    _browserText = code.Text;
                }
            }
        }

        private void ShowBrowser(bool shown)
        {
            if (shown == _browserShown)
                return;
            _browserShown = shown;
            CodeMirrorBridge.Show(shown);
            if (!shown)
                _browserAt = default;
        }

        /// <summary>
        /// CodeMirror laid over the body, where the code is, and shown while the editor is -- but not while a popup of the
        /// shell is open, since the page draws it over the canvas and so over every popup too. And the game's keys are the
        /// editor's while it has the keyboard.
        /// </summary>
        private void PlaceBrowser()
        {
            bool shown = _root.gameObject.activeInHierarchy && !ShellPopup.AnyOpen;
            ShowBrowser(shown);
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = !CodeMirrorBridge.Focused;
#endif
            if (!shown)
                return;

            var corners = new Vector3[4];
            _body.GetWorldCorners(corners);   // a screen-space canvas: world is the screen's pixels
            Rect at = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            Canvas canvas = _root.GetComponentInParent<Canvas>();
            float font = UiTheme.SizeOf(UiType.Body) * (canvas != null ? canvas.rootCanvas.scaleFactor : 1f);
            if (at == _browserAt && Mathf.Approximately(font, _browserFont))
                return;
            _browserAt = at;
            _browserFont = font;
            CodeMirrorBridge.Place(at, font);
        }

        /// <summary>The palette as CodeMirror's colours: CSS, as JSON, every one the game's.</summary>
        private static string ColoursOf(SchematicPalette palette)
        {
            string Css(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
            Color selection = palette.Over(palette.Selection, 0.45f);
            bool dark = palette.Ground.r * 0.2126f + palette.Ground.g * 0.7152f + palette.Ground.b * 0.0722f < 0.5f;
            return "{\"ground\":\"" + Css(palette.Ground) + "\",\"text\":\"" + Css(palette.Text) + "\",\"muted\":\"" + Css(palette.Muted)
                + "\",\"accent\":\"" + Css(palette.Accent) + "\",\"one\":\"" + Css(palette.One) + "\",\"selection\":\"" + Css(selection)
                + "\",\"error\":\"" + Css(palette.Unknown) + "\",\"warning\":\"" + Css(palette.Cursor) + "\",\"dark\":" + (dark ? "true" : "false") + "}";
        }

        /// <summary>The marks as CodeMirror's: JSON, lines and columns from 1.</summary>
        private static string MarksOf(IReadOnlyList<CodeMark> marks)
        {
            var json = new StringBuilder("[");
            for (int i = 0; i < marks.Count; i++)
            {
                CodeMark m = marks[i];
                if (i > 0)
                    json.Append(',');
                json.Append("{\"line\":").Append(m.Line.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"column\":").Append(m.Column.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"error\":").Append(m.IsError ? "true" : "false")
                    .Append(",\"message\":\"").Append(Escaped(m.Message)).Append("\"}");
            }

            return json.Append(']').ToString();
        }

        private static string Escaped(string text)
        {
            var escaped = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (c == '"' || c == '\\')
                    escaped.Append('\\').Append(c);
                else if (c < ' ')
                    escaped.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    escaped.Append(c);
            }

            return escaped.ToString();
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

            if (_browser)
            {
                // What the page's editor did since the last frame: typed, asked to apply, let go on Escape.
                if (CodeMirrorBridge.TakeChanged())
                {
                    _browserText = CodeMirrorBridge.GetText();
                    _code.Type(_browserText, Now);
                }

                if (CodeMirrorBridge.TakeApply())
                    _applyAsked = true;
                if (CodeMirrorBridge.TakeEscape())
                    _letGoFrame = Time.frameCount;
            }
            else if (_field.isFocused || _indentAsked != 0)
            {
                TakeKeys();
            }

            if (_applyAsked)
            {
                _applyAsked = false;
                _code.Apply();
            }
            else
            {
                _code.Frame(Now);
            }

            if (_browser)
            {
                if (_browserText != _code.Text)
                {
                    // An undo, a redo or REVERT changed the text under the editor.
                    CodeMirrorBridge.SetText(_code.Text);
                    _browserText = _code.Text;
                }
            }
            else if (_field.text != _code.Text)
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
                if (_browser)
                    CodeMirrorBridge.SetMarks(MarksOf(_code.Marks));
            }

            if (_browser)
            {
                PlaceBrowser();
                return;
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
            if (_browser)
            {
                CodeMirrorBridge.GoTo(line, column);
                return;
            }

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
            WorkbenchPanel.TopLeft(_status.rectTransform, new Vector2(Pad, 8f), new Vector2(Mathf.Max(0f, size.x - 2f * Pad - RevertWidth - Pad - SwitchRoom - (_keepsLockRoom ? LockRoom : 0f)), ButtonHeight));
            WorkbenchPanel.TopLeft((RectTransform)_revert.transform, new Vector2(size.x - Pad - RevertWidth - SwitchRoom - (_keepsLockRoom ? LockRoom : 0f), 8f), new Vector2(RevertWidth, ButtonHeight));
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
            if (_readOnly)
                return "LOCKED: the waves are this code's. Press the lock to change it.";   // nothing typed applies (Part 15h-4d)
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
    /// The monospaced face code is set in: IBM Plex Mono, which ships with the game beside Plex Sans (Part 12f, through
    /// <see cref="ShellFonts"/>); and, in a scene without it, one the computer already has, found by name -- Consolas on
    /// Windows, Menlo on a Mac, DejaVu Sans Mono or Liberation Mono on Linux (Part 12d). Null where there is neither, and the
    /// code is set in the game's own face.
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

                if (ShellFonts.Mono != null)
                    _asset = TMP_FontAsset.CreateFontAsset(ShellFonts.Mono);
                if (_asset == null && Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    foreach (string family in Families)
                    {
                        // Null, and a line in the log, for a face this computer does not have: the next is tried.
                        _asset = TMP_FontAsset.CreateFontAsset(family, "Regular");
                        if (_asset != null)
                            break;
                    }
                }

                if (_asset == null)
                    return null;
                _asset.hideFlags = HideFlags.HideAndDontSave;
                if (_asset.material != null)
                    _asset.material.hideFlags = HideFlags.HideAndDontSave;
                if (_asset.atlasTextures != null)
                    foreach (Texture2D atlas in _asset.atlasTextures)
                        if (atlas != null)
                            atlas.hideFlags = HideFlags.HideAndDontSave;
                _asset.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
                _found = true;
                return _asset;
            }
        }
    }
}
