using System;

namespace BitSorter.Board
{
    /// <summary>
    /// A 4.x board as it stood in its level: the level's sources and bins, the gates, blocks and wires
    /// the player put down, and the level's clock and expectations. The shape 4.x saved in, by cell and
    /// port index, which <see cref="Migrator"/> makes into a document.
    /// </summary>
    /// <remarks>
    /// Plain fields, so the view can fill one from a level and a board (and Unity's JSON reader could
    /// fill one from a file), and a test can write one out by hand. Nothing here knows about Unity: the
    /// 4.x types stay in the view, and this is the seam between them and the document.
    /// </remarks>
    [Serializable]
    public sealed class LegacyCircuit
    {
        /// <summary>The level's file name, or what free play called the board.</summary>
        public string name;

        /// <summary>4.x's spacing between vectors, in ticks: more than 1 on a clocked level. Recorded, not used: 5.0's cycle is worked out from the circuit.</summary>
        public int clockPeriod = 1;

        public LegacyFixture[] fixtures = new LegacyFixture[0];
        public LegacyGate[] gates = new LegacyGate[0];
        public LegacyBlockPlacement[] blocks = new LegacyBlockPlacement[0];
        public LegacyWire[] wires = new LegacyWire[0];

        /// <summary>What each bin should receive, as the level says it: '0', '1', 'x' for either, '-' for nothing.</summary>
        public LegacyExpectation[] expected = new LegacyExpectation[0];
    }

    /// <summary>A source, with the bit it sends each vector, or a bin.</summary>
    [Serializable]
    public sealed class LegacyFixture
    {
        public string id;
        public bool source;
        public int x;
        public int y;

        /// <summary>A source's bits, one a vector: '0' and '1'.</summary>
        public string stream;
    }

    /// <summary>A gate on a cell: Not, And, Or, Xor, Nand, Nor or Register.</summary>
    [Serializable]
    public sealed class LegacyGate
    {
        public int x;
        public int y;
        public string kind;
    }

    /// <summary>
    /// A wire from an output to an input, by cell and port index, with its delay. A gate's inputs are 0
    /// and 1 and its output 0; a source's output and a bin's input are 0; a register's are 0; a block's
    /// are its ports' places in its lists, on the block's top cell.
    /// </summary>
    [Serializable]
    public sealed class LegacyWire
    {
        public int fromX;
        public int fromY;
        public int fromPort;
        public int toX;
        public int toY;
        public int toPort;
        public int delay = 1;
    }

    /// <summary>A block on the board, by its top cell.</summary>
    [Serializable]
    public sealed class LegacyBlockPlacement
    {
        public int x;
        public int y;
        public LegacyBlock block;
    }

    /// <summary>A block's contents: the small board it was made from, its sources the inputs and its bins the outputs.</summary>
    [Serializable]
    public sealed class LegacyBlock
    {
        public string name;
        public LegacyPort[] inputs = new LegacyPort[0];
        public LegacyPort[] outputs = new LegacyPort[0];
        public LegacyGate[] gates = new LegacyGate[0];
        public LegacyWire[] wires = new LegacyWire[0];
    }

    /// <summary>A block's port: its name, and the cell it stood on on the board the block was made from.</summary>
    [Serializable]
    public sealed class LegacyPort
    {
        public string id;
        public int x;
        public int y;
    }

    [Serializable]
    public sealed class LegacyExpectation
    {
        public string sink;
        public string values;
    }
}
