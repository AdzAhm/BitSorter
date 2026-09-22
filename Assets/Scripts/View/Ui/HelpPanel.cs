using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// The "?" button, and what it opens: the level's truth table and its hint.
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

        /// <summary>
        /// The hint, and the room it needs.
        /// </summary>
        /// <remarks>
        /// It was 15pt in <see cref="UiTheme.TextDim"/>, the dimmest colour in the interface, on
        /// the one line a player went out of their way to ask for. A nudge nobody can read is a
        /// button not worth pressing -- the same mistake as printing the hint twice, arrived at
        /// from the other side.
        ///
        /// Each row is stated from the one below it, so making the hint taller moves the heading
        /// and the divider instead of running into them.
        /// </remarks>
        public const float HintFontSize = 17f;

        /// <inheritdoc cref="HintFontSize"/>
        private const float HintBottom = 12f;

        /// <summary>
        /// Room for the hint, and the width it wraps in.
        /// </summary>
        /// <remarks>
        /// Public because nothing was checking it. The banner's goal is measured against the box
        /// that holds it and a level whose goal will not fit is a failing test; the hint had the
        /// same shape of problem and none of the guard, so a hint one line longer than whoever
        /// wrote this box expected simply printed past the bottom of the panel.
        /// </remarks>
        public const float HintHeight = 92f;

        /// <inheritdoc cref="HintHeight"/>
        public const float HintWidth = 300f;

        /// <inheritdoc cref="HintHeight"/>
        public const float HintLineSpacing = 6f;

        /// <inheritdoc cref="HintFontSize"/>
        private const float HeadingHeight = 18f;

        /// <inheritdoc cref="HintFontSize"/>
        private const float HeadingBottom = HintBottom + HintHeight + 4f;

        /// <inheritdoc cref="HintFontSize"/>
        private const float DividerBottom = HeadingBottom + HeadingHeight + 6f;

        /// <summary>The title's row at the top, and the gap under it.</summary>
        private const float TitleRoom = 46f;

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
        /// <summary>
        /// The character on the badge.
        /// </summary>
        /// <remarks>
        /// A question mark, not an exclamation mark. "!" is what this game uses for things that have
        /// gone wrong -- the refusal toast, the bits-lost meter, the scorch marks -- so a permanent
        /// one in the corner reads as a warning the player cannot clear.
        ///
        /// A constant rather than a literal because level text tells the player to press it, and
        /// those two drifted apart the last time this changed: the badge became "?" and
        /// `four-corners` went on saying "!" for a release. `PlayerTextTests` now reads this and
        /// checks every button a player is told to press against it.
        /// </remarks>
        public const string BadgeGlyph = "?";

        /// <summary>
        /// Width of one character of the table, in canvas units.
        /// </summary>
        /// <remarks>
        /// The table is rendered at font size 18 inside an <c>mspace</c> tag of 0.62em, so every
        /// character advances by exactly 18 * 0.62. Stated as arithmetic on those two numbers
        /// rather than as the product, so changing the font size cannot leave this behind.
        /// </remarks>
        private const float TableFontSize = 18f;
        private const float TableMonospace = 0.62f;
        private const float TableCharacterWidth = TableFontSize * TableMonospace;

        /// <summary>Gap between the table and the panel edge, on each side.</summary>
        private const float TablePadding = 15f;

        /// <summary>Never narrower than this, so the hint below still has a column to wrap in.</summary>
        /// <remarks>
        /// Stated in <see cref="UiTheme"/>, because it is half of whether this panel and free
        /// play's setup panel fit on the same column.
        /// </remarks>
        private const float MinimumWidth = UiTheme.HelpMinimumWidth;

        private Image _badge;
        private bool _shown;

        /// <summary>
        /// Whether free play's setup panel is docked on this level, and therefore holds the right
        /// edge that this panel would otherwise take.
        /// </summary>
        /// <remarks>
        /// Asked of the session rather than of <see cref="SandboxPanel"/>, which is the same
        /// question its own <c>IsOpen</c> asks -- two readouts of one fact, neither owning the
        /// other.
        /// </remarks>
        private bool BesideSetupPanel =>
            _session != null && _session.LevelName == SandboxLevel.Key;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void OnEnable()
        {
            if (_session != null)
            {
                _session.LevelLoaded += OnLevelLoaded;

                // Free play's streams are the player's to edit, and the truth table is built from
                // them. Filled without closing: the setup is edited with this panel open.
                _session.LevelChanged += Fill;
            }
        }

        private void OnDisable()
        {
            if (_session != null)
            {
                _session.LevelLoaded -= OnLevelLoaded;
                _session.LevelChanged -= Fill;
            }
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
            // The badge, and the panel if it was open, step aside for a full-screen panel and come
            // back as they were when it closes.
            bool hud = UiModal.HudVisible;
            UiTheme.SetShown(_badge, hud);
            UiTheme.SetShown(_panel, _shown && hud);

            Keyboard keyboard = Keyboard.current;

            // H as well as the button. A player mid-wire should not have to find a target. Suppressed
            // while a full-screen panel is up, where the help would open behind it.
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame && !UiModal.OpenOrJustClosed)
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
                new Vector2(-UiTheme.Margin, -UiTheme.BadgeRow),
                new Vector2(UiTheme.BadgeSize, UiTheme.BadgeSize));

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

            TextMeshProUGUI mark = UiTheme.Label(
                "mark", rect, 22f, Color.white, TextAlignmentOptions.Center);
            UiTheme.Stretch(mark.rectTransform);
            mark.text = BadgeGlyph;

            // The badge is round and unlabelled, which is not obviously a button. The key beside it
            // says both that it opens something and how to open it without aiming at all.
            TextMeshProUGUI key = UiTheme.Label(
                "key", rect, 12f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(key.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(60f, UiTheme.BadgeKeyHeight - 4f));
            key.text = "H";
        }

        private void BuildPanel()
        {
            Image background = UiTheme.Panel_("Help", _canvas.transform, UiTheme.Panel);
            _panel = background.GetComponent<RectTransform>();
            UiTheme.Anchor(_panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.HelpRight(BesideSetupPanel), -UiTheme.HelpTop),
                new Vector2(UiTheme.HelpMinimumWidth, 380f));

            background.raycastTarget = false;

            TextMeshProUGUI title = UiTheme.Label(
                "title", _panel, 17f, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(300f, 24f));
            title.text = "WHAT THE BINS WANT";

            // The hint block, stacked from the bottom edge so each piece is stated in terms of the
            // one below it. Three numbers that had to stay apart by hand collided the moment the
            // hint was made readable: a taller hint box ran into the heading, and moving the
            // heading ran it into the divider.

            // Monospaced, or the columns do not line up and the table is worse than no table. The
            // size is shared with the width arithmetic in Fill, which measures characters.
            _table = UiTheme.Label(
                "table", _panel, TableFontSize, UiTheme.Accent, TextAlignmentOptions.Top);
            UiTheme.Anchor(_table.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(300f, 260f));

            // The hint block is built from the bottom edge up, so it keeps its shape when Fill
            // resizes the panel for a taller table. Before this the hint sat straight under the
            // table in the same weight and read as more rows of it -- two different kinds of thing
            // with nothing between them saying so.
            // Bigger and brighter than the panel's other small print. This was 15pt in TextDim --
            // the dimmest colour in the interface -- on the one line a player went out of their way
            // to ask for. A nudge nobody can read is a button not worth pressing, which is the same
            // mistake as printing the hint twice, arrived at from the other side.
            _hint = UiTheme.Label(
                "hint", _panel, HintFontSize, UiTheme.Text, TextAlignmentOptions.Top);
            UiTheme.Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, HintBottom), new Vector2(HintWidth, HintHeight));
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.lineSpacing = HintLineSpacing;

            _hintHeading = UiTheme.Label(
                "hint heading", _panel, 13f, UiTheme.TextDim, TextAlignmentOptions.Center);
            UiTheme.Anchor(_hintHeading.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, HeadingBottom), new Vector2(300f, HeadingHeight));
            _hintHeading.text = "A NUDGE";

            Image rule = UiTheme.Panel_("divider", _panel, UiTheme.PanelEdge);
            _divider = rule;
            _divider.sprite = null;   // a plain hairline, not the rounded panel silhouette
            _divider.raycastTarget = false;
            UiTheme.Anchor(rule.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, DividerBottom), new Vector2(280f, 1f));
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
            _table.text = hasTable
                ? $"<mspace={TableMonospace}em>{table}</mspace>"
                : string.Empty;

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
            //
            // The room under the table is worked out from the hint block rather than stated, because
            // it was stated: two literals that had to be kept above whatever the bottom of the panel
            // held, and making the hint readable pushed the hint straight through the title on the
            // one case with no table at all -- free play, which is the only level that has no
            // expectations to tabulate.
            int lines = hasTable ? level.VectorCount + 2 : 0;
            float below = hasTable ? DividerBottom + 8f : HintBottom + HintHeight + 8f;
            float height = below + TitleRoom + lines * 24f;

            // And wider tables need a wider panel. Columns are as wide as the longest fixture name
            // now that they are no longer truncated, so a level grading "binOne" and "binZero" needs
            // more room than one grading "out". Measured off the finished table rather than
            // recomputed from the level, so the panel cannot disagree with the text inside it.
            float tableWidth = TruthTable.WidestLine(table) * TableCharacterWidth;
            float width = Mathf.Max(MinimumWidth, tableWidth + 2f * TablePadding);

            _panel.sizeDelta = new Vector2(width, height);
            _panel.anchoredPosition =
                new Vector2(-UiTheme.HelpRight(BesideSetupPanel), -UiTheme.HelpTop);
            _table.rectTransform.sizeDelta = new Vector2(width - 2f * TablePadding, lines * 24f + 8f);
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
