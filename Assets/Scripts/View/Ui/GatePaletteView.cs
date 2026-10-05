using System.Collections.Generic;
using UnityEngine;
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

            /// <summary>The block this row places, or null for a gate's row.</summary>
            public BlockDefinition Block;

            public Button Button;
            public Image Frame;
            public Image Icon;
            public TextMeshProUGUI Count;

            // What Count was last drawn from. Formatting it every frame handed TextMeshPro text
            // equal to what it already had while leaving garbage behind; it is redrawn on change.
            // A fresh row starts undrawn.
            public int DrawnPlaced = -1;
            public int DrawnTotal = -1;
            public bool DrawnUnlimited;
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

        // What the delay line was last drawn from, for the same reason each row keeps its own.
        private int _drawnSpent = -1;
        private int _drawnDelayBudget = -1;

        private readonly Vector3[] _corners = new Vector3[4];

        /// <summary>
        /// The parts list's right edge, in screen pixels, or zero before it is built.
        /// </summary>
        /// <remarks>
        /// Read by <see cref="CameraFit"/>, which frames the board clear of it. The canvas is a
        /// screen-space overlay, so world corners are screen pixels.
        ///
        /// Measured whether or not the list is showing. It steps aside for full-screen panels, and a
        /// board that re-framed every time one opened would jump about behind it.
        /// </remarks>
        public float ScreenRightEdge
        {
            get
            {
                if (_root == null)
                    return 0f;

                _root.GetWorldCorners(_corners);
                return _corners[2].x;
            }
        }

        /// <summary>
        /// Where a part's row sits, for the tutorial to point at. Null if this level does not offer
        /// that part, or before the palette has been built.
        /// </summary>
        /// <remarks>
        /// Read-only, and returns the transform rather than the Row: a caller that could reach the
        /// Button could also disable it, and nothing outside this component decides whether a part
        /// is available.
        /// </remarks>
        public RectTransform RectFor(GateKind kind)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Block == null && _rows[i].Kind == kind && _rows[i].Button != null)
                    return _rows[i].Button.GetComponent<RectTransform>();
            }

            return null;
        }

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
            {
                _session.LevelLoaded += Rebuild;

                // Free play's parts are the player's: a block made or deleted from the library
                // changes the list without changing the level.
                _session.LevelChanged += Rebuild;
            }
        }

        private void OnDisable()
        {
            if (_session != null)
            {
                _session.LevelLoaded -= Rebuild;
                _session.LevelChanged -= Rebuild;
            }
        }

        private void Update()
        {
            if (_session == null || !_session.IsLoaded)
                return;

            // Out of the way of a full-screen panel, which would otherwise have it lit beside it.
            bool shown = UiModal.HudVisible;
            UiTheme.SetShown(_root, shown);

            if (!shown)
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

            if (spent != _drawnSpent || total != _drawnDelayBudget)
            {
                _delay.text = $"DELAY  {spent} of {total}";
                _drawnSpent = spent;
                _drawnDelayBudget = total;
            }

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

            int count = level == null ? 0 : level.Budget.Count + level.BlockBudget.Count;

            if (count == 0)
                return;

            float rowHeight = RowHeightFor(count);
            _root.sizeDelta = new Vector2(_root.sizeDelta.x, ListHeight(count));

            for (int i = 0; i < level.Budget.Count; i++)
                _rows.Add(BuildRow(level.Budget[i], i, rowHeight));

            // Blocks under the gates, each by its name.
            for (int i = 0; i < level.BlockBudget.Count; i++)
            {
                BlockDefinition block = level.BlockNamed(level.BlockBudget[i].Block);

                if (block != null)
                    _rows.Add(BuildBlockRow(block, _rows.Count, rowHeight));
            }

            _delay = UiTheme.Label("delay", _root, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.Left);
            _drawnSpent = -1;
            _drawnDelayBudget = -1;
            UiTheme.Anchor(_delay.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(UiTheme.Gap, 0f), new Vector2(_root.sizeDelta.x, 20f));
        }

        private Row BuildRow(LevelBudgetEntry entry, int index, float height)
        {
            var row = new Row { Kind = entry.Kind };

            Lay(row, $"Part {entry.Kind}", index, height,
                NodeShapes.SpriteFor(entry.Kind), NodeShapes.ColourFor(entry.Kind), GatePalette.Label(entry.Kind));

            GateKind kind = entry.Kind;
            row.Button.onClick.AddListener(() => Choose(kind));

            // Dragging and clicking coexist: Unity only raises the drag handlers once the pointer has
            // moved past the drag threshold, so a press that stays put is still a click.
            var drag = row.Button.gameObject.AddComponent<PaletteDragSource>();
            drag.Configure(kind, _session, _pointer, _grid, _camera, _canvas);

            return row;
        }

        /// <summary>A block's row: its box for an icon, its name, and how many are left.</summary>
        private Row BuildBlockRow(BlockDefinition block, int index, float height)
        {
            var row = new Row { Block = block };

            // "Part block", so a block named like a gate cannot share a row's name with that gate's.
            Lay(row, $"Part block {block.Name}", index, height,
                NodeShapes.BlockSprite(), NodeShapes.BlockColour(), block.Name.ToUpperInvariant());

            row.Button.onClick.AddListener(() => ChooseBlock(block));

            var drag = row.Button.gameObject.AddComponent<PaletteDragSource>();
            drag.Configure(block, _session, _pointer, _grid, _camera, _canvas);

            return row;
        }

        /// <summary>Most rows a list draws at their full height; a longer one draws every row compact.</summary>
        public const int FullRows = 9;

        /// <summary>A compact row: the same icon, name and count, closer together.</summary>
        public const float CompactRow = 48f;

        /// <summary>Room under the rows for the delay budget's line.</summary>
        private const float DelayLine = 26f;

        /// <summary>How tall each row of a list this long is drawn.</summary>
        /// <remarks>
        /// Every row the same, never a mix: a list that changed size part-way down would read as two
        /// lists. Only free play with a library reaches the compact size; no level stocks more than
        /// nine parts.
        /// </remarks>
        public static float RowHeightFor(int rows) => rows > FullRows ? CompactRow : UiTheme.PaletteButton;

        /// <summary>How tall a list of this many rows is, its delay line included.</summary>
        public static float ListHeight(int rows) =>
            rows * (RowHeightFor(rows) + UiTheme.Gap) - UiTheme.Gap + DelayLine;

        /// <summary>A row's button, icon, name and count, laid out the same for a gate and a block.</summary>
        private void Lay(Row row, string name, int index, float height, Sprite icon, Color colour, string caption)
        {
            // A compact row keeps its name and count apart by drawing them nearer its edges.
            float inset = height < UiTheme.PaletteButton ? UiTheme.Gap * 0.5f : UiTheme.Gap;

            row.Button = UiTheme.RowButton(name, _root);   // this row draws its own contents

            var rect = row.Button.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -index * (height + UiTheme.Gap)),
                new Vector2(_root.sizeDelta.x, height));

            row.Frame = row.Button.GetComponent<Image>();

            // The icon is the same silhouette the gate will have on the board, so a player never has
            // to learn two visual languages for one gate.
            RectTransform iconRect = UiTheme.Rect("icon", rect);
            UiTheme.Anchor(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(UiTheme.Gap, 0f), new Vector2(height - inset * 2f, height - inset * 2f));

            row.Icon = iconRect.gameObject.AddComponent<Image>();
            row.Icon.sprite = icon;
            row.Icon.color = colour;
            row.Icon.raycastTarget = false;

            TextMeshProUGUI label = UiTheme.Label(
                "name", rect, UiType.Label, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(height, -inset), new Vector2(_root.sizeDelta.x - height - UiTheme.Gap, 22f));
            label.text = caption;

            row.Count = UiTheme.Label("count", rect, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(row.Count.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(height, inset), new Vector2(_root.sizeDelta.x - height - UiTheme.Gap, 20f));
        }

        private void Choose(GateKind kind)
        {
            if (_placement != null)
                _placement.TrySelect(kind);

            UiTheme.Defocus();
        }

        private void ChooseBlock(BlockDefinition block)
        {
            if (_placement != null)
                _placement.TrySelectBlock(block);

            UiTheme.Defocus();
        }

        private void Refresh(Row row)
        {
            bool isBlock = row.Block != null;
            int placed = isBlock ? _session.PlacedCountOfBlock(row.Block.Name) : _session.PlacedCountOf(row.Kind);
            int total = isBlock ? _session.Level.BlockBudgetFor(row.Block.Name) : _session.Level.BudgetFor(row.Kind);
            bool unlimited = total == LevelDefinition.UnlimitedBudget;
            int left = unlimited ? 1 : total - placed;

            // Free play still shows what is on the board -- the count is the useful half of this row
            // even when nothing is being spent -- but there is no "of" to put after it.
            if (placed != row.DrawnPlaced || total != row.DrawnTotal || unlimited != row.DrawnUnlimited)
            {
                row.Count.text = unlimited ? $"{placed} placed" : $"{left} of {total}";
                row.DrawnPlaced = placed;
                row.DrawnTotal = total;
                row.DrawnUnlimited = unlimited;
            }

            // Exhausted is dimmed but still selectable: the player may yet remove one and place it
            // elsewhere, which is exactly what LevelDefinition.Offers documents.
            row.Count.color = left > 0 ? UiTheme.TextDim : UiTheme.Bad;
            Color colour = isBlock ? NodeShapes.BlockColour() : NodeShapes.ColourFor(row.Kind);
            row.Icon.color = colour * (left > 0 ? 1f : 0.45f);

            // One part is in hand, a gate or a block, so a gate's row is chosen only while no block is.
            bool selected = _placement != null && (isBlock
                ? _placement.SelectedBlock != null && _placement.SelectedBlock.Name == row.Block.Name
                : _placement.SelectedBlock == null && _placement.Selected == row.Kind);
            row.Frame.color = UiTheme.SelectedFill(selected);

            // Editing only. During a run the parts list is a readout, not a control.
            row.Button.interactable = _session.CanEdit;
        }
    }
}
