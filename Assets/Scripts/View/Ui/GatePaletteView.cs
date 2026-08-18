using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The parts list, as clickable buttons down the left edge: each gate the level stocks, drawn
    /// with the silhouette it will actually place, and how many are left.
    /// </summary>
    /// <remarks>
    /// Rebuilt on <see cref="LevelSession.LevelLoaded"/> rather than kept in step, because the whole
    /// list changes with the level. Counts and the selection highlight are polled per frame, which is
    /// the house pattern -- every renderer already polls rather than subscribing.
    ///
    /// Clicking a row goes through <see cref="PlacementController.TrySelect"/>, the same entry point
    /// the number keys use, so the button and the key cannot drift apart.
    /// </remarks>
    public sealed class GatePaletteView : MonoBehaviour
    {
        private sealed class Row
        {
            public GateKind Kind;
            public Button Button;
            public Image Frame;
            public Image Icon;
            public TextMeshProUGUI Count;
        }

        [SerializeField] private LevelSession _session;
        [SerializeField] private PlacementController _placement;

        [Tooltip("Canvas the palette is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        [Tooltip("Handed to each row so a drag out of the menu owns the pointer.")]
        [SerializeField] private PointerGate _pointer;

        [SerializeField] private PlacementGrid _grid;
        [SerializeField] private Camera _camera;

        private readonly List<Row> _rows = new List<Row>();
        private RectTransform _root;
        private TextMeshProUGUI _delay;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_placement == null) _placement = FindFirstObjectByType<PlacementController>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
            if (_pointer == null) _pointer = FindFirstObjectByType<PointerGate>();
            if (_grid == null) _grid = FindFirstObjectByType<PlacementGrid>();
            if (_camera == null) _camera = Camera.main;
        }

        // OnEnable, not Start: the first level is loaded during LevelSession's own Start, and Unity
        // has run every OnEnable by then but not every Start.
        private void OnEnable()
        {
            if (_session != null)
                _session.LevelLoaded += Rebuild;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= Rebuild;
        }

        private void Update()
        {
            if (_session == null || !_session.IsLoaded)
                return;

            for (int i = 0; i < _rows.Count; i++)
                Refresh(_rows[i]);

            RefreshDelay();
        }

        /// <summary>
        /// How much of the delay budget is spent.
        /// </summary>
        /// <remarks>
        /// Belongs with the parts list because it is a part: on a level like the-slow-lane the ticks
        /// are as much a resource as the gates, and its budget is exactly the solution with nothing
        /// spare. A player who cannot see the spend cannot tell a wrong guess from a wrong circuit.
        ///
        /// Hidden on levels that set no budget, where the number would only be noise.
        /// </remarks>
        private void RefreshDelay()
        {
            if (_delay == null)
                return;

            bool budgeted = _session.Level != null && _session.Level.HasDelayBudget;

            if (_delay.gameObject.activeSelf != budgeted)
                _delay.gameObject.SetActive(budgeted);

            if (!budgeted)
                return;

            int spent = _session.SpentDelay;
            int total = _session.Level.DelayBudget;

            _delay.text = $"DELAY  {spent} of {total}";
            _delay.color = spent < total ? UiTheme.TextDim : UiTheme.Bad;
        }

        /// <summary>Throws the old rows away and builds the new level's parts list.</summary>
        private void Rebuild(LevelDefinition level)
        {
            if (_canvas == null)
                return;

            if (_root == null)
            {
                _root = UiTheme.Rect("Palette", _canvas.transform);
                UiTheme.Anchor(_root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(UiTheme.Margin, 0f), new Vector2(UiTheme.PaletteButton + 76f, 0f));
            }

            for (int i = _root.childCount - 1; i >= 0; i--)
                Destroy(_root.GetChild(i).gameObject);

            _rows.Clear();

            if (level == null || level.Budget.Count == 0)
                return;

            float rowHeight = UiTheme.PaletteButton;
            float total = level.Budget.Count * (rowHeight + UiTheme.Gap) - UiTheme.Gap;
            _root.sizeDelta = new Vector2(_root.sizeDelta.x, total + 26f);

            for (int i = 0; i < level.Budget.Count; i++)
                _rows.Add(BuildRow(level.Budget[i], i, rowHeight));

            _delay = UiTheme.Label("delay", _root, 13f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(_delay.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(UiTheme.Gap, 0f), new Vector2(_root.sizeDelta.x, 20f));
        }

        private Row BuildRow(LevelBudgetEntry entry, int index, float height)
        {
            var row = new Row { Kind = entry.Kind };

            row.Button = UiTheme.Button_($"Part {entry.Kind}", _root, string.Empty, out TextMeshProUGUI caption);
            Destroy(caption.gameObject);   // this row draws its own contents

            var rect = row.Button.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -index * (height + UiTheme.Gap)),
                new Vector2(_root.sizeDelta.x, height));

            row.Frame = row.Button.GetComponent<Image>();

            // The icon is the same silhouette the gate will have on the board, so a player never has
            // to learn two visual languages for one gate.
            RectTransform iconRect = UiTheme.Rect("icon", rect);
            UiTheme.Anchor(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(UiTheme.Gap, 0f), new Vector2(height - UiTheme.Gap * 2f, height - UiTheme.Gap * 2f));

            row.Icon = iconRect.gameObject.AddComponent<Image>();
            row.Icon.sprite = NodeShapes.SpriteFor(entry.Kind);
            row.Icon.color = NodeShapes.ColourFor(entry.Kind);
            row.Icon.raycastTarget = false;

            TextMeshProUGUI label = UiTheme.Label(
                "name", rect, 15f, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(height, -UiTheme.Gap), new Vector2(_root.sizeDelta.x - height - UiTheme.Gap, 22f));
            label.text = GatePalette.Label(entry.Kind);

            row.Count = UiTheme.Label("count", rect, 13f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(row.Count.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(height, UiTheme.Gap), new Vector2(_root.sizeDelta.x - height - UiTheme.Gap, 20f));

            GateKind kind = entry.Kind;
            row.Button.onClick.AddListener(() => Choose(kind));

            // Dragging and clicking coexist: Unity only raises the drag handlers once the pointer has
            // moved past the drag threshold, so a press that stays put is still a click.
            var drag = row.Button.gameObject.AddComponent<PaletteDragSource>();
            drag.Configure(kind, _session, _pointer, _grid, _camera, _canvas);

            return row;
        }

        private void Choose(GateKind kind)
        {
            if (_placement != null)
                _placement.TrySelect(kind);

            // Drop focus straight away. A Button that keeps it consumes Space and Enter, and this
            // game binds both -- the player would press Space expecting a pause and re-click Run.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void Refresh(Row row)
        {
            int placed = _session.PlacedCountOf(row.Kind);
            int total = _session.Level.BudgetFor(row.Kind);
            int left = total - placed;

            row.Count.text = $"{left} of {total}";

            // Exhausted is dimmed but still selectable: the player may yet remove one and place it
            // elsewhere, which is exactly what LevelDefinition.Offers documents.
            row.Count.color = left > 0 ? UiTheme.TextDim : UiTheme.Bad;
            row.Icon.color = NodeShapes.ColourFor(row.Kind) * (left > 0 ? 1f : 0.45f);

            bool selected = _placement != null && _placement.Selected == row.Kind;
            row.Frame.color = selected ? UiTheme.Accent * 0.55f : UiTheme.PanelEdge;

            // Editing only. During a run the parts list is a readout, not a control.
            row.Button.interactable = _session.CanEdit;
        }
    }
}
