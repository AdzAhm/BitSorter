using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// Asks for a free-play board's name: a field, OK and CANCEL.
    /// </summary>
    /// <remarks>
    /// The game's first text field. A name is the one thing in it that cannot be picked from a
    /// list or stepped with arrows, and a docked panel is no place to type -- so it is a full-screen
    /// panel of its own, which takes the board's keys and clicks out of the way the way every other
    /// full-screen panel does, and <see cref="UiText"/> keeps the game's keys out of the field.
    ///
    /// Enter means OK and Escape means CANCEL, while typing and after. While typing the field hears
    /// them first and says which (<see cref="TMP_InputField.onSubmit"/>, and an end of editing that
    /// was cancelled); once it has let go, this reads them itself -- and not on the frame it opened,
    /// for the reason <see cref="FullScreenPanel.OpenedThisFrame"/> gives.
    ///
    /// What happens to the name is the caller's: it is handed a function that either takes the
    /// name or says why not, and a refusal is shown under the field with the panel left open, so a
    /// name already in use is corrected rather than lost.
    /// </remarks>
    public sealed class NameBoardPanel : FullScreenPanel
    {
        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        /// <summary>The panel's heading.</summary>
        public const string Title = "NAME THIS BOARD";

        private TMP_InputField _field;
        private TextMeshProUGUI _refusal;

        /// <summary>Takes a name, returning null, or refuses it with the reason.</summary>
        private Func<string, string> _accept;

        private const float FieldWidth = 520f;
        private const float FieldHeight = 52f;

        private void Awake()
        {
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Build();
            SetShowing(false);
        }

        /// <summary>
        /// Opens the panel on a name, selected in full so typing replaces it.
        /// </summary>
        /// <param name="current">The name the field starts with.</param>
        /// <param name="accept">Takes a name, returning null, or refuses it with the reason.</param>
        public void Ask(string current, Func<string, string> accept)
        {
            if (Root == null || accept == null)
                return;

            _accept = accept;
            _refusal.text = string.Empty;
            _field.text = current ?? string.Empty;

            SetShowing(true);
            Focus();
        }

        /// <summary>The name in the field now, for the tests.</summary>
        public string Typed => _field != null ? _field.text : null;

        /// <summary>The refusal under the field, or empty.</summary>
        public string Refusal => _refusal != null ? _refusal.text : string.Empty;

        private void Update()
        {
            if (!IsShowing || UiText.Typing || OpenedThisFrame)
                return;

            // Only once the field has let go of the keys: while it holds them, it answers Enter and
            // Escape itself, and the keyboard reads as absent here as it does everywhere else.
            Keyboard keyboard = UiText.Keyboard;

            if (keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
                Cancel();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                Confirm();
        }

        private void Build()
        {
            Image scrim = UiTheme.Scrim("Name board", _canvas.transform, Palette.Current.CardScrim);
            Root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(Root);

            TextMeshProUGUI title = UiTheme.Label(
                "title", Root, UiType.Heading, UiTheme.Accent, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 110f), new Vector2(FieldWidth, 40f));
            title.text = Title;

            _field = BuildField();

            _refusal = UiTheme.Label(
                "name refusal", Root, UiType.Body, UiTheme.Bad, TextAlignmentOptions.Center);
            UiTheme.Anchor(_refusal.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -10f), new Vector2(FieldWidth, 26f));

            const float buttonWidth = 200f;

            Button ok = UiTheme.Button_("Name ok", Root, "OK", out TextMeshProUGUI _, role: ButtonRole.Primary);
            UiTheme.Anchor(ok.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(buttonWidth * 0.5f + 8f, -80f), new Vector2(buttonWidth, UiTheme.ButtonHeight + 6f));
            ok.onClick.AddListener(Confirm);

            Button cancel = UiTheme.Button_("Name cancel", Root, "CANCEL", out TextMeshProUGUI _, role: ButtonRole.Quiet);
            UiTheme.Anchor(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-buttonWidth * 0.5f - 8f, -80f), new Vector2(buttonWidth, UiTheme.ButtonHeight + 6f));
            cancel.onClick.AddListener(Cancel);

            TextMeshProUGUI help = UiTheme.Label(
                "help", Root, UiTheme.HelpLineType, UiTheme.HelpLineColour, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -150f), new Vector2(FieldWidth, UiTheme.HelpLineHeight));
            help.text = $"Enter to keep it, escape to leave it as it was. Up to {BoardNames.MaxLength} characters.";
        }

        /// <summary>
        /// A single-line field: a panel for its box, a masked area for the text to scroll in, and the
        /// text and its placeholder -- the pieces TMP_InputField needs handed to it.
        /// </summary>
        private TMP_InputField BuildField()
        {
            Image box = UiTheme.Panel_("name field", Root, UiTheme.Panel);
            RectTransform rect = box.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 40f), new Vector2(FieldWidth, FieldHeight));

            RectTransform area = UiTheme.Rect("text area", rect);
            UiTheme.Stretch(area, 12f);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = UiTheme.Label(
                "placeholder", area, UiType.Lead, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Stretch(placeholder.rectTransform, 0f);
            placeholder.text = BoardNames.First;

            TextMeshProUGUI text = UiTheme.Label(
                "text", area, UiType.Lead, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Stretch(text.rectTransform, 0f);

            TMP_InputField field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = BoardNames.MaxLength;
            field.caretColor = UiTheme.Text;
            field.customCaretColor = true;

            Color selection = UiTheme.Accent;
            selection.a = 0.35f;
            field.selectionColor = selection;

            // Escape leaves the name as it was, and ends the question; Enter keeps what was typed.
            field.restoreOriginalTextOnEscape = true;
            field.onSubmit.AddListener(_ => Confirm());
            field.onEndEdit.AddListener(_ =>
            {
                if (field.wasCanceled)
                    Cancel();
            });

            return field;
        }

        /// <summary>Puts the keyboard in the field, with its text selected.</summary>
        private void Focus()
        {
            UiTheme.Focus(_field);
            _field.selectionAnchorPosition = 0;
            _field.selectionFocusPosition = _field.text.Length;
        }

        /// <summary>
        /// Offers what was typed. Closes on a name that was taken; stays open, saying why, on one
        /// that was not.
        /// </summary>
        public void Confirm()
        {
            if (!IsShowing || _accept == null)
                return;

            string refusal = _accept(_field.text);

            if (refusal == null)
            {
                Close();
                return;
            }

            _refusal.text = refusal;
            Focus();
        }

        /// <summary>Closes without changing anything.</summary>
        public void Cancel()
        {
            if (IsShowing)
                Close();
        }

        private void Close()
        {
            _accept = null;
            UiTheme.Defocus();
            SetShowing(false);
        }

        /// <summary>For the tests: types a whole name in at once, as a paste would.</summary>
        public void SetTyped(string name)
        {
            if (_field != null)
                _field.text = name;
        }
    }
}
