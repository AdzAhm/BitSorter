using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>The symbols the schematic draws a part as.</summary>
    public enum PartSymbol
    {
        And, Or, Xor, Nand, Nor, Xnor, Not, Buffer, TriBuffer, TriInverter,
        Mux, Register, Constant, Lamp, Splitter, Joiner, Block,

        /// <summary>An operator part (Part 15h-3): a box with its symbol, <c>+</c> or <c>&lt;</c>, as a block's but smaller in name.</summary>
        Operator,
    }

    /// <summary>A net as the schematic draws it: its branches from its driver to each load, and where its wires meet.</summary>
    public sealed class NetLayout
    {
        public NetLayout(string name, string signal, IReadOnlyList<string> aliases, int width, IReadOnlyList<Vec2> junctions, IReadOnlyList<StageRoute> branches,
            string says, PinRef source = default, IReadOnlyList<PinRef> loads = null, IReadOnlyList<PinRef> drivers = null)
        {
            Says = says;
            Source = source;
            Loads = loads ?? System.Array.Empty<PinRef>();
            Drivers = drivers ?? new[] { source };
            Name = name;
            Signal = signal;
            Aliases = aliases;
            Width = width;
            Junctions = junctions;
            Branches = branches;
        }

        /// <summary>The net's name in the printed SystemVerilog.</summary>
        public string Name { get; }

        /// <summary>The design's full name for the net's signal: what its value is read from.</summary>
        public string Signal { get; }

        /// <summary>
        /// Every signal whose changes travel this net: the net's own, and the output of a library cell
        /// that drives it, where a gate with delay records its change before the cell's port hands it on.
        /// </summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>How many bits wide it is: one is a wire, more is a bus.</summary>
        public int Width { get; }

        /// <summary>Where one of its wires taps another: a dot.</summary>
        public IReadOnlyList<Vec2> Junctions { get; }

        /// <summary>Each branch from the driver to one load. A change travels down all of them at once.</summary>
        public IReadOnlyList<StageRoute> Branches { get; }

        /// <summary>What pointing at it says: its name, what drives it and what reads it. Written once, so hovering formats nothing.</summary>
        public string Says { get; }

        /// <summary>The pin that drives it (Part 14b): what a timing path through it comes out of.</summary>
        public PinRef Source { get; }

        /// <summary>The pin each branch ends on, in the order of <see cref="Branches"/>: which of them a timing path takes.</summary>
        public IReadOnlyList<PinRef> Loads { get; }

        /// <summary>
        /// Every pin that drives it: <see cref="Source"/> alone, or on a bus (Part 14f) each tri-state gate's output -- the
        /// drawing roots its branches at the first, so the others end branches of their own as well.
        /// </summary>
        public IReadOnlyList<PinRef> Drivers { get; }
    }

    /// <summary>A part as the schematic draws it: its symbol, where it stands, and what is written beside it.</summary>
    public sealed class PartLayout
    {
        public PartLayout(PartSymbol symbol, Part part, Placement placement, PartOutline local, string name, string delay, string output,
            string[] wired, SchematicLayout.Frame frame)
        {
            Symbol = symbol;
            Part = part;
            Placement = placement;
            Local = local;
            Name = name;
            Delay = delay;
            Output = output;
            _wired = wired;
            _frame = frame;
        }

        private readonly SchematicLayout.Frame _frame;
        private readonly string[] _wired;

        /// <summary>Whether a wire reaches this pin, so its lead is the wire's end rather than a stub of the part's.</summary>
        public bool IsWired(string pin)
        {
            // A loop over an array, as the drawing asks every frame and must not allocate.
            for (int i = 0; i < _wired.Length; i++)
                if (_wired[i] == pin)
                    return true;
            return false;
        }

        public PartSymbol Symbol { get; }

        /// <summary>The part in the document.</summary>
        public Part Part { get; }

        public Placement Placement { get; }

        /// <summary>Its outline and pins as its kind is drawn, centred and facing right, in cells.</summary>
        public PartOutline Local { get; }

        /// <summary>What it is labelled: a gate's kind, as a textbook labels one; a block's module.</summary>
        public string Name { get; }

        /// <summary>Its delay as written beside it -- rise and fall apart when they differ -- or empty.</summary>
        public string Delay { get; }

        /// <summary>The signal it drives, whose flips make it flash, or null.</summary>
        public string Output { get; }

        /// <summary>Whether a symbol is roomy enough to carry its name inside: the D, the shield, the mux.</summary>
        public static bool NamedInside(PartSymbol symbol) =>
            symbol == PartSymbol.And || symbol == PartSymbol.Nand || symbol == PartSymbol.Or || symbol == PartSymbol.Nor ||
            symbol == PartSymbol.Xor || symbol == PartSymbol.Xnor || symbol == PartSymbol.Mux;

        /// <summary>
        /// Whether its name is written over it: a triangle is too small to hold one and a block's
        /// inside names its ports. A constant is its value, inside it; a lamp and a splitter say nothing.
        /// </summary>
        public static bool NameAbove(PartSymbol symbol) =>
            !NamedInside(symbol) && symbol != PartSymbol.Constant && symbol != PartSymbol.Lamp &&
            symbol != PartSymbol.Splitter && symbol != PartSymbol.Joiner && symbol != PartSymbol.Operator;

        /// <summary>A point of the part's own drawing, in cells, where it lands on the stage.</summary>
        public Vec2 ToStage(double x, double y) => _frame.ToStage(PartGeometry.Transform(Placement, new BoardPoint(x, y)));

        /// <summary>How many stage units a cell is.</summary>
        public float Cell => _frame.Cell;
    }

    /// <summary>A port: a rounded box, an input's on the left of its pin and an output's on the right.</summary>
    public sealed class PortLayout
    {
        public PortLayout(string name, string signal, Rect2 box, bool output, int part = -1, Vec2 pin = default, Vec2 leadEnd = default,
            bool wired = false)
        {
            Name = name;
            Signal = signal;
            Box = box;
            Output = output;
            Part = part;
            Pin = pin;
            LeadEnd = leadEnd;
            IsWired = wired;
        }

        /// <summary>Its pin on the stage, where a wire starts or ends: a lead out from its box (Part 15g).</summary>
        public Vec2 Pin { get; }

        /// <summary>Where its lead meets its box, on the stage: a wire to it runs on to here.</summary>
        public Vec2 LeadEnd { get; }

        /// <summary>Whether a wire reaches its pin: then the wire is its lead, and no stub is drawn.</summary>
        public bool IsWired { get; }

        /// <summary>The port's part in the module drawn, by id; -1 for a port that is no part (Part 14b).</summary>
        public int Part { get; }

        /// <summary>What the box says: the port's name, and its range when it is a bus.</summary>
        public string Name { get; }

        public string Signal { get; }

        /// <summary>The box on the stage, y down.</summary>
        public Rect2 Box { get; }

        public bool Output { get; }

        /// <summary>The box's left edge, at its middle, as the reference's ports were placed.</summary>
        public Vec2 At => new Vec2(Box.xMin, Box.center.y);

        public float Width => Box.width;
    }

    /// <summary>A row of the waveform: which signal, and what it is called there.</summary>
    public sealed class WaveRow
    {
        public WaveRow(string label, string signal)
        {
            Label = label;
            Signal = signal;
        }

        public string Label { get; }
        public string Signal { get; }
    }

    /// <summary>
    /// Where everything on the workbench is drawn: the schematic's nets, parts and ports, and the
    /// waveform's rows, in the stage's own units with y down. Made from a document by
    /// <see cref="DocumentSchematic"/>; nothing in it is written by hand.
    /// </summary>
    public sealed class SchematicLayout
    {
        public SchematicLayout(Vec2 size, IReadOnlyList<NetLayout> nets, IReadOnlyList<PartLayout> parts, IReadOnlyList<PortLayout> ports, IReadOnlyList<WaveRow> rows,
            string designPath = null, IReadOnlyList<string> cells = null, string unit = null, Frame board = null, string instancePath = null)
        {
            InstancePath = instancePath ?? designPath;
            Size = size;
            Board = board;
            Unit = unit ?? BitSorter.SystemVerilog.TimeUnits.Default;
            Nets = nets;
            Parts = parts;
            Ports = ports;
            Rows = rows;
            DesignPath = designPath;
            Cells = cells ?? System.Array.Empty<string>();
        }

        /// <summary>
        /// Where the drawn module's signals are in the elaborated design (<see cref="Board.CompiledProject.DesignPath"/>):
        /// what every signal's key is relative to (<see cref="SignalIndex"/>). Null for a layout not made from a document.
        /// </summary>
        public string DesignPath { get; }

        /// <summary>
        /// Where the module drawn is in the design: <see cref="DesignPath"/> for the top, <c>full_adder_tb.dut.h1</c> stepped
        /// inside h1 (Part 12a). What the blocks drawn are named under (Part 14b).
        /// </summary>
        public string InstancePath { get; }

        /// <summary>The unit the document counts its time in, which every time on screen is written in (Part 9b-3).</summary>
        public string Unit { get; }

        /// <summary>The library cells the board's gates print as, by module name: the scopes a signal list hides unless asked.</summary>
        public IReadOnlyList<string> Cells { get; }

        /// <summary>The schematic's extent.</summary>
        public Vec2 Size { get; }

        /// <summary>How the document's cells land on the stage (Part 10): what a point the pointer is at is, on the board. Null for a layout not made from a document.</summary>
        public Frame Board { get; }

        public IReadOnlyList<NetLayout> Nets { get; }
        public IReadOnlyList<PartLayout> Parts { get; }
        public IReadOnlyList<PortLayout> Ports { get; }
        public IReadOnlyList<WaveRow> Rows { get; }

        /// <summary>The net of this name, by its index; -1 when there is none drawn.</summary>
        /// <summary>
        /// The net with a wire within <paramref name="within"/> of <paramref name="point"/> (stage
        /// units, y down), the nearest if several are, or -1.
        /// </summary>
        public int NetAt(Vec2 point, float within)
        {
            int best = -1;
            float bestSquared = within * within;

            for (int n = 0; n < Nets.Count; n++)
            {
                foreach (StageRoute branch in Nets[n].Branches)
                {
                    for (int i = 1; i < branch.PointCount; i++)
                    {
                        float squared = SquaredDistance(point, branch.Point(i - 1), branch.Point(i));
                        if (squared <= bestSquared)
                        {
                            bestSquared = squared;
                            best = n;
                        }
                    }
                }
            }

            return best;
        }

        private static float SquaredDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float along = ab.sqrMagnitude > 1e-8f ? Arith.Clamp01(Vec2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * along - p).sqrMagnitude;
        }

        public int NetNamed(string name)
        {
            for (int i = 0; i < Nets.Count; i++)
                if (Nets[i].Name == name)
                    return i;
            return -1;
        }

        /// <summary>How the document's cells, y up, land on the stage, y down: a scale and where the board's corner is.</summary>
        public sealed class Frame
        {
            public Frame(float cell, double left, double top, float margin)
            {
                Cell = cell;
                Left = left;
                Top = top;
                Margin = margin;
            }

            public float Cell { get; }
            public double Left { get; }
            public double Top { get; }
            public float Margin { get; }

            public Vec2 ToStage(BoardPoint p) =>
                new Vec2(Margin + (float)(p.X - Left) * Cell, Margin + (float)(Top - p.Y) * Cell);

            /// <summary>A point of the stage, y down, where it falls on the board, in cells and y up: <see cref="ToStage"/> backwards.</summary>
            public BoardPoint ToBoard(Vec2 stage) =>
                new BoardPoint(Left + (stage.x - Margin) / (double)Cell, Top - (stage.y - Margin) / (double)Cell);

            /// <summary>How far the same point of the board lies from where it lay on <paramref name="before"/>'s stage: what an edit that moved the board's corner shifted everything by.</summary>
            public Vec2 ShiftFrom(Frame before) => ToStage(new BoardPoint(0, 0)) - before.ToStage(new BoardPoint(0, 0));
        }
    }
}
