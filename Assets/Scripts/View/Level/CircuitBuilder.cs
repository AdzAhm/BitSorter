using System;
using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>A freshly built graph, plus the two lookups the rest of the game needs into it.</summary>
    public sealed class BuiltCircuit
    {
        public BuiltCircuit(
            Simulation simulation,
            Dictionary<string, int> fixtureNodeIds,
            Dictionary<int, Vector2Int> cells)
        {
            Simulation = simulation;
            FixtureNodeIds = fixtureNodeIds;
            Cells = cells;
        }

        public Simulation Simulation { get; }

        /// <summary>
        /// Fixture id to node id. The grader's way in: a level names its sinks, and the simulator only
        /// knows ids. Looked up, never iterated.
        /// </summary>
        public IReadOnlyDictionary<string, int> FixtureNodeIds { get; }

        /// <summary>Node id to the cell it occupies. The layout table.</summary>
        public IReadOnlyDictionary<int, Vector2Int> Cells { get; }
    }

    /// <summary>
    /// Turns a level's fixtures plus the player's blueprint into a <see cref="Simulation"/>.
    /// </summary>
    /// <remarks>
    /// Static and free of MonoBehaviour on purpose. This is the step the grading tests need most --
    /// they have a level and a blueprint and want a graph to run -- and burying it inside
    /// <see cref="SimulationRunner"/> would have meant every such test standing up a GameObject.
    ///
    /// The order here is a contract, not a convenience: fixtures in level-file order, then placements
    /// in placement order, then wires in creation order. Node ids are nothing but Simulation.Add call
    /// order, so this ordering is exactly what makes two builds of one blueprint produce identical
    /// ids -- and anything keyed by id, the layout table included, depends on that.
    /// </remarks>
    public static class CircuitBuilder
    {
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
                    simulation, cells, nodesByCell, CreateFixture(fixture), fixture.Cell);

                fixtureNodeIds[fixture.Id] = node.Id;
            }

            for (int i = 0; i < blueprint.Placements.Count; i++)
            {
                GatePlacement placement = blueprint.Placements[i];

                Register(
                    simulation, cells, nodesByCell, GatePalette.Create(placement.Kind), placement.Cell);
            }

            for (int i = 0; i < blueprint.Wires.Count; i++)
            {
                BlueprintWire wire = blueprint.Wires[i];

                // A wire whose endpoint cell no longer holds a node is skipped rather than treated as
                // an error. CircuitBlueprint.RemoveAt already drops the wires it orphans, so this is
                // belt and braces against a blueprint edited by some other path.
                if (TryResolveOutput(nodesByCell, wire.From, out OutputPort source) &&
                    TryResolveInput(nodesByCell, wire.To, out InputPort target))
                {
                    simulation.Connect(source, target, wire.Delay);
                }
            }

            return new BuiltCircuit(simulation, fixtureNodeIds, cells);
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

        private static Node CreateFixture(LevelFixture fixture)
        {
            switch (fixture.Kind)
            {
                case FixtureKind.Source:
                    return new SourceNode(fixture.Stream) { Name = fixture.Id };

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
