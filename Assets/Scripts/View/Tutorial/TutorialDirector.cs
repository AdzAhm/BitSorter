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
    /// Three phases either side of the six steps. An opening line orients the player before being
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

        private const string Intro =
            "Welcome. A bit starts at A on the left and has to reach the bin on the right. " +
            "Follow the highlights.";

        private Phase _phase = Phase.Idle;
        private bool _menuHasClosed;

        /// <summary>Whether the tutorial is running right now.</summary>
        public bool IsRunning => _phase != Phase.Idle;

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

        /// <summary>
        /// Leaving the tutorial's board ends the tutorial, whatever it was in the middle of.
        /// </summary>
        /// <remarks>
        /// The player can always reach the level list, so they can always walk away. Highlights that
        /// outlived the board would point at cells belonging to a level that never asked for them.
        /// </remarks>
        private void OnLevelLoaded(LevelDefinition level)
        {
            if (_phase != Phase.Idle && _session.LevelName != TutorialLevel.Key)
                Stop(record: false);
        }

        /// <summary>Adopts the tutorial board and starts from the top. Used by the level list.</summary>
        public void Begin()
        {
            if (_session == null || _runner == null)
                return;

            _session.Adopt(TutorialLevel.Build(_runner.HalfExtents), TutorialLevel.Key);
            _phase = Phase.Intro;

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

            if (_phase == Phase.Idle)
            {
                OfferOnAFreshSave();
                return;
            }

            // A panel over the board means the player has gone looking for something else. The
            // tutorial waits rather than talking over it.
            if (UiModal.AnyOpen)
            {
                _panel.Show(false);
                _highlighter.Begin();
                _highlighter.End();
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

                case Phase.Card:
                    _panel.Show(
                        "That is the whole loop. Everything else you can do:\n" +
                        ControlsReference.Card,
                        showNext: true, nextCaption: "DONE");

                    if (_panel.ConsumeNext())
                        Stop(record: true);

                    break;
            }
        }

        /// <summary>
        /// Starts the tutorial by itself, once, for someone who has never seen it.
        /// </summary>
        /// <remarks>
        /// Waits for the main menu to have been closed rather than firing at startup. The menu is
        /// open on boot and holds <see cref="UiModal"/>, so a tutorial that began there would run
        /// its first step behind it -- which is exactly how the first-time hints were silently
        /// swallowed until it was found.
        /// </remarks>
        private void OfferOnAFreshSave()
        {
            if (!_menuHasClosed || UiModal.AnyOpen)
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

            if (index >= TutorialScript.Count)
            {
                _phase = Phase.Card;
                return;
            }

            TutorialStep step = TutorialScript.Steps[index];

            // A failed run replaces the line rather than the step. The step is still "press RUN",
            // which is right -- Run rebuilds first, so it works straight after a failure -- but its
            // own text would claim the circuit works while the board says otherwise.
            string recovery = TutorialScript.RecoveryText(facts);

            _panel.Show(recovery ?? step.Text);

            _highlighter.Begin();

            if (recovery == null)
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

            return new BoardFacts(
                selected: _placement != null ? _placement.Selected : default,
                gateOnCell: gate,
                sourceWiredToGate: sourceWired,
                gateWiredToBin: binWired,
                running: _session.State == RunState.Running,
                passed: _session.State == RunState.Passed,
                runFailed: _session.State == RunState.Failed);
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
