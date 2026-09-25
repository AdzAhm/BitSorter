using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Runs the guided tutorial: adopts its board, reads the board back to work out which step the
    /// player is on, points at what that step wants, and records that it has been done.
    /// </summary>
    /// <remarks>
    /// The only component here that knows about the scene. Which step is current is
    /// <see cref="TutorialScript"/>'s answer, from facts gathered below; this decides nothing about
    /// the sequence, and nothing here blocks any input.
    ///
    /// One phase either side of the six steps. An opening line orients the player before being
    /// asked to click anything, and a closing card lists the controls the six steps never touched.
    /// Neither is a step: a step is a predicate over board state, and "the player pressed Next" is
    /// not, so making them steps would mean tracking exactly the latching state the design avoids.
    /// </remarks>
    public sealed class TutorialDirector : MonoBehaviour
    {
        private enum Phase
        {
            Idle,
            Intro,
            Steps,
            Card,
        }

        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private ProgressTracker _progress;
        [SerializeField] private PlacementController _placement;
        [SerializeField] private PlacementGrid _grid;
        [SerializeField] private GatePaletteView _palette;
        [SerializeField] private RunControls _runControls;
        [SerializeField] private TutorialPanel _panel;
        [SerializeField] private TutorialHighlighter _highlighter;
        [SerializeField] private TutorialCard _card;
        [SerializeField] private WinPanel _winPanel;

        private const string Intro =
            "Welcome. A bit starts at A on the left and has to reach the bin on the right. " +
            "Follow the highlights.";

        private Phase _phase = Phase.Idle;
        private bool _menuHasClosed;

        /// <summary>Whether the tutorial's run has passed since it last began.</summary>
        /// <remarks>
        /// Kept rather than read off the session when the board is left: by the time the level
        /// switch says so, the session is already on the next level, and its state is that level's.
        /// </remarks>
        private bool _solved;

        /// <summary>Whether the tutorial has run at all this session.</summary>
        private bool _begunThisSession;

        /// <summary>Whether the tutorial is running right now.</summary>
        public bool IsRunning => _phase != Phase.Idle;

        /// <summary>The director in the scene, so the board rules can ask it a question cheaply.</summary>
        private static TutorialDirector _live;

        /// <summary>
        /// Whether the tutorial is still asking whether to start, and is holding the board until
        /// it gets an answer.
        /// </summary>
        /// <remarks>
        /// True only during the intro. Once the player has pressed START or SKIP the board is
        /// theirs, and every step after that leaves every action legal -- an unsatisfied step
        /// simply does not advance, and a step un-satisfies itself when the player deletes what it
        /// asked for.
        ///
        /// The intro is the one moment where a free board costs the player something they cannot
        /// get back. The tutorial stocks one NOT and one AND decoy, and the step that wants the
        /// NOT wants it on one cell; a part spent before it is asked for leaves a step that cannot
        /// be met. Reported from play as the instructions asking for a gate that was no longer
        /// there.
        ///
        /// Derived from the phase and never set, which is the rule pointer ownership follows and
        /// for the same reason: a hold that can be claimed is a hold that can leak, and a leaked
        /// hold is a board nobody can touch with nothing on screen explaining why. There is no
        /// state here to leak. No director in the scene, a destroyed one, or any phase but the
        /// intro, and the board is free.
        /// </remarks>
        public static bool HoldingTheBoard => _live != null && _live._phase == Phase.Intro;

        /// <summary>
        /// Whether the tutorial will put up its own ending once the solved card is dismissed.
        /// </summary>
        /// <remarks>
        /// The solved card asks, so it can lead there rather than past it: it used to offer PLAY
        /// THE FIRST LEVEL, which loaded the level and skipped the ending card -- the controls, and
        /// the one moment the tutorial says it is over. Only while the tutorial is running its
        /// steps: a player who skipped it and solved the board anyway gets no ending card, and for
        /// them the solved card's own way on is the first level.
        ///
        /// Derived from the phase and never set, like <see cref="HoldingTheBoard"/>.
        /// </remarks>
        public static bool EndsOnItsCard => _live != null && _live._phase == Phase.Steps;

        /// <summary>
        /// How many steps the board currently satisfies, counting from the top.
        /// </summary>
        /// <remarks>
        /// Derived on every read, never stored -- the same rule the rest of the view follows, and
        /// the reason a step un-finishes by itself when the player deletes what it asked for.
        ///
        /// Public because the interesting failures are all of the form "a step was already
        /// satisfied before the player did anything": the auto-selected budget row made step one
        /// free, and a restored board would have satisfied all six the instant it loaded. Neither
        /// is visible from outside without asking the director what it thinks the board says.
        /// </remarks>
        public int CurrentStep =>
            _session == null || _runner == null || !_runner.IsReady
                ? 0
                : TutorialScript.CurrentStep(Gather());

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_progress == null) _progress = FindFirstObjectByType<ProgressTracker>();
            if (_placement == null) _placement = FindFirstObjectByType<PlacementController>();
            if (_grid == null) _grid = FindFirstObjectByType<PlacementGrid>();
            if (_palette == null) _palette = FindFirstObjectByType<GatePaletteView>();
            if (_runControls == null) _runControls = FindFirstObjectByType<RunControls>();
            if (_panel == null) _panel = FindFirstObjectByType<TutorialPanel>();
            if (_highlighter == null) _highlighter = FindFirstObjectByType<TutorialHighlighter>();
            if (_card == null) _card = FindFirstObjectByType<TutorialCard>();
            if (_winPanel == null) _winPanel = FindFirstObjectByType<WinPanel>();
        }

        private void OnEnable()
        {
            _live = this;

            if (_session != null)
                _session.LevelLoaded += OnLevelLoaded;

            if (_progress != null)
                _progress.ProgressReset += OnProgressReset;
        }

        private void OnDisable()
        {
            // Only if it is still this one. A scene reload builds the next director before the
            // last one is disabled, and clearing unconditionally would drop the live reference
            // the moment the old one went away.
            if (_live == this)
                _live = null;

            if (_session != null)
                _session.LevelLoaded -= OnLevelLoaded;

            if (_progress != null)
                _progress.ProgressReset -= OnProgressReset;
        }

        /// <summary>
        /// A reset save is a new player's, so the tutorial offers itself again.
        /// </summary>
        /// <remarks>
        /// The milestone went with the rest of the save; this is the other half, the once-a-session
        /// rule. Someone who reset from the menu has asked to start from nothing, and "nothing"
        /// includes being shown how to play.
        /// </remarks>
        private void OnProgressReset()
        {
            _begunThisSession = false;
            _solved = false;
        }

        /// <summary>
        /// Leaving the tutorial's board ends the tutorial, whatever it was in the middle of -- and
        /// leaving it once its run has passed is finishing it.
        /// </summary>
        /// <remarks>
        /// The player can always reach the level list, so they can always walk away. Highlights that
        /// outlived the board would point at cells belonging to a level that never asked for them.
        ///
        /// The solved card offers PLAY THE FIRST LEVEL, which leaves the board without the ending
        /// card ever coming up. Recorded as walking away, that left a save with no milestone on an
        /// empty first-level board -- a first-time player -- and the tutorial started again.
        /// </remarks>
        private void OnLevelLoaded(LevelDefinition level)
        {
            if (_phase != Phase.Idle && _session.LevelName != TutorialLevel.Key)
                Stop(record: _solved);
        }

        /// <summary>Adopts the tutorial board and starts from the top. Used by the level list.</summary>
        public void Begin()
        {
            if (_session == null || _runner == null)
                return;

            _session.Adopt(TutorialLevel.Build(_runner.HalfExtents), TutorialLevel.Key);
            _phase = Phase.Intro;
            _solved = false;
            _begunThisSession = true;

            // Anything pressed before this moment belonged to a tutorial that is over.
            if (_panel != null)
                _panel.ForgetPresses();
        }

        private void Update()
        {
            if (_session == null || _runner == null || _panel == null || !_runner.IsReady)
                return;

            if (!_menuHasClosed && !UiModal.AnyOpen)
                _menuHasClosed = true;

            if (_phase != Phase.Idle && _session.State == RunState.Passed && _session.LevelName == TutorialLevel.Key)
                _solved = true;

            if (_phase == Phase.Idle)
            {
                OfferOnAFreshSave();
                return;
            }

            // Before the modal guard, because the card *is* a modal: it registers with UiModal so
            // the board holds still behind it, and a guard that fired on that would make the card
            // hide itself the frame after it appeared.
            if (_phase == Phase.Card)
            {
                RunCard();
                return;
            }

            // Something is covering the board -- the level list, or the solved panel. Either way the
            // tutorial waits rather than talking over it.
            if (UiModal.AnyOpen || WinShowing)
            {
                Hide();
                return;
            }

            if (_panel.ConsumeSkip())
            {
                Stop(record: true);
                return;
            }

            switch (_phase)
            {
                case Phase.Intro:
                    _panel.Show(Intro, showNext: true, nextCaption: "START");

                    if (_panel.ConsumeNext())
                        _phase = Phase.Steps;

                    break;

                case Phase.Steps:
                    RunSteps();
                    break;
            }
        }

        private bool WinShowing => _winPanel != null && _winPanel.IsShowing;

        /// <summary>Takes the instruction strip and every highlight off the screen.</summary>
        private void Hide()
        {
            _panel.Show(false);
            _highlighter.Begin();
            _highlighter.End();
        }

        private void ShowCard()
        {
            Hide();
            _phase = Phase.Card;

            if (_card != null)
                _card.Show(true);
        }

        /// <summary>
        /// The ending. One button, and it leads into the first level rather than merely closing.
        /// </summary>
        private void RunCard()
        {
            if (_card == null || !_card.ConsumeFinish())
                return;

            Stop(record: true);

            // Straight into the run. The tutorial sits before the first level, so finishing it and
            // arriving there should be one action rather than two.
            if (_session.AvailableLevels.Count > 0)
                _session.LoadLevel(_session.AvailableLevels[0]);
        }

        /// <summary>
        /// Starts the tutorial by itself, once, for someone who has never seen it.
        /// </summary>
        /// <remarks>
        /// Waits for the main menu to have been closed rather than firing at startup. The menu is
        /// open on boot and holds <see cref="UiModal"/>, so a tutorial that began there would run
        /// its first step behind it -- which is exactly how the first-time hints were silently
        /// swallowed until it was found.
        ///
        /// Once a session at most. Someone who walked away from it to the first level is not
        /// arriving there for the first time, and offering it again put them straight back in it.
        /// </remarks>
        private void OfferOnAFreshSave()
        {
            if (_begunThisSession || !_menuHasClosed || UiModal.AnyOpen)
                return;

            ProgressStore store = _progress != null ? _progress.Store : null;

            if (store == null || store.HasMilestone(TutorialLevel.Key))
                return;

            // Only from a standing start. Someone who has already wired something up does not want
            // to be interrupted and have their board replaced.
            if (_session.LevelName != _session.AvailableLevels[0] || !_session.Blueprint.IsEmpty)
                return;

            Begin();
        }

        private void RunSteps()
        {
            BoardFacts facts = Gather();
            int index = TutorialScript.CurrentStep(facts);

            // A passed run finishes the tutorial whatever the checklist says. "Pick up the NOT"
            // only holds while the NOT is the part in hand, so a player who picked up the decoy
            // after placing it would pass with a step still open -- and the solved card, which now
            // leads to the ending, would have led nowhere.
            if (index >= TutorialScript.Count || facts.Passed)
            {
                // The run has passed. The ordinary solved panel goes first, and the ending waits
                // until it has been shown and dismissed rather than sharing the screen with it. The
                // guard above already holds everything back while the panel is up; this is the
                // other half, "not yet presented", which the panel says itself (PresentedThisRun) --
                // "not showing" alone cannot, because on the frame a run passes the panel may simply
                // not have updated yet.
                if (_winPanel != null && !_winPanel.PresentedThisRun)
                {
                    Hide();
                    return;
                }

                ShowCard();
                return;
            }

            TutorialStep step = TutorialScript.Steps[index];

            // A failed run replaces the line rather than the step. The step is still "press RUN",
            // which is right -- Run rebuilds first, so it works straight after a failure -- but its
            // own text would claim the circuit works while the board says otherwise.
            string recovery = TutorialScript.RecoveryText(facts);

            // And the wrong part on the square the step points at says so, with the ring kept on
            // that square: the step's own line would ask for a click the square refuses.
            string correction = TutorialScript.CorrectionText(facts);

            _panel.Show(correction ?? recovery ?? step.Text);

            _highlighter.Begin();

            if (correction != null)
            {
                Point(TutorialTarget.BoardCell);
            }
            else if (recovery == null)
            {
                Point(step.From);
                Point(step.To);
            }

            _highlighter.End();
        }

        private void Point(TutorialTarget target)
        {
            switch (target)
            {
                case TutorialTarget.None:
                    return;

                case TutorialTarget.PaletteEntry:
                    if (_palette != null)
                        _highlighter.PointAt(_palette.RectFor(TutorialLevel.Part));
                    break;

                case TutorialTarget.RunButton:
                    if (_runControls != null)
                        _highlighter.PointAt(_runControls.RunButton);
                    break;

                case TutorialTarget.BoardCell:
                    _highlighter.PointAt(CellToWorld(TutorialLevel.GateCell), 1.4f);
                    break;

                case TutorialTarget.Bin:
                    if (TryFixture(TutorialLevel.SinkId, out int binId))
                        _highlighter.PointAt(_runner.PositionOf(binId), 1.6f);
                    break;

                case TutorialTarget.SourcePort:
                    PointAtPort(TutorialLevel.SourceId, isInput: false, index: 0);
                    break;

                case TutorialTarget.SinkPort:
                    PointAtPort(TutorialLevel.SinkId, isInput: true, index: 0);
                    break;

                case TutorialTarget.GateInput:
                    PointAtGatePort(isInput: true);
                    break;

                case TutorialTarget.GateOutput:
                    PointAtGatePort(isInput: false);
                    break;
            }
        }

        private void PointAtPort(string fixtureId, bool isInput, int index)
        {
            if (TryFixture(fixtureId, out int nodeId))
                PointAtPortOf(nodeId, isInput, index);
        }

        private void PointAtGatePort(bool isInput)
        {
            if (TryGate(out int gateId))
                PointAtPortOf(gateId, isInput, 0);
        }

        /// <summary>
        /// Rings a port using the geometry the hit tester uses, so the ring is where the click has
        /// to land rather than near it.
        /// </summary>
        private void PointAtPortOf(int nodeId, bool isInput, int index)
        {
            Node node = _runner.NodeAt(nodeId);

            if (node == null)
                return;

            int count = isInput ? node.InputCount : node.OutputCount;

            if (index >= count)
                return;

            Vector2 centre = _runner.PositionOf(nodeId);
            _highlighter.PointAt(PortGeometry.PositionOf(centre, isInput, index, count), 0.7f);
        }

        private bool TryFixture(string id, out int nodeId) =>
            _runner.FixtureNodeIds.TryGetValue(id, out nodeId);

        /// <summary>
        /// The part the tutorial asked for, on the cell it asked for, if the player has placed it.
        /// </summary>
        /// <remarks>
        /// The kind is checked as well as the cell. The palette offers a decoy, and dropping that on
        /// the right square is not the step: without this the tutorial would accept the wrong part
        /// and move on to wiring a gate the level cannot be solved with.
        /// </remarks>
        private bool TryGate(out int nodeId)
        {
            nodeId = -1;

            SimulationView view = _runner.View;

            for (int id = 0; id < view.NodeCount; id++)
            {
                Node node = view.GetNode(id);

                if (node == null || !(node is NotGate))
                    continue;

                if (_runner.TryCellOf(id, out Vector2Int cell) && cell == TutorialLevel.GateCell)
                {
                    nodeId = id;
                    return true;
                }
            }

            return false;
        }

        private Vector2 CellToWorld(Vector2Int cell) =>
            _grid != null ? _grid.CellToWorld(cell) : new Vector2(cell.x * 2f, cell.y * 2f);

        /// <summary>Reads the board into the plain values the steps are written against.</summary>
        private BoardFacts Gather()
        {
            bool gate = TryGate(out int gateId);

            bool sourceWired = gate
                && TryFixture(TutorialLevel.SourceId, out int sourceId)
                && IsWired(sourceId, gateId);

            bool binWired = gate
                && TryFixture(TutorialLevel.SinkId, out int binId)
                && IsWired(gateId, binId);

            GateKind? onCell = _session.Blueprint.TryGetPlacement(TutorialLevel.GateCell, out GateKind placed)
                ? placed
                : (GateKind?)null;

            // One NOT is stocked, so one on the board that is not on its square is on another.
            bool elsewhere = _session.Blueprint.CountOf(TutorialLevel.Part) > 0 && onCell != TutorialLevel.Part;

            return new BoardFacts(
                selected: _placement != null ? _placement.Selected : default,
                gateOnCell: gate,
                sourceWiredToGate: sourceWired,
                gateWiredToBin: binWired,
                running: _session.State == RunState.Running,
                passed: _session.State == RunState.Passed,
                runFailed: _session.State == RunState.Failed,
                partOnCell: onCell,
                partElsewhere: elsewhere);
        }

        /// <summary>
        /// Whether any wire runs from one node to another.
        /// </summary>
        /// <remarks>
        /// Asked of the built graph rather than the blueprint, so it reads the same board the player
        /// is looking at -- and node to node rather than port to port, because on a one-input gate
        /// there is only one port it could be and naming it would be precision the step does not
        /// ask for.
        /// </remarks>
        private bool IsWired(int fromNode, int toNode)
        {
            SimulationView view = _runner.View;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);

                if (edge == null)
                    continue;   // retired id

                if (edge.Source.Owner.Id == fromNode && edge.Target.Owner.Id == toNode)
                    return true;
            }

            return false;
        }

        private void Stop(bool record)
        {
            _phase = Phase.Idle;

            if (_panel != null)
                _panel.Show(false);

            if (_card != null)
                _card.Show(false);

            if (_highlighter != null)
            {
                _highlighter.Begin();
                _highlighter.End();
            }

            ProgressStore store = record && _progress != null ? _progress.Store : null;

            // Recorded on skip as well as on finishing. Someone who chose to leave has decided they
            // do not need it, and being handed it again next launch would be the game arguing.
            store?.MarkMilestone(TutorialLevel.Key);
        }
    }
}
