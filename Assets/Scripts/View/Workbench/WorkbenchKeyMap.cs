using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// The keyboard map (Part 11d): every key the workbench answers, under RUN, CIRCUIT, WAVES and VIEW, and the mouse's
    /// gestures on the circuit and the waves -- <see cref="KeyMap"/>'s, drawn in three columns, each key in its cap.
    /// </summary>
    /// <remarks>
    /// A popup of the shell (<see cref="ShellPopup"/>), in the middle of the window: <c>?</c> opens and shuts it, and a
    /// click off it or Escape shuts it before Escape closes the workbench (<see cref="IHoldsEscape"/>). It replaced the two
    /// lines of help along the bottom, which named every gesture at once and no key.
    /// </remarks>
    public sealed class WorkbenchKeyMap : MonoBehaviour, IHoldsEscape
    {
        /// <summary>The object's name, which the tests look for.</summary>
        public const string RootName = "Workbench keys";

        private const float Pad = 22f, TitleHeight = 44f, HeadingHeight = 34f, RowHeight = 26f, CapWidth = 150f, ColumnGap = 28f,
            MostWidth = 1800f;

        /// <summary>Which headings each column holds, by their place in <see cref="KeyMap.Groups"/>, as <see cref="KeyMap.Columns"/> names them.</summary>
        private static readonly int[][] Columns = PlacesOf(KeyMap.Columns);

        private static int[][] PlacesOf(System.Collections.Generic.IReadOnlyList<System.Collections.Generic.IReadOnlyList<string>> titles)
        {
            var columns = new int[titles.Count][];
            for (int c = 0; c < titles.Count; c++)
            {
                columns[c] = new int[titles[c].Count];
                for (int i = 0; i < titles[c].Count; i++)
                {
                    columns[c][i] = -1;
                    for (int g = 0; g < KeyMap.Groups.Count; g++)
                        if (KeyMap.Groups[g].Title == titles[c][i])
                            columns[c][i] = g;
                }
            }

            return columns;
        }

        private ShellPopup _popup;
        private VisualElement[] _columns;
        private float _height;

        /// <summary>Whether it is up.</summary>
        public bool IsOpen => _popup != null && _popup.IsOpen;

        /// <inheritdoc/>
        public bool HoldsEscapeNow => IsOpen || (_popup != null && _popup.ClosedFrame == Time.frameCount);

        /// <summary>The map on <paramref name="shell"/>, shut; its component on a child of <paramref name="owner"/>.</summary>
        public static WorkbenchKeyMap Build(ShellPanel shell, Transform owner, SchematicPalette palette)
        {
            var host = new GameObject(RootName);
            host.transform.SetParent(owner, false);
            var map = host.AddComponent<WorkbenchKeyMap>();
            map._popup = new ShellPopup(shell, RootName, palette);
            VisualElement panel = map._popup.Panel;

            Label title = ShellTheme.Label("title", "KEYS AND GESTURES", UiType.Label, palette.Text, bold: true);
            ShellTheme.Place(title, Pad, 12f, 400f, 30f);
            panel.Add(title);
            Label hint = ShellTheme.Label("hint", "? or Escape closes this", UiType.Caption, palette.TextDim, TextAnchor.MiddleRight);
            ShellTheme.PlaceRight(hint, Pad, 12f, 300f, 30f);
            panel.Add(hint);

            map._columns = new VisualElement[Columns.Length];
            float tallest = 0f;
            for (int c = 0; c < Columns.Length; c++)
            {
                var column = new VisualElement { name = "column " + c, pickingMode = PickingMode.Ignore };
                column.style.position = Position.Absolute;
                float y = 0f;
                foreach (int g in Columns[c])
                {
                    KeyMapGroup group = KeyMap.Groups[g];
                    Label heading = ShellTheme.Label("heading", group.Title, UiType.Caption, palette.TextDim, bold: true);
                    ShellTheme.Place(heading, 0f, y + 8f, -1f, 22f);
                    column.Add(heading);
                    y += HeadingHeight;

                    foreach (KeyMapEntry entry in group.Entries)
                    {
                        VisualElement cap = ShellTheme.KeyCap(entry.Cap, palette, always: true);
                        cap.style.top = y + (RowHeight - UiTheme.ControlsHeight) * 0.5f;
                        cap.style.left = 0f;
                        column.Add(cap);
                        Label says = ShellTheme.Label("says", entry.Says, UiType.Caption, palette.Text);
                        ShellTheme.Place(says, CapWidth, y, -1f, RowHeight);
                        says.style.right = 0f;
                        ShellTheme.Ellipsis(says);
                        column.Add(says);
                        y += RowHeight;
                    }

                    y += 6f;
                }

                tallest = Mathf.Max(tallest, y);
                panel.Add(column);
                map._columns[c] = column;
            }

            map._height = TitleHeight + tallest + Pad;
            return map;
        }

        /// <summary>Opens it in the middle of the window, or shuts it if it is up: what <c>?</c> does.</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            float scale = _popup.Layer.panel != null ? _popup.Layer.panel.scaledPixelsPerPoint : 1f;
            float window = Screen.width / scale, tall = Screen.height / scale;
            float width = Mathf.Min(MostWidth, window - 2f * UiTheme.Margin);
            float column = (width - 2f * Pad - (Columns.Length - 1) * ColumnGap) / Columns.Length;
            for (int c = 0; c < _columns.Length; c++)
                ShellTheme.Place(_columns[c], Pad + c * (column + ColumnGap), TitleHeight, column, _height - TitleHeight - Pad);

            float x = (window - width) * 0.5f, y = Mathf.Max(0f, (tall - _height) * 0.5f);
            _popup.Open(new Vector2(x * scale, Screen.height - y * scale), new Vector2(width, _height));
            UiEscape.Opened(this);
        }

        /// <summary>Shuts it.</summary>
        public void Close() => _popup?.Close();

        private void Awake() => UiEscape.Join(this);

        private void OnDestroy() => UiEscape.Leave(this);

        private void Update()
        {
            Keyboard keyboard = UiText.Keyboard;
            if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && UiEscape.TryTake(this))
                Close();
        }
    }
}
