using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// RENAME on a row's menu (Part 9b-2b): a field holding the row's name, Enter giving the row or
    /// group the name typed -- an empty one gives a row its own back -- and a click off it, or Escape,
    /// leaving it as it was.
    /// </summary>
    /// <remarks>
    /// Built as the add list is, blocker and panel side by side, and holding Escape the same way
    /// (<see cref="IHoldsEscape"/>): while the field has the keyboard it answers Enter and Escape itself.
    /// </remarks>
    public sealed class WaveRenameField : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The object's name, which the tests look for.</summary>
        public const string RootName = "Workbench rename";

        /// <summary>The name of the same field asking for a breakpoint's value (Part 9b-3).</summary>
        public const string BreakValueName = "Workbench break value";

        /// <summary>The name of the same field asking for an input's value over a stretch (Part 9b-3).</summary>
        public const string SetValueName = "Workbench set value";

        private const float Width = 320f, Pad = 12f;

        private RectTransform _panel;
        private TextMeshProUGUI _title;
        private TMP_InputField _field;
        private Action<string> _onRename;
        private int _closedFrame = -1;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>
        /// The field under <paramref name="root"/>, shut, naming through <paramref name="onRename"/> -- or,
        /// named and titled otherwise, asking for any one line of text, as BREAK ON VALUE does.
        /// </summary>
        public static WaveRenameField Build(RectTransform root, SchematicPalette palette, Action<string> onRename,
            string name = RootName, string titleText = "RENAME", string placeholder = "empty for its own name")
        {
            RectTransform container = UiTheme.Rect(name, root);
            UiTheme.Stretch(container);

            Image blockerImage = UiTheme.Panel_("blocker", container, new Color(0f, 0f, 0f, 0f));
            blockerImage.sprite = null;
            var blocker = blockerImage.gameObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            UiTheme.Stretch(blockerImage.rectTransform);

            var rename = container.gameObject.AddComponent<WaveRenameField>();
            rename._onRename = onRename;
            blocker.onClick.AddListener(() => { rename.Close(); UiTheme.Defocus(); });

            Image panel = WorkbenchPanel.CanvasChip("panel", container, palette);
            panel.raycastTarget = true;
            rename._panel = panel.rectTransform;
            rename._panel.anchorMin = rename._panel.anchorMax = new Vector2(0f, 1f);
            rename._panel.pivot = new Vector2(0f, 1f);

            TextMeshProUGUI title = UiTheme.Label("title", rename._panel, UiType.Caption, palette.TextDim, TextAlignmentOptions.MidlineLeft);
            WorkbenchPanel.TopLeft(title.rectTransform, new Vector2(Pad, Pad), new Vector2(Width - 2f * Pad, 24f));
            title.text = titleText;
            rename._title = title;

            rename._field = WorkbenchPanel.CanvasField("name", rename._panel, palette, placeholder);
            WorkbenchPanel.TopLeft(rename._field.GetComponent<RectTransform>(), new Vector2(Pad, Pad + 30f), new Vector2(Width - 2f * Pad, 38f));
            rename._field.onSubmit.AddListener(text => rename.Confirm(text));
            rename._field.onEndEdit.AddListener(_ =>
            {
                if (rename._field.wasCanceled)
                    rename.Close();
            });

            rename._panel.sizeDelta = new Vector2(Width, Pad + 30f + 38f + Pad);
            rename.gameObject.SetActive(false);
            return rename;
        }

        /// <summary>
        /// Opens the field holding <paramref name="name"/>, its corner at <paramref name="screenPoint"/>, with
        /// the keyboard -- titled <paramref name="title"/> when it says what it is for each time, as SET does.
        /// </summary>
        public void Open(string name, Vector2 screenPoint, string title = null)
        {
            if (title != null)
                _title.text = title;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, null, out Vector2 local);
            Rect area = root.rect;
            Vector2 size = _panel.sizeDelta;
            float x = Mathf.Clamp(local.x - area.xMin, 0f, Mathf.Max(0f, area.width - size.x));
            float down = Mathf.Clamp(area.yMax - local.y, 0f, Mathf.Max(0f, area.height - size.y));
            _panel.anchoredPosition = new Vector2(x, -down);

            _field.SetTextWithoutNotify(name ?? "");
            UiTheme.Focus(_field);
            _field.selectionAnchorPosition = 0;
            _field.selectionFocusPosition = _field.text.Length;
            UiEscape.Opened(this);
        }

        /// <summary>Gives the row <paramref name="name"/> and closes, as Enter does.</summary>
        public void Confirm(string name)
        {
            if (!IsOpen)
                return;
            Close();
            _onRename?.Invoke(name);
        }

        /// <summary>Shuts it, leaving the name as it was.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            _closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            UiTheme.Defocus();
        }

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void Update()
        {
            Keyboard keyboard = UiText.Keyboard;
            if (!IsOpen || keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame && UiEscape.TryTake(this))
                Close();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                Confirm(_field.text);
        }
    }
}
