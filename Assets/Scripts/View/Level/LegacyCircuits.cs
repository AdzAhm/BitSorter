using System.Collections.Generic;
using System.Text;
using BitSorter.Board;
using BitSorter.LogicCore;

namespace BitSorter.View
{
    /// <summary>
    /// The seam from 4.x to 5.0: a level, and a board drawn on it, as the plain
    /// <see cref="LegacyCircuit"/> the document layer migrates (Docs/architecture-5.md, section 8).
    /// </summary>
    /// <remarks>
    /// Everything that knows the 4.x types stays on this side; <see cref="Migrator"/> sees cells, port
    /// indices, kinds by name and streams as text, and is tested without Unity.
    /// </remarks>
    public static class LegacyCircuits
    {
        /// <summary>
        /// <paramref name="board"/> on <paramref name="level"/>, under <paramref name="name"/> -- the
        /// level's file name, or free play's board's.
        /// </summary>
        public static LegacyCircuit From(string name, LevelDefinition level, CircuitBlueprint board)
        {
            var circuit = new LegacyCircuit { name = name, clockPeriod = level.ClockPeriod };

            var fixtures = new List<LegacyFixture>();
            foreach (LevelFixture fixture in level.Fixtures)
            {
                fixtures.Add(new LegacyFixture
                {
                    id = fixture.Id,
                    source = fixture.Kind == FixtureKind.Source,
                    x = fixture.Cell.x,
                    y = fixture.Cell.y,
                    stream = Stream(fixture.Stream),
                });
            }

            circuit.fixtures = fixtures.ToArray();
            circuit.gates = Gates(board.Placements);

            var blocks = new List<LegacyBlockPlacement>();
            foreach (BlockPlacement placed in board.Blocks)
                blocks.Add(new LegacyBlockPlacement { x = placed.Cell.x, y = placed.Cell.y, block = Block(placed.Block) });
            circuit.blocks = blocks.ToArray();

            circuit.wires = Wires(board.Wires);

            var expected = new List<LegacyExpectation>();
            foreach (LevelExpectation expectation in level.Expectations)
                expected.Add(new LegacyExpectation { sink = expectation.SinkId, values = expectation.Values });
            circuit.expected = expected.ToArray();

            return circuit;
        }

        /// <summary><paramref name="board"/> on <paramref name="level"/>, made into a document.</summary>
        public static MigrationResult Migrate(string name, LevelDefinition level, CircuitBlueprint board) =>
            Migrator.Migrate(From(name, level, board));

        private static string Stream(IReadOnlyList<Bit> bits)
        {
            if (bits == null)
                return null;
            var text = new StringBuilder(bits.Count);
            foreach (Bit bit in bits)
                text.Append(bit == Bit.One ? '1' : '0');
            return text.ToString();
        }

        private static LegacyGate[] Gates(IReadOnlyList<GatePlacement> placements)
        {
            var gates = new LegacyGate[placements.Count];
            for (int i = 0; i < gates.Length; i++)
                gates[i] = new LegacyGate { x = placements[i].Cell.x, y = placements[i].Cell.y, kind = placements[i].Kind.ToString() };
            return gates;
        }

        private static LegacyWire[] Wires(IReadOnlyList<BlueprintWire> wires)
        {
            var legacy = new LegacyWire[wires.Count];
            for (int i = 0; i < legacy.Length; i++)
            {
                BlueprintWire wire = wires[i];
                legacy[i] = new LegacyWire
                {
                    fromX = wire.From.Cell.x,
                    fromY = wire.From.Cell.y,
                    fromPort = wire.From.Index,
                    toX = wire.To.Cell.x,
                    toY = wire.To.Cell.y,
                    toPort = wire.To.Index,
                    delay = wire.Delay,
                };
            }

            return legacy;
        }

        private static LegacyBlock Block(BlockDefinition block)
        {
            LegacyPort[] Ports(IReadOnlyList<BlockPort> ports)
            {
                var legacy = new LegacyPort[ports.Count];
                for (int i = 0; i < legacy.Length; i++)
                    legacy[i] = new LegacyPort { id = ports[i].Id, x = ports[i].Cell.x, y = ports[i].Cell.y };
                return legacy;
            }

            return new LegacyBlock
            {
                name = block.Name,
                inputs = Ports(block.Inputs),
                outputs = Ports(block.Outputs),
                gates = Gates(block.Gates),
                wires = Wires(block.Wires),
            };
        }
    }
}
