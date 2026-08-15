using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Owns the simulation, drives it on a fixed interval, and holds the screen layout.
    /// </summary>
    /// <remarks>
    /// Layout lives here and never in LogicCore. The simulator has no concept of position, and it
    /// must stay that way -- a node's id is the only thing the two sides agree on.
    /// </remarks>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [Tooltip("Seconds of real time per simulation tick.")]
        [SerializeField] private float _tickInterval = 0.5f;

        [Tooltip("Rebuild the graph once every bit has drained, so the demo loops forever.")]
        [SerializeField] private bool _restartWhenIdle = true;

        [Tooltip("Delay on both edges leaving source A.")]
        [SerializeField] private int _sourceADelay = 1;

        [Tooltip("Delay on both edges leaving source B. Set this higher than source A's to " +
                 "watch the gates hold one input and wait for the other.")]
        [SerializeField] private int _sourceBDelay = 3;

        [SerializeField] private PlacementGrid _grid;

        /// <summary>Node id to screen position. The authority for layout, and view-side only.</summary>
        private readonly Dictionary<int, Vector2> _layout = new Dictionary<int, Vector2>();

        /// <summary>Cell to node id: the reverse lookup that makes a click hit-testable.</summary>
        private readonly Dictionary<Vector2Int, int> _occupied = new Dictionary<Vector2Int, int>();

        private Simulation _simulation;
        private float _accumulator;

        /// <summary>
        /// Bumped whenever the graph's shape changes. Renderers that build visuals once compare
        /// against this to know when to rebuild.
        /// </summary>
        public int GraphRevision { get; private set; }

        /// <summary>Read-only handle for the renderers. Fetch it per frame; it is a struct.</summary>
        public SimulationView View => _simulation.View;

        public bool IsReady => _simulation != null;

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
            if (!IsReady)
                return;

            _accumulator = 0f;
            _simulation.Tick();

            if (_restartWhenIdle && IsIdle())
                Build();
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
            _layout.TryGetValue(nodeId, out Vector2 position) ? position : Vector2.zero;

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
            IsReady && nodeId >= 0 && nodeId < _simulation.NodeCount ? _simulation.GetNode(nodeId) : null;

        // -----------------------------------------------------------------
        // Rejected edits
        // -----------------------------------------------------------------

        /// <summary>Why the last attempted edit was refused, for the HUD to surface.</summary>
        public string LastRejectionReason { get; private set; }

        public float LastRejectionTime { get; private set; } = float.NegativeInfinity;

        public bool WasRecentlyRejected(float seconds) => Time.time - LastRejectionTime < seconds;

        /// <summary>
        /// Records a refusal. Shared by placement and wiring so the HUD has one channel to read
        /// rather than polling each controller.
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
            // Found before Build, which needs the grid to turn fixture cells into world positions.
            // Safe in Awake because cell size is a serialized field, not something the grid
            // computes for itself later.
            if (_grid == null)
                _grid = FindFirstObjectByType<PlacementGrid>();

            Build();
        }

        private void Update()
        {
            // Paused deliberately leaves the accumulator alone, so TickProgress holds its value
            // and bits freeze mid-wire instead of snapping back to their last whole-tick position.
            if (!IsReady || IsPaused || _tickInterval <= 0f)
                return;

            _accumulator += Time.deltaTime;

            while (_accumulator >= _tickInterval)
            {
                _accumulator -= _tickInterval;
                _simulation.Tick();
            }

            if (_restartWhenIdle && IsIdle())
                Build();
        }

        /// <summary>
        /// The half adder from HalfAdderTests: sum is A xor B, carry is A and B. Each source fans
        /// out from its single output port to both gates, so the two gates see the same bits.
        /// </summary>
        /// <remarks>
        /// The two source delays are deliberately unequal by default, which makes the gates visibly
        /// hold one input while waiting for the other -- and then corrupt, because the waiting bit
        /// is still sitting there when the next one lands. The logic is untouched; only the wire
        /// lengths differ. Set both delays equal to get a correctly timed half adder back.
        /// </remarks>
        private void Build()
        {
            _simulation = new Simulation();
            _accumulator = 0f;

            // One tick per column: the four rows of the truth table, in order.
            Bit[] streamA = { Bit.Zero, Bit.Zero, Bit.One, Bit.One };
            Bit[] streamB = { Bit.Zero, Bit.One, Bit.Zero, Bit.One };

            SourceNode sourceA = _simulation.Add(new SourceNode(streamA) { Name = "A" });
            SourceNode sourceB = _simulation.Add(new SourceNode(streamB) { Name = "B" });
            XorGate sum = _simulation.Add(new XorGate { Name = "Sum (XOR)" });
            AndGate carry = _simulation.Add(new AndGate { Name = "Carry (AND)" });
            SinkNode sumSink = _simulation.Add(new SinkNode() { Name = "Sum out" });
            SinkNode carrySink = _simulation.Add(new SinkNode() { Name = "Carry out" });

            int delayA = Mathf.Max(1, _sourceADelay);
            int delayB = Mathf.Max(1, _sourceBDelay);

            _simulation.Connect(sourceA.Out(0), sum.In(0), delayA);
            _simulation.Connect(sourceA.Out(0), carry.In(0), delayA);
            _simulation.Connect(sourceB.Out(0), sum.In(1), delayB);
            _simulation.Connect(sourceB.Out(0), carry.In(1), delayB);
            _simulation.Connect(sum.Out(0), sumSink.In(0), 1);
            _simulation.Connect(carry.Out(0), carrySink.In(0), 1);

            _layout.Clear();
            _occupied.Clear();

            // Cells, not world units. On the default 2-unit grid these are the same six positions
            // the fixture has always used, but expressed so the cells read as occupied and the
            // player cannot place on top of them.
            PlaceFixture(sourceA, new Vector2Int(-3, 1));
            PlaceFixture(sourceB, new Vector2Int(-3, -1));
            PlaceFixture(sum, new Vector2Int(0, 1));
            PlaceFixture(carry, new Vector2Int(0, -1));
            PlaceFixture(sumSink, new Vector2Int(3, 1));
            PlaceFixture(carrySink, new Vector2Int(3, -1));

            GraphRevision++;
        }

        private void PlaceFixture(Node node, Vector2Int cell)
        {
            _layout[node.Id] = CellToWorld(cell);
            _occupied[cell] = node.Id;
        }

        private Vector2 CellToWorld(Vector2Int cell) =>
            _grid != null ? _grid.CellToWorld(cell) : new Vector2(cell.x * 2f, cell.y * 2f);

        // -----------------------------------------------------------------
        // Editing
        // -----------------------------------------------------------------

        /// <summary>
        /// Places a gate on an empty cell. Returns false if the cell is taken.
        /// </summary>
        /// <remarks>
        /// The first successful edit switches off auto-restart for good. Otherwise the graph would
        /// rebuild itself the next time it drained and silently delete everything the player put
        /// down.
        /// </remarks>
        public bool TryPlaceGate(GateKind kind, Vector2Int cell)
        {
            if (!IsReady || _occupied.ContainsKey(cell))
                return false;

            Node node = _simulation.Add(GatePalette.Create(kind));
            _layout[node.Id] = CellToWorld(cell);
            _occupied[cell] = node.Id;

            MarkEdited();
            return true;
        }

        /// <summary>
        /// Removes whatever occupies a cell, along with every edge touching it. Returns false if
        /// the cell is empty.
        /// </summary>
        public bool TryRemoveAt(Vector2Int cell)
        {
            if (!IsReady || !_occupied.TryGetValue(cell, out int nodeId))
                return false;

            Node node = _simulation.GetNode(nodeId);
            _occupied.Remove(cell);
            _layout.Remove(nodeId);

            if (node == null)
                return false;   // stale entry; the cell is now correctly empty

            _simulation.Remove(node);
            MarkEdited();
            return true;
        }

        private void MarkEdited()
        {
            // From the first edit on this is a sandbox, not a looping demo.
            _restartWhenIdle = false;
            GraphRevision++;
        }

        // -----------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------

        /// <summary>
        /// Creates an edge between two ports if the drag is legal, reporting the reason if not.
        /// Either end may have been grabbed first; the edge is always output to input.
        /// </summary>
        public bool TryConnect(PortAddress from, PortAddress to, int delay = 1)
        {
            if (!IsReady)
                return false;

            WiringVerdict verdict = WiringRules.Validate(View, from, to);

            if (!verdict.IsValid)
            {
                RejectEdit(verdict.Reason);   // null reason stays silent
                return false;
            }

            _simulation.Connect(verdict.Source, verdict.Target, delay);
            MarkEdited();
            return true;
        }

        /// <summary>
        /// Deletes the wire nearest to a world point, within
        /// <see cref="PortGeometry.WireHitRadius"/>. Bits travelling it are destroyed and are not
        /// counted as corruption -- an edit is not a collision.
        /// </summary>
        public bool TryDeleteWireAt(Vector2 world)
        {
            if (!IsReady)
                return false;

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

            if (nearest == null)
                return false;

            _simulation.Disconnect(nearest);
            MarkEdited();
            return true;
        }

        /// <summary>
        /// True once nothing can ever happen again: no bits are in transit and no source has
        /// anything left to emit.
        /// </summary>
        /// <remarks>
        /// Deliberately does not require the input ports to be empty. With unequal source delays
        /// the faster stream runs out first, stranding the slower stream's last bit in a port
        /// whose partner will never arrive. That is a terminal state, not a busy one -- a node
        /// with every port filled would already have fired during the last evaluate phase, so if
        /// nothing is in flight, nothing can become ready. Requiring empty ports here would leave
        /// the demo frozen on that stranded bit instead of looping.
        /// </remarks>
        private bool IsIdle()
        {
            SimulationView view = _simulation.View;

            for (int id = 0; id < view.EdgeCount; id++)
            {
                Edge edge = view.GetEdge(id);   // null for a removed id
                if (edge != null && edge.InTransitCount > 0)
                    return false;
            }

            // The 'is' pattern already yields false for a null, so removed ids fall through safely.
            for (int id = 0; id < view.NodeCount; id++)
            {
                if (view.GetNode(id) is SourceNode source && !source.IsExhausted)
                    return false;
            }

            return true;
        }
    }
}
