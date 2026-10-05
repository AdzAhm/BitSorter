using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>A freshly built graph, plus the lookups the rest of the game needs into it.</summary>
    public sealed class BuiltCircuit
    {
        private readonly int[] _blockOf;
        private readonly int[] _anchorPort;
        private readonly bool[] _anchorIsInput;

        public BuiltCircuit(
            Simulation simulation,
            Dictionary<string, int> fixtureNodeIds,
            Dictionary<int, Vector2Int> cells,
            IReadOnlyList<EdgeShape> shapes,
            IReadOnlyList<BuiltBlock> blocks)
        {
            Simulation = simulation;
            FixtureNodeIds = fixtureNodeIds;
            Cells = cells;
            Shapes = shapes;
            Blocks = blocks;

            _blockOf = new int[simulation.NodeCount];
            _anchorPort = new int[simulation.NodeCount];
            _anchorIsInput = new bool[simulation.NodeCount];

            for (int id = 0; id < _blockOf.Length; id++)
            {
                _blockOf[id] = -1;
                _anchorPort[id] = -1;
            }

            for (int b = 0; b < blocks.Count; b++)
            {
                BuiltBlock block = blocks[b];

                for (int i = 0; i < block.InputAnchors.Count; i++)
                    MarkAnchor(block.InputAnchors[i], b, true, i);

                for (int j = 0; j < block.OutputAnchors.Count; j++)
                    MarkAnchor(block.OutputAnchors[j], b, false, j);

                for (int g = 0; g < block.Gates.Count; g++)
                    _blockOf[block.Gates[g]] = b;
            }
        }

        private void MarkAnchor(int nodeId, int block, bool isInput, int port)
        {
            _blockOf[nodeId] = block;
            _anchorPort[nodeId] = port;
            _anchorIsInput[nodeId] = isInput;
        }

        public Simulation Simulation { get; }

        /// <summary>
        /// Fixture id to node id. The grader's way in: a level names its sinks, and the simulator only
        /// knows ids. Looked up, never iterated.
        /// </summary>
        public IReadOnlyDictionary<string, int> FixtureNodeIds { get; }

        /// <summary>
        /// Node id to the cell it occupies. The layout table. Every node of a block -- its anchors and
        /// the gates inside -- is on the block's top cell.
        /// </summary>
        public IReadOnlyDictionary<int, Vector2Int> Cells { get; }

        /// <summary>How each edge is drawn, by edge id. See <see cref="EdgeShape"/>.</summary>
        public IReadOnlyList<EdgeShape> Shapes { get; }

        /// <summary>The blocks on the board, in placement order, as the build expanded them.</summary>
        public IReadOnlyList<BuiltBlock> Blocks { get; }

        /// <summary>
        /// The block a node belongs to, as an anchor or as a gate inside it, or -1 for a node on the
        /// board itself.
        /// </summary>
        public int BlockOf(int nodeId) => nodeId >= 0 && nodeId < _blockOf.Length ? _blockOf[nodeId] : -1;

        /// <summary>Whether a node is one of a block's port anchors.</summary>
        public bool IsAnchor(int nodeId) => nodeId >= 0 && nodeId < _anchorPort.Length && _anchorPort[nodeId] >= 0;

        /// <summary>Which of its block's ports an anchor stands for: an input or an output, and which.</summary>
        public bool TryAnchorOf(int nodeId, out bool isInput, out int port)
        {
            bool anchor = IsAnchor(nodeId);

            isInput = anchor && _anchorIsInput[nodeId];
            port = anchor ? _anchorPort[nodeId] : -1;
            return anchor;
        }

        /// <summary>
        /// Whether a node's port is on the board for the player to see and wire: every port of a part
        /// on the board, and the side of an anchor that faces out. Nothing inside a block.
        /// </summary>
        public bool IsShownPort(int nodeId, bool isInput)
        {
            if (BlockOf(nodeId) < 0)
                return true;

            return IsAnchor(nodeId) && _anchorIsInput[nodeId] == isInput;
        }

        /// <summary>Whether a node is a gate inside a block, which nothing on the board shows.</summary>
        public bool IsInsideABlock(int nodeId) => BlockOf(nodeId) >= 0 && !IsAnchor(nodeId);

        /// <summary>
        /// The board port a node's port stands for, as the blueprint stores wires: a part's own, or a
        /// block's for the side of an anchor that faces out. False for anything inside a block.
        /// </summary>
        public bool TryBoardPort(int nodeId, bool isInput, int index, out CellPort port)
        {
            port = default;

            int block = BlockOf(nodeId);

            if (block < 0)
            {
                if (!Cells.TryGetValue(nodeId, out Vector2Int cell))
                    return false;

                port = new CellPort(cell, isInput, index);
                return true;
            }

            // An anchor faces out on one side only: an input anchor takes wires into the box, an output
            // anchor sends them out. Its other side is inside, joined to nothing.
            if (_anchorPort[nodeId] < 0 || isInput != _anchorIsInput[nodeId] || index != 0)
                return false;

            port = new CellPort(Blocks[block].Cell, isInput, _anchorPort[nodeId]);
            return true;
        }
    }

    /// <summary>
    /// Turns a level's fixtures plus the player's blueprint into a <see cref="Simulation"/>.
    /// </summary>
    /// <remarks>
    /// Static and free of MonoBehaviour on purpose. This is the step the grading tests need most --
    /// they have a level and a blueprint and want a graph to run -- and burying it inside
    /// <see cref="SimulationRunner"/> would have meant every such test standing up a GameObject.
    ///
    /// The order here is a contract, not a convenience: fixtures in level-file order, then gates in
    /// placement order, then blocks in placement order -- each its input anchors, its output anchors and
    /// its gates -- then wires in creation order, and last the wires inside each block. Node ids are
    /// nothing but Simulation.Add call order, so this ordering is exactly what makes two builds of one
    /// blueprint produce identical ids -- and anything keyed by id, the layout table included, depends
    /// on that.
    ///
    /// **A block's boundary is spliced, and costs nothing.** Every wire takes at least a tick, so a
    /// node at each port would make the way into a block two wires where a gate placed there directly
    /// takes one, and every block crossed would add a tick each way -- a delay no hardware has. Instead
    /// the wire drawn to an input and the block's own wire from that input become one edge, of the two
    /// delays less one; the way out the same, and a wire from one block to another joins all three. So
    /// a block takes exactly as long as its contents placed directly, and with every wire at a tick the
    /// splice is a tick. A drawn wire into an input that leads to two gates inside is two edges. The
    /// view draws each as the wire the player drew (<see cref="EdgeShape"/>), between the block's
    /// <b>port anchors</b>: inert pass-through nodes with no edges, one at each port, which never fire.
    /// </remarks>
    public static class CircuitBuilder
    {
        /// <summary>Where a bit drawn into a block's input goes inside, and how long that wire takes.</summary>
        private readonly struct Landing
        {
            public readonly InputPort Port;
            public readonly int Delay;

            public Landing(InputPort port, int delay)
            {
                Port = port;
                Delay = delay;
            }
        }

        /// <summary>What feeds a block's output from inside, and how long that wire takes.</summary>
        private readonly struct Departure
        {
            public readonly OutputPort Port;
            public readonly int Delay;

            public Departure(OutputPort port, int delay)
            {
                Port = port;
                Delay = delay;
            }
        }

        /// <summary>Every port of every block on the board, by the board port it stands for.</summary>
        private sealed class BlockPorts
        {
            public readonly Dictionary<CellPort, Landing[]> Into = new Dictionary<CellPort, Landing[]>();
            public readonly Dictionary<CellPort, Departure> OutOf = new Dictionary<CellPort, Departure>();
            public readonly Dictionary<CellPort, InputPort> InputAnchors = new Dictionary<CellPort, InputPort>();
            public readonly Dictionary<CellPort, OutputPort> OutputAnchors = new Dictionary<CellPort, OutputPort>();
            public readonly List<Dictionary<Vector2Int, Node>> Insides = new List<Dictionary<Vector2Int, Node>>();
        }

        public static BuiltCircuit Build(LevelDefinition level, CircuitBlueprint blueprint)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));

            var simulation = new Simulation();
            var fixtureNodeIds = new Dictionary<string, int>();
            var cells = new Dictionary<int, Vector2Int>();

            // Cell to node, needed only to resolve wire endpoints below. Looked up, never iterated,
            // so its ordering cannot reach the graph.
            var nodesByCell = new Dictionary<Vector2Int, Node>();

            for (int i = 0; i < level.Fixtures.Count; i++)
            {
                LevelFixture fixture = level.Fixtures[i];

                Node node = Register(
                    simulation, cells, nodesByCell, CreateFixture(fixture, level.ClockPeriod), fixture.Cell);

                fixtureNodeIds[fixture.Id] = node.Id;
            }

            for (int i = 0; i < blueprint.Placements.Count; i++)
            {
                GatePlacement placement = blueprint.Placements[i];

                Register(
                    simulation, cells, nodesByCell, GatePalette.Create(placement.Kind), placement.Cell);
            }

            var ports = new BlockPorts();
            var blocks = new List<BuiltBlock>(blueprint.Blocks.Count);

            for (int i = 0; i < blueprint.Blocks.Count; i++)
                blocks.Add(Expand(simulation, cells, blueprint.Blocks[i], i, ports));

            var shapes = new List<EdgeShape>(blueprint.Wires.Count);

            for (int i = 0; i < blueprint.Wires.Count; i++)
            {
                BlueprintWire wire = blueprint.Wires[i];

                // A wire whose endpoint cell no longer holds a node is skipped rather than treated as
                // an error. CircuitBlueprint.RemoveAt already drops the wires it orphans, so this is
                // belt and braces against a blueprint edited by some other path.
                OutputPort source;
                OutputPort drawnFrom;
                int before;

                if (TryResolveOutput(nodesByCell, wire.From, out source))
                {
                    drawnFrom = source;
                    before = 0;
                }
                else if (ports.OutOf.TryGetValue(wire.From, out Departure departure))
                {
                    source = departure.Port;
                    drawnFrom = ports.OutputAnchors[wire.From];
                    before = departure.Delay - 1;
                }
                else
                {
                    continue;
                }

                if (TryResolveInput(nodesByCell, wire.To, out InputPort target))
                {
                    simulation.Connect(source, target, before + wire.Delay);
                    shapes.Add(new EdgeShape(drawnFrom, target, before, wire.Delay, 0, i, true));
                }
                else if (ports.Into.TryGetValue(wire.To, out Landing[] landings))
                {
                    InputPort drawnTo = ports.InputAnchors[wire.To];

                    for (int t = 0; t < landings.Length; t++)
                    {
                        int after = landings[t].Delay - 1;

                        simulation.Connect(source, landings[t].Port, before + wire.Delay + after);
                        shapes.Add(new EdgeShape(drawnFrom, drawnTo, before, wire.Delay, after, i, t == 0));
                    }
                }
            }

            for (int b = 0; b < blocks.Count; b++)
                ConnectInside(simulation, blocks[b].Definition, ports.Insides[b], shapes);

            return new BuiltCircuit(simulation, fixtureNodeIds, cells, shapes, blocks);
        }

        /// <summary>
        /// Adds a block's anchors and gates, and records where each of its ports leads inside.
        /// </summary>
        private static BuiltBlock Expand(
            Simulation simulation, Dictionary<int, Vector2Int> cells, BlockPlacement placement, int index,
            BlockPorts ports)
        {
            BlockDefinition block = placement.Block;
            Vector2Int at = placement.Cell;

            var inputAnchors = new int[block.Inputs.Count];
            var outputAnchors = new int[block.Outputs.Count];
            var gates = new int[block.Gates.Count];

            for (int i = 0; i < inputAnchors.Length; i++)
            {
                Node anchor = Anchor(simulation, cells, block, block.Inputs[i], at);
                inputAnchors[i] = anchor.Id;
                ports.InputAnchors[new CellPort(at, true, i)] = anchor.In(0);
            }

            for (int j = 0; j < outputAnchors.Length; j++)
            {
                Node anchor = Anchor(simulation, cells, block, block.Outputs[j], at);
                outputAnchors[j] = anchor.Id;
                ports.OutputAnchors[new CellPort(at, false, j)] = anchor.Out(0);
            }

            var inside = new Dictionary<Vector2Int, Node>(block.Gates.Count);

            for (int g = 0; g < gates.Length; g++)
            {
                Node gate = simulation.Add(GatePalette.Create(block.Gates[g].Kind));
                cells[gate.Id] = at;
                inside[block.Gates[g].Cell] = gate;
                gates[g] = gate.Id;
            }

            ports.Insides.Add(inside);

            // BlockRules has already held every input to at least one wire inside and every output to
            // exactly one, and kept inputs from running straight to outputs, so each of these resolves.
            for (int i = 0; i < inputAnchors.Length; i++)
            {
                var landings = new List<Landing>();

                for (int w = 0; w < block.Wires.Count; w++)
                {
                    BlueprintWire wire = block.Wires[w];

                    if (wire.From.Cell == block.Inputs[i].Cell && inside.TryGetValue(wire.To.Cell, out Node gate))
                        landings.Add(new Landing(gate.In(wire.To.Index), wire.Delay));
                }

                ports.Into[new CellPort(at, true, i)] = landings.ToArray();
            }

            for (int j = 0; j < outputAnchors.Length; j++)
            {
                for (int w = 0; w < block.Wires.Count; w++)
                {
                    BlueprintWire wire = block.Wires[w];

                    if (wire.To.Cell == block.Outputs[j].Cell && inside.TryGetValue(wire.From.Cell, out Node gate))
                        ports.OutOf[new CellPort(at, false, j)] = new Departure(gate.Out(wire.From.Index), wire.Delay);
                }
            }

            return new BuiltBlock(index, at, block, inputAnchors, outputAnchors, gates);
        }

        /// <summary>One port of a block, standing on its top cell until the view says where.</summary>
        private static Node Anchor(
            Simulation simulation, Dictionary<int, Vector2Int> cells, BlockDefinition block, BlockPort port,
            Vector2Int at)
        {
            Node anchor = simulation.Add(new PassThroughNode { Name = $"{block.Name}.{port.Id}" });
            cells[anchor.Id] = at;
            return anchor;
        }

        /// <summary>The wires between gates inside a block. Those at its ports were spliced already.</summary>
        private static void ConnectInside(
            Simulation simulation, BlockDefinition block, Dictionary<Vector2Int, Node> inside, List<EdgeShape> shapes)
        {
            for (int w = 0; w < block.Wires.Count; w++)
            {
                BlueprintWire wire = block.Wires[w];

                if (!inside.TryGetValue(wire.From.Cell, out Node from) || !inside.TryGetValue(wire.To.Cell, out Node to))
                    continue;

                Edge edge = simulation.Connect(from.Out(wire.From.Index), to.In(wire.To.Index), wire.Delay);
                shapes.Add(EdgeShape.Inside(edge));
            }
        }

        private static Node Register(
            Simulation simulation,
            Dictionary<int, Vector2Int> cells,
            Dictionary<Vector2Int, Node> nodesByCell,
            Node node,
            Vector2Int cell)
        {
            simulation.Add(node);
            cells[node.Id] = cell;
            nodesByCell[cell] = node;
            return node;
        }

        private static Node CreateFixture(LevelFixture fixture, int clockPeriod)
        {
            switch (fixture.Kind)
            {
                case FixtureKind.Source:
                    return new SourceNode(OnTheClock(fixture.Stream, clockPeriod)) { Name = fixture.Id };

                case FixtureKind.Sink:
                    // Single-port by construction. The level format has no way to express a wider
                    // sink, because one expectation character per vector could not say which port a
                    // bit was meant for.
                    return new SinkNode() { Name = fixture.Id };

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(fixture), fixture.Kind, "Unknown fixture kind.");
            }
        }

        /// <summary>
        /// A stream spread onto the level's clock: one bit per cycle, silent ticks in between.
        /// </summary>
        /// <remarks>
        /// The gaps go *between* vectors and never after the last one. A source is exhausted only
        /// once its whole sequence has played, and the grader waits for that before it will call a
        /// run settled -- a trailing silence would hold every run open to the tick limit.
        ///
        /// A period of 1 gives back the stream unchanged, which is every combinational level.
        /// </remarks>
        private static Bit?[] OnTheClock(IReadOnlyList<Bit> stream, int clockPeriod)
        {
            int period = clockPeriod > 0 ? clockPeriod : 1;
            var ticks = new Bit?[stream.Count == 0 ? 0 : (stream.Count - 1) * period + 1];

            for (int vector = 0; vector < stream.Count; vector++)
                ticks[vector * period] = stream[vector];

            return ticks;
        }

        private static bool TryResolveOutput(
            Dictionary<Vector2Int, Node> nodesByCell, CellPort port, out OutputPort output)
        {
            output = null;

            if (port.IsInput || !nodesByCell.TryGetValue(port.Cell, out Node node))
                return false;

            if (port.Index < 0 || port.Index >= node.OutputCount)
                return false;

            output = node.Out(port.Index);
            return true;
        }

        private static bool TryResolveInput(
            Dictionary<Vector2Int, Node> nodesByCell, CellPort port, out InputPort input)
        {
            input = null;

            if (!port.IsInput || !nodesByCell.TryGetValue(port.Cell, out Node node))
                return false;

            if (port.Index < 0 || port.Index >= node.InputCount)
                return false;

            input = node.In(port.Index);
            return true;
        }
    }
}
