using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The "!" button, and what it opens: the level's truth table and its hint.
    /// </summary>
    /// <remarks>
    /// Exists because four-corners was unsolvable in practice. Its goal had to describe an
    /// eight-row function in prose -- "a 1 on every row except A=0 B=1 C=1 and A=1 B=0 C=0" -- which
    /// nobody can hold in their head while wiring. The table was always in the level data; it just
    /// had nowhere to be shown.
    ///
    /// Behind a button rather than always open, because on a two-input level the table is four rows
    /// the player does not need, and because a hint that is always visible stops being something you
    /// choose to read.
    /// </remarks>
    public sealed class HelpPanel : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private RectTransform _panel;
        private TextMeshProUGUI _table;
        private TextMeshProUGUI _hint;

        /// <summary>The rule and heading that mark where the table stops and the nudge starts.</summary>
        /// <remarks>
        /// Both are hidden on a level with no table, where there is nothing on the other side of the
        /// line to divide the hint from.
        /// </remarks>
        private Image _divider;
        private TextMeshProUGUI _hintHeading;
        private Image _badge;
        private bool _shown;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void OnEnable()
        {
            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;
        }

        private void OnDisable()
        {
            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            BuildBadge();
            BuildPanel();

            Show(false);
            Fill(_session != null ? _session.Level : null);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;

            // H as well as the button. A player mid-wire should not have to find a target. Suppressed
            // while a full-screen panel is up, where the help would open behind it.
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame && !UiModal.AnyOpen)
                Show(!_shown);
        }

        private void OnLevelLoaded(LevelDefinition level)
        {
            Fill(level);
            Show(false);   // a new level starts closed, whatever the last one was left as
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void BuildBadge()
        {
            _badge = UiTheme.Panel_("Help badge", _canvas.transform, UiTheme.Accent * 0.5f);
            var rect = _badge.GetComponent<RectTransform>();

            // Top right, clear of the status banner and above the bits-lost meter's corner.
            UiTheme.Anchor(rect, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -(UiTheme.Margin + 56f)), new Vector2(38f, 38f));

            _badge.sprite = ProceduralSprites.Circle();

            var button = _badge.gameObject.AddComponent<Button>();
            button.targetGraphic = _badge;

            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            button.onClick.AddListener(() =>
            {
                Show(!_shown);

                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            });

            // A question mark, not an exclamation mark. "!" is what this game uses for things that
            // have gone wrong -- the refusal toast, the bits-lost meter, the scorch marks -- so a
            // permanent one in the corner reads as a warning the player cannot clear.
            TextMeshProUGUI mark = UiTheme.Label(
                "mark", rect, 22f, Color.white, TextAlignmentOptions.Center);
            UiTheme.Stretch(mark.rectTransform);
            mark.text = "?";

            // The badge is round and unlabelled, which is not obviously a button. The key beside it
            // says both that it opens something and how to open it without aiming at all.
            TextMeshProUGUI key = UiTheme.Label(
                "key", rect, 12f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(key.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(60f, 16f));
            key.text = "H";
        }

        private void BuildPanel()
        {
            Image background = UiTheme.Panel_("Help", _canvas.transform, UiTheme.Panel);
            _panel = background.GetComponent<RectTransform>();
            UiTheme.Anchor(_panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -(UiTheme.Margin + 100f)), new Vector2(330f, 380f));

            background.raycastTarget = false;

            TextMeshProUGUI title = UiTheme.Label(
                "title", _panel, 17f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(300f, 24f));
            title.text = "WHAT THE BINS WANT";

            // Monospaced, or the columns do not line up and the table is worse than no table.
            _table = UiTheme.Label("table", _panel, 18f, UiTheme.Accent, TextAlignmentOptions.Top);
            UiTheme.Anchor(_table.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(300f, 260f));

            // The hint block is built from the bottom edge up, so it keeps its shape when Fill
            // resizes the panel for a taller table. Before this the hint sat straight under the
            // table in the same weight and read as more rows of it -- two different kinds of thing
            // with nothing between them saying so.
            _hint = UiTheme.Label("hint", _panel, 15f, UiTheme.TextDim, TextAlignmentOptions.Top);
            UiTheme.Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 12f), new Vector2(300f, 72f));
            _hint.textWrappingMode = TextWrappingModes.Normal;

            _hintHeading = UiTheme.Label(
                "hint heading", _panel, 13f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(_hintHeading.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 88f), new Vector2(300f, 18f));
            _hintHeading.text = "A NUDGE";

            Image rule = UiTheme.Panel_("divider", _panel, UiTheme.PanelEdge);
            _divider = rule;
            _divider.sprite = null;   // a plain hairline, not the rounded panel silhouette
            _divider.raycastTarget = false;
            UiTheme.Anchor(rule.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 112f), new Vector2(280f, 1f));
        }

        // -----------------------------------------------------------------
        // Contents
        // -----------------------------------------------------------------

        private void Fill(LevelDefinition level)
        {
            if (_table == null)
                return;

            string table = TruthTable.Format(level);
            bool hasTable = !string.IsNullOrEmpty(table);

            // Empty for a level that grades nothing -- free play has no expectations, so there is no
            // table to draw. Rendering the wrapper anyway left a blank block sized for a table that
            // was never coming, which reads as something having failed to load.
            //
            // mspace rather than a monospaced font: the project ships one font, and forcing an
            // advance width is enough to make columns line up without adding another asset.
            _table.text = hasTable ? $"<mspace=0.62em>{table}</mspace>" : string.Empty;

            _hint.text = level != null ? level.Hint : string.Empty;

            // Nothing to divide the hint from on a level that grades nothing, so the rule and its
            // heading go with the table rather than floating above an empty space.
            if (_divider != null)
                _divider.enabled = hasTable;

            if (_hintHeading != null)
                _hintHeading.gameObject.SetActive(hasTable);

            // Taller tables need a taller panel. Eight vectors plus a header and rule is ten lines,
            // and the per-line figure tracks the table's font size rather than being guessed. With no
            // table the panel shrinks to the hint rather than keeping the space open.
            int lines = hasTable ? level.VectorCount + 2 : 0;
            float height = (hasTable ? 160f : 130f) + lines * 24f;
            _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, height);
        }

        private void Show(bool visible)
        {
            _shown = visible;

            if (_panel != null && _panel.gameObject.activeSelf != visible)
                _panel.gameObject.SetActive(visible);

            if (visible)
                UiTheme.BringToFront(_panel);
        }
    }
}
