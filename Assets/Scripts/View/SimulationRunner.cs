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

        private readonly Dictionary<int, Vector2> _layout = new Dictionary<int, Vector2>();
        private Simulation _simulation;
        private float _accumulator;

        /// <summary>Read-only handle for the renderers. Fetch it per frame; it is a struct.</summary>
        public SimulationView View => _simulation.View;

        public bool IsReady => _simulation != null;

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

        private void Awake()
        {
            Build();
        }

        private void Update()
        {
            if (_tickInterval <= 0f)
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
            _layout[sourceA.Id] = new Vector2(-6f, 2f);
            _layout[sourceB.Id] = new Vector2(-6f, -2f);
            _layout[sum.Id] = new Vector2(0f, 2f);
            _layout[carry.Id] = new Vector2(0f, -2f);
            _layout[sumSink.Id] = new Vector2(6f, 2f);
            _layout[carrySink.Id] = new Vector2(6f, -2f);
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
                if (view.GetEdge(id).InTransitCount > 0)
                    return false;
            }

            for (int id = 0; id < view.NodeCount; id++)
            {
                if (view.GetNode(id) is SourceNode source && !source.IsExhausted)
                    return false;
            }

            return true;
        }
    }
}
