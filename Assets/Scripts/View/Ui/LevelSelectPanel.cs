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
            /// <summary>
            /// A drawn dot, not a text glyph.
            /// </summary>
            /// <remarks>
            /// This was a tick character, and LiberationSans -- the only font the project ships --
            /// has no U+2713. TMP fell back to nothing and logged a warning for every row on every
            /// refresh, which is several a second with the panel open. A generated sprite has no
            /// font to be missing from.
            /// </remarks>
            public Image Tick;

            public TextMeshProUGUI Label;
            public TextMeshProUGUI Best;

            /// <summary>
            /// What this row was last drawn from, so a frame that changes nothing draws nothing.
            /// </summary>
            /// <remarks>
            /// The same guard the status banner and the parts list keep. This panel refreshes
            /// every frame it is open and it is the one panel that stays open while it is read,
            /// so a record rebuilt per frame is rebuilt hundreds of times -- and there is one per
            /// solved level, which means the waste grows with how much of the game is finished.
            ///
            /// Drawn starts false so the first refresh always draws, whatever the fields happen
            /// to hold.
            /// </remarks>
            public bool Drawn;

            /// <inheritdoc cref="Drawn"/>
            public bool DrawnDone;

            /// <inheritdoc cref="Drawn"/>
            public bool DrawnHere;

            /// <inheritdoc cref="Drawn"/>
            public int DrawnGates;

            /// <inheritdoc cref="Drawn"/>
            public int DrawnTicks;
        }

        [SerializeField] private LevelSession _session;
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private SandboxPanel _sandbox;
        [SerializeField] private TutorialDirector _tutorial;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private readonly List<Row> _rows = new List<Row>();
        private RectTransform _root;
        private bool _shown;

        /// <summary>Whether the list is covering the board.</summary>
        /// <remarks>
        /// Read by <see cref="GameAudio"/>: the list carries the menu's music, because choosing
        /// what to play next is the same thing whichever panel it is done in.
        /// </remarks>
        public bool IsShowing => _shown;

        /// <summary>
        /// The tutorial row's tick. Kept on its own rather than in <see cref="_rows"/>, because a
        /// Row also carries a personal best and the tutorial has none.
        /// </summary>
        private Image _tutorialTick;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_sandbox == null) _sandbox = FindFirstObjectByType<SandboxPanel>();
            if (_tutorial == null) _tutorial = FindFirstObjectByType<TutorialDirector>();
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

            // Escape closes this whatever else is open, but only opens it when nothing else is --
            // otherwise it would stack the list on top of the main menu.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame
                && (_shown || !UiModal.OpenOrJustClosed))
            {
                Show(!_shown);
            }

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
            Image scrim = UiTheme.Scrim("Level select", _canvas.transform, Palette.Current.ListScrim);
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

            const float rowHeight = RowHeight;
            const float gap = RowGap;

            RectTransform list = UiTheme.Rect("list", _root);

            // Walked down with a cursor rather than multiplied out from a row index. Two headings
            // and three different gaps sit between the rows now, and index arithmetic that has to
            // know about all of them is the shape this file had when the sandbox row was added --
            // every new spacer meant another term in every other row's offset.
            var column = new UiColumn();

            // The tutorial sits above the run, free play below it. Neither is in Catalogue, and the
            // gaps on either side are what say so: what lies between them is the run.
            BuildTutorialRow(list, column.Take(rowHeight, gap + TutorialGap), rowHeight);

            bool sequentialStarted = false;

            for (int i = 0; i < catalogue.Count; i++)
            {
                if (i == 0)
                    BuildHeading(list, column.Take(HeadingHeight), LevelCatalog.HeadingFor(false));

                // The break is where the first level stocking a register is, which is the same fact
                // the chapter card fires on, so the two cannot end up in different places.
                if (!sequentialStarted && catalogue[i].IsSequential)
                {
                    sequentialStarted = true;
                    column.Space(ChapterGap);
                    BuildHeading(list, column.Take(HeadingHeight), LevelCatalog.HeadingFor(true));
                }

                _rows.Add(BuildRow(catalogue[i], list, column.Take(rowHeight, gap), rowHeight));
            }

            column.Space(SandboxGap);
            BuildSandboxRow(list, column.Take(rowHeight), rowHeight);

            // Sized once everything is placed. Rows anchor to the list's top edge, so growing it
            // downwards afterwards moves nothing.
            UiTheme.Anchor(list, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, column.Next));
        }

        /// <summary>Extra space before the sequential chapter, on top of the ordinary row gap.</summary>
        private const float ChapterGap = 16f;

        /// <summary>Height a chapter heading takes, including the space under it.</summary>
        private const float HeadingHeight = 26f;

        /// <summary>
        /// A row, and the space under it.
        /// </summary>
        /// <remarks>
        /// Was 42. The two chapter headings and the gap before the second cost sixty-eight pixels,
        /// and the list is centred on the screen, so it grew up into the LEVELS title as well as
        /// down. Six pixels off each of nineteen rows buys back more than the headings cost.
        /// </remarks>
        private const float RowHeight = 36f;

        /// <inheritdoc cref="RowHeight"/>
        private const float RowGap = 6f;

        /// <summary>
        /// Where a row's text starts, in from the row's left edge.
        /// </summary>
        /// <remarks>
        /// Shared with the chapter headings above them. The heading used to state its own inset
        /// from the middle of the list instead, which put it twenty-six pixels left of the names it
        /// was heading -- a label out of line with the thing it labels.
        /// </remarks>
        private const float LabelInset = 52f;

        /// <summary>
        /// How tall the list is, for a run of this many levels split into this many chapters.
        /// </summary>
        /// <remarks>
        /// The list is centred between the panel's title and its help line, and there is no
        /// scrolling: a list taller than the room between them does not clip, it draws over them.
        /// Stated as a formula so <see cref="UiThemeTests"/> can ask whether the run still fits
        /// before anyone sees it not fitting.
        ///
        /// Counts the tutorial row and free play's row, which are not levels but are rows.
        /// </remarks>
        public static float ListHeight(int levels, int chapters)
        {
            float rows = (levels + 2) * (RowHeight + RowGap) - RowGap;
            float headings = chapters * HeadingHeight;
            float spacers = TutorialGap + SandboxGap + Mathf.Max(0, chapters - 1) * ChapterGap;

            return rows + headings + spacers;
        }

        /// <summary>
        /// A chapter's name over the first of its levels, <see cref="HeadingHeight"/> tall.
        /// </summary>
        /// <remarks>
        /// Bolder and brighter than the "the controls" and "free play" notes, which are asides on a
        /// single row; a chapter heading has nine and eight rows under it and has to hold them
        /// together. Still well under a level's own 17pt, so it labels the run rather than
        /// competing with it. The words come from <see cref="LevelCatalog"/>, which is also where
        /// the chapter card gets its title.
        /// </remarks>
        private void BuildHeading(RectTransform list, float y, string text)
        {
            TextMeshProUGUI heading = UiTheme.Label(
                "chapter", list, 14f, UiTheme.Text, TextAlignmentOptions.Left);

            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 6f;

            // Same rect a row gets, and the text starts where a row's text starts.
            UiTheme.Anchor(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(520f, HeadingHeight));

            heading.margin = new Vector4(LabelInset, 0f, 0f, 0f);

            heading.text = text;
        }

        /// <summary>Extra space between the last level and free play, so the run reads as ending.</summary>
        private const float SandboxGap = 14f;

        /// <summary>The same, above the run, so the tutorial reads as coming before it.</summary>
        private const float TutorialGap = 14f;

        /// <summary>
        /// The guided tutorial, above the run. Deliberately not a <see cref="Row"/>, for the same
        /// reason free play is not: rows carry a completion tick and a personal best, and the
        /// tutorial has neither.
        /// </summary>
        private void BuildTutorialRow(RectTransform list, float y, float height)
        {
            Button button = UiTheme.Button_("Tutorial", list, string.Empty,
                out TextMeshProUGUI caption);
            Destroy(caption.gameObject);

            var rect = button.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(520f, height));

            // Same drawn dot the levels use, in the same place, so "done" reads the same way down
            // the whole list. A glyph would not: the one font this project ships has no tick in it.
            RectTransform tickRect = UiTheme.Rect("tick", rect);
            UiTheme.Anchor(tickRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(26f, 0f), new Vector2(14f, 14f));

            _tutorialTick = tickRect.gameObject.AddComponent<Image>();
            _tutorialTick.sprite = ProceduralSprites.Circle();
            _tutorialTick.color = UiTheme.Good;
            _tutorialTick.raycastTarget = false;
            _tutorialTick.enabled = false;

            TextMeshProUGUI label = UiTheme.Label(
                "name", rect, 17f, UiTheme.Accent, TextAlignmentOptions.Left);
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(LabelInset, 0f), new Vector2(330f, height));
            label.text = "Tutorial";

            TextMeshProUGUI note = UiTheme.Label(
                "note", rect, 13f, UiTheme.TextDim, TextAlignmentOptions.Right);
            UiTheme.Anchor(note.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(200f, height));
            note.text = "the controls";

            button.onClick.AddListener(() =>
            {
                Show(false);

                if (_tutorial != null)
                    _tutorial.Begin();

                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            });
        }

        /// <summary>
        /// Free play, below the run. Deliberately not a <see cref="Row"/>: rows carry a completion
        /// tick and a personal best, and a sandbox has neither and never will.
        /// </summary>
        private void BuildSandboxRow(RectTransform list, float y, float height)
        {
            Button button = UiTheme.Button_("Sandbox", list, string.Empty,
                out TextMeshProUGUI caption);
            Destroy(caption.gameObject);

            var rect = button.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(520f, height));

            TextMeshProUGUI label = UiTheme.Label(
                "name", rect, 17f, UiTheme.Accent, TextAlignmentOptions.Left);
            UiTheme.Anchor(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(LabelInset, 0f), new Vector2(330f, height));
            label.text = "Sandbox";

            TextMeshProUGUI note = UiTheme.Label(
                "note", rect, 13f, UiTheme.TextDim, TextAlignmentOptions.Right);
            UiTheme.Anchor(note.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(200f, height));
            note.text = "free play";

            button.onClick.AddListener(() =>
            {
                Show(false);

                if (_sandbox != null)
                    _sandbox.Open();

                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            });
        }

        private Row BuildRow(LevelEntry entry, RectTransform list, float y, float height)
        {
            var row = new Row { FileName = entry.FileName };

            row.Button = UiTheme.Button_($"Level {entry.FileName}", list, string.Empty,
                out TextMeshProUGUI caption);
            Destroy(caption.gameObject);

            var rect = row.Button.GetComponent<RectTransform>();
            UiTheme.Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(520f, height));

            row.Frame = row.Button.GetComponent<Image>();

            RectTransform tickRect = UiTheme.Rect("tick", rect);
            UiTheme.Anchor(tickRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(20f, 0f), new Vector2(14f, 14f));

            row.Tick = tickRect.gameObject.AddComponent<Image>();
            row.Tick.sprite = ProceduralSprites.Circle();
            row.Tick.color = UiTheme.Good;
            row.Tick.raycastTarget = false;

            row.Label = UiTheme.Label("name", rect, 17f, UiTheme.Text, TextAlignmentOptions.Left);
            UiTheme.Anchor(row.Label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(LabelInset, 0f), new Vector2(330f, height));
            row.Label.text = entry.DisplayName;

            row.Best = UiTheme.Label("best", rect, 14f, UiTheme.TextDim, TextAlignmentOptions.Right);
            UiTheme.Anchor(row.Best.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-16f, 0f), new Vector2(130f, height));

            string file = entry.FileName;
            row.Button.onClick.AddListener(() => Choose(file));

            return row;
        }

        // -----------------------------------------------------------------
        // Showing
        // -----------------------------------------------------------------

        /// <summary>Opens the list. Used by the main menu's Levels item as well as by Escape.</summary>
        public void Open() => Show(true);

        private void Show(bool visible)
        {
            _shown = visible;

            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);

            if (visible)
            {
                UiTheme.BringToFront(_root);
                UiModal.Opened(this);
                Refresh();
            }
            else
            {
                UiModal.Closed(this);
            }
        }

        private void OnDisable() => UiModal.Closed(this);

        private void Refresh()
        {
            string current = _session.LevelName;

            // Driven by the milestone, never by IsComplete. The tutorial is off-catalogue and must
            // never appear in the completed list -- that is exactly the bug this tick would
            // otherwise be reporting.
            if (_tutorialTick != null)
            {
                _tutorialTick.enabled = _progress != null
                                        && _progress.Store != null
                                        && _progress.Store.HasMilestone(TutorialLevel.Key);
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];

                bool done = _progress != null && _progress.IsComplete(row.FileName);
                bool here = row.FileName == current;

                // Asked every frame, which costs nothing: both are dictionary lookups. It is
                // building the string out of them that had to stop happening.
                int gates = done && _progress != null ? _progress.BestGates(row.FileName) : 0;
                int ticks = done && _progress != null ? _progress.BestLatency(row.FileName) : 0;

                if (row.Drawn && done == row.DrawnDone && here == row.DrawnHere
                    && gates == row.DrawnGates && ticks == row.DrawnTicks)
                {
                    continue;
                }

                row.Drawn = true;
                row.DrawnDone = done;
                row.DrawnHere = here;
                row.DrawnGates = gates;
                row.DrawnTicks = ticks;

                row.Tick.enabled = done;

                // The record sits beside the name rather than replacing it, so an unsolved level and
                // a solved one read the same way and the list stays scannable.
                row.Best.text = gates > 0 ? $"{gates}g  {ticks}t" : string.Empty;

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
