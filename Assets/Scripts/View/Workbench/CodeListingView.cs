using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The dock's CODE tab (Part 12e): one file of the SystemVerilog the document prints as, read-only, with its line numbers
    /// -- selectable, so a student can copy it out -- in the code's monospaced face (<see cref="CodeFont"/>).
    /// </summary>
    /// <remarks>
    /// A read-only field rather than a label, for the selection and Ctrl+C that come with one. Like the code editor's, it
    /// holds Escape while it has the keyboard, so the press that lets it go does not close the workbench as well.
    ///
    /// **It is linked to the drawing of code** (Part 15h-5): the lines what is selected there comes from are marked with a
    /// band behind them and scrolled into view, and a click on a line says which (<see cref="LineClicked"/>). A written
    /// module's file is numbered as its editor numbers it, from its own first line.
    /// </remarks>
    public sealed class CodeListingView : MonoBehaviour, IHoldsEscape, IScrollHandler
    {
        /// <summary>The object's name, which the tests look for.</summary>
        public const string RootName = "Workbench code listing";

        private const float GutterWidth = 64f, Pad = 10f;

        private RectTransform _root, _gutterView, _gutterText, _marks;
        private TMP_InputField _field;
        private TextMeshProUGUI _gutter;
        private int _letGoFrame = -1, _shownLines = -1, _shownBody = -1;
        private Color _markColour;
        private readonly List<Image> _bands = new List<Image>();
        private int[] _marked = Array.Empty<int>();
        private bool _scrollToMark;

        /// <summary>A plain click on a line of the file, by its number in the file from 1.</summary>
        public event Action<int> LineClicked;

        /// <summary>The lines of the file marked, by number from 1 (<see cref="Mark"/>).</summary>
        public IReadOnlyList<int> Marked => _marked;

        /// <summary>The field the file is shown in.</summary>
        public TMP_InputField Field => _field;

        /// <summary>The text shown.</summary>
        public string Text => _field != null ? _field.text : null;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => (_field != null && _field.isFocused) || _letGoFrame == Time.frameCount;

        /// <summary>The listing built under <paramref name="parent"/>, hidden until the CODE tab shows it.</summary>
        public static CodeListingView Build(RectTransform parent, SchematicPalette palette)
        {
            Image ground = UiTheme.Panel_(RootName, parent, palette.Ground);
            ground.sprite = null;
            var view = ground.gameObject.AddComponent<CodeListingView>();
            view._root = ground.rectTransform;
            view.BuildInside(palette);
            UiTheme.SetShown(ground, false);
            return view;
        }

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void BuildInside(SchematicPalette palette)
        {
            _gutterView = UiTheme.Rect("gutter", _root);
            _gutterView.gameObject.AddComponent<RectMask2D>();
            _gutter = UiTheme.Label("numbers", _gutterView, UiType.Body, palette.Muted, TextAlignmentOptions.TopRight);
            _gutter.raycastTarget = false;
            _gutter.textWrappingMode = TextWrappingModes.NoWrap;
            _gutter.overflowMode = TextOverflowModes.Overflow;
            _gutterText = _gutter.rectTransform;

            Image box = UiTheme.Panel_("listing", _root, palette.Ground);
            box.sprite = null;
            RectTransform area = UiTheme.Rect("text area", box.rectTransform);
            UiTheme.Stretch(area, 0f);
            area.gameObject.AddComponent<RectMask2D>();

            // The marks behind the text, moved with it as it scrolls (Part 15h-5).
            _marks = UiTheme.Rect("marks", area);
            _markColour = palette.Over(palette.Accent, 0.2f);   // the selection's own colour, faint: the selection's fill is too near the ground to show behind text

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
            _field.readOnly = true;
            _field.richText = false;
            _field.onFocusSelectAll = false;
            _field.resetOnDeActivation = false;
            _field.restoreOriginalTextOnEscape = false;
            _field.scrollSensitivity = 3f;
            _field.caretColor = palette.Text;
            _field.customCaretColor = true;
            _field.selectionColor = palette.Over(palette.Selection, 0.45f);
            _field.onEndEdit.AddListener(_ =>
            {
                // As the code editor's: Escape holds this frame's press, and a finished field lets go of itself alone.
                if (_field.wasCanceled)
                    _letGoFrame = Time.frameCount;
                UiTheme.Defocus(_field);
            });
            box.gameObject.AddComponent<Clicks>().View = this;
        }

        /// <summary>
        /// The field's clicks, heard beside the field's own on its object: a click on a line, never a drag that selects text.
        /// The event system still calls a press and release on one object a click after a drag between them, since the field
        /// is what was both pressed and dragged (the review of 15h-5). A double-click is two clicks on its line, harmless: a
        /// count of clicks would also take two quick clicks on two lines for one.
        /// </summary>
        private sealed class Clicks : MonoBehaviour, IPointerClickHandler
        {
            /// <summary>How far, in pixels, a press may move before its release and still be a click on its line.</summary>
            private const float Slop = 4f;

            public CodeListingView View;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left || View == null || View._field == null
                    || eventData.dragging || (eventData.pressPosition - eventData.position).sqrMagnitude > Slop * Slop)
                    return;
                int line = TMP_TextUtilities.FindIntersectingLine(View._field.textComponent, eventData.position, eventData.pressEventCamera);
                if (line >= 0)
                    View.LineClicked?.Invoke(line + 1);
            }
        }

        /// <summary>The middle of line <paramref name="line"/> of the file on the screen, where a click on it lands: for the tests.</summary>
        public Vector2 ScreenPointOfLine(int line)
        {
            TMP_Text text = _field.textComponent;
            text.ForceMeshUpdate();
            if (line < 1 || line > text.textInfo.lineCount)
                return new Vector2(-1f, -1f);
            TMP_LineInfo info = text.textInfo.lineInfo[line - 1];
            Vector3 local = new Vector3(text.rectTransform.rect.xMin + 40f, (info.ascender + info.descender) / 2f, 0f);
            return RectTransformUtility.WorldToScreenPoint(null, text.rectTransform.TransformPoint(local));
        }

        /// <summary>
        /// Marks <paramref name="lines"/> of the file, by number from 1, with a band behind each (Part 15h-5), scrolling the
        /// first into view when they change and it is out of it. None takes the marks away.
        /// </summary>
        public void Mark(IReadOnlyList<int> lines)
        {
            lines ??= Array.Empty<int>();
            bool same = lines.Count == _marked.Length;
            for (int i = 0; same && i < lines.Count; i++)
                same = lines[i] == _marked[i];
            if (same)
                return;
            _marked = new int[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                _marked[i] = lines[i];
            _scrollToMark = _marked.Length > 0;
        }

        /// <summary>
        /// Shows <paramref name="text"/> from its first line, or keeps the place when it is the same file printed again, its
        /// lines numbered from <paramref name="bodyLine"/>: the lines over it, which BitSorter prints, have no number.
        /// </summary>
        public void Show(string text, bool sameFile, int bodyLine = 1)
        {
            text ??= "";
            if (_field.text == text && bodyLine == _shownBody)
                return;
            int caret = sameFile ? Mathf.Min(_field.stringPosition, text.Length) : 0;
            _field.SetTextWithoutNotify(text);
            _field.stringPosition = caret;
            int lines = 1;
            foreach (char c in text)
                if (c == '\n')
                    lines++;
            if (lines != _shownLines || bodyLine != _shownBody)
            {
                _shownLines = lines;
                _shownBody = bodyLine;
                var numbers = new StringBuilder();
                for (int line = 1; line <= lines; line++)
                {
                    numbers.Append(line > 1 ? "\n" : "");
                    if (line >= bodyLine)
                        numbers.Append((line - bodyLine + 1).ToString(CultureInfo.InvariantCulture));
                }
                _gutter.text = numbers.ToString();
            }
        }

        /// <summary>Shows or hides the listing; hidden, it lets the keyboard go.</summary>
        public void SetShown(bool shown)
        {
            if (!shown && _field != null && _field.isFocused)
                UiTheme.Defocus();
            UiTheme.SetShown(_root, shown);
        }

        /// <summary>The listing over <paramref name="size"/> at <paramref name="corner"/>.</summary>
        public void Place(Vector2 corner, Vector2 size)
        {
            WorkbenchPanel.TopLeft(_root, corner, size);
            WorkbenchPanel.TopLeft(_gutterView, new Vector2(0f, Pad), new Vector2(GutterWidth, Mathf.Max(0f, size.y - Pad)));
            WorkbenchPanel.TopLeft(_gutterText, Vector2.zero, new Vector2(GutterWidth - Pad, 100000f));
            WorkbenchPanel.TopLeft((RectTransform)_field.transform, new Vector2(GutterWidth + Pad, Pad),
                new Vector2(Mathf.Max(0f, size.x - GutterWidth - 2f * Pad), Mathf.Max(0f, size.y - Pad)));
        }

        private void LateUpdate()
        {
            if (_field == null || !_root.gameObject.activeInHierarchy)
                return;

            // Read-only, the field draws no caret, so a plain click must not take every workbench key with nothing on
            // screen to say so: it keeps the keyboard only while text is selected, which shows as a highlight -- what
            // Ctrl+C has to copy -- and lets go once the button is up with nothing selected. Held, a drag still selects.
            if (_field.isFocused && _field.selectionStringAnchorPosition == _field.selectionStringFocusPosition
                && !(UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed))
                UiTheme.Defocus(_field);

            if (_scrollToMark)
            {
                _scrollToMark = false;
                ScrollIntoView();
            }

            // The numbers and the marks follow the text as the field scrolls it.
            _gutterText.anchoredPosition = new Vector2(_gutterText.anchoredPosition.x, _field.textComponent.rectTransform.anchoredPosition.y);
            PlaceMarks();
        }

        /// <summary>A band behind each marked line, in the text's own place: the marks are laid out as the text is.</summary>
        private void PlaceMarks()
        {
            RectTransform text = _field.textComponent.rectTransform;
            _marks.anchorMin = text.anchorMin;
            _marks.anchorMax = text.anchorMax;
            _marks.pivot = text.pivot;
            _marks.sizeDelta = text.sizeDelta;
            _marks.anchoredPosition = text.anchoredPosition;

            TMP_TextInfo info = _field.textComponent.textInfo;
            Rect rect = text.rect;
            int shown = 0;
            foreach (int line in _marked)
            {
                if (info == null || line < 1 || line > info.lineCount)
                    continue;
                if (shown == _bands.Count)
                {
                    Image band = UiTheme.Panel_("mark", _marks, _markColour);
                    band.sprite = null;
                    band.raycastTarget = false;
                    _bands.Add(band);
                }
                TMP_LineInfo at = info.lineInfo[line - 1];
                RectTransform r = _bands[shown].rectTransform;
                r.anchorMin = r.anchorMax = text.pivot;
                r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(rect.xMin - Pad, at.ascender);
                r.sizeDelta = new Vector2(rect.width + 2f * Pad, Mathf.Max(1f, at.ascender - at.descender));
                _bands[shown].gameObject.SetActive(true);
                shown++;
            }
            for (int i = shown; i < _bands.Count; i++)
                _bands[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// The first marked line brought into view, a third of the way down, when no marked line is in view already -- a box
        /// drawn over the drawing, or Ctrl+A, keeps the code where it was (the review of 15h-5) -- through the field's own
        /// scroll, which keeps its place as its own: to the top, then down by the line's share of the text.
        /// </summary>
        private void ScrollIntoView()
        {
            TMP_Text text = _field.textComponent;
            text.ForceMeshUpdate();
            TMP_TextInfo info = text.textInfo;
            float view = _field.textViewport.rect.height, whole = text.preferredHeight;
            if (whole <= view)
                return;

            float scrolled = text.rectTransform.anchoredPosition.y;
            foreach (int marked in _marked)
                if (marked >= 1 && marked <= info.lineCount
                    && info.lineInfo[0].ascender - info.lineInfo[marked - 1].ascender >= scrolled
                    && info.lineInfo[0].ascender - info.lineInfo[marked - 1].descender <= scrolled + view)
                    return;

            int line = _marked[0];
            if (line < 1 || line > info.lineCount)
                return;
            float top = info.lineInfo[0].ascender - info.lineInfo[line - 1].ascender;

            float wanted = Mathf.Clamp(top - view / 3f, 0f, whole - view) / (whole - view);
            var toTheTop = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, 1e6f) };
            _field.OnScroll(toTheTop);
            var down = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -wanted * info.lineCount / Mathf.Max(0.0001f, _field.scrollSensitivity)) };
            _field.OnScroll(down);
        }

        /// <summary>The wheel over the line numbers or the margins scrolls the file, as it does over the text.</summary>
        public void OnScroll(PointerEventData eventData) => _field?.OnScroll(eventData);
    }
}
