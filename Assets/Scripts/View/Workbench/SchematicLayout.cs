using System.Collections.Generic;
using BitSorter.Board;
using UnityEngine;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.View
{
    /// <summary>The symbols the schematic draws a part as.</summary>
    public enum PartSymbol
    {
        And, Or, Xor, Nand, Nor, Xnor, Not, Buffer, TriBuffer, TriInverter,
        Mux, Register, Constant, Lamp, Splitter, Joiner, Block,
    }

    /// <summary>A net as the schematic draws it: its branches from its driver to each load, and where its wires meet.</summary>
    public sealed class NetLayout
    {
        public NetLayout(string name, string signal, IReadOnlyList<string> aliases, int width, IReadOnlyList<Vector2> junctions, IReadOnlyList<Route> branches)
        {
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
        public IReadOnlyList<Vector2> Junctions { get; }

        /// <summary>Each branch from the driver to one load. A change travels down all of them at once.</summary>
        public IReadOnlyList<Route> Branches { get; }
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
            symbol != PartSymbol.Splitter && symbol != PartSymbol.Joiner;

        /// <summary>A point of the part's own drawing, in cells, where it lands on the stage.</summary>
        public Vector2 ToStage(double x, double y) => _frame.ToStage(PartGeometry.Transform(Placement, new BoardPoint(x, y)));

        /// <summary>How many stage units a cell is.</summary>
        public float Cell => _frame.Cell;
    }

    /// <summary>A port: a rounded box, an input's on the left of its pin and an output's on the right.</summary>
    public sealed class PortLayout
    {
        public PortLayout(string name, string signal, Rect box, bool output)
        {
            Name = name;
            Signal = signal;
            Box = box;
            Output = output;
        }

        /// <summary>What the box says: the port's name, and its range when it is a bus.</summary>
        public string Name { get; }

        public string Signal { get; }

        /// <summary>The box on the stage, y down.</summary>
        public Rect Box { get; }

        public bool Output { get; }

        /// <summary>The box's left edge, at its middle, as the reference's ports were placed.</summary>
        public Vector2 At => new Vector2(Box.xMin, Box.center.y);

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
        public SchematicLayout(Vector2 size, IReadOnlyList<NetLayout> nets, IReadOnlyList<PartLayout> parts, IReadOnlyList<PortLayout> ports, IReadOnlyList<WaveRow> rows)
        {
            Size = size;
            Nets = nets;
            Parts = parts;
            Ports = ports;
            Rows = rows;
        }

        /// <summary>The schematic's extent.</summary>
        public Vector2 Size { get; }

        public IReadOnlyList<NetLayout> Nets { get; }
        public IReadOnlyList<PartLayout> Parts { get; }
        public IReadOnlyList<PortLayout> Ports { get; }
        public IReadOnlyList<WaveRow> Rows { get; }

        /// <summary>The net of this name, by its index; -1 when there is none drawn.</summary>
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

            public Vector2 ToStage(BoardPoint p) =>
                new Vector2(Margin + (float)(p.X - Left) * Cell, Margin + (float)(Top - p.Y) * Cell);
        }
    }
}
