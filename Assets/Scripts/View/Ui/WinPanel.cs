using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// What a solved level looks like: the name, what it cost, and somewhere to go next.
    /// </summary>
    /// <remarks>
    /// Shows what the player built rather than a score. CLAUDE.md keeps scoring under "Not yet", and
    /// this deliberately stays on the right side of that line -- gates used and ticks taken are facts
    /// about the circuit, not a rank, and they are the two figures the roadmap identifies as the only
    /// meaningful ones. Throughput is not among them, because a balanced circuit always manages one
    /// vector per tick and an unbalanced one fails outright.
    /// </remarks>
    public sealed class WinPanel : MonoBehaviour
    {
        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private ProgressTracker _progress;

        [Tooltip("Canvas the panel is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        // A strip in three columns: SOLVED and the level's name; what the circuit cost; and the
        // buttons, stacked. See UiTheme.SolvedCardWidth for why it is a strip and where it sits.

        private const float Padding = 18f;
        private const float ColumnGap = 16f;
        private const float ButtonGap = 8f;

        /// <summary>The left column: SOLVED, and the level's name under it.</summary>
        public const float NameWidth = 190f;

        /// <summary>Room for the level's name under SOLVED: two lines.</summary>
        public const float NameHeight = 44f;

        /// <summary>
        /// The buttons' column. Wide enough for "PLAY THE FIRST LEVEL", which a fixed 160 once was
        /// not -- the caption printed out of its button and over the one beside it.
        /// </summary>
        private const float ButtonWidth = 230f;

        /// <summary>How the level's name and the cost of the circuit are set.</summary>
        /// <remarks>
        /// Body, as text meant to be read. It was Label, a step smaller so it would fit a strip, and
        /// a playtest (2026-09-25) found it too small; the card is wider instead.
        /// </remarks>
        public const UiType DetailType = UiType.Body;

        /// <summary>The middle column, for what the circuit cost.</summary>
        public const float DetailWidth =
            UiTheme.SolvedCardWidth - 2f * Padding - NameWidth - ButtonWidth - 2f * ColumnGap;

        /// <summary>Room for what the circuit cost, top to bottom inside the card.</summary>
        public const float DetailHeight = UiTheme.SolvedCardHeight - 2f * 12f;

        private RectTransform _root;
        private Button _stay;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _detail;
        private Button _next;
        private TextMeshProUGUI _nextLabel;

        private RunState _state = RunState.Editing;
        private bool _shown;

        /// <summary>
        /// Whether the solved panel is on screen.
        /// </summary>
        /// <remarks>
        /// Exposed for the tutorial, which shows its own ending card and must wait for this one to
        /// be gone first. It cannot ask <see cref="UiModal"/>, because this panel deliberately never
        /// registers there -- it is a strip at the foot of the board, not a full-screen takeover,
        /// and the board behind it stays live so the player can keep editing.
        /// </remarks>
        public bool IsShowing => _shown;

        /// <summary>
        /// Whether this run's solved card has been put up, whether or not it is still up.
        /// </summary>
        /// <remarks>
        /// Set where the card is presented, so nobody reading it has to have caught the card on
        /// screen. The tutorial's ending waits for this card to have had its turn, and it used to
        /// watch for it: "seen showing, then gone". Whether it saw the showing depended on which of
        /// the two updated first, and a card dismissed in the frame it appeared was never seen at
        /// all -- which left the ending waiting for a card that had already come and gone.
        /// Cleared when the run changes.
        /// </remarks>
        public bool PresentedThisRun { get; private set; }

        /// <summary>
        /// Whether Escape is this card's this frame, so the main menu stands aside for it.
        /// </summary>
        /// <remarks>
        /// Escape closes whatever is on top, and on a board with nothing open that is this card --
        /// every other card took Escape and this one took nothing. The main menu opens on the same
        /// key -- the level list did, until M and Escape swapped -- and this card is not a modal, so
        /// the menu cannot learn it is covered from <see cref="UiModal"/>. It asks here instead,
        /// and the answer holds for the whole frame either way round: true while the card is drawn
        /// with nothing over it, and still true after Escape has taken it down. Whichever of the
        /// two Unity updates first, one press closes the card and opens nothing.
        /// </remarks>
        public static bool HoldsEscape => _live != null && _live.HoldsEscapeNow;

        private static WinPanel _live;

        private bool HoldsEscapeNow =>
            _escapedOn == Time.frameCount || (IsDrawn && !UiModal.OpenOrJustClosed);

        private bool IsDrawn => _root != null && _root.gameObject.activeSelf;

        /// <summary>The frame Escape took the card down on, or -1.</summary>
        private int _escapedOn = -1;

        private void Awake()
        {
            // Here and in OnDestroy rather than OnEnable and OnDisable: a test that calls Update by
            // hand disables the component, and the card has not gone anywhere.
            _live = this;

            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null)
                return;

            Image panel = UiTheme.Panel_("Win", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();
            UiTheme.Anchor(_root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, UiRows.SolvedCard.Offset),
                new Vector2(UiTheme.SolvedCardWidth, UiRows.SolvedCard.Height));

            _title = UiTheme.Label("title", _root, UiType.Heading, UiTheme.Good, TextAlignmentOptions.TopLeft);
            UiTheme.Anchor(_title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Padding, -12f), new Vector2(NameWidth, 34f));

            _name = UiTheme.Label("name", _root, DetailType, UiTheme.Text, TextAlignmentOptions.TopLeft);
            UiTheme.Anchor(_name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Padding, -50f), new Vector2(NameWidth, NameHeight));
            _name.textWrappingMode = TextWrappingModes.Normal;

            _detail = UiTheme.Label("detail", _root, DetailType, UiTheme.Text, TextAlignmentOptions.TopLeft);
            UiTheme.Anchor(_detail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Padding + NameWidth + ColumnGap, -12f), new Vector2(DetailWidth, DetailHeight));
            _detail.textWrappingMode = TextWrappingModes.Normal;

            _next = UiTheme.Button_("Next", _root, "NEXT LEVEL", out _nextLabel, role: ButtonRole.Primary);
            UiTheme.Anchor(_next.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-Padding, 0f),
                new Vector2(ButtonWidth, UiTheme.ButtonHeight));
            _next.onClick.AddListener(NextLevel);

            _stay = UiTheme.Button_("Stay", _root, "KEEP TINKERING", out TextMeshProUGUI _, role: ButtonRole.Quiet);
            UiTheme.Anchor(_stay.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(-Padding, 0f),
                new Vector2(ButtonWidth, UiTheme.ButtonHeight));
            _stay.onClick.AddListener(Dismiss);

            _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_session == null || _root == null)
                return;

            // Every frame, not only on a state change: the panel that covers this one can open and
            // close without the run changing at all.
            Draw();

            // After Draw, so a panel that closed on this same Escape has already been taken into
            // account -- HoldsEscapeNow refuses the frame a panel closed on, whatever order the two
            // ran in, so the Escape that closed the level list over this card does not close the
            // card as well.
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame
                && _escapedOn != Time.frameCount && HoldsEscapeNow)
            {
                _escapedOn = Time.frameCount;
                Dismiss();
            }

            RunState now = _session.State;

            if (now != _state)
            {
                // The last level with everything else already solved belongs to EndingPanel. The
                // condition lives there so the two cannot disagree and show both or neither.
                if (now == RunState.Passed && !EndingPanel.IsTheEnd(_session, _progress))
                {
                    Present();
                }
                else
                {
                    PresentedThisRun = false;

                    if (_shown)
                        Show(false);   // reset or a new run takes the panel away
                }

                _state = now;
            }
        }

        /// <summary>Fills in what the solved circuit cost and shows the panel.</summary>
        private void Present()
        {
            PresentedThisRun = true;

            LevelDefinition level = _session.Level;

            _title.text = "SOLVED";

            int gates = 0;
            var parts = new List<string>();

            foreach (LevelBudgetEntry entry in level.Budget)
            {
                int placed = _session.PlacedCountOf(entry.Kind);

                if (placed <= 0)
                    continue;

                gates += placed;
                parts.Add($"{placed} {GatePalette.Label(entry.Kind)}");
            }

            string built = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no gates at all";
            string plural = gates == 1 ? "gate" : "gates";

            _name.text = level.Name;

            var detail = new System.Text.StringBuilder();
            detail.Append($"{gates} {plural}  -  {built}");

            if (level.HasDelayBudget)
                detail.Append($"\n{_session.SpentDelay} of {level.DelayBudget} delay spent");

            AppendRecords(detail);

            _detail.text = detail.ToString();

            int index = _session.LevelIndex;
            int count = _session.AvailableLevels.Count;

            // The tutorial ends on its own card, after this one: one way on, to it, and nothing to
            // tinker with first -- KEEP TINKERING would have led to the same card.
            bool toTheTutorialsEnd = TutorialDirector.EndsOnItsCard;
            bool onward = toTheTutorialsEnd || HasSomewhereToGo(index, count);

            UiTheme.SetShown(_next, onward);
            UiTheme.SetShown(_stay, !toTheTutorialsEnd);
            _nextLabel.text = toTheTutorialsEnd ? "CONTINUE" : index < 0 ? "PLAY THE FIRST LEVEL" : "NEXT LEVEL";

            // Stacked, the way on above KEEP TINKERING. One button left takes the middle rather than
            // sitting where its pair used to be: on the last level, and at the end of the tutorial.
            bool both = onward && !toTheTutorialsEnd;
            float half = (UiTheme.ButtonHeight + ButtonGap) * 0.5f;

            _next.GetComponent<RectTransform>().anchoredPosition = new Vector2(-Padding, both ? half : 0f);
            _stay.GetComponent<RectTransform>().anchoredPosition = new Vector2(-Padding, both ? -half : 0f);

            Show(true);
        }

        /// <summary>
        /// Whether the panel can offer a way onward from a level at this index.
        /// </summary>
        /// <remarks>
        /// The last level of the run has nowhere to go, and says so by having no button rather than
        /// wrapping silently back to the first.
        ///
        /// **A level off the rotation has somewhere to go, and it is the first level.** The guided
        /// tutorial is deliberately not in `AvailableLevels`, so its index is -1, and an index of -1
        /// used to fail the same test the last level fails -- leaving KEEP TINKERING as the only
        /// button on the panel. The closing card normally carries a player onward from there, but it
        /// only comes to someone who did not press skip; skip, then solve the level anyway, and the
        /// game had no way forward that was not a keyboard shortcut. Stepping one from -1 lands on
        /// the first level, which is where that player wants to be.
        ///
        /// Static so the three cases can be tested without a scene: off the rotation, mid-run, and
        /// the last level.
        /// </remarks>
        public static bool HasSomewhereToGo(int levelIndex, int levelCount)
        {
            if (levelCount <= 0)
                return false;

            return levelIndex < 0 || levelIndex < levelCount - 1;
        }

        /// <summary>
        /// The player's own best on this level, and whether this run beat it.
        /// </summary>
        /// <remarks>
        /// A personal best, not a rank. It is the same kind of fact as the gate count already on
        /// this panel -- a description of the circuit in front of them, measured against the circuit
        /// they built last time. Nobody else's number appears anywhere.
        ///
        /// Gates and latency are reported separately because they trade against each other, so
        /// beating one while losing the other is a real and interesting outcome rather than a
        /// contradiction to be collapsed.
        /// </remarks>
        private void AppendRecords(System.Text.StringBuilder detail)
        {
            if (_progress == null)
                return;

            string level = _session.LevelName;

            int bestGates = _progress.BestGates(level);
            int bestLatency = _progress.BestLatency(level);

            if (bestGates <= 0 && bestLatency <= 0)
                return;

            detail.Append($"\nbest so far   {bestGates} gates   {bestLatency} ticks");

            if (_progress.BeatGateRecord && _progress.BeatLatencyRecord)
                detail.Append("\nsmaller and faster than last time");
            else if (_progress.BeatGateRecord)
                detail.Append("\nsmaller than last time");
            else if (_progress.BeatLatencyRecord)
                detail.Append("\nfaster than last time");
        }

        private void Show(bool visible)
        {
            _shown = visible;
            Draw();
        }

        /// <summary>
        /// Puts the card on screen when it is both owed and not covered.
        /// </summary>
        /// <remarks>
        /// This card is deliberately not a modal -- it belongs on the board, not over the whole
        /// screen -- but that settles what it does to other panels, not what they do to it. It used
        /// to hide from nothing and merely skip coming to the front, which laid a scrim over a live
        /// panel; and a run that passed while a panel was open activated it behind that scrim at
        /// whatever sibling index it held, with no BringToFront ever to correct it.
        ///
        /// <see cref="IsShowing"/> stays the intent rather than what is drawn, because the tutorial
        /// director and the music both read it to know the card is still owed. A card that forgot
        /// itself here would let the tutorial's ending card through early.
        /// </remarks>
        private void Draw()
        {
            bool wanted = _shown && UiModal.HudVisible;

            if (_root.gameObject.activeSelf == wanted)
                return;

            _root.gameObject.SetActive(wanted);

            // Forward as it appears, including when it comes back after the panel that covered it
            // has closed.
            if (wanted)
                UiTheme.BringToFront(_root);
        }

        private void NextLevel()
        {
            // At the end of the tutorial the way on is its ending card, which comes up once this
            // one has gone.
            if (TutorialDirector.EndsOnItsCard)
            {
                Dismiss();
                return;
            }

            Show(false);
            _session.CycleLevel(1);
            UiTheme.Defocus();
        }

        /// <summary>
        /// Dismisses without leaving the level.
        /// </summary>
        /// <remarks>
        /// Deliberately does not reset the board. A player who wants to try a smaller circuit should
        /// find their solved one still sitting there to edit, not an empty grid.
        /// </remarks>
        private void Dismiss()
        {
            Show(false);
            UiTheme.Defocus();
        }

        private void OnDestroy()
        {
            if (_live == this)
                _live = null;
        }
    }
}
