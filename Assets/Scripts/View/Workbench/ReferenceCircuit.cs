using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using UnityEngine;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.View
{
    /// <summary>The symbols the schematic can draw a driver as.</summary>
    public enum PartShape { And, Not, Buffer, TriState }

    /// <summary>A signal as the schematic draws it: its wire's branches, from its driver to each load.</summary>
    public sealed class NetLayout
    {
        public NetLayout(string signal, int width, Vector2? junction, params Route[] branches)
        {
            Signal = signal;
            Width = width;
            Junction = junction;
            Branches = branches;
        }

        /// <summary>The design's name for the signal.</summary>
        public string Signal { get; }

        /// <summary>How many bits wide it is: one is a wire, more is a bus.</summary>
        public int Width { get; }

        /// <summary>Where the wire splits, drawn as a dot; none for a wire that does not.</summary>
        public Vector2? Junction { get; }

        /// <summary>Each branch from the driver to one load. A change travels down all of them at once.</summary>
        public IReadOnlyList<Route> Branches { get; }
    }

    /// <summary>A driver as the schematic draws it: a symbol, where it stands, its name and its delay.</summary>
    public sealed class GateLayout
    {
        public GateLayout(PartShape shape, Vector2 at, string output, string name, string delay)
        {
            Shape = shape;
            At = at;
            Output = output;
            Name = name;
            Delay = delay;
        }

        public PartShape Shape { get; }

        /// <summary>The middle of the symbol's back edge.</summary>
        public Vector2 At { get; }

        /// <summary>The signal it drives, whose flips make it flash.</summary>
        public string Output { get; }

        /// <summary>What the symbol is labelled: the gate's kind, as a textbook labels one.</summary>
        public string Name { get; }

        /// <summary>Its delay as written beside it: rise and fall apart when they differ.</summary>
        public string Delay { get; }
    }

    /// <summary>A port: a rounded box at the edge of the schematic, an input on the left or an output on the right.</summary>
    public sealed class PortLayout
    {
        public PortLayout(string name, string signal, Vector2 at, float width)
        {
            Name = name;
            Signal = signal;
            At = at;
            Width = width;
        }

        public string Name { get; }
        public string Signal { get; }

        /// <summary>The box's left edge, at its middle.</summary>
        public Vector2 At { get; }

        public float Width { get; }
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
    /// waveform's rows, in the stage's own units with y down.
    /// </summary>
    /// <remarks>
    /// A stand-in for Part 7's board document and its layout, which hold the same facts for any
    /// circuit; Part 8 feeds the renderer from those instead, and this hard-written one goes.
    /// </remarks>
    public sealed class SchematicLayout
    {
        public SchematicLayout(Vector2 size, NetLayout[] nets, GateLayout[] gates, PortLayout[] ports, WaveRow[] rows)
        {
            Size = size;
            Nets = nets;
            Gates = gates;
            Ports = ports;
            Rows = rows;
        }

        /// <summary>The schematic's extent.</summary>
        public Vector2 Size { get; }

        public IReadOnlyList<NetLayout> Nets { get; }
        public IReadOnlyList<GateLayout> Gates { get; }
        public IReadOnlyList<PortLayout> Ports { get; }
        public IReadOnlyList<WaveRow> Rows { get; }
    }

    /// <summary>
    /// The workbench preview's circuit: the look reference's glitch, A AND NOT A, beside the three
    /// things the reference cannot show -- a glitch too short to get through (inertial delay), a bus,
    /// and a tri-state's z.
    /// </summary>
    /// <remarks>
    /// Built with <see cref="NetlistBuilder"/> and run on the Part 2 engine, so every value, every
    /// change travelling and the one swallowed is the engine's, never animated to look right:
    /// - Y = A AND NOT A, the NOT 3 ns and the AND rise 2 / fall 3 ns. A rises at 6 and the NOT
    ///   takes 3 to follow, so for those 3 ns both inputs are 1: the glitch, which passes.
    ///   The reference raised A at 2; on a real simulator NOT A is still x until 3, which puts the
    ///   AND at x until 7 -- the reference was drawn by hand. Raising A at 6 shows the glitch alone.
    /// - Y2 is the same with the NOT at 1 ns and the AND at 2: a 1 ns glitch, shorter than the AND's
    ///   delay, so the AND swallows it and the change dies on its wire.
    /// - OUT is S, a 4-bit bus, through a 2 ns buffer: 3, then A, then 5.
    /// - Q is D through a tri-state buffer, z until its enable rises at 17.
    /// </remarks>
    public static class ReferenceCircuit
    {
        /// <summary>How far the run goes, in nanoseconds: past the last change, so the waveform shows it settled.</summary>
        public const long EndTime = 26;

        /// <summary>The circuit as a design, and the testbench's writes to its inputs.</summary>
        public static Design Build(out Stimulus stimulus)
        {
            var b = new NetlistBuilder("workbench");

            SignalId a = b.Variable("a"), na = b.Net("na"), y = b.Net("y");
            b.Gate(EngineGate.Not, "not1", na, Delay.Of(3), a);
            b.Gate(EngineGate.And, "and1", y, Delay.Of(2, 3), a, na);

            SignalId bIn = b.Variable("b"), nb = b.Net("nb"), y2 = b.Net("y2");
            b.Gate(EngineGate.Not, "not2", nb, Delay.Of(1), bIn);
            b.Gate(EngineGate.And, "and2", y2, Delay.Of(2), bIn, nb);

            SignalId s = b.Variable("s", 4), outBus = b.Net("out", 4);
            b.Assign(outBus, b.Read(s), Delay.Of(2), name: "buf4");

            SignalId d = b.Variable("d"), en = b.Variable("en"), q = b.Net("q");
            b.Gate(EngineGate.Bufif1, "tri1", q, Delay.Of(1), d, en);

            stimulus = new Stimulus()
                .Set(0, a, Logic4.Zero).Set(6, a, Logic4.One)
                .Set(0, bIn, Logic4.Zero).Set(13, bIn, Logic4.One)
                .Set(1, s, LogicVec.FromUInt64(0x3, 4)).Set(10, s, LogicVec.FromUInt64(0xA, 4)).Set(19, s, LogicVec.FromUInt64(0x5, 4))
                .Set(0, en, Logic4.Zero).Set(17, en, Logic4.One)
                .Set(0, d, Logic4.One);

            return b.Build();
        }

        /// <summary>Runs the circuit to <see cref="EndTime"/> and returns the session, history and all.</summary>
        public static SimSession Run()
        {
            Design design = Build(out Stimulus stimulus);
            var session = new SimSession(design, stimulus);
            session.RunTo(EndTime);
            return session;
        }

        /// <summary>Where it is drawn: the mock's coordinates, so the two can be laid side by side.</summary>
        public static SchematicLayout Layout { get; } = new SchematicLayout(
            new Vector2(720f, 360f),
            new[]
            {
                new NetLayout("a", 1, new Vector2(116, 55),
                    new Route(V(80, 55), V(116, 55), V(116, 43), V(456, 43)),
                    new Route(V(80, 55), V(116, 55), V(116, 92), V(236, 92))),
                new NetLayout("na", 1, null, new Route(V(279, 92), V(416, 92), V(416, 67), V(456, 67))),
                new NetLayout("y", 1, null, new Route(V(502, 55), V(640, 55))),

                new NetLayout("b", 1, new Vector2(116, 150),
                    new Route(V(80, 150), V(116, 150), V(116, 138), V(456, 138)),
                    new Route(V(80, 150), V(116, 150), V(116, 187), V(236, 187))),
                new NetLayout("nb", 1, null, new Route(V(279, 187), V(416, 187), V(416, 162), V(456, 162))),
                new NetLayout("y2", 1, null, new Route(V(502, 150), V(640, 150))),

                new NetLayout("s", 4, null, new Route(V(88, 240), V(300, 240))),
                new NetLayout("out", 4, null, new Route(V(332, 240), V(640, 240))),

                new NetLayout("d", 1, null, new Route(V(80, 300), V(300, 300))),
                new NetLayout("en", 1, null, new Route(V(80, 335), V(316, 335), V(316, 308))),
                new NetLayout("q", 1, null, new Route(V(332, 300), V(640, 300))),
            },
            new[]
            {
                new GateLayout(PartShape.Not, V(236, 92), "na", "NOT", "3 ns"),
                new GateLayout(PartShape.And, V(456, 55), "y", "AND", "rise 2 / fall 3 ns"),
                new GateLayout(PartShape.Not, V(236, 187), "nb", "NOT", "1 ns"),
                new GateLayout(PartShape.And, V(456, 150), "y2", "AND", "2 ns"),
                new GateLayout(PartShape.Buffer, V(300, 240), "out", "BUF", "2 ns"),
                new GateLayout(PartShape.TriState, V(300, 300), "q", "BUFIF1", "1 ns"),
            },
            new[]
            {
                new PortLayout("A", "a", V(24, 55), 56), new PortLayout("B", "b", V(24, 150), 56),
                new PortLayout("S[3:0]", "s", V(24, 240), 64), new PortLayout("D", "d", V(24, 300), 56),
                new PortLayout("EN", "en", V(24, 335), 56),
                new PortLayout("Y", "y", V(640, 55), 64), new PortLayout("Y2", "y2", V(640, 150), 64),
                new PortLayout("OUT", "out", V(640, 240), 64), new PortLayout("Q", "q", V(640, 300), 64),
            },
            new[]
            {
                new WaveRow("A", "a"), new WaveRow("Y", "y"), new WaveRow("B", "b"), new WaveRow("Y2", "y2"),
                new WaveRow("S[3:0]", "s"), new WaveRow("OUT[3:0]", "out"), new WaveRow("EN", "en"), new WaveRow("Q", "q"),
            });

        private static Vector2 V(float x, float y) => new Vector2(x, y);
    }
}
