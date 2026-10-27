using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Boards for the compiler's tests and the oracle's, each with a stimulus: between them every kind
    /// of part, delays both ways, a hierarchy, clocks and starting values, buses. Built through the
    /// editor, as the view will build them. Shared with the Oracle project, which runs each under
    /// Icarus as printed.
    /// </summary>
    public static class SampleBoards
    {
        public static IEnumerable<string> Names => new[]
        {
            "half_adder", "half_adder_delayed", "full_adder", "johnson", "johnson_timed", "buses", "mux4", "tristate", "half_adders", "ripple8", "written",
            "full_adder_steps", "false_path_steps", "bus", "written_cells", "devices_bus",
        };

        /// <summary>The boards drawn throughout: what an import can draw again. A written module comes in as written with Part 13.</summary>
        public static IEnumerable<string> DrawnNames => Names.Where(name => name != "written" && name != "written_cells" && name != "devices_bus");

        public static Project Build(string name)
        {
            switch (name)
            {
                case "half_adder": return Exhaustive(HalfAdder("half_adder", 0, 0), new[] { "a", "b" }, 10);
                case "half_adder_delayed": return Exhaustive(HalfAdder("half_adder", 2, 3), new[] { "a", "b" }, 10);
                case "full_adder": return FullAdder();
                case "johnson": return Johnson();
                case "johnson_timed": return Timed(Johnson(), 2, 3);
                case "buses": return Buses();
                case "mux4": return Mux4();
                case "tristate": return TriState();
                case "half_adders": return HalfAdders();
                case "ripple8": return Ripple8();
                case "full_adder_steps": return Stepped(FullAdder());
                case "false_path_steps": return Stepped(FalsePath());
                case "written": return Written();
                case "bus": return Bus();
                case "written_cells": return WrittenCells();
                case "devices_bus": return DevicesBus();
                default: throw new ArgumentException(name);
            }
        }

        private static LogicVec Bits(ulong value, int width) => LogicVec.FromUInt64(value, width);

        internal static int Must(EditResult result)
        {
            if (!result.Done)
                throw new InvalidOperationException("refused: " + result.Refusal);
            return result.Id;
        }

        internal sealed class Builder
        {
            private int _x;

            public Builder(BoardEditor editor, string module)
            {
                Editor = editor;
                Module = module;
            }

            public BoardEditor Editor { get; }
            public string Module { get; }

            public int Add(Part part) => Must(Editor.Add(Module, part, new Placement(_x++, 0)));
            public int In(string name, int width = 1) => Add(new PortPart { Name = name, Width = width });
            public int Out(string name, int width = 1) => Add(new PortPart { Name = name, Direction = PortDirection.Output, Width = width });

            public int Gate(GateKind kind, long rise = 0, long fall = 0, int inputs = 2, int width = 1) =>
                Add(new GatePart { Kind = kind, Rise = rise, Fall = fall, Width = width, Inputs = kind == GateKind.Not || kind == GateKind.Buf ? 1 : inputs });

            public void Wire(int from, string fromPin, int to, string toPin) =>
                Must(Editor.Connect(Module, Endpoint.At(from, fromPin), Endpoint.At(to, toPin)));
        }

        private static BoardEditor HalfAdder(string module, long rise, long fall)
        {
            var editor = new BoardEditor(Project.New(module));
            AddHalfAdder(new Builder(editor, module), rise, fall);
            return editor;
        }

        private static void AddHalfAdder(Builder b, long rise, long fall)
        {
            int a = b.In("a"), bb = b.In("b"), s = b.Out("s"), c = b.Out("c");
            int x = b.Gate(GateKind.Xor, rise, fall), n = b.Gate(GateKind.And, rise, fall);
            b.Wire(a, "out", x, "a");
            b.Wire(bb, "out", x, "b");
            b.Wire(a, "out", n, "a");
            b.Wire(bb, "out", n, "b");
            b.Wire(x, "y", s, "in");
            b.Wire(n, "y", c, "in");
        }

        /// <summary>Every combination of one-bit inputs, the first input the most significant, <paramref name="gap"/> apart.</summary>
        internal static Project Exhaustive(BoardEditor editor, string[] inputs, long gap)
        {
            var tracks = new List<Track>();
            int count = 1 << inputs.Length;
            for (int i = 0; i < inputs.Length; i++)
            {
                var steps = new List<Step>();
                for (int v = 0; v < count; v++)
                    steps.Add(new Step(v * gap, Bits((ulong)(v >> (inputs.Length - 1 - i)) & 1, 1)));
                tracks.Add(new Track { Port = inputs[i], Steps = steps });
            }

            Must(editor.SetStimulus(new Stimulus { Tracks = tracks, End = count * gap }));
            return editor.Project;
        }

        /// <summary>A full adder of two half-adder blocks and an OR: the course's first hierarchy.</summary>
        private static Project FullAdder()
        {
            BoardEditor editor = HalfAdder("half_adder", 1, 2);
            Must(editor.AddModule("full_adder"));
            Must(editor.SetTop("full_adder"));

            var b = new Builder(editor, "full_adder");
            int a = b.In("a"), bb = b.In("b"), cin = b.In("cin"), sum = b.Out("sum"), cout = b.Out("cout");
            int h1 = b.Add(new BlockPart { Module = "half_adder" }), h2 = b.Add(new BlockPart { Module = "half_adder" });
            int or = b.Gate(GateKind.Or, 1, 1);
            b.Wire(a, "out", h1, "a");
            b.Wire(bb, "out", h1, "b");
            b.Wire(h1, "s", h2, "a");
            b.Wire(cin, "out", h2, "b");
            b.Wire(h2, "s", sum, "in");
            b.Wire(h1, "c", or, "a");
            b.Wire(h2, "c", or, "b");
            b.Wire(or, "y", cout, "in");

            return Exhaustive(editor, new[] { "a", "b", "cin" }, 20);
        }

        /// <summary>
        /// <paramref name="project"/> driven by the measuring run (Part 14c): each input stepped up and down from every value
        /// of the others, each step left longer than its slowest path to settle.
        /// </summary>
        private static Project Stepped(Project project)
        {
            long settle = StepTests.SettleFor(TimingAnalysis.Of(project).Paths.Worst?.Worst ?? 0);
            var editor = new BoardEditor(project);
            Must(editor.SetStimulus(StepTests.Exhaustive(project, settle).Stimulus));
            return editor.Project;
        }

        /// <summary>
        /// A false path (Part 14c): y = s ? (s ? a : b) : c, of gates -- an inner choice of a and b, and an outer one of that
        /// and c, both on s. b's way through the inner choice needs s at 0 and through the outer s at 1, so no step of any
        /// input ever sends a change from b to y, though the gates make a path of four, 8 long.
        /// </summary>
        private static Project FalsePath()
        {
            var editor = new BoardEditor(Project.New("false_path"));
            var b = new Builder(editor, "false_path");
            int s = b.In("s"), a = b.In("a"), bb = b.In("b"), c = b.In("c"), y = b.Out("y");
            int not = b.Gate(GateKind.Not, 1, 1, inputs: 1);
            int innerA = b.Gate(GateKind.And, 2, 2), innerB = b.Gate(GateKind.And, 2, 2), inner = b.Gate(GateKind.Or, 2, 2);
            int outerIn = b.Gate(GateKind.And, 2, 2), outerC = b.Gate(GateKind.And, 2, 2), outer = b.Gate(GateKind.Or, 2, 2);
            b.Wire(s, "out", not, "a");
            b.Wire(s, "out", innerA, "a");
            b.Wire(a, "out", innerA, "b");
            b.Wire(not, "y", innerB, "a");
            b.Wire(bb, "out", innerB, "b");
            b.Wire(innerA, "y", inner, "a");
            b.Wire(innerB, "y", inner, "b");
            b.Wire(s, "out", outerIn, "a");
            b.Wire(inner, "y", outerIn, "b");
            b.Wire(not, "y", outerC, "a");
            b.Wire(c, "out", outerC, "b");
            b.Wire(outerIn, "y", outer, "a");
            b.Wire(outerC, "y", outer, "b");
            b.Wire(outer, "y", y, "in");
            return editor.Project;
        }

        /// <summary>Four half adders as one array block (Part 12c): bit i of each bus to copy i.</summary>
        private static Project HalfAdders()
        {
            BoardEditor editor = HalfAdder("half_adder", 1, 2);
            Must(editor.AddModule("half_adders"));
            Must(editor.SetTop("half_adders"));

            var b = new Builder(editor, "half_adders");
            int a = b.In("a", 4), bb = b.In("b", 4), s = b.Out("s", 4), c = b.Out("c", 4);
            int ha = b.Add(new BlockPart { Module = "half_adder", Copies = 4 });
            b.Wire(a, "out", ha, "a");
            b.Wire(bb, "out", ha, "b");
            b.Wire(ha, "s", s, "in");
            b.Wire(ha, "c", c, "in");
            return Random(editor, new[] { ("a", 4), ("b", 4) }, 12, 9, 7);
        }

        /// <summary>
        /// An eight-bit ripple-carry adder (Part 12c): eight full adders as one array block, the carry passed from each copy
        /// to the next through a splitter and a joiner -- copy i's cout into copy i+1's cin, cin into copy 0's, copy 7's out.
        /// </summary>
        private static Project Ripple8()
        {
            Project fullAdder = FullAdder();
            var editor = new BoardEditor(fullAdder with { Stimulus = null });
            Must(editor.AddModule("ripple8"));
            Must(editor.SetTop("ripple8"));

            var b = new Builder(editor, "ripple8");
            int a = b.In("a", 8), bb = b.In("b", 8), cin = b.In("cin"), sum = b.Out("sum", 8), cout = b.Out("cout");
            int fa = b.Add(new BlockPart { Module = "full_adder", Copies = 8 });
            int carries = b.Add(new SplitterPart { Width = 8 });
            int chain = b.Add(new JoinerPart { Width = 8 });
            b.Wire(a, "out", fa, "a");
            b.Wire(bb, "out", fa, "b");
            b.Wire(fa, "sum", sum, "in");
            b.Wire(fa, "cout", carries, "in");
            b.Wire(cin, "out", chain, "i0");
            for (int i = 1; i < 8; i++)
                b.Wire(carries, "o" + (i - 1), chain, "i" + i);
            b.Wire(carries, "o7", cout, "in");
            b.Wire(chain, "out", fa, "cin");
            return Random(editor, new[] { ("a", 8), ("b", 8), ("cin", 1) }, 12, 40, 11);
        }

        /// <summary>
        /// A block written as code (Part 12d): a four-bit adder whose width is a parameter, its sum three units after its
        /// operands, inside a drawn top.
        /// </summary>
        private static Project Written()
        {
            var editor = new BoardEditor(Project.New("adder"));
            Must(editor.AddWrittenModule("add4", WrittenAdder));
            var b = new Builder(editor, "adder");
            int a = b.In("a", 4), bb = b.In("b", 4), s = b.Out("s", 4), cout = b.Out("cout");
            int add = b.Add(new BlockPart { Module = "add4" });
            b.Wire(a, "out", add, "a");
            b.Wire(bb, "out", add, "b");
            b.Wire(add, "s", s, "in");
            b.Wire(add, "cout", cout, "in");
            return Random(editor, new[] { ("a", 4), ("b", 4) }, 12, 10, 5);
        }

        /// <summary>
        /// A written block of the library's cells, written without their delays, under a library that gives them (Part 14g):
        /// a NAND2 at rise 2 and fall 3, then an INV at 1 and 2. It runs at the library's delays here, and under Icarus from
        /// the cells' file printed with them as its defaults -- what the export hands ModelSim.
        /// </summary>
        private static Project WrittenCells()
        {
            var editor = new BoardEditor(Project.New("pair"));
            Must(editor.AddWrittenModule("nand_inv",
                "module nand_inv (\n    input logic a,\n    input logic b,\n    output logic y,\n    output logic n\n);\n    NAND2 g1 (.A(a), .B(b), .Z(n));\n    INV g2 (.A(n), .Z(y));\nendmodule\n"));
            var b = new Builder(editor, "pair");
            int a = b.In("a"), bb = b.In("b"), y = b.Out("y"), n = b.Out("n");
            int u = b.Add(new BlockPart { Module = "nand_inv" });
            b.Wire(a, "out", u, "a");
            b.Wire(bb, "out", u, "b");
            b.Wire(u, "y", y, "in");
            b.Wire(u, "n", n, "in");

            GateLibrary library = GateLibrary.CourseCells(1);
            library = library.Retimed(library.CellFor(GateKind.Nand, 2), 2, 3);
            library = library.Retimed(library.CellFor(GateKind.Not, 1), 1, 2);
            Must(editor.SetLibrary(library));
            return Exhaustive(editor, new[] { "a", "b" }, 10);
        }

        /// <summary>
        /// Two written devices on one drawn wire through their inout pins (Part 14g), each driving it through a BUFIF1 of its
        /// own and reading it back, with a drawn BUFIF1 on it too and the wire an output of the board: a bus of three drivers,
        /// each alone, two at once, and none -- what Icarus must agree with, change for change.
        /// </summary>
        private static Project DevicesBus()
        {
            var editor = new BoardEditor(Project.New("shared"));
            Must(editor.AddWrittenModule("dev", Device));
            var b = new Builder(editor, "shared");
            int e1 = b.In("e1"), d1 = b.In("d1"), e2 = b.In("e2"), d2 = b.In("d2"), e3 = b.In("e3"), y = b.Out("y"), seen = b.Out("seen");
            int u1 = b.Add(new BlockPart { Module = "dev" }), u2 = b.Add(new BlockPart { Module = "dev" });
            int g = b.Add(new GatePart { Kind = GateKind.Bufif1, Inputs = 2, Rise = 2, Fall = 2 });
            b.Wire(e1, "out", u1, "en");
            b.Wire(d1, "out", u1, "d");
            b.Wire(e2, "out", u2, "en");
            b.Wire(d2, "out", u2, "d");
            b.Wire(u1, "io", y, "in");
            b.Wire(u2, "io", y, "in");
            b.Wire(d1, "out", g, "a");
            b.Wire(e3, "out", g, "en");
            b.Wire(g, "y", y, "in");
            b.Wire(u2, "seen", seen, "in");
            return Exhaustive(editor, new[] { "e1", "d1", "e2", "d2", "e3" }, 10);
        }

        /// <summary>A device on a bus, written: it drives its inout while enabled, and reads it back.</summary>
        public const string Device =
            "module dev (\n" +
            "    input logic en,\n" +
            "    input logic d,\n" +
            "    inout wire io,\n" +
            "    output logic seen\n" +
            ");\n" +
            "    bufif1 #1 g (io, d, en);\n" +
            "    assign seen = io;\n" +
            "endmodule\n";

        /// <summary>The written adder, as the printer would write it.</summary>
        public const string WrittenAdder =
            "module add4 #(\n" +
            "    parameter N = 4\n" +
            ") (\n" +
            "    input logic [N-1:0] a,\n" +
            "    input logic [N-1:0] b,\n" +
            "    output logic [N-1:0] s,\n" +
            "    output logic cout\n" +
            ");\n" +
            "    assign #3 {cout, s} = a + b;\n" +
            "endmodule\n";

        /// <summary><paramref name="vectors"/> random values on each input, <paramref name="gap"/> apart, from a fixed seed.</summary>
        private static Project Random(BoardEditor editor, (string Port, int Width)[] inputs, int vectors, long gap, int seed)
        {
            var random = new Random(seed);
            var tracks = new List<Track>();
            foreach ((string port, int width) in inputs)
            {
                var steps = new List<Step>();
                for (int t = 0; t < vectors; t++)
                    steps.Add(new Step(t * gap, Bits((ulong)random.Next(1 << width), width)));
                tracks.Add(new Track { Port = port, Steps = steps });
            }

            Must(editor.SetStimulus(new Stimulus { Tracks = tracks, End = vectors * gap }));
            return editor.Project;
        }

        /// <summary>
        /// A two-bit Johnson counter on a clock, its registers starting at 0 -- one of them straight
        /// onto an output port, which cannot itself be given a starting value -- and a third register
        /// with a reset, pulsed by the stimulus.
        /// </summary>
        /// <summary>
        /// A project with every register given a clock to Q and a setup (Part 14a): its writes carry the delay, so it runs --
        /// and prints, and imports again -- with each register's q that long after the edge.
        /// </summary>
        private static Project Timed(Project project, long tcq, long tsetup)
        {
            Module top = project.TopModule;
            var parts = new List<Part>();
            foreach (Part part in top.Parts)
                parts.Add(part is RegisterPart r ? r with { Tcq = tcq, Tsetup = tsetup } : part);
            return project.WithModule(top with { Parts = parts });
        }

        private static Project Johnson()
        {
            var editor = new BoardEditor(Project.New("johnson"));
            var b = new Builder(editor, "johnson");
            int clk = b.In("clk"), rst = b.In("rst"), q0 = b.Out("q0"), q1 = b.Out("q1"), held = b.Out("held");
            int r1 = b.Add(new RegisterPart { Initial = Bits(0, 1) });
            int r2 = b.Add(new RegisterPart { Initial = Bits(0, 1) });
            int r3 = b.Add(new RegisterPart { HasReset = true, ResetValue = Bits(1, 1) });
            int inv = b.Gate(GateKind.Not, 1, 1);

            b.Wire(clk, "out", r1, "clk");
            b.Wire(clk, "out", r2, "clk");
            b.Wire(clk, "out", r3, "clk");
            b.Wire(rst, "out", r3, "rst");
            b.Wire(r1, "q", r2, "d");
            b.Wire(r2, "q", inv, "a");
            b.Wire(inv, "y", r1, "d");
            b.Wire(r1, "q", q0, "in");
            b.Wire(r2, "q", q1, "in");
            b.Wire(r2, "q", r3, "d");
            b.Wire(r3, "q", held, "in");

            Must(editor.SetStimulus(new Stimulus
            {
                Clocks = new[] { new ClockTrack { Port = "clk", Period = 10 } },
                Tracks = new[] { new Track { Port = "rst", Steps = new[] { new Step(0, Bits(1, 1)), new Step(12, Bits(0, 1)), new Step(61, Bits(1, 1)), new Step(64, Bits(0, 1)) } } },
                End = 100,
            }));
            return editor.Project;
        }

        /// <summary>
        /// Buses: two four-bit inputs ANDed and XORed with a constant as vectors with delay, a four-bit
        /// mux choosing between them, a splitter and a NOR of the bits for "zero", and a joiner putting
        /// the bits back the other way round.
        /// </summary>
        private static Project Buses()
        {
            var editor = new BoardEditor(Project.New("buses"));
            var b = new Builder(editor, "buses");
            int a = b.In("a", 4), bb = b.In("b", 4), s = b.In("s");
            int y = b.Out("y", 4), zero = b.Out("zero"), reversed = b.Out("reversed", 4);
            int and = b.Gate(GateKind.And, 1, 1, width: 4);
            // Equal rise and fall: which of two a vector takes is where Icarus and the standard part (Docs/oracle-notes.md).
            int xor = b.Gate(GateKind.Xor, 2, 2, width: 4);
            int k = b.Add(new ConstantPart { Value = Bits(0b1010, 4) });
            int mux = b.Add(new MuxPart { Width = 4 });
            int split = b.Add(new SplitterPart { Width = 4 });
            int nor = b.Gate(GateKind.Nor, 1, 2, inputs: 4);
            int join = b.Add(new JoinerPart { Width = 4 });

            b.Wire(a, "out", and, "a");
            b.Wire(bb, "out", and, "b");
            b.Wire(bb, "out", xor, "a");
            b.Wire(k, "y", xor, "b");
            b.Wire(and, "y", mux, "d0");
            b.Wire(xor, "y", mux, "d1");
            b.Wire(s, "out", mux, "s");
            b.Wire(mux, "y", y, "in");
            b.Wire(mux, "y", split, "in");
            for (int i = 0; i < 4; i++)
            {
                b.Wire(split, "o" + i, nor, GatePart.InputName(i));
                b.Wire(split, "o" + i, join, "i" + (3 - i));
            }

            b.Wire(nor, "y", zero, "in");
            b.Wire(join, "out", reversed, "in");

            var random = new Random(5);
            var steps = new Dictionary<string, List<Step>> { ["a"] = new List<Step>(), ["b"] = new List<Step>(), ["s"] = new List<Step>() };
            for (int t = 0; t < 12; t++)
            {
                steps["a"].Add(new Step(t * 7, Bits((ulong)random.Next(16), 4)));
                steps["b"].Add(new Step(t * 7, Bits((ulong)random.Next(16), 4)));
                steps["s"].Add(new Step(t * 7, Bits((ulong)random.Next(2), 1)));
            }

            Must(editor.SetStimulus(new Stimulus
            {
                Tracks = new[]
                {
                    new Track { Port = "a", Steps = steps["a"] },
                    new Track { Port = "b", Steps = steps["b"] },
                    new Track { Port = "s", Steps = steps["s"] },
                },
                End = 100,
            }));
            return editor.Project;
        }

        /// <summary>
        /// Tri-state gates, each the one driver of its net: q is d while en is 1 and z otherwise, through
        /// a bufif1 with delay; nq is d inverted while en is 0, through an ideal notif0.
        /// </summary>
        private static Project TriState()
        {
            var editor = new BoardEditor(Project.New("tristate"));
            var b = new Builder(editor, "tristate");
            int d = b.In("d"), en = b.In("en"), q = b.Out("q"), nq = b.Out("nq");
            int buf = b.Add(new GatePart { Kind = GateKind.Bufif1, Inputs = 2, Rise = 1, Fall = 2 });
            int inv = b.Add(new GatePart { Kind = GateKind.Notif0, Inputs = 2 });
            b.Wire(d, "out", buf, "a");
            b.Wire(en, "out", buf, "en");
            b.Wire(buf, "y", q, "in");
            b.Wire(d, "out", inv, "a");
            b.Wire(en, "out", inv, "en");
            b.Wire(inv, "y", nq, "in");

            Must(editor.SetStimulus(new Stimulus
            {
                Tracks = new[]
                {
                    new Track { Port = "d", Steps = new[] { new Step(0, Bits(1, 1)), new Step(9, Bits(0, 1)), new Step(14, Bits(1, 1)) } },
                    new Track { Port = "en", Steps = new[] { new Step(0, Bits(0, 1)), new Step(5, Bits(1, 1)), new Step(12, Bits(0, 1)) } },
                },
                End = 20,
            }));
            return editor.Project;
        }

        /// <summary>
        /// A bus (Part 14f): two tri-state gates on one wire, each driving while its enable is high -- a's 0 alone, then both
        /// at once, where they disagree and the bus reads x, then b's 1 alone, then neither, and it floats.
        /// </summary>
        private static Project Bus()
        {
            var editor = new BoardEditor(Project.New("bus"));
            var b = new Builder(editor, "bus");
            int a = b.In("a"), bb = b.In("b"), ea = b.In("ea"), eb = b.In("eb"), y = b.Out("y");
            int ga = b.Add(new GatePart { Kind = GateKind.Bufif1, Inputs = 2, Rise = 1, Fall = 2 });
            int gb = b.Add(new GatePart { Kind = GateKind.Bufif1, Inputs = 2, Rise = 1, Fall = 2 });
            b.Wire(a, "out", ga, "a");
            b.Wire(ea, "out", ga, "en");
            b.Wire(ga, "y", y, "in");
            b.Wire(bb, "out", gb, "a");
            b.Wire(eb, "out", gb, "en");
            b.Wire(gb, "y", y, "in");

            Must(editor.SetStimulus(new Stimulus
            {
                Tracks = new[]
                {
                    new Track { Port = "a", Steps = new[] { new Step(0, Bits(0, 1)) } },
                    new Track { Port = "b", Steps = new[] { new Step(0, Bits(1, 1)) } },
                    new Track { Port = "ea", Steps = new[] { new Step(0, Bits(0, 1)), new Step(10, Bits(1, 1)), new Step(30, Bits(0, 1)) } },
                    new Track { Port = "eb", Steps = new[] { new Step(0, Bits(0, 1)), new Step(20, Bits(1, 1)), new Step(40, Bits(0, 1)) } },
                },
                End = 50,
            }));
            return editor.Project;
        }

        /// <summary>A four-input mux on single bits, its select two bits wide, and an ideal gate on its output.</summary>
        private static Project Mux4()
        {
            var editor = new BoardEditor(Project.New("mux4"));
            var b = new Builder(editor, "mux4");
            int d0 = b.In("d0"), d1 = b.In("d1"), d2 = b.In("d2"), d3 = b.In("d3"), s = b.In("s", 2), y = b.Out("y"), ny = b.Out("ny");
            int mux = b.Add(new MuxPart { Inputs = 4 });
            int not = b.Gate(GateKind.Not);
            b.Wire(d0, "out", mux, "d0");
            b.Wire(d1, "out", mux, "d1");
            b.Wire(d2, "out", mux, "d2");
            b.Wire(d3, "out", mux, "d3");
            b.Wire(s, "out", mux, "s");
            b.Wire(mux, "y", y, "in");
            b.Wire(mux, "y", not, "a");
            b.Wire(not, "y", ny, "in");

            var tracks = new List<Track>();
            string[] data = { "d0", "d1", "d2", "d3" };
            for (int i = 0; i < 4; i++)
                tracks.Add(new Track { Port = data[i], Steps = new[] { new Step(0, Bits((ulong)(0b0110 >> i) & 1, 1)) } });
            var select = new List<Step>();
            for (int v = 0; v < 4; v++)
                select.Add(new Step(v * 5, Bits((ulong)v, 2)));
            tracks.Add(new Track { Port = "s", Steps = select });

            Must(editor.SetStimulus(new Stimulus { Tracks = tracks, End = 25 }));
            return editor.Project;
        }
    }
}
