using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// The + SIGNAL list (Part 9b-2b): a search over every signal the design has
    /// (<see cref="SignalIndex.Search"/>), its best hits as buttons, and each hit clicked added to the
    /// waveform. It stays up to add another; Enter adds the first hit; a click off it, or Escape, closes it.
    /// </summary>
    /// <remarks>
    /// Kept thin, in the workbench's own uGUI, as the row menu is: Part 11's hierarchy panel draws
    /// <see cref="SignalIndex.Root"/> as a tree. While the field holds the keyboard the game's keys are
    /// off (<see cref="UiText"/>) and the field answers Escape itself; once it has let go, this reads
    /// Escape and Enter, and holds Escape (<see cref="IHoldsEscape"/>) so one press closes the list and
    /// leaves the workbench up. A library cell's pins are left out unless CELLS is on.
    /// </remarks>
    public sealed class WaveSignalPicker : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The objects' names, which the tests look for.</summary>
        public const string RootName = "Workbench add list";

        /// <inheritdoc cref="RootName"/>
        public const string CellsButton = "Workbench add cells";

        /// <summary>A hit's button's name: this, and its place in the list.</summary>
        public const string HitButton = "Workbench add hit ";

        /// <summary>How many hits are shown at once; past them a line says how many more there are.</summary>
        public const int Shown = 10;

        private const float Width = 440f, Pad = 12f, HitHeight = 30f, Gap = 4f;

        private RectTransform _panel;
        private TMP_InputField _field;
        private readonly Button[] _hits = new Button[Shown];
        private readonly TextMeshProUGUI[] _hitLabels = new TextMeshProUGUI[Shown];
        private TextMeshProUGUI _more, _cellsLabel;
        private SignalIndex _index;
        private bool _cells;
        private readonly List<SignalHit> _found = new List<SignalHit>();
        private Action<int> _onAdd;
        private int _closedFrame = -1;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>What the search found, best first.</summary>
        public IReadOnlyList<SignalHit> Found => _found;

        /// <summary>What the search field holds.</summary>
        public string Query => _field.text;

        /// <summary>The list under <paramref name="root"/>, shut, adding a hit through <paramref name="onAdd"/>.</summary>
        public static WaveSignalPicker Build(RectTransform root, SchematicPalette palette, Action<int> onAdd)
        {
            // The blocker and the panel side by side under one root, so a click on the panel's own
            // ground is the panel's and never reaches the blocker that closes it.
            RectTransform container = UiTheme.Rect(RootName, root);
            UiTheme.Stretch(container);

            Image blockerImage = UiTheme.Panel_("blocker", container, new Color(0f, 0f, 0f, 0f));
            blockerImage.sprite = null;
            var blocker = blockerImage.gameObject.AddComponent<Button>();
            blocker.targetGraphic = blockerImage;
            blocker.transition = Selectable.Transition.None;
            UiTheme.Stretch(blockerImage.rectTransform);

            var picker = container.gameObject.AddComponent<WaveSignalPicker>();
            picker._onAdd = onAdd;
            blocker.onClick.AddListener(() => { picker.Close(); UiTheme.Defocus(); });

            Image panel = WorkbenchPanel.CanvasChip("panel", container, palette);
            picker._panel = panel.rectTransform;
            picker._panel.anchorMin = picker._panel.anchorMax = new Vector2(0f, 1f);
            picker._panel.pivot = new Vector2(0f, 1f);
            // A click on the panel is the panel's, not the blocker's behind it.
            panel.raycastTarget = true;

            TextMeshProUGUI title = UiTheme.Label("title", picker._panel, UiType.Caption, palette.TextDim, TextAlignmentOptions.MidlineLeft);
            WorkbenchPanel.TopLeft(title.rectTransform, new Vector2(Pad, Pad), new Vector2(200f, 28f));
            title.text = "ADD A SIGNAL";

            Button cells = WorkbenchPanel.CanvasButton(CellsButton, picker._panel, "CELLS: HIDDEN", out picker._cellsLabel, UiType.Body, palette);
            WorkbenchPanel.TopLeft(cells.GetComponent<RectTransform>(), new Vector2(Width - Pad - 170f, Pad - 2f), new Vector2(170f, 32f));
            cells.onClick.AddListener(() => { picker.ShowCells(!picker._cells); UiTheme.Defocus(); picker.FocusField(); });

            picker._field = WorkbenchPanel.CanvasField("search", picker._panel, palette, "s, h1., *out");
            WorkbenchPanel.TopLeft(picker._field.GetComponent<RectTransform>(), new Vector2(Pad, Pad + 40f), new Vector2(Width - 2f * Pad, 38f));
            picker._field.onValueChanged.AddListener(_ => picker.Refresh());
            picker._field.onSubmit.AddListener(_ => { picker.AddFirst(); picker.FocusField(); });
            picker._field.onEndEdit.AddListener(_ =>
            {
                if (picker._field.wasCanceled)
                    picker.Close();
            });

            float y = Pad + 90f;
            for (int i = 0; i < Shown; i++)
            {
                int at = i;
                Button hit = WorkbenchPanel.CanvasButton(HitButton + i.ToString(CultureInfo.InvariantCulture), picker._panel, "", out picker._hitLabels[i], UiType.Body, palette);
                picker._hitLabels[i].alignment = TextAlignmentOptions.MidlineLeft;
                picker._hitLabels[i].margin = new Vector4(12f, 0f, 12f, 0f);
                WorkbenchPanel.TopLeft(hit.GetComponent<RectTransform>(), new Vector2(Pad, y), new Vector2(Width - 2f * Pad, HitHeight));
                hit.onClick.AddListener(() => { picker.AddAt(at); picker.FocusField(); });
                picker._hits[i] = hit;
                y += HitHeight + Gap;
            }

            picker._more = UiTheme.Label("more", picker._panel, UiType.Caption, palette.Muted, TextAlignmentOptions.MidlineLeft);
            WorkbenchPanel.TopLeft(picker._more.rectTransform, new Vector2(Pad, y + 2f), new Vector2(Width - 2f * Pad, 24f));

            // As tall as it can be, for keeping it on the screen as it opens; Refresh fits it to its hits.
            picker._panel.sizeDelta = new Vector2(Width, y + 30f + Pad);
            picker.gameObject.SetActive(false);
            return picker;
        }

        /// <summary>
        /// Opens the list over <paramref name="index"/>'s signals with its corner at
        /// <paramref name="screenPoint"/>, kept on the screen, the field holding the keyboard.
        /// </summary>
        public void Open(SignalIndex index, Vector2 screenPoint)
        {
            _index = index;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            var root = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, null, out Vector2 local);
            Rect area = root.rect;
            var size = new Vector2(Width, Pad + 90f + Shown * (HitHeight + Gap) + 30f + Pad);
            float x = Mathf.Clamp(local.x - area.xMin, 0f, Mathf.Max(0f, area.width - size.x));
            float down = Mathf.Clamp(area.yMax - local.y, 0f, Mathf.Max(0f, area.height - size.y));
            _panel.anchoredPosition = new Vector2(x, -down);

            _field.SetTextWithoutNotify("");
            Refresh();
            FocusField();
            UiEscape.Opened(this);
        }

        /// <summary>Searches for <paramref name="query"/>, as typing it would.</summary>
        public void Search(string query) => _field.text = query;

        /// <summary>Whether a cell's pins are offered too.</summary>
        public void ShowCells(bool shown)
        {
            _cells = shown;
            _cellsLabel.text = shown ? "CELLS: SHOWN" : "CELLS: HIDDEN";
            Refresh();
        }

        /// <summary>Adds the best hit, if there is one: what Enter does.</summary>
        public void AddFirst() => AddAt(0);

        /// <summary>Shuts it.</summary>
        public void Close()
        {
            if (!IsOpen)
                return;
            _closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            UiTheme.Defocus();
        }

        private void AddAt(int hit)
        {
            if (hit < _found.Count)
                _onAdd?.Invoke(_found[hit].Signal);
        }

        private void FocusField()
        {
            if (IsOpen)
                UiTheme.Focus(_field);
        }

        private void Refresh()
        {
            _found.Clear();
            if (_index != null)
                _index.Search(_field.text, _found, _cells);

            for (int i = 0; i < Shown; i++)
            {
                bool shown = i < _found.Count;
                WorkbenchPanel.Show(_hits[i], shown);
                if (shown)
                    _hitLabels[i].text = _index.ShortName(_found[i].Signal);
            }

            int more = _found.Count - Shown;
            _more.text = more > 0 ? $"and {more.ToString(CultureInfo.InvariantCulture)} more: type to narrow them down"
                : _found.Count == 0 ? "nothing matches" : "";

            // As tall as what it shows: the hits, and the line under them when it says anything.
            int rows = Mathf.Min(_found.Count, Shown);
            float bottom = Pad + 90f + rows * (HitHeight + Gap);
            _more.rectTransform.anchoredPosition = new Vector2(Pad, -(bottom + 2f));
            _panel.sizeDelta = new Vector2(Width, bottom + (_more.text.Length > 0 ? 30f : 0f) + Pad);
        }

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void Update()
        {
            // While the field has the keyboard it answers Escape and Enter itself.
            Keyboard keyboard = UiText.Keyboard;
            if (!IsOpen || keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame && UiEscape.TryTake(this))
                Close();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                AddFirst();
        }
    }
}
