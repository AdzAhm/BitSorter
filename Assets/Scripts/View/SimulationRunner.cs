using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Owns the simulation, drives its clock, and holds the screen layout. Knows nothing about levels,
    /// budgets or grading -- <see cref="LevelSession"/> owns those and calls
    /// <see cref="Rebuild"/> here.
    /// </summary>
    /// <remarks>
    /// Layout lives here and never in LogicCore. The simulator has no concept of position, and it
    /// must stay that way -- a node's id is the only thing the two sides agree on.
    ///
    /// There is no reset and no snapshot. A Simulation is a derived, disposable artifact: every
    /// rebuild throws the old one away and constructs a fresh graph from a
    /// <see cref="CircuitBlueprint"/>. That is what makes the level's Reset button a one-liner, and it
    /// is why nothing here has to know how to deep-copy node state.
    /// </remarks>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [Tooltip("Seconds of real time per simulation tick.")]
        [SerializeField] private float _tickInterval = 0.5f;

        [SerializeField] private PlacementGrid _grid;

        /// <summary>
        /// The most recent build. Holds the graph plus the layout table -- node id to cell -- which is
        /// view-side and never in LogicCore.
        /// </summary>
        private BuiltCircuit _circuit;

        private float _accumulator;

        /// <summary>
        /// Bumped whenever the graph's shape changes. Renderers that build visuals once compare
        /// against this to know when to rebuild.
        /// </summary>
        public int GraphRevision { get; private set; }

        /// <summary>Read-only handle for the renderers. Fetch it per frame; it is a struct.</summary>
        public SimulationView View => _circuit.Simulation.View;

        public bool IsReady => _circuit != null;

        /// <summary>The level the current graph was built from, or null before the first build.</summary>
        /// <remarks>
        /// For anything that hears <see cref="Rebuilt"/> and needs to know what the new graph's
        /// fixtures are: it is the level passed to <see cref="Rebuild"/>, so it cannot disagree with
        /// the graph, whatever order the session updates its own state in.
        /// </remarks>
        public LevelDefinition BuiltLevel { get; private set; }

        /// <summary>
        /// Raised after every tick of the live graph, with the tick just executed, from inside the
        /// call that ran it.
        /// </summary>
        /// <remarks>
        /// For anything that records what happened on each tick, which is the timing diagram. Several
        /// ticks can run in one frame -- the timed loop catches up after a slow frame or at free
        /// play's faster speeds, and <see cref="StepOneTick"/> runs one per call, as often as a caller
        /// likes -- so a poll once a frame would see only the last of them. Per-tick facts do not
        /// accumulate the way <see cref="Simulation.CorruptionSites"/> does: which bit a wire
        /// delivered on a tick is gone by the next one.
        ///
        /// A handler must not throw. It runs inside the timed loop, and an exception would end that
        /// frame's catching up part-way.
        /// </remarks>
        public event Action<int> Ticked;

        /// <summary>
        /// Raised when <see cref="Rebuild"/> has replaced the graph, so the clock is back at tick 0.
        /// </summary>
        /// <remarks>
        /// Every way a run restarts comes through here -- RUN, RESET, every edit, undo, a level
        /// loading -- and a recording of the old graph describes nothing on the new one. Raised in
        /// the call rather than left to a poll of <see cref="GraphRevision"/>, because RUN and the
        /// new graph's first tick can fall in the same frame, and a poll after that tick would clear
        /// it away.
        /// </remarks>
        public event Action Rebuilt;

        /// <summary>
        /// Fixture id to node id, from the most recent rebuild. The grader's way in: a level names its
        /// sinks, and the simulator only knows ids.
        /// </summary>
        public IReadOnlyDictionary<string, int> FixtureNodeIds =>
            _circuit != null ? _circuit.FixtureNodeIds : EmptyFixtureIds;

        private static readonly Dictionary<string, int> EmptyFixtureIds = new Dictionary<string, int>();

        /// <summary>Node id to the cell it sits on, from the most recent rebuild.</summary>
        public IReadOnlyDictionary<int, Vector2Int> NodeCells =>
            _circuit != null ? _circuit.Cells : EmptyCells;

        private static readonly Dictionary<int, Vector2Int> EmptyCells = new Dictionary<int, Vector2Int>();

        /// <summary>
        /// Whether the clock may advance at all. Set by the level session, which holds it false while
        /// the player is editing.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="IsPaused"/> on purpose. Pause is the player's inspection toggle and
        /// they may hit it whenever they like; this is the run state's gate. Folding the two together
        /// would let Space start the clock while the board is still being edited, which would break
        /// the rule that an editable graph sits at tick 0.
        /// </remarks>
        public bool ClockRunning { get; set; }

        /// <summary>While paused the clock does not advance, so bits hold their on-screen position.</summary>
        public bool IsPaused { get; private set; }

        public void TogglePause() => IsPaused = !IsPaused;

        public void SetPaused(bool paused) => IsPaused = paused;

        /// <summary>
        /// Advances exactly one tick, for stepping while paused. Clearing the accumulator restarts
        /// interpolation at the new tick, so bits move forward into their new positions rather
        /// than snapping backwards from wherever the clock was frozen.
        /// </summary>
        public void StepOneTick()
        {
            // Gated on the clock as well, so stepping cannot walk an editable graph off tick 0.
            if (!IsReady || !ClockRunning)
                return;

            _accumulator = 0f;
            Advance();
        }

        /// <summary>Runs one tick and says so: the one way the live graph is ticked.</summary>
        private void Advance()
        {
            _circuit.Simulation.Tick();
            Ticked?.Invoke(_circuit.Simulation.CurrentTick - 1);
        }

        /// <summary>
        /// How far the clock has run into the tick that has not happened yet, 0 to 1.
        /// </summary>
        /// <remarks>
        /// Renderers need this to interpolate. The simulator moves bits only on whole ticks, so
        /// without it a bit on a delay-1 edge would report a progress of 0 for its entire life and
        /// sit motionless on top of its source node.
        /// </remarks>
        public float TickProgress =>
            Interval <= 0f ? 0f : Mathf.Clamp01(_accumulator / Interval);

        /// <summary>The authored rate, and the speed it is currently run at.</summary>
        public const int DefaultSpeed = 1;

        private int _speed = DefaultSpeed;

        /// <summary>
        /// How many times the authored rate the clock runs at.
        /// </summary>
        /// <remarks>
        /// Offered in free play only, where a circuit the player built themselves can take the best
        /// part of a minute to say what it does. Held here rather than in the panel because the clock
        /// is here; the level session puts it back to <see cref="DefaultSpeed"/> whenever a level is
        /// installed, so a fast sandbox cannot leak into a taught level.
        /// </remarks>
        public int Speed
        {
            get => _speed;
            set => _speed = value < DefaultSpeed ? DefaultSpeed : value;
        }

        /// <summary>Seconds per tick at the current speed.</summary>
        private float Interval => IntervalFor(_tickInterval, _speed);

        /// <summary>
        /// Seconds per tick for an authored interval run at a speed. Pure, so the arithmetic is
        /// checkable without a scene.
        /// </summary>
        public static float IntervalFor(float authored, int speed) =>
            authored / (speed < DefaultSpeed ? DefaultSpeed : speed);

        /// <summary>Screen position for a node id, or the origin if the id is unknown.</summary>
        /// <remarks>
        /// A block's nodes all stand on its top cell in the layout table, and are placed here instead:
        /// a port anchor where its port lands on the box's face, and a gate inside at the box's middle.
        /// </remarks>
        public Vector2 PositionOf(int nodeId)
        {
            if (!TryCellOf(nodeId, out Vector2Int cell))
                return Vector2.zero;

            int block = _circuit.BlockOf(nodeId);

            if (block < 0)
                return CellToWorld(cell);

            BuiltBlock built = _circuit.Blocks[block];

            if (_circuit.TryAnchorOf(nodeId, out bool isInput, out int port))
                return PortGeometry.AnchorCentre(BlockPortPosition(built, isInput, port), isInput);

            return BlockCentre(built);
        }

        /// <summary>The most recent build, for what needs more of it than the graph.</summary>
        public BuiltCircuit Circuit => _circuit;

        /// <summary>The middle of a block's box, in world space.</summary>
        public Vector2 BlockCentre(BuiltBlock block) =>
            PortGeometry.BlockCentre(CellToWorld(block.Cell), block.Definition.Height, CellSize);

        /// <summary>How tall a block's box is drawn, in world units.</summary>
        public float BlockHeight(BuiltBlock block) => PortGeometry.BlockHeight(block.Definition.Height, CellSize);

        /// <summary>Where one of a block's ports sits on its box, in world space.</summary>
        public Vector2 BlockPortPosition(BuiltBlock block, bool isInput, int port) =>
            PortGeometry.BlockPortPosition(BlockCentre(block), BlockHeight(block), isInput, port,
                isInput ? block.Definition.Inputs.Count : block.Definition.Outputs.Count);

        private float CellSize => _grid != null ? _grid.CellSize : 2f;

        /// <summary>Whether a node is drawn as itself: every part on the board, and nothing of a block's.</summary>
        public bool IsShownNode(int nodeId) => !IsReady || _circuit.BlockOf(nodeId) < 0;

        /// <summary>
        /// Whether a port is on the board to be seen and wired: every port of a part, and the side of
        /// a block's anchor that faces out.
        /// </summary>
        public bool IsShownPort(int nodeId, bool isInput) => !IsReady || _circuit.IsShownPort(nodeId, isInput);

        /// <summary>How an edge is drawn. See <see cref="EdgeShape"/>.</summary>
        public EdgeShape ShapeOf(Edge edge) => _circuit.Shapes[edge.Id];

        /// <summary>Whether an edge draws a wire: false inside a block, and for the copies of one.</summary>
        public bool IsDrawn(Edge edge) =>
            IsReady && edge != null && edge.Id >= 0 && edge.Id < _circuit.Shapes.Count && _circuit.Shapes[edge.Id].Drawn;

        /// <summary>The two ends a drawn edge's wire runs between, from the shared port geometry.</summary>
        public void DrawnEndsOf(Edge edge, out Vector2 from, out Vector2 to)
        {
            EdgeShape shape = _circuit.Shapes[edge.Id];

            from = PortGeometry.EndpointOf(shape.DrawnFrom, PositionOf(shape.DrawnFrom.Owner.Id));
            to = PortGeometry.EndpointOf(shape.DrawnTo, PositionOf(shape.DrawnTo.Owner.Id));
        }

        /// <summary>
        /// Where an input port is shown: on its part, or for a port inside a block, on the box that
        /// hides it.
        /// </summary>
        public Vector2 ShownPositionOf(InputPort port)
        {
            int block = _circuit.BlockOf(port.Owner.Id);

            return block >= 0 && !_circuit.IsShownPort(port.Owner.Id, true)
                ? BlockCentre(_circuit.Blocks[block])
                : PortGeometry.EndpointOf(port, PositionOf(port.Owner.Id));
        }

        /// <summary>The port on the board a node's port stands for, as the blueprint stores wires.</summary>
        public bool TryBoardPort(int nodeId, bool isInput, int index, out CellPort port)
        {
            if (_circuit != null)
                return _circuit.TryBoardPort(nodeId, isInput, index, out port);

            port = default;
            return false;
        }

        /// <summary>World position of a port, from the shared geometry both sides agree on.</summary>
        public Vector2 PositionOf(PortAddress address)
        {
            Node node = NodeAt(address.NodeId);
            if (node == null)
                return Vector2.zero;

            int count = address.IsInput ? node.InputCount : node.OutputCount;
            return PortGeometry.PositionOf(PositionOf(address.NodeId), address.IsInput, address.Index, count);
        }

        /// <summary>The node with this id, or null if it is out of range or has been removed.</summary>
        public Node NodeAt(int nodeId) =>
            IsReady && nodeId >= 0 && nodeId < _circuit.Simulation.NodeCount
                ? _circuit.Simulation.GetNode(nodeId)
                : null;

        /// <summary>
        /// The cell a node sits on. False for an unknown id, which a drag begun before a rebuild can
        /// legitimately be holding.
        /// </summary>
        public bool TryCellOf(int nodeId, out Vector2Int cell)
        {
            if (_circuit != null)
                return _circuit.Cells.TryGetValue(nodeId, out cell);

            cell = default;
            return false;
        }

        /// <summary>The playfield's half extents in cells, for rules that need the board edge.</summary>
        public Vector2Int HalfExtents =>
            _grid != null ? _grid.HalfExtents : new Vector2Int(4, 2);

        /// <summary>The scene's own board, which a level that does not name one is played on.</summary>
        public Vector2Int DefaultHalfExtents =>
            _grid != null ? _grid.DefaultHalfExtents : new Vector2Int(4, 2);

        /// <summary>Sizes the board for a level: its own, or the scene's when it names none.</summary>
        public void ResizeBoard(Vector2Int halfExtents)
        {
            if (_grid != null)
                _grid.Resize(halfExtents);
        }

        // -----------------------------------------------------------------
        // Rejected edits
        // -----------------------------------------------------------------

        /// <summary>Why the last attempted edit was refused, for the HUD to surface.</summary>
        public string LastRejectionReason { get; private set; }

        public float LastRejectionTime { get; private set; } = float.NegativeInfinity;

        public bool WasRecentlyRejected(float seconds) => Time.time - LastRejectionTime < seconds;

        /// <summary>
        /// Records a refusal. Shared by placement, wiring and the level rules so the HUD has one
        /// channel to read rather than polling each controller.
        /// </summary>
        public void RejectEdit(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return;   // some rejections are deliberately silent, such as a click that did not drag

            LastRejectionReason = reason;
            LastRejectionTime = Time.time;
        }

        private void Awake()
        {
            // Found before any rebuild, which needs the grid to turn cells into world positions. Safe
            // in Awake because cell size is a serialized field, not something the grid computes later.
            if (_grid == null)
                _grid = FindFirstObjectByType<PlacementGrid>();
        }

        private void Update()
        {
            float interval = Interval;

            // Paused deliberately leaves the accumulator alone, so TickProgress holds its value
            // and bits freeze mid-wire instead of snapping back to their last whole-tick position.
            if (!IsReady || !ClockRunning || IsPaused || interval <= 0f)
                return;

            _accumulator += Time.deltaTime;

            while (_accumulator >= interval)
            {
                _accumulator -= interval;
                Advance();
            }
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        /// <summary>
        /// Throws away the current graph and builds a fresh one from a level's fixtures plus the
        /// player's blueprint. The only way a graph ever comes into existence.
        /// </summary>
        /// <remarks>
        /// The construction itself lives in <see cref="CircuitBuilder"/>, which is static and needs no
        /// GameObject -- so the grading tests can build the same graph the game does. All this adds is
        /// the clock reset and the revision bump the renderers watch.
        /// </remarks>
        public void Rebuild(LevelDefinition level, CircuitBlueprint blueprint)
        {
            _circuit = CircuitBuilder.Build(level, blueprint);
            BuiltLevel = level;
            _accumulator = 0f;

            GraphRevision++;
            Rebuilt?.Invoke();
        }

        private Vector2 CellToWorld(Vector2Int cell) =>
            _grid != null ? _grid.CellToWorld(cell) : new Vector2(cell.x * 2f, cell.y * 2f);

        // -----------------------------------------------------------------
        // Queries the level session builds edits on
        // -----------------------------------------------------------------

        /// <summary>
        /// The wire nearest a world point, within <see cref="PortGeometry.WireHitRadius"/>, or null.
        /// Geometry lives here because layout does; the session decides what to do with the answer.
        /// </summary>
        public Edge NearestEdge(Vector2 world)
        {
            if (!IsReady)
                return null;

            SimulationView view = View;
            Edge nearest = null;
            float nearestDistance = PortGeometry.WireHitRadius;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);

                // A retired id, a wire inside a block, or a copy of a wire already drawn.
                if (edge == null || !IsDrawn(edge))
                    continue;

                DrawnEndsOf(edge, out Vector2 a, out Vector2 b);

                float distance = PortGeometry.DistanceToSegment(world, a, b);
                if (distance > nearestDistance)
                    continue;

                nearestDistance = distance;
                nearest = edge;
            }

            return nearest;
        }

        /// <summary>
        /// True once nothing can ever happen again. Delegates to <see cref="LevelGrader.IsSettled"/>
        /// so the game and the tests share one definition of a finished run.
        /// </summary>
        public bool IsIdle() => IsReady && LevelGrader.IsSettled(_circuit.Simulation.View);
    }
}
