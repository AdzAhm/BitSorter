using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// What a block may be, and the one way to make one: from its parts, or from a board.
    /// </summary>
    /// <remarks>
    /// Pure and static, like <see cref="LevelRules"/>, and shared by everything that makes a block --
    /// MAKE BLOCK in free play and a level file's own blocks alike -- so a block is held to the same
    /// rules wherever it came from. Each refusal is a sentence the player can be shown.
    ///
    /// Most of the rules exist because of how a block is built (<see cref="CircuitBuilder"/>). The wire
    /// the player draws to an input and the block's own wire from that input are joined into one, so
    /// the block costs exactly what its contents cost and its boundary adds no tick. That join needs
    /// every input to lead somewhere inside, every output to be fed by exactly one wire, and no input
    /// to run straight to an output, where there would be nothing inside to join to.
    /// </remarks>
    public static class BlockRules
    {
        /// <summary>Most ports on either face.</summary>
        public const int MaxPorts = 5;

        /// <summary>Longest name, in characters: short enough to sit across a box one cell wide.</summary>
        public const int MaxNameLength = 4;

        /// <summary>
        /// Makes a block from its parts, or says why it cannot be one.
        /// </summary>
        /// <param name="inputs">Its sources, top to bottom: the order the box's left face shows them.</param>
        /// <param name="outputs">Its sinks, top to bottom.</param>
        public static bool TryDefine(
            string name,
            IReadOnlyList<BlockPort> inputs,
            IReadOnlyList<BlockPort> outputs,
            IReadOnlyList<GatePlacement> gates,
            IReadOnlyList<BlueprintWire> wires,
            out BlockDefinition block,
            out string refusal)
        {
            block = null;
            refusal = null;

            string trimmed = name == null ? string.Empty : name.Trim();

            if (trimmed.Length == 0)
                return Refuse("A block needs a name.", out refusal);

            if (trimmed.Length > MaxNameLength)
            {
                return Refuse($"A block's name is at most {MaxNameLength} characters, and \"{trimmed}\" " +
                              $"is {trimmed.Length}.", out refusal);
            }

            int inCount = inputs?.Count ?? 0;
            int outCount = outputs?.Count ?? 0;

            if (inCount == 0)
                return Refuse("A block needs at least one input.", out refusal);

            if (inCount > MaxPorts)
                return Refuse($"A block takes at most {MaxPorts} inputs, and this has {inCount}.", out refusal);

            if (outCount == 0)
                return Refuse("A block needs at least one output.", out refusal);

            if (outCount > MaxPorts)
                return Refuse($"A block takes at most {MaxPorts} outputs, and this has {outCount}.", out refusal);

            gates = gates ?? System.Array.Empty<GatePlacement>();
            wires = wires ?? System.Array.Empty<BlueprintWire>();

            var cells = new HashSet<Vector2Int>();
            var names = new HashSet<string>(System.StringComparer.Ordinal);

            if (!TryClaimPorts(inputs, cells, names, out refusal) || !TryClaimPorts(outputs, cells, names, out refusal))
                return false;

            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i].Kind == GateKind.Register)
                    return Refuse("A block can't hold a register: it would need a clock of its own.", out refusal);

                if (!cells.Add(gates[i].Cell))
                    return Refuse($"Two parts inside share the cell {gates[i].Cell}.", out refusal);
            }

            var intoOutput = new int[outCount];
            var outOfInput = new int[inCount];
            var joined = new HashSet<(CellPort, CellPort)>();

            for (int w = 0; w < wires.Count; w++)
            {
                BlueprintWire wire = wires[w];

                if (wire.Delay < 1)
                    return Refuse("A wire inside is shorter than one tick.", out refusal);

                if (wire.From.IsInput || !wire.To.IsInput)
                    return Refuse("A wire inside runs from an input or into an output.", out refusal);

                int fromInput = wire.From.Index == 0 ? IndexAt(inputs, wire.From.Cell) : -1;
                int toOutput = wire.To.Index == 0 ? IndexAt(outputs, wire.To.Cell) : -1;

                if (fromInput < 0 && !GateHasPort(gates, wire.From))
                {
                    return Refuse($"A wire inside starts at {wire.From.Cell}, where nothing has that output.",
                        out refusal);
                }

                if (toOutput < 0 && !GateHasPort(gates, wire.To))
                    return Refuse($"A wire inside ends at {wire.To.Cell}, where nothing has that input.", out refusal);

                if (fromInput >= 0 && toOutput >= 0)
                {
                    return Refuse($"Input {inputs[fromInput].Id} is wired straight to output " +
                                  $"{outputs[toOutput].Id}, with nothing in between.", out refusal);
                }

                if (!joined.Add((wire.From, wire.To)))
                    return Refuse("Two wires inside join the same two ports.", out refusal);

                if (fromInput >= 0)
                    outOfInput[fromInput]++;

                if (toOutput >= 0)
                    intoOutput[toOutput]++;
            }

            for (int i = 0; i < inCount; i++)
            {
                if (outOfInput[i] == 0)
                    return Refuse($"Input {inputs[i].Id} isn't wired to anything inside.", out refusal);
            }

            for (int j = 0; j < outCount; j++)
            {
                if (intoOutput[j] == 0)
                    return Refuse($"Nothing is wired into output {outputs[j].Id}.", out refusal);

                if (intoOutput[j] > 1)
                {
                    return Refuse($"Output {outputs[j].Id} has {intoOutput[j]} wires into it, and a block's " +
                                  "output takes one.", out refusal);
                }
            }

            block = new BlockDefinition(trimmed, ToArray(inputs), ToArray(outputs), ToArray(gates), ToArray(wires));
            return true;
        }

        /// <summary>
        /// Makes a block from a board: its sources become the inputs and its sinks the outputs, each
        /// top to bottom, and its gates and wires go inside.
        /// </summary>
        /// <remarks>
        /// A wire that ends on an empty cell is left out, as a build leaves it out: in free play a
        /// source or sink counted away leaves its wires behind, drawn by nothing and simulated by
        /// nothing, until it is counted back.
        /// </remarks>
        public static bool TryMakeFromBoard(
            string name, LevelDefinition level, CircuitBlueprint board, out BlockDefinition block, out string refusal)
        {
            block = null;

            if (board.Blocks.Count > 0)
                return Refuse("A board with a block on it can't become a block.", out refusal);

            var inputs = new List<BlockPort>();
            var outputs = new List<BlockPort>();

            for (int i = 0; i < level.Fixtures.Count; i++)
            {
                LevelFixture fixture = level.Fixtures[i];
                (fixture.Kind == FixtureKind.Source ? inputs : outputs).Add(new BlockPort(fixture.Id, fixture.Cell));
            }

            inputs.Sort(TopToBottom);
            outputs.Sort(TopToBottom);

            var wires = new List<BlueprintWire>(board.Wires.Count);

            for (int i = 0; i < board.Wires.Count; i++)
            {
                BlueprintWire wire = board.Wires[i];

                if (Occupied(level, board, wire.From.Cell) && Occupied(level, board, wire.To.Cell))
                    wires.Add(wire);
            }

            return TryDefine(name, inputs, outputs, board.Placements, wires, out block, out refusal);
        }

        /// <summary>Top row first, and left to right along a row.</summary>
        private static int TopToBottom(BlockPort a, BlockPort b) =>
            a.Cell.y != b.Cell.y ? b.Cell.y.CompareTo(a.Cell.y) : a.Cell.x.CompareTo(b.Cell.x);

        private static bool Occupied(LevelDefinition level, CircuitBlueprint board, Vector2Int cell) =>
            level.FixtureAt(cell) != null || board.HasPlacementAt(cell);

        private static bool TryClaimPorts(
            IReadOnlyList<BlockPort> ports, HashSet<Vector2Int> cells, HashSet<string> names, out string refusal)
        {
            refusal = null;

            for (int i = 0; i < ports.Count; i++)
            {
                string id = ports[i].Id;

                if (string.IsNullOrWhiteSpace(id))
                    return Refuse("Every port of a block needs a name.", out refusal);

                if (!names.Add(id))
                    return Refuse($"Two ports of the block are both called {id}.", out refusal);

                if (!cells.Add(ports[i].Cell))
                    return Refuse($"Two parts inside share the cell {ports[i].Cell}.", out refusal);
            }

            return true;
        }

        private static int IndexAt(IReadOnlyList<BlockPort> ports, Vector2Int cell)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i].Cell == cell)
                    return i;
            }

            return -1;
        }

        private static bool GateHasPort(IReadOnlyList<GatePlacement> gates, CellPort port)
        {
            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i].Cell != port.Cell)
                    continue;

                int count = port.IsInput ? GatePalette.InputsOf(gates[i].Kind) : GatePalette.OutputsOf(gates[i].Kind);
                return port.Index >= 0 && port.Index < count;
            }

            return false;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> list)
        {
            var array = new T[list.Count];

            for (int i = 0; i < array.Length; i++)
                array[i] = list[i];

            return array;
        }

        private static bool Refuse(string reason, out string refusal)
        {
            refusal = reason;
            return false;
        }
    }
}
