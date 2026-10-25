using System;
using System.Collections.Generic;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The inspector (Part 10c), a popover beside what it inspects: a field for each thing a part or a wire has --
    /// <see cref="Inspector"/> says which, and reads them back -- APPLY and CANCEL. Enter in any field applies them
    /// together, as one step; a refusal stays in the popover, in the document's words, for the field to be put right;
    /// a click off it, or Escape, leaves everything as it was. Temporary, as the parts strip is, until Part 11's shell.
    /// </summary>
    /// <remarks>
    /// Built as RENAME is (<see cref="WaveRenameField"/>), blocker and panel side by side, and holding Escape the same
    /// way: while a field has the keyboard it answers Enter and Escape itself.
    /// </remarks>
    public sealed class InspectorPopover : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench inspector", ApplyName = "Workbench inspector apply", CancelName = "Workbench inspector cancel";

        /// <summary>A field's name, for the field of this key (<see cref="InspectorField.Key"/>).</summary>
        public static string FieldName(string key) => "Workbench inspector " + key;

        private const float Width = 380f, Pad = 12f, LabelWidth = 116f, RowHeight = 34f, RowGap = 6f, ButtonWidth = 96f;

        private RectTransform _panel;
        private TextMeshProUGUI _title, _says, _refusal;
        private RectTransform _rows, _buttons;
        private SchematicPalette _palette;
        private Func<Inspection, Dictionary<string, string>, string> _onApply;
        private readonly List<(InspectorField Field, TMP_InputField Input)> _fields = new List<(InspectorField, TMP_InputField)>();
        private Inspection _inspection;
        private int _closedFrame = -1;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>What it shows; null while it is shut.</summary>
        public Inspection Inspection => IsOpen ? _inspection : null;

        /// <summary>What the last APPLY was refused with, as it shows it; null when nothing was.</summary>
        public string Refusal => _refusal != null && _refusal.gameObject.activeSelf ? _refusal.text : null;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>
        /// The popover under <paramref name="root"/>, shut. <paramref name="onApply"/> makes what the fields say, and
        /// answers null when it was made, or the words it was refused with.
        /// </summary>
        public static InspectorPopover Build(RectTransform root, SchematicPalette palette, Func<Inspection, Dictionary<string, string>, string> onApply)
        {
            RectTransform container = UiTheme.Rect(RootName, root);
            UiTheme.Stretch(container);

            Image blockerImage = UiTheme.Panel_("blocker", container, new Color(0f, 0f, 0f, 0f));
            blockerImage.sprite = null;
            var blocker = blockerImage.gameObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            UiTheme.Stretch(blockerImage.rectTransform);

            var popover = container.gameObject.AddComponent<InspectorPopover>();
            popover._palette = palette;
            popover._onApply = onApply;
            blocker.onClick.AddListener(() => { popover.Close(); UiTheme.Defocus(); });

            Image panel = WorkbenchPanel.CanvasChip("panel", container, palette);
            panel.raycastTarget = true;
            popover._panel = panel.rectTransform;
            popover._panel.anchorMin = popover._panel.anchorMax = new Vector2(0f, 1f);
            popover._panel.pivot = new Vector2(0f, 1f);

            popover._title = UiTheme.Label("title", popover._panel, UiType.Label, palette.Text, TextAlignmentOptions.MidlineLeft);
            popover._says = UiTheme.Label("says", popover._panel, UiType.Caption, palette.TextDim, TextAlignmentOptions.TopLeft);
            popover._says.textWrappingMode = TextWrappingModes.Normal;
            popover._rows = UiTheme.Rect("rows", popover._panel);
            // In the text's colour, which every look holds to 4.5:1 on a chip; x's colour is a value's, held only to 3:1.
            popover._refusal = UiTheme.Label("refusal", popover._panel, UiType.Caption, palette.Text, TextAlignmentOptions.TopLeft);
            popover._refusal.textWrappingMode = TextWrappingModes.Normal;
            popover._buttons = UiTheme.Rect("buttons", popover._panel);

            Button apply = WorkbenchPanel.CanvasButton(ApplyName, popover._buttons, "APPLY", out TextMeshProUGUI _, UiType.Body, palette);
            WorkbenchPanel.TopLeft(apply.GetComponent<RectTransform>(), Vector2.zero, new Vector2(ButtonWidth, UiTheme.ButtonHeight * 0.8f));
            apply.onClick.AddListener(() => popover.Apply());
            Button cancel = WorkbenchPanel.CanvasButton(CancelName, popover._buttons, "CANCEL", out TextMeshProUGUI _, UiType.Body, palette);
            WorkbenchPanel.TopLeft(cancel.GetComponent<RectTransform>(), new Vector2(ButtonWidth + 8f, 0f), new Vector2(ButtonWidth, UiTheme.ButtonHeight * 0.8f));
            cancel.onClick.AddListener(() => { popover.Close(); UiTheme.Defocus(); });

            popover.gameObject.SetActive(false);
            return popover;
        }

        /// <summary>Opens on <paramref name="inspection"/>, its corner at <paramref name="screenPoint"/>, the first field holding the keyboard.</summary>
        public void Open(Inspection inspection, Vector2 screenPoint)
        {
            if (inspection == null)
                return;
            _inspection = inspection;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            _title.text = inspection.Title;
            _says.text = inspection.Says;
            BuildRows(inspection);
            ShowRefusal(null);
            Place(screenPoint);

            if (_fields.Count > 0)
            {
                TMP_InputField first = _fields[0].Input;
                UiTheme.Focus(first);
                first.selectionAnchorPosition = 0;
                first.selectionFocusPosition = first.text.Length;
            }

            UiEscape.Opened(this);
        }

        /// <summary>What each field holds now, by key.</summary>
        public Dictionary<string, string> Typed()
        {
            var typed = new Dictionary<string, string>();
            foreach ((InspectorField field, TMP_InputField input) in _fields)
                typed[field.Key] = input.text;
            return typed;
        }

        /// <summary>Puts <paramref name="text"/> in the field of <paramref name="key"/>, as typing would; false for no such field.</summary>
        public bool Type(string key, string text)
        {
            foreach ((InspectorField field, TMP_InputField input) in _fields)
            {
                if (field.Key != key)
                    continue;
                input.text = text;
                return true;
            }

            return false;
        }

        /// <summary>Makes what the fields say, as APPLY and Enter do: closed when it was made, open with the words when it was refused.</summary>
        public void Apply()
        {
            if (!IsOpen || _inspection == null)
                return;
            string refused = _onApply?.Invoke(_inspection, Typed());
            if (refused == null)
            {
                Close();
                return;
            }

            ShowRefusal(refused);
        }

        /// <summary>Shuts it, leaving everything as it was.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            _closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            UiTheme.Defocus();
        }

        private void BuildRows(Inspection inspection)
        {
            foreach (Transform child in _rows)
                Destroy(child.gameObject);
            _fields.Clear();

            float y = 0f;
            foreach (InspectorField field in inspection.Fields)
            {
                TextMeshProUGUI label = UiTheme.Label("label " + field.Key, _rows, UiType.Caption, _palette.TextDim, TextAlignmentOptions.MidlineLeft);
                WorkbenchPanel.TopLeft(label.rectTransform, new Vector2(0f, y), new Vector2(LabelWidth, RowHeight));
                label.text = field.Label;

                TMP_InputField input = WorkbenchPanel.CanvasField(FieldName(field.Key), _rows, _palette, field.Hint);
                WorkbenchPanel.TopLeft(input.GetComponent<RectTransform>(), new Vector2(LabelWidth, y), new Vector2(Width - 2f * Pad - LabelWidth, RowHeight));
                input.SetTextWithoutNotify(field.Text);
                input.onSubmit.AddListener(_ => Apply());
                input.onEndEdit.AddListener(_ =>
                {
                    if (input.wasCanceled)
                        Close();
                });
                _fields.Add((field, input));
                y += RowHeight + RowGap;
            }

            Layout();
        }

        private void ShowRefusal(string words)
        {
            _refusal.gameObject.SetActive(!string.IsNullOrEmpty(words));
            _refusal.text = words ?? "";
            Layout();
        }

        /// <summary>The title, what it is, the rows, any refusal and the buttons, each under the one before.</summary>
        private void Layout()
        {
            float inner = Width - 2f * Pad;
            float y = Pad;
            WorkbenchPanel.TopLeft(_title.rectTransform, new Vector2(Pad, y), new Vector2(inner, 24f));
            y += 26f;

            float says = string.IsNullOrEmpty(_says.text) ? 0f : _says.GetPreferredValues(_says.text, inner, 0f).y;
            WorkbenchPanel.TopLeft(_says.rectTransform, new Vector2(Pad, y), new Vector2(inner, says));
            y += says + (says > 0f ? 8f : 0f);

            float rows = _fields.Count * (RowHeight + RowGap);
            WorkbenchPanel.TopLeft(_rows, new Vector2(Pad, y), new Vector2(inner, rows));
            y += rows;

            if (_refusal.gameObject.activeSelf)
            {
                float refusal = _refusal.GetPreferredValues(_refusal.text, inner, 0f).y;
                WorkbenchPanel.TopLeft(_refusal.rectTransform, new Vector2(Pad, y), new Vector2(inner, refusal));
                y += refusal + 8f;
            }

            WorkbenchPanel.TopLeft(_buttons, new Vector2(Pad, y), new Vector2(inner, UiTheme.ButtonHeight * 0.8f));
            y += UiTheme.ButtonHeight * 0.8f + Pad;
            _panel.sizeDelta = new Vector2(Width, y);
        }

        /// <summary>The panel's corner at <paramref name="screenPoint"/>, kept on the screen.</summary>
        private void Place(Vector2 screenPoint)
        {
            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, null, out Vector2 local);
            Rect area = root.rect;
            Vector2 size = _panel.sizeDelta;
            float x = Mathf.Clamp(local.x - area.xMin, 0f, Mathf.Max(0f, area.width - size.x));
            float down = Mathf.Clamp(area.yMax - local.y, 0f, Mathf.Max(0f, area.height - size.y));
            _panel.anchoredPosition = new Vector2(x, -down);
        }

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void Update()
        {
            // While a field holds the keyboard it answers Enter and Escape itself; with none focused, the popover does.
            Keyboard keyboard = UiText.Keyboard;
            if (!IsOpen || keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame && UiEscape.TryTake(this))
                Close();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                Apply();
        }
    }
}
