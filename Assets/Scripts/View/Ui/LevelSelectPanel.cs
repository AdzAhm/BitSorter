using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The level list, on Escape: every level in play order, with a tick against the ones solved.
    /// </summary>
    /// <remarks>
    /// An overlay rather than a second scene. <see cref="LevelSession.LoadLevel"/> already switches
    /// level at runtime, and the play scene is generated from code -- a second scene would mean a
    /// second thing for the builder to construct and keep in step, for no gain.
    ///
    /// Built once, then refreshed. The list of levels cannot change while the game runs, so only the
    /// completion marks and which row is current need updating.
    /// </remarks>
    public sealed class LevelSelectPanel : MonoBehaviour
    {
        private sealed class Row
        {
            public string FileName;
            public Button Button;
            public Image Frame;
            public TextMeshProUGUI Tick;
            public TextMeshProUGUI Label;
        }

        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private readonly List<Row> _rows = new List<Row>();
        private RectTransform _root;
        private bool _shown;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null || _session == null)
                return;

            Build();
            Show(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Show(!_shown);

            if (_shown)
                Refresh();
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            IReadOnlyList<LevelEntry> catalogue = _session.Catalogue;

            // A full-screen scrim, so the board behind reads as suspended rather than still live, and
            // so a stray click cannot reach it.
            Image scrim = UiTheme.Panel_("Level select", _canvas.transform, new Color(0f, 0f, 0f, 0.78f));
            _root = scrim.GetComponent<RectTransform>();
            UiTheme.Stretch(_root);

            TextMeshProUGUI title = UiTheme.Label(
                "title", _root, 26f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -48f), new Vector2(600f, 34f));
            title.text = "LEVELS";

            TextMeshProUGUI help = UiTheme.Label(
                "help", _root, 13f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(600f, 20f));
            help.text = "escape to close    Q / E also change level";

            const float rowHeight = 42f;
            const float gap = 6f;

            float total = catalogue.Count * (rowHeight + gap) - gap;

            RectTransform list = UiTheme.Rect("list", _root);
            UiTheme.Anchor(list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, total));

            for (int i = 0; i < catalogue.Count; i++)
                _rows.Add(BuildRow(catalogue[i], list, i, rowHeight, gap));
        }

        private Row BuildRow(LevelEntry entry, RectTransform list, int index, float height, float gap)
        {
            var row = new Row { FileName = entry.FileName };

            row.Button = UiTheme.Button_($"Level {entry.FileName}", list, string.Empty,
                out TextMeshProUGUI caption);
            Destroy(caption.gameObject);

            var rect = row.Button.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -index * (height + gap)), new Vector2(520f, height));

            row.Frame = row.Button.GetComponent<Image>();

            row.Tick = UiTheme.Label("tick", rect, 18f, UiTheme.Good, TextAlignmentOptions.Center);
            UiTheme.Anchor(row.Tick.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(14f, 0f), new Vector2(28f, height));

            row.Label = UiTheme.Label("name", rect, 17f, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Anchor(row.Label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(52f, 0f), new Vector2(440f, height));
            row.Label.text = entry.DisplayName;

            string file = entry.FileName;
            row.Button.onClick.AddListener(() => Choose(file));

            return row;
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        private void Show(bool visible)
        {
            _shown = visible;

            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);

            if (visible)
                Refresh();
        }

        private void Refresh()
        {
            string current = _session.LevelName;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];

                bool done = _progress != null && _progress.IsComplete(row.FileName);
                bool here = row.FileName == current;

                row.Tick.text = done ? "✓" : string.Empty;

                // Current level highlighted, solved ones dimmed but still selectable -- replaying is
                // how a player improves a circuit, and nothing here should discourage it.
                row.Frame.color = here ? UiTheme.Accent * 0.55f : UiTheme.PanelEdge;
                row.Label.color = here ? UiTheme.Text : (done ? UiTheme.TextDim : UiTheme.Text);
            }
        }

        private void Choose(string fileName)
        {
            _session.LoadLevel(fileName);
            Show(false);

            // Drop focus, or the clicked row keeps it and swallows Space and Enter.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
