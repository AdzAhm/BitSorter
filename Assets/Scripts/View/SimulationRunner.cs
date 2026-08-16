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

        /// <summary>
        /// Fixture id to node id, from the most recent rebuild. The grader's way in: a level names its
        /// sinks, and the simulator only knows ids.
        /// </summary>
        public IReadOnlyDictionary<string, int> FixtureNodeIds =>
            _circuit != null ? _circuit.FixtureNodeIds : EmptyFixtureIds;

        private static readonly Dictionary<string, int> EmptyFixtureIds = new Dictionary<string, int>();

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
            _circuit.Simulation.Tick();
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
            _tickInterval <= 0f ? 0f : Mathf.Clamp01(_accumulator / _tickInterval);

        /// <summary>Screen position for a node id, or the origin if the id is unknown.</summary>
        public Vector2 PositionOf(int nodeId) =>
            TryCellOf(nodeId, out Vector2Int cell) ? CellToWorld(cell) : Vector2.zero;

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
            // Paused deliberately leaves the accumulator alone, so TickProgress holds its value
            // and bits freeze mid-wire instead of snapping back to their last whole-tick position.
            if (!IsReady || !ClockRunning || IsPaused || _tickInterval <= 0f)
                return;

            _accumulator += Time.deltaTime;

            while (_accumulator >= _tickInterval)
            {
                _accumulator -= _tickInterval;
                _circuit.Simulation.Tick();
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
            _accumulator = 0f;

            GraphRevision++;
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
                if (edge == null)
                    continue;   // retired id

                Vector2 a = PortGeometry.EndpointOf(edge.Source, PositionOf(edge.Source.Owner.Id));
                Vector2 b = PortGeometry.EndpointOf(edge.Target, PositionOf(edge.Target.Owner.Id));

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
