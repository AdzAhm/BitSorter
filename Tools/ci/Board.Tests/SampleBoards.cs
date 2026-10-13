using System;
using System.Collections.Generic;
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
            "half_adder", "half_adder_delayed", "full_adder", "johnson", "buses", "mux4",
        };

        public static Project Build(string name)
        {
            switch (name)
            {
                case "half_adder": return Exhaustive(HalfAdder("half_adder", 0, 0), new[] { "a", "b" }, 10);
                case "half_adder_delayed": return Exhaustive(HalfAdder("half_adder", 2, 3), new[] { "a", "b" }, 10);
                case "full_adder": return FullAdder();
                case "johnson": return Johnson();
                case "buses": return Buses();
                case "mux4": return Mux4();
                default: throw new ArgumentException(name);
            }
        }

        private static LogicVec Bits(ulong value, int width) => LogicVec.FromUInt64(value, width);

        private static int Must(EditResult result)
        {
            if (!result.Done)
                throw new InvalidOperationException("refused: " + result.Refusal);
            return result.Id;
        }

        private sealed class Builder
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
        private static Project Exhaustive(BoardEditor editor, string[] inputs, long gap)
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
        /// A two-bit Johnson counter on a clock, its registers starting at 0 -- one of them straight
        /// onto an output port, which cannot itself be given a starting value -- and a third register
        /// with a reset, pulsed by the stimulus.
        /// </summary>
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
