using System.Collections.Generic;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>
    /// What one row of the timing diagram holds on one tick: a bit or none, and whether a collision
    /// took a bit where it arrived.
    /// </summary>
    public readonly struct WaveCell
    {
        internal const byte NoBit = 0;
        internal const byte ZeroBit = 1;
        internal const byte OneBit = 2;
        internal const byte BitMask = 3;
        internal const byte CollidedFlag = 4;

        private readonly byte _code;

        internal WaveCell(byte code) => _code = code;

        /// <summary>The bit on this tick, or null for none.</summary>
        public Bit? Value =>
            (_code & BitMask) == OneBit ? Bit.One :
            (_code & BitMask) == ZeroBit ? Bit.Zero : (Bit?)null;

        /// <summary>
        /// Whether a bit was destroyed where this row's bit arrived, on this tick.
        /// </summary>
        /// <remarks>
        /// "Arrived into a collision", not "was the bit destroyed": when two bits of one value land in
        /// an empty port together, one copy survives and the model does not say which -- only edge
        /// order would -- so both of them are marked. A row can therefore show more crosses than the
        /// bits-lost meter counts, and must never be read as a count of lost bits.
        /// </remarks>
        public bool Collided => (_code & CollidedFlag) != 0;

        internal static byte CodeOf(Bit bit) => bit == Bit.One ? OneBit : ZeroBit;

        public override string ToString() =>
            (Value.HasValue ? (Value.Value == Bit.One ? "1" : "0") : "-") + (Collided ? "x" : "");
    }

    /// <summary>
    /// Records, tick by tick, what every source sent, every bin received and every wire delivered, for
    /// the timing diagram to draw.
    /// </summary>
    /// <remarks>
    /// **Everything is read from the simulation as it stands after each tick, and nothing in
    /// LogicCore changes for it.**
    /// - A source's bit on tick t is <see cref="SourceNode.Sequence"/>[t]: the clock's silences are
    ///   already in the sequence, so this is not worked out again from the period.
    /// - A bin is single-input, so it receives at most one bit a tick, stamped with that tick.
    /// - A wire forgets a bit once it has delivered it, so what it delivers is read the tick
    ///   before: after every tick, the bit at the front of each wire with one tick left to go.
    ///   Nothing can change a bit in flight in between -- the same fact that lets the board warn of
    ///   a collision before it happens -- so that is exactly the bit the wire delivers next tick.
    /// - A collision is <see cref="InputPort.LastCollisionTick"/> at the port the bit arrived in.
    ///
    /// **Every wire is recorded, not only the ones the player has asked to see**, so a wire added to
    /// the diagram mid-run shows everything it has done since tick 0. It costs a byte a wire a tick.
    ///
    /// Hear every tick through <see cref="SimulationRunner.Ticked"/> and start again on every
    /// <see cref="SimulationRunner.Rebuilt"/>: several ticks can pass in one frame, and a rebuild is a
    /// new graph whose ids mean different things. Nothing here allocates once
    /// <see cref="Reset"/> has sized the rows, so recording costs nothing per tick but the reads.
    /// </remarks>
    public sealed class WaveformRecorder
    {
        /// <summary>How many ticks are kept, the latest last; older ones are forgotten.</summary>
        public const int Capacity = 128;

        private readonly List<int> _sources = new List<int>();
        private readonly List<int> _sinks = new List<int>();

        private byte[] _sourceCells = new byte[0];
        private byte[] _sinkCells = new byte[0];
        private byte[] _edgeCells = new byte[0];

        /// <summary>For each wire, the bit it will deliver on the next tick, as a cell code.</summary>
        private byte[] _arriving = new byte[0];

        private int _edgeCount;

        /// <summary>How each edge is drawn, or null: a bit crosses a wire's drawn end with HiddenAfter + 1 ticks left.</summary>
        private IReadOnlyList<EdgeShape> _shapes;

        /// <summary>The last tick recorded, or -1 before the first.</summary>
        public int LastTick { get; private set; } = -1;

        /// <summary>The earliest tick still held.</summary>
        public int FirstTick => LastTick < Capacity ? 0 : LastTick - Capacity + 1;

        /// <summary>Moves on whenever anything recorded changes, so a view knows when to redraw.</summary>
        public int Revision { get; private set; }

        /// <summary>How many source rows there are, in the level's fixture order.</summary>
        public int SourceCount => _sources.Count;

        /// <summary>How many bin rows there are, in the level's fixture order.</summary>
        public int SinkCount => _sinks.Count;

        /// <summary>How many wire rows there are: one per edge id of the recorded graph.</summary>
        public int EdgeCount => _edgeCount;

        /// <summary>The node a source row records.</summary>
        public int SourceNodeId(int row) => _sources[row];

        /// <summary>The node a bin row records.</summary>
        public int SinkNodeId(int row) => _sinks[row];

        /// <summary>
        /// Whether the clock is high on an executed tick: on the tick every source sends its next
        /// vector, and low for the rest of the period.
        /// </summary>
        /// <remarks>
        /// A period of one is high throughout. This is the executed tick -- the one
        /// <see cref="SimulationRunner.Ticked"/> reports -- where the clock strip counts
        /// <c>CurrentTick</c>, one on from it. <see cref="ClockDiagram"/>, behind F2, draws its wave
        /// from this too.
        /// </remarks>
        public static bool ClockOn(int executedTick, int period) =>
            period <= 1 || (executedTick >= 0 && executedTick % period == 0);

        /// <summary>
        /// Starts again on a newly built graph: sizes the rows for its sources, bins and wires, and
        /// forgets everything recorded.
        /// </summary>
        /// <remarks>
        /// Rows follow the level's fixtures in their order, skipping any the graph did not build --
        /// free play can count a fixture away while its entry is still listed. The one place that
        /// allocates, and only when a graph is bigger than any before it.
        /// </remarks>
        public void Reset(SimulationView view, LevelDefinition level, IReadOnlyDictionary<string, int> fixtureNodeIds,
            IReadOnlyList<EdgeShape> shapes = null)
        {
            _shapes = shapes;
            _sources.Clear();
            _sinks.Clear();

            if (level != null && fixtureNodeIds != null)
            {
                for (int i = 0; i < level.Fixtures.Count; i++)
                {
                    LevelFixture fixture = level.Fixtures[i];

                    if (!fixtureNodeIds.TryGetValue(fixture.Id, out int nodeId))
                        continue;

                    if (fixture.Kind == FixtureKind.Source)
                        _sources.Add(nodeId);
                    else
                        _sinks.Add(nodeId);
                }
            }

            _edgeCount = view.EdgeCount;

            _sourceCells = Cleared(_sourceCells, _sources.Count * Capacity);
            _sinkCells = Cleared(_sinkCells, _sinks.Count * Capacity);
            _edgeCells = Cleared(_edgeCells, _edgeCount * Capacity);
            _arriving = Cleared(_arriving, _edgeCount);

            LastTick = -1;
            CacheArrivals(view);
            Revision++;
        }

        /// <summary>Records the tick just executed. Call it once for every tick, in order.</summary>
        /// <remarks>
        /// A tick already recorded is ignored, and one that was skipped is recorded as nothing
        /// happening rather than left to show the ring's older contents.
        /// </remarks>
        public void AfterTick(SimulationView view, int tick)
        {
            if (tick <= LastTick)
                return;

            for (int missed = LastTick + 1; missed < tick; missed++)
                ClearColumn(missed);

            int column = tick % Capacity;

            for (int row = 0; row < _sources.Count; row++)
                _sourceCells[row * Capacity + column] = SourceCode(view, _sources[row], tick);

            for (int row = 0; row < _sinks.Count; row++)
                _sinkCells[row * Capacity + column] = SinkCode(view, _sinks[row], tick);

            int edges = _edgeCount < view.EdgeCount ? _edgeCount : view.EdgeCount;

            for (int id = 0; id < edges; id++)
            {
                byte code = _arriving[id];
                Edge edge = view.GetEdge(id);

                if (code != WaveCell.NoBit && edge != null && HiddenAfter(id) == 0 && edge.Target.LastCollisionTick == tick)
                    code |= WaveCell.CollidedFlag;

                _edgeCells[id * Capacity + column] = code;
            }

            LastTick = tick;
            CacheArrivals(view);
            Revision++;
        }

        /// <summary>A source row's cell on a tick; nothing outside what is held.</summary>
        public WaveCell SourceCell(int row, int tick) => Cell(_sourceCells, row, tick);

        /// <summary>A bin row's cell on a tick; nothing outside what is held.</summary>
        public WaveCell SinkCell(int row, int tick) => Cell(_sinkCells, row, tick);

        /// <summary>
        /// What the wire with this edge id delivered on a tick; nothing outside what is held, or for
        /// an id this graph does not have.
        /// </summary>
        public WaveCell EdgeCell(int edgeId, int tick) =>
            edgeId < 0 || edgeId >= _edgeCount ? default : Cell(_edgeCells, edgeId, tick);

        private WaveCell Cell(byte[] cells, int row, int tick)
        {
            if (tick < FirstTick || tick > LastTick || row < 0)
                return default;

            int index = row * Capacity + tick % Capacity;
            return index < cells.Length ? new WaveCell(cells[index]) : default;
        }

        private static byte SourceCode(SimulationView view, int nodeId, int tick)
        {
            if (nodeId < 0 || nodeId >= view.NodeCount || !(view.GetNode(nodeId) is SourceNode source))
                return WaveCell.NoBit;

            IReadOnlyList<Bit?> sequence = source.Sequence;

            if (tick >= sequence.Count || !sequence[tick].HasValue)
                return WaveCell.NoBit;

            return WaveCell.CodeOf(sequence[tick].Value);
        }

        private static byte SinkCode(SimulationView view, int nodeId, int tick)
        {
            if (nodeId < 0 || nodeId >= view.NodeCount || !(view.GetNode(nodeId) is SinkNode sink))
                return WaveCell.NoBit;

            byte code = WaveCell.NoBit;
            IReadOnlyList<SinkNode.Reception> received = sink.Received;
            int count = received.Count;

            if (count > 0 && received[count - 1].Tick == tick)
                code = WaveCell.CodeOf(received[count - 1].Value);

            if (sink.InputCount > 0 && sink.In(0).LastCollisionTick == tick)
                code |= WaveCell.CollidedFlag;

            return code;
        }

        /// <summary>
        /// For every wire, the bit it will deliver on the next tick: the one at its front with a tick
        /// left, if there is one. Only the front bit can be that close -- a wire takes at most one
        /// bit a tick and they all travel at its delay.
        /// </summary>
        private void CacheArrivals(SimulationView view)
        {
            int edges = _edgeCount < view.EdgeCount ? _edgeCount : view.EdgeCount;

            for (int id = 0; id < _edgeCount; id++)
                _arriving[id] = WaveCell.NoBit;

            for (int id = 0; id < edges; id++)
            {
                Edge edge = view.GetEdge(id);

                if (edge == null || edge.InTransitCount == 0)
                    continue;

                // The bit crossing the drawn end next tick. Into a block that is not the front bit
                // but the one with the block's own wire still to go; a wire takes a bit a tick, so
                // there is at most one.
                int crossing = HiddenAfter(id) + 1;

                for (int i = 0; i < edge.InTransitCount; i++)
                {
                    BitInTransit bit = edge.GetBitInTransit(i);

                    if (bit.TicksRemaining == crossing)
                    {
                        _arriving[id] = WaveCell.CodeOf(bit.Value);
                        break;
                    }
                }
            }
        }

        private int HiddenAfter(int edgeId) =>
            _shapes != null && edgeId < _shapes.Count ? _shapes[edgeId].HiddenAfter : 0;

        private void ClearColumn(int tick)
        {
            int column = tick % Capacity;

            for (int row = 0; row < _sources.Count; row++)
                _sourceCells[row * Capacity + column] = WaveCell.NoBit;

            for (int row = 0; row < _sinks.Count; row++)
                _sinkCells[row * Capacity + column] = WaveCell.NoBit;

            for (int id = 0; id < _edgeCount; id++)
                _edgeCells[id * Capacity + column] = WaveCell.NoBit;
        }

        /// <summary>An array of at least <paramref name="length"/>, all zero: the old one if it is big enough.</summary>
        private static byte[] Cleared(byte[] cells, int length)
        {
            if (cells.Length < length)
                return new byte[length];

            System.Array.Clear(cells, 0, cells.Length);
            return cells;
        }
    }
}
