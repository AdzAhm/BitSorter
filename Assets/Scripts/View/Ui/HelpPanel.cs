using System.Collections.Generic;
using UnityEngine;
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
    public sealed class HelpPanel : MonoBehaviour, IHoldsEscape
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
        public const UiType HintType = UiType.Body;

        /// <inheritdoc cref="HintType"/>
        private const float HintBottom = 12f;

        /// <summary>
        /// Room for the hint, and the width it wraps in: five wrapped lines at
        /// <see cref="HintType"/>.
        /// </summary>
        /// <remarks>
        /// Reserved rather than fitted, the way the banner reserves room for a goal -- and, like
        /// the banner's, refused by a test when a level asks for more than it holds. That is the
        /// half that was missing. This was four lines, sized by counting what the longest hint
        /// needed at the time and writing the total down, and spot-the-pattern was written later
        /// and wraps to 101px, so nine pixels of it fell outside the panel with nothing to say so.
        ///
        /// Raising it is not the fix; <see cref="HelpPanelTests"/> is. A number chosen by
        /// measuring today's longest hint is exactly what was here before.
        /// </remarks>
        public const float HintHeight = 116f;

        /// <inheritdoc cref="HintHeight"/>
        public const float HintWidth = 300f;

        /// <inheritdoc cref="HintHeight"/>
        public const float HintLineSpacing = 6f;

        /// <inheritdoc cref="HintType"/>
        private const float HeadingHeight = 18f;

        /// <inheritdoc cref="HintType"/>
        private const float HeadingBottom = HintBottom + HintHeight + 4f;

        /// <inheritdoc cref="HintType"/>
        private const float DividerBottom = HeadingBottom + HeadingHeight + 6f;

        /// <summary>The title's row at the top, and the gap under it.</summary>
        private const float TitleRoom = 46f;

        /// <summary>How tall one line of the table or the map is allowed.</summary>
        private const float LineHeight = 24f;

        /// <summary>
        /// The row of tabs under the title -- TABLE, then a map for each bin -- on a level that has a
        /// Karnaugh map, and the room it takes.
        /// </summary>
        /// <remarks>
        /// One tab per bin rather than one K-MAP tab showing every map: stacked, three four-input
        /// maps are 23 lines and would not fit above the run buttons. See <see cref="KarnaughMap"/>.
        ///
        /// Captioned at <see cref="UiType.Body"/> like every other button, and padded no more than
        /// they need, because One of four's row -- TABLE and four bins -- has to fit the panel's
        /// narrowest width.
        /// </remarks>
        private const float TabHeight = 26f;

        /// <inheritdoc cref="TabHeight"/>
        private const float TabGap = 6f;

        /// <inheritdoc cref="TabHeight"/>
        private const float TabPad = 8f;

        /// <inheritdoc cref="TabHeight"/>
        private const float TabMinimumWidth = 36f;

        /// <inheritdoc cref="TabHeight"/>
        private const float TabRoom = TabHeight + 10f;

        /// <summary>The small print before the map tabs, so a tab named OUT reads as OUT's map.</summary>
        private const string MapCaption = "K-MAP";

        /// <inheritdoc cref="MapCaption"/>
        private const UiType MapCaptionType = UiType.Micro;

        private RectTransform _panel;
        private TextMeshProUGUI _table;
        private TextMeshProUGUI _hint;

        private RectTransform _tabs;
        private Button _tableTab;
        private TextMeshProUGUI _mapCaption;
        private readonly List<Button> _mapTabs = new List<Button>();

        /// <summary>The level the panel is showing, for the tabs to redraw from.</summary>
        private LevelDefinition _level;

        /// <summary>
        /// Whether the map is showing rather than the table, and which bin's.
        /// </summary>
        /// <remarks>
        /// Kept for the session and never saved: a player who reads maps keeps reading them from one
        /// level to the next, and the next level's first bin is where that starts, since bins are
        /// named per level.
        ///
        /// On the panel and not static, though a static would read as "for the session" too. Every
        /// Play Mode fixture runs inside one play session, and a static set by the fixture that
        /// presses a map tab would still say "map" in every fixture after it. The panel lives as long
        /// as the scene, which is the session for a player and one fixture for a test.
        /// </remarks>
        private bool _showMap;

        /// <inheritdoc cref="_showMap"/>
        private int _mapBin;

        /// <summary>The canvas height the panel was last fitted to, so a resized window refits it.</summary>
        private float _fittedTo = -1f;

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
        private const UiType TableType = UiType.Body;
        private const float TableMonospace = 0.62f;
        private static float TableCharacterWidth => UiTheme.SizeOf(TableType) * TableMonospace;

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

            UiEscape.Join(this);
        }

        private void OnDestroy() => UiEscape.Leave(this);

        /// <summary>
        /// Whether Escape is this panel's this frame, so the main menu stands aside for it.
        /// </summary>
        /// <remarks>
        /// Escape closes what is on top, and the open help is on top of the board. Once Escape was
        /// the main menu's key, pressing it to put the help away covered the screen with the menu
        /// instead. The panel is not a modal, so it joins <see cref="UiEscape"/>, as the solved card
        /// and the hint do: true while it is open with nothing over it, and still true for the rest
        /// of the frame once Escape has closed it.
        /// </remarks>
        public bool HoldsEscapeNow =>
            _escapedOn == Time.frameCount || (_shown && UiModal.HudVisible && !UiModal.OpenOrJustClosed);

        /// <summary>The frame Escape closed the panel on, or -1.</summary>
        private int _escapedOn = -1;

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

            if (!Mathf.Approximately(CanvasHeight(), _fittedTo))
                Fit();

            Keyboard keyboard = UiText.Keyboard;

            // H as well as the button. A player mid-wire should not have to find a target. Suppressed
            // while a full-screen panel is up, where the help would open behind it.
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame && !UiModal.OpenOrJustClosed)
                Show(!_shown);

            // Escape closes it, as Escape closes whatever is on top.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame
                && _escapedOn != Time.frameCount && HoldsEscapeNow && UiEscape.TryTake())
            {
                _escapedOn = Time.frameCount;
                Show(false);
            }
        }

        private void OnLevelLoaded(LevelDefinition level)
        {
            _mapBin = 0;   // bins are named per level, so a new one starts on its first
            Fill(level);
            Show(false);   // a new level starts closed, whatever the last one was left as
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void BuildBadge()
        {
            _badge = UiTheme.Panel_("Help badge", _canvas.transform, Palette.Current.BadgeBackdrop);
            var rect = _badge.GetComponent<RectTransform>();

            // Top right, clear of the status banner and above the bits-lost meter's corner.
            UiTheme.Anchor(rect, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.Margin, -UiRows.Badge.Offset),
                new Vector2(UiTheme.BadgeSize, UiRows.Badge.Height));

            _badge.sprite = ProceduralSprites.Circle();

            var button = _badge.gameObject.AddComponent<Button>();
            button.targetGraphic = _badge;

            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            button.onClick.AddListener(() =>
            {
                Show(!_shown);
                UiTheme.Defocus();
            });

            TextMeshProUGUI mark = UiTheme.Label(
                "mark", rect, UiType.Numeral, Color.white, TextAlignmentOptions.Center);
            UiTheme.Stretch(mark.rectTransform);
            mark.text = BadgeGlyph;

            // The badge is round and unlabelled, which is not obviously a button. The key beside it
            // says both that it opens something and how to open it without aiming at all.
            TextMeshProUGUI key = UiTheme.Label(
                "key", rect, UiTheme.KeyCaptionType, UiTheme.TextDim, TextAlignmentOptions.Center);
            key.raycastTarget = false;
            UiTheme.Anchor(key.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, -UiTheme.BadgeKeyDrop), new Vector2(UiTheme.BadgeKeyWidth, UiTheme.BadgeKeyLabelHeight));
            key.text = ControlsReference.At(ControlSpot.HelpBadge);
        }

        private void BuildPanel()
        {
            Image background = UiTheme.Panel_("Help", _canvas.transform, UiTheme.Panel);
            _panel = background.GetComponent<RectTransform>();
            UiTheme.Anchor(_panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-UiTheme.HelpRight(BesideSetupPanel), -UiRows.Panels.Offset),
                new Vector2(UiTheme.HelpMinimumWidth, 380f));

            background.raycastTarget = false;

            TextMeshProUGUI title = UiTheme.Label(
                "title", _panel, UiType.Body, UiTheme.Text, TextAlignmentOptions.Center);
            UiTheme.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(300f, 24f));
            title.text = "WHAT THE BINS WANT";

            // The tabs, under the title and left-aligned with the table. Only the TABLE tab and the
            // caption are built here; the map tabs are the level's bins, so Fill makes them.
            _tabs = UiTheme.Rect("Help tabs", _panel);
            UiTheme.Anchor(_tabs, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(TablePadding, -TitleRoom + 2f), new Vector2(0f, TabHeight));

            _tableTab = Tab("Help table tab", "TABLE", () => { _showMap = false; ShowContents(); });

            _mapCaption = UiTheme.Label(
                "Help map caption", _tabs, MapCaptionType, UiTheme.TextDim, TextAlignmentOptions.Center);
            _mapCaption.text = MapCaption;

            // The hint block, stacked from the bottom edge so each piece is stated in terms of the
            // one below it. Three numbers that had to stay apart by hand collided the moment the
            // hint was made readable: a taller hint box ran into the heading, and moving the
            // heading ran it into the divider.

            // Monospaced, or the columns do not line up and the table is worse than no table. The
            // size is shared with the width arithmetic in Fill, which measures characters.
            _table = UiTheme.Label(
                "table", _panel, TableType, UiTheme.Accent, TextAlignmentOptions.Top);
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
                "hint", _panel, HintType, UiTheme.Text, TextAlignmentOptions.Top);
            UiTheme.Anchor(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, HintBottom), new Vector2(HintWidth, HintHeight));
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.lineSpacing = HintLineSpacing;

            _hintHeading = UiTheme.Label(
                "hint heading", _panel, UiType.Caption, UiTheme.TextDim, TextAlignmentOptions.Center);
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

            _level = level;

            bool hasTable = !string.IsNullOrEmpty(TruthTable.Format(level));

            _hint.text = level != null ? level.Hint : string.Empty;

            // Nothing to divide the hint from on a level that grades nothing, so the rule and its
            // heading go with the table rather than floating above an empty space.
            if (_divider != null)
                _divider.enabled = hasTable;

            if (_hintHeading != null)
                _hintHeading.gameObject.SetActive(hasTable);

            BuildMapTabs(level);

            // One size for the table and every map, so switching tabs never resizes the panel --
            // and a resized panel would re-frame the board, since CameraFit fits it into what the
            // panel leaves.
            float width = WidthFor(level);
            float lines = ContentLines(level);
            float tabs = TabRoomFor(level);

            _panel.sizeDelta = new Vector2(width, HeightFor(level));
            _panel.anchoredPosition =
                new Vector2(-UiTheme.HelpRight(BesideSetupPanel), -UiRows.Panels.Offset);
            _table.rectTransform.anchoredPosition = new Vector2(0f, -44f - tabs);
            _table.rectTransform.sizeDelta = new Vector2(width - 2f * TablePadding, lines * LineHeight + 8f);

            ShowContents();
            Fit();
        }

        /// <summary>
        /// Puts the chosen tab's text in the panel -- the table, or one bin's map -- and marks the
        /// tab. Never resizes: the size was chosen in <see cref="Fill"/> to hold all of them.
        /// </summary>
        private void ShowContents()
        {
            IReadOnlyList<string> bins = KarnaughMap.Bins(_level);
            bool map = _showMap && bins.Count > 0;

            if (_mapBin >= bins.Count)
                _mapBin = 0;

            string text = map ? KarnaughMap.Format(_level, bins[_mapBin]) : TruthTable.Format(_level);

            // Empty for a level that grades nothing -- free play has no expectations, so there is no
            // table to draw. Rendering the wrapper anyway left a blank block sized for a table that
            // was never coming, which reads as something having failed to load.
            //
            // mspace rather than a monospaced font: forcing an advance width is enough to make
            // columns line up in the game's own font, without adding another asset.
            _table.text = string.IsNullOrEmpty(text)
                ? string.Empty
                : $"<mspace={TableMonospace}em>{text}</mspace>";

            // The chosen tab in the selected fill, the others quiet. Captions stay bright on all of
            // them: a dim caption is how a button says it cannot be pressed.
            SetTabFill(_tableTab, !map);

            for (int i = 0; i < _mapTabs.Count; i++)
                SetTabFill(_mapTabs[i], map && i == _mapBin);
        }

        private static void SetTabFill(Button tab, bool chosen)
        {
            if (tab != null)
                ((Image)tab.targetGraphic).color =
                    chosen ? UiTheme.SelectedFill(true) : UiTheme.FillOf(ButtonRole.Quiet);
        }

        /// <summary>
        /// One tab per bin with a map, after the TABLE tab and the caption; none, and the row
        /// hidden, on a level with no map.
        /// </summary>
        /// <remarks>
        /// Rebuilt on every fill, which is a level load or a free-play edit -- never a frame. Hidden
        /// before it is destroyed, because destruction waits for the end of the frame and a test
        /// that looks a tab up by name in the same frame would otherwise find the old one.
        /// </remarks>
        private void BuildMapTabs(LevelDefinition level)
        {
            foreach (Button old in _mapTabs)
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }

            _mapTabs.Clear();

            IReadOnlyList<string> bins = KarnaughMap.Bins(level);
            _tabs.gameObject.SetActive(bins.Count > 0);

            if (bins.Count == 0)
                return;

            float x = TabWidth("TABLE") + TabGap;

            float captionWidth = CaptionWidth();
            UiTheme.Anchor(_mapCaption.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(x, 0f), new Vector2(captionWidth, TabHeight));
            x += captionWidth + TabGap;

            for (int i = 0; i < bins.Count; i++)
            {
                int bin = i;
                string caption = LevelRules.BoardLabel(bins[i]);

                Button tab = Tab($"Help map tab {bins[i]}", caption, () =>
                {
                    _showMap = true;
                    _mapBin = bin;
                    ShowContents();
                });

                ((RectTransform)tab.transform).anchoredPosition = new Vector2(x, 0f);
                x += TabWidth(caption) + TabGap;

                _mapTabs.Add(tab);
            }
        }

        /// <summary>One tab on the row, sized to its caption and placed by the caller.</summary>
        private Button Tab(string name, string caption, System.Action chosen)
        {
            Button tab = UiTheme.Button_(name, _tabs, caption, out TextMeshProUGUI _, UiType.Body,
                ButtonRole.Quiet);

            UiTheme.Anchor((RectTransform)tab.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(TabWidth(caption), TabHeight));

            tab.onClick.AddListener(() =>
            {
                chosen();
                UiTheme.Defocus();
            });

            return tab;
        }

        private static float TabWidth(string caption) =>
            Mathf.Max(TabMinimumWidth, UiTheme.TextWidth(caption, UiType.Body) + 2f * TabPad);

        private static float CaptionWidth() => UiTheme.TextWidth(MapCaption, MapCaptionType) + 2f;

        // -----------------------------------------------------------------
        // Size, worked out from the level so a test can hold every level to it
        // -----------------------------------------------------------------

        /// <summary>
        /// How tall the panel is on this level, before any fitting to a short window.
        /// </summary>
        /// <remarks>
        /// Taller tables need a taller panel. Eight vectors plus a header and rule is ten lines, and
        /// the per-line figure tracks the table's font size rather than being guessed. With no table
        /// the panel shrinks to the hint rather than keeping the space open.
        ///
        /// The room under the table is worked out from the hint block rather than stated, because it
        /// was stated: two literals that had to be kept above whatever the bottom of the panel held,
        /// and making the hint readable pushed the hint straight through the title on the one case
        /// with no table at all -- free play, which is the only level that has no expectations to
        /// tabulate.
        /// </remarks>
        public static float HeightFor(LevelDefinition level)
        {
            bool hasTable = !string.IsNullOrEmpty(TruthTable.Format(level));
            float below = hasTable ? DividerBottom + 8f : HintBottom + HintHeight + 8f;

            return below + TitleRoom + TabRoomFor(level) + ContentLines(level) * LineHeight;
        }

        /// <summary>
        /// How wide the panel is on this level: the widest line of the table or of any of its maps.
        /// </summary>
        /// <remarks>
        /// Wider tables need a wider panel. Columns are as wide as the longest fixture name now that
        /// they are no longer truncated, so a level grading "binOne" and "binZero" needs more room
        /// than one grading "out". Measured off the finished text rather than recomputed from the
        /// level, so the panel cannot disagree with what is inside it.
        /// </remarks>
        public static float WidthFor(LevelDefinition level)
        {
            int widest = TruthTable.WidestLine(TruthTable.Format(level));

            foreach (string bin in KarnaughMap.Bins(level))
                widest = Mathf.Max(widest, TruthTable.WidestLine(KarnaughMap.Format(level, bin)));

            return Mathf.Max(MinimumWidth, widest * TableCharacterWidth + 2f * TablePadding);
        }

        /// <summary>How wide this level's row of tabs is, or zero when it has none.</summary>
        public static float TabRowWidth(LevelDefinition level)
        {
            IReadOnlyList<string> bins = KarnaughMap.Bins(level);

            if (bins.Count == 0)
                return 0f;

            float width = TabWidth("TABLE") + TabGap + CaptionWidth();

            for (int i = 0; i < bins.Count; i++)
                width += TabGap + TabWidth(LevelRules.BoardLabel(bins[i]));

            return width;
        }

        /// <summary>The room the tabs take inside the panel on each side, for the width test.</summary>
        public const float ContentPadding = TablePadding;

        /// <summary>
        /// The height a canvas this tall leaves between the help badge and the run buttons.
        /// </summary>
        public static float Room(float canvasHeight) =>
            canvasHeight - UiRows.Panels.Offset - UiRows.PanelFloor;

        /// <summary>
        /// Lines of table or map, whichever is longer. It is always the table -- no map is taller,
        /// and <c>KarnaughMapTests</c> holds every level to that -- but the panel asks rather than
        /// assumes.
        /// </summary>
        private static int ContentLines(LevelDefinition level)
        {
            if (string.IsNullOrEmpty(TruthTable.Format(level)))
                return 0;

            return Mathf.Max(level.VectorCount + 2, KarnaughMap.LineCount(level));
        }

        private static float TabRoomFor(LevelDefinition level) =>
            KarnaughMap.Applies(level) ? TabRoom : 0f;

        /// <summary>
        /// Shrinks the panel to fit a short window, and puts it back at full size in a tall one.
        /// </summary>
        /// <remarks>
        /// The canvas scales halfway between the window's width and its height, so a wide, short
        /// window -- a browser tab 1920 by 800 -- has about 930 of height to give rather than 1080.
        /// A sixteen-row table needs about 640 of the 640 that leaves, before the tabs, and ran into
        /// the run buttons. Free play's setup panel, in the same column, already stops at the same
        /// floor. Scaled rather than cut short, as Settings is, and from the top right corner it is
        /// pinned by, so it stays where it was.
        /// </remarks>
        private void Fit()
        {
            if (_panel == null)
                return;

            _fittedTo = CanvasHeight();

            float scale = SettingsPanel.FitScale(Room(_fittedTo), _panel.sizeDelta.y);

            if (!Mathf.Approximately(_panel.localScale.x, scale))
                _panel.localScale = new Vector3(scale, scale, 1f);
        }

        private float CanvasHeight() =>
            _canvas != null && _canvas.transform is RectTransform rect ? rect.rect.height : 1080f;

        /// <summary>The scale the panel is drawn at now, for the tests.</summary>
        public float PanelScale => _panel != null ? _panel.localScale.x : 1f;

        private readonly Vector3[] _edgeCorners = new Vector3[4];

        /// <summary>
        /// The open panel's left edge in screen pixels, or zero while it is shut.
        /// </summary>
        /// <remarks>
        /// Read by <see cref="CameraFit"/>, which frames the board clear of it. Measured whenever
        /// the panel is open, drawn or not, by the setup panel's rule: stepping aside for a
        /// full-screen panel must not re-frame the board behind it.
        /// </remarks>
        public float ScreenLeftEdge
        {
            get
            {
                if (_panel == null || !_shown)
                    return 0f;

                _panel.GetWorldCorners(_edgeCorners);
                return _edgeCorners[0].x;
            }
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
