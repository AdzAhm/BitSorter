using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The + SIGNAL list (Part 9b-2b): a search over every signal the design has
    /// (<see cref="SignalIndex.Search"/>), its best hits as buttons, and each hit clicked added to the
    /// waveform. It stays up to add another; Enter adds the first hit; a click off it, or Escape, closes it.
    /// </summary>
    /// <remarks>
    /// In the shell since Part 11b-3 (<see cref="ShellPopup"/>), over the docked panels. Its search field keeps the
    /// keyboard after Enter, for the next search (<see cref="ShellTheme.Field"/>'s <c>releaseOnEnter</c>), and answers
    /// Escape itself; let go of, this reads Escape and Enter, and holds Escape (<see cref="IHoldsEscape"/>) so one press
    /// closes the list and leaves the workbench up. A library cell's pins are left out unless CELLS is on. Part 11c's
    /// hierarchy draws <see cref="SignalIndex.Root"/> as a tree.
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

        private const float Width = 440f, Pad = 12f, HitHeight = 30f, Gap = 4f, HitsTop = Pad + 90f;

        private ShellPopup _popup;
        private TextField _field;
        private readonly ShellButton[] _hits = new ShellButton[Shown];
        private Label _more;
        private ShellButton _cellsButton;
        private SignalIndex _index;
        private bool _cells;
        private readonly List<SignalHit> _found = new List<SignalHit>();
        private Action<int> _onAdd;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => _popup != null && _popup.IsOpen;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || (_popup != null && _popup.ClosedFrame == Time.frameCount);

        /// <summary>What the search found, best first.</summary>
        public IReadOnlyList<SignalHit> Found => _found;

        /// <summary>What the search field holds.</summary>
        public string Query => _field.value;

        /// <summary>The list on <paramref name="shell"/>, shut, adding a hit through <paramref name="onAdd"/>; its component on a child of <paramref name="owner"/>.</summary>
        public static WaveSignalPicker Build(ShellPanel shell, Transform owner, SchematicPalette palette, Action<int> onAdd)
        {
            var host = new GameObject(RootName);
            host.transform.SetParent(owner, false);
            var picker = host.AddComponent<WaveSignalPicker>();
            picker._onAdd = onAdd;
            picker._popup = new ShellPopup(shell, RootName, palette);
            VisualElement panel = picker._popup.Panel;

            Label title = ShellTheme.Label("title", "ADD A SIGNAL", UiType.Caption, palette.TextDim);
            ShellTheme.Place(title, Pad, Pad, 200f, 28f);
            panel.Add(title);

            picker._cellsButton = ShellTheme.Button(CellsButton, "CELLS: HIDDEN", palette, () =>
            {
                picker.ShowCells(!picker._cells);
                picker.FocusField();
            });
            ShellTheme.Place(picker._cellsButton, Width - Pad - 170f, Pad - 2f, 170f, 32f);
            panel.Add(picker._cellsButton);

            picker._field = ShellTheme.Field("search", "s, h1., *out", palette, _ => picker.AddFirst(), submitOnLeave: false,
                releaseOnEnter: false);
            ShellTheme.Place(picker._field, Pad, Pad + 40f, Width - 2f * Pad, 38f);
            picker._field.RegisterValueChangedCallback(_ => picker.Refresh());
            picker._field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                    picker.Close();
            });
            panel.Add(picker._field);

            float y = HitsTop;
            for (int i = 0; i < Shown; i++)
            {
                int at = i;
                ShellButton hit = ShellTheme.Button(HitButton + i.ToString(CultureInfo.InvariantCulture), "", palette, () =>
                {
                    picker.AddAt(at);
                    picker.FocusField();
                });
                hit.style.unityTextAlign = TextAnchor.MiddleLeft;
                hit.style.paddingLeft = hit.style.paddingRight = 12f;
                ShellTheme.Ellipsis(hit);
                ShellTheme.Place(hit, Pad, y, Width - 2f * Pad, HitHeight);
                panel.Add(hit);
                picker._hits[i] = hit;
                y += HitHeight + Gap;
            }

            picker._more = ShellTheme.Label("more", "", UiType.Caption, palette.Muted);
            panel.Add(picker._more);
            return picker;
        }

        /// <summary>
        /// Opens the list over <paramref name="index"/>'s signals with its corner at
        /// <paramref name="screenPoint"/>, kept on the screen, the field holding the keyboard.
        /// </summary>
        public void Open(SignalIndex index, Vector2 screenPoint)
        {
            _index = index;

            // As tall as it can be, for keeping it on the screen as it opens; Refresh fits it to its hits.
            _popup.Open(screenPoint, new Vector2(Width, HitsTop + Shown * (HitHeight + Gap) + 30f + Pad));
            _field.SetValueWithoutNotify("");
            Refresh();
            FocusField();
            UiEscape.Opened(this);
        }

        /// <summary>Searches for <paramref name="query"/>, as typing it would.</summary>
        public void Search(string query) => _field.value = query;

        /// <summary>Whether a cell's pins are offered too.</summary>
        public void ShowCells(bool shown)
        {
            _cells = shown;
            _cellsButton.text = shown ? "CELLS: SHOWN" : "CELLS: HIDDEN";
            Refresh();
        }

        /// <summary>Adds the best hit, if there is one: what Enter does.</summary>
        public void AddFirst() => AddAt(0);

        /// <summary>Shuts it.</summary>
        public void Close() => _popup?.Close();

        private void AddAt(int hit)
        {
            if (hit < _found.Count)
                _onAdd?.Invoke(_found[hit].Signal);
        }

        private void FocusField()
        {
            if (IsOpen)
                _field.Focus();
        }

        private void Refresh()
        {
            _found.Clear();
            if (_index != null)
                _index.Search(_field.value, _found, _cells);

            for (int i = 0; i < Shown; i++)
            {
                bool shown = i < _found.Count;
                _hits[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (shown)
                    _hits[i].text = _index.ShortName(_found[i].Signal);
            }

            int more = _found.Count - Shown;
            _more.text = more > 0 ? $"and {more.ToString(CultureInfo.InvariantCulture)} more: type to narrow them down"
                : _found.Count == 0 ? "nothing matches" : "";

            // As tall as what it shows: the hits, and the line under them when it says anything.
            int rows = Mathf.Min(_found.Count, Shown);
            float bottom = HitsTop + rows * (HitHeight + Gap);
            ShellTheme.Place(_more, Pad, bottom + 2f, Width - 2f * Pad, 24f);
            if (IsOpen)
                _popup.Resize(new Vector2(Width, bottom + (_more.text.Length > 0 ? 30f : 0f) + Pad));
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
