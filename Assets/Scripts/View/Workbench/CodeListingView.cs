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
    /// </remarks>
    public sealed class CodeListingView : MonoBehaviour, IHoldsEscape, IScrollHandler
    {
        /// <summary>The object's name, which the tests look for.</summary>
        public const string RootName = "Workbench code listing";

        private const float GutterWidth = 64f, Pad = 10f;

        private RectTransform _root, _gutterView, _gutterText;
        private TMP_InputField _field;
        private TextMeshProUGUI _gutter;
        private int _letGoFrame = -1, _shownLines = -1;

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
        }

        /// <summary>Shows <paramref name="text"/> from its first line, or keeps the place when it is the same file printed again.</summary>
        public void Show(string text, bool sameFile)
        {
            text ??= "";
            if (_field.text == text)
                return;
            int caret = sameFile ? Mathf.Min(_field.stringPosition, text.Length) : 0;
            _field.SetTextWithoutNotify(text);
            _field.stringPosition = caret;
            int lines = 1;
            foreach (char c in text)
                if (c == '\n')
                    lines++;
            if (lines != _shownLines)
            {
                _shownLines = lines;
                var numbers = new StringBuilder();
                for (int line = 1; line <= lines; line++)
                    numbers.Append(line > 1 ? "\n" : "").Append(line.ToString(CultureInfo.InvariantCulture));
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

            // The numbers follow the text as the field scrolls it.
            _gutterText.anchoredPosition = new Vector2(_gutterText.anchoredPosition.x, _field.textComponent.rectTransform.anchoredPosition.y);
        }

        /// <summary>The wheel over the line numbers or the margins scrolls the file, as it does over the text.</summary>
        public void OnScroll(PointerEventData eventData) => _field?.OnScroll(eventData);
    }
}
