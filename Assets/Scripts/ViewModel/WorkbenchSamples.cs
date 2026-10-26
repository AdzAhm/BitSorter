using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using BoardPoint = BitSorter.Board.Point;
using BoardRoute = BitSorter.Board.Route;
using BoardStimulus = BitSorter.Board.Stimulus;
using BoardTrack = BitSorter.Board.Track;
using EngineGate = BitSorter.Sim.GateKind;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The circuits the workbench opens, switched from its SAMPLE chip (Ahmad's choice, 2026-10-16):
    /// the reference circuit, a full adder of two half-adder blocks for hierarchy, and a clocked
    /// counter for a register, a clock and a reset.
    /// </summary>
    /// <remarks>
    /// Each is drawn through <see cref="BoardEditor"/>, as <see cref="ReferenceCircuit"/> is and as a
    /// player would draw it, so the editor holds it to every rule a board is held to -- and has
    /// nothing to say about any of them, not even a warning. Compiled and run on the engine, every
    /// value, change and glitch on screen is the engine's.
    ///
    /// **Laid out to be read**, held by <c>SampleTests</c> rather than by eye: inputs on the left and
    /// outputs on the right, every wire at right angles, no wire through a part or the words written
    /// round one, no two nets running along or touching each other, and a wire crossing another only
    /// where the circuit leaves no way round -- once in each new sample, said where below; the
    /// reference crosses nothing. The waveform's rows are the ports top to bottom
    /// (<see cref="DocumentSchematic"/>), so where a port stands decides where its row is, and each
    /// sample places its ports for its rows as well as its wires.
    ///
    /// **Sized by the reference**, 17 cells by 7: none taller, and none more than a quarter wider. The
    /// stage is fitted to the window's width but scrolls rather than shrink below its own height, so
    /// a wider sample only draws its words smaller on a narrow window, and a taller one scrolls the
    /// page on a short one -- height is what a sample must not spend.
    /// </remarks>
    public static class WorkbenchSamples
    {
        /// <summary>
        /// What the chip offers, in its order: the reference, the full adder, the counter -- each with
        /// its name, the line the workbench says it with under its title, and its document.
        /// </summary>
        public static readonly IReadOnlyList<(string Name, string Says, Func<Project> Build)> All =
            Array.AsReadOnly(new (string Name, string Says, Func<Project> Build)[]
            {
                ("Reference",
                    "The look reference's circuit as a board document, run on BitSorter's 5.0 engine: every value, and every change on its way, is the engine's.",
                    ReferenceCircuit.Document),
                ("Full adder",
                    "A full adder of two half-adder blocks and an OR, stepped through all eight inputs: where the two paths to s differ, it glitches.",
                    FullAdder),
                ("Counter",
                    "A three-bit counter: a register with an asynchronous reset, counting on each rising edge of its clock and wrapping round to 0.",
                    Counter),
                ("Four half adders",
                    "Four half adders as one block of four copies, printed as a generate for: bit i of a, b, s and c is copy i's. Double-click it to step inside the first.",
                    FourHalfAdders),
            });

        /// <summary>
        /// The student's own circuit (Part 10a), the first thing the SAMPLE chip steps to: a blank document the
        /// first time, and whatever was built in it after that, kept in the save under <see cref="MyCircuitKey"/>.
        /// </summary>
        public static readonly (string Name, string Says, Func<Project> Build) MyCircuit =
            ("My circuit", "Your own circuit, kept for next time: pick a part on the left and click to place it, then drag from a pin to wire it.", Blank);

        /// <summary>What MY CIRCUIT is kept under in the save's 5.0 documents.</summary>
        public const string MyCircuitKey = "workbench";

        /// <summary>A document with nothing in it, its top module called my_circuit: where MY CIRCUIT starts.</summary>
        public static Project Blank() => Project.New("my_circuit");

        /// <summary>What the SAMPLE chip steps through, in its order: MY CIRCUIT, then <see cref="All"/>.</summary>
        public static readonly IReadOnlyList<(string Name, string Says, Func<Project> Build)> Chip = BuildChip();

        private static IReadOnlyList<(string, string, Func<Project>)> BuildChip()
        {
            var chip = new List<(string, string, Func<Project>)> { MyCircuit };
            chip.AddRange(All);
            return chip.AsReadOnly();
        }

        /// <summary>How long the full adder's testbench holds each combination of its inputs, in nanoseconds.</summary>
        public const long FullAdderStep = 6;

        /// <summary>Where the full adder's run ends: a step after its last combination, so the waveform shows it settled.</summary>
        public const long FullAdderEnd = 8 * FullAdderStep;

        /// <summary>The counter's clock period, in nanoseconds: the clock starts low, so it rises at 2 and every 4 after.</summary>
        public const long CounterPeriod = 4;

        /// <summary>When the counter's reset falls: on the clock's first falling edge, after the first rising one.</summary>
        public const long CounterResetFalls = 4;

        /// <summary>How many bits the counter counts in.</summary>
        public const int CounterBits = 3;

        /// <summary>Where the counter's run ends: nine periods, through 1 to 7 and back to 0.</summary>
        public const long CounterEnd = 9 * CounterPeriod;

        /// <summary>
        /// A full adder of two half-adder blocks and an OR, the course's first hierarchy: s is a XOR b
        /// XOR cin and cout is the carry of either half, worked out by each half adder's own XOR and
        /// AND. The testbench steps through all eight combinations in truth-table order, a first,
        /// <see cref="FullAdderStep"/> apart.
        /// </summary>
        /// <remarks>
        /// - **Delays so a change is seen travelling**: the half adder's XOR 2 ns and AND 1 ns, the OR
        ///   1 ns. The slowest way through is the two XORs, 4 ns, so each combination is still for 2
        ///   before the next.
        /// - **s glitches, twice, and the engine is right to.** Where cin changes and the first half's
        ///   sum changes with it, the second XOR hears the two 2 ns apart and passes a pulse of 2: s
        ///   falls at 14 and is back at 16, and rises at 38 and falls again at 40. A hazard, shown and
        ///   not taught (CLAUDE.md, Syllabus scope).
        /// - **The second half adder takes the first's sum on a and cin on b**, cin from beneath. That
        ///   costs the one crossing: cin has to reach h2 between the blocks, and h1's carry has to leave
        ///   between them for the OR. Brought in over the top to h2's a, the sum moved to b, cin would
        ///   cross nothing, but its port would head the waveform; from beneath the rows read a, b, cin,
        ///   s, cout, as a truth table does, which is why s and cout stand low on the right too.
        /// - The half adder's own module is laid out as well, for the day a block is opened.
        /// </remarks>
        public static Project FullAdder()
        {
            var editor = new BoardEditor(Project.New("full_adder"));
            DrawHalfAdder(editor);

            var top = new Drawing(editor, "full_adder");
            int a = top.In("a", 1, 0.5), b = top.In("b", 1, -0.5), cin = top.In("cin", 1, -2);
            int s = top.Out("s", 16.5, -2), cout = top.Out("cout", 16.5, -3);
            int h1 = top.Add(new BlockPart { Name = "h1", Module = "half_adder" }, 5, 0);
            int h2 = top.Add(new BlockPart { Name = "h2", Module = "half_adder" }, 10, 0);
            int or = top.Gate(EngineGate.Or, 14, -3, 1);

            top.Wire(At(a, "out"), At(h1, "a"));
            top.Wire(At(b, "out"), At(h1, "b"));
            top.Wire(At(h1, "s"), At(h2, "a"));
            top.Wire(At(cin, "out"), At(h2, "b"), Bends((8, -2), (8, -0.25)));
            top.Wire(At(h1, "c"), At(or, "b"), Bends((7, -0.25), (7, -3.25)));
            top.Wire(At(h2, "c"), At(or, "a"), Bends((12, -0.25), (12, -2.75)));
            top.Wire(At(h2, "s"), At(s, "in"), Bends((12.5, 0.25), (12.5, -2)));
            top.Wire(At(or, "y"), At(cout, "in"));

            // Every combination, a the most significant: the truth table's rows, in its order.
            string[] inputs = { "a", "b", "cin" };
            var tracks = new BoardTrack[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                var steps = new Step[8];
                for (int v = 0; v < 8; v++)
                    steps[v] = new Step(v * FullAdderStep, Bits((ulong)(v >> (inputs.Length - 1 - i)) & 1, 1));
                tracks[i] = new BoardTrack { Port = inputs[i], Steps = steps };
            }

            Must(editor.SetStimulus(new BoardStimulus { Tracks = tracks, End = FullAdderEnd }));
            return editor.Project;
        }

        /// <summary>How long the four half adders' testbench holds each pair of inputs, in nanoseconds.</summary>
        public const long FourHalfAddersStep = 6;

        /// <summary>
        /// Four half adders as one array block (Part 12c): a and b four bits each, s and c four bits each, and copy i
        /// working on bit i of each -- printed as a <c>generate for</c>, as the course writes four of anything. The testbench
        /// gives eight pairs of inputs, <see cref="FourHalfAddersStep"/> apart.
        /// </summary>
        public static Project FourHalfAdders()
        {
            var editor = new BoardEditor(Project.New("half_adders"));
            DrawHalfAdder(editor);

            var top = new Drawing(editor, "half_adders");
            int a = top.In("a", 1, 0.5, 4), b = top.In("b", 1, -0.5, 4);
            int s = top.Out("s", 10, 0.5, 4), c = top.Out("c", 10, -0.5, 4);
            int ha = top.Add(new BlockPart { Name = "ha", Module = "half_adder", Copies = 4 }, 5.5, 0);
            top.Wire(At(a, "out"), At(ha, "a"));
            top.Wire(At(b, "out"), At(ha, "b"));
            top.Wire(At(ha, "s"), At(s, "in"));
            top.Wire(At(ha, "c"), At(c, "in"));

            ulong[] aValues = { 0x0, 0x5, 0xA, 0xF, 0x3, 0xC, 0x9, 0xF };
            ulong[] bValues = { 0x0, 0x3, 0x5, 0x1, 0xF, 0xC, 0x6, 0xF };
            var aSteps = new Step[aValues.Length];
            var bSteps = new Step[bValues.Length];
            for (int i = 0; i < aValues.Length; i++)
            {
                aSteps[i] = new Step(i * FourHalfAddersStep, Bits(aValues[i], 4));
                bSteps[i] = new Step(i * FourHalfAddersStep, Bits(bValues[i], 4));
            }

            Must(editor.SetStimulus(new BoardStimulus
            {
                Tracks = new[] { new BoardTrack { Port = "a", Steps = aSteps }, new BoardTrack { Port = "b", Steps = bSteps } },
                End = aValues.Length * FourHalfAddersStep,
            }));
            return editor.Project;
        }

        /// <summary>
        /// The half adder every sample with blocks uses: a XOR for s and an AND for c, 2 ns and 1 ns. Each input feeds
        /// both gates through a tap, as a textbook draws it, and the two taps cross -- a half adder drawn with its inputs
        /// on one side cannot avoid it. A block's pins are its module's ports, so it is drawn before anything uses it.
        /// </summary>
        private static void DrawHalfAdder(BoardEditor editor)
        {
            Must(editor.AddModule("half_adder"));
            var half = new Drawing(editor, "half_adder");
            int ha = half.In("a", 1, 0.75), hb = half.In("b", 1, -1.25);
            int hs = half.Out("s", 9, 0.5), hc = half.Out("c", 9, -1);
            int xor = half.Gate(EngineGate.Xor, 6, 0.5, 2);
            int and = half.Gate(EngineGate.And, 6, -1, 1);
            int toXor = half.Wire(At(ha, "out"), At(xor, "a"));
            int toAnd = half.Wire(At(hb, "out"), At(and, "b"));
            half.Wire(Endpoint.OnWire(toXor), At(and, "a"), Bends((3, -0.75)), tapAt: 1.5 / 3.5);
            half.Wire(Endpoint.OnWire(toAnd), At(xor, "b"), Bends((3.5, 0.25)), tapAt: 2.0 / 3.5);
            half.Wire(At(xor, "y"), At(hs, "in"));
            half.Wire(At(and, "y"), At(hc, "in"));
        }

        /// <summary>
        /// A three-bit synchronous counter: one register of <see cref="CounterBits"/> bits, taken apart
        /// by a splitter, its next value worked out a bit at a time, joined and fed back. On a 4 ns
        /// clock, with an asynchronous reset held through the first rising edge, it counts 0 to 7 and
        /// wraps to 0 at 34.
        /// </summary>
        /// <remarks>
        /// - **Bit 0 flips every clock (NOT), bit 1 when bit 0 is 1 (XOR), bit 2 when bits 1 and 0 both
        ///   are (an AND for the carry, then a XOR)**: adding one, as the course derives it.
        /// - **One register of three bits, not three of one**, as the course writes
        ///   <c>logic [2:0] count</c>. The clock and the reset are then one wire each. Three flip-flops in
        ///   a row, each fed from shared clock and reset lines, were tried on paper first: the lines
        ///   crossed each other at every flip-flop but the last, and each bit's way back crossed the
        ///   others' ways out.
        /// - **Everything settles inside a period, with a nanosecond to spare.** The slowest way from the
        ///   register back to it is the AND and the second XOR, 3 ns of the clock's 4: the rule every
        ///   sequential circuit lives by, there to be watched.
        /// - **Twice a change is swallowed on its way out of the second XOR**, at 19 and 35: bit 2 moves
        ///   at the edge, the carry follows a nanosecond later, and the XOR's answer to the first is
        ///   cancelled by the second before it arrives. Inertial delay, in a circuit that works.
        /// - **The reset is the register's own**, asynchronous and active high, the one kind a
        ///   <see cref="RegisterPart"/> has. It rises at 0, so the register is 0 from the start; the edge
        ///   at 2 comes while it is held and changes nothing; it falls at 4, on a falling edge, which is
        ///   when the course's testbenches change an input.
        /// - **The one crossing** is bit 0 on its way up to the AND, over bit 1 on its way along to its
        ///   XOR. The splitter puts bit 1 between bits 2 and 0, and the AND, which takes bits 1 and 0,
        ///   feeds bit 2's XOR at the top, so bit 0 has to get past bit 1 to reach it.
        /// - **The rows read count, clk, rst**, the ports top to bottom. Dropping count below rst to
        ///   put it last bent its wire down the right-hand side for nothing but the order.
        /// - The bits' nets are named q0 to q2, the next value's d0 to d2 and next, and the AND's
        ///   carry, so pointing at a wire says what it carries.
        /// </remarks>
        public static Project Counter()
        {
            var editor = new BoardEditor(Project.New("counter"));
            var d = new Drawing(editor, "counter");

            int clk = d.In("clk", 1, -2.5), rst = d.In("rst", 1, -3.25);
            int count = d.Out("count", 18.5, 0, CounterBits);

            int split = d.Add(new SplitterPart { Width = CounterBits }, 3, 0);
            int and = d.Gate(EngineGate.And, 7, 1.25, 1);
            int xor2 = d.Gate(EngineGate.Xor, 10, 2.25, 2);
            int xor1 = d.Gate(EngineGate.Xor, 10, 0, 2);
            int not = d.Gate(EngineGate.Not, 10, -1.5, 1);
            int join = d.Add(new JoinerPart { Width = CounterBits }, 13, 0);
            int reg = d.Add(new RegisterPart { Width = CounterBits, HasReset = true, ResetValue = Bits(0, CounterBits) }, 15.5, -0.25);

            // The count, out to its port and back over the top to be taken apart.
            int q = d.Wire(At(reg, "q"), At(count, "in"));
            d.Wire(Endpoint.OnWire(q), At(split, "in"), Bends((17, 3.25), (2, 3.25), (2, 0)), tapAt: 0.5 / 1.5);

            // Each bit, and what it takes. Bit 0 goes up to the AND left of bit 1's way up, so that
            // bit 0's horizontal into it passes over the top of bit 1's.
            int q2 = d.Wire(At(split, "o2"), At(xor2, "a"), Bends((4, 0.5), (4, 2.5)));
            int q1 = d.Wire(At(split, "o1"), At(xor1, "a"), Bends((8.25, 0), (8.25, 0.25)));
            int q0 = d.Wire(At(split, "o0"), At(xor1, "b"), Bends((8.25, -0.5), (8.25, -0.25)));
            d.Wire(Endpoint.OnWire(q1), At(and, "b"), Bends((5.5, 1)), tapAt: 2.0 / 5.75);
            d.Wire(Endpoint.OnWire(q0), At(and, "a"), Bends((4.5, 1.5)), tapAt: 1.0 / 5.75);
            d.Wire(Endpoint.OnWire(q0), At(not, "a"), Bends((7.5, -1.5)), tapAt: 4.0 / 5.75);
            int carry = d.Wire(At(and, "y"), At(xor2, "b"));

            // The next value, a bit a gate, joined and into the register.
            int d2 = d.Wire(At(xor2, "y"), At(join, "i2"), Bends((11.5, 2.25), (11.5, 0.5)));
            int d1 = d.Wire(At(xor1, "y"), At(join, "i1"));
            int d0 = d.Wire(At(not, "y"), At(join, "i0"), Bends((12, -1.5), (12, -0.5)));
            int next = d.Wire(At(join, "out"), At(reg, "d"));

            d.Wire(At(clk, "out"), At(reg, "clk"), Bends((14, -2.5), (14, -0.5)));
            d.Wire(At(rst, "out"), At(reg, "rst"), Bends((15.5, -3.25)));

            foreach ((int wire, string name) in new[] { (q0, "q0"), (q1, "q1"), (q2, "q2"), (carry, "carry"), (d0, "d0"), (d1, "d1"), (d2, "d2"), (next, "next") })
                Must(editor.NameWire("counter", wire, name));

            Must(editor.SetStimulus(new BoardStimulus
            {
                Clocks = new[] { new ClockTrack { Port = "clk", Period = CounterPeriod } },
                Tracks = new[] { new BoardTrack { Port = "rst", Steps = new[] { new Step(0, Bits(1, 1)), new Step(CounterResetFalls, Bits(0, 1)) } } },
                End = CounterEnd,
            }));
            return editor.Project;
        }

        // -----------------------------------------------------------------
        // Drawing
        // -----------------------------------------------------------------

        private static LogicVec Bits(ulong value, int width) => LogicVec.FromUInt64(value, width);

        private static Endpoint At(int part, string pin) => Endpoint.At(part, pin);

        /// <summary>
        /// A wire's own bends, from its <see cref="Wire.From"/> end to its <see cref="Wire.To"/>: given
        /// wherever the automatic route would run through a part or along another wire, and where a
        /// tap must turn at a place of its own choosing rather than half-way.
        /// </summary>
        private static BoardRoute Bends(params (double X, double Y)[] bends)
        {
            var points = new BoardPoint[bends.Length];
            for (int i = 0; i < bends.Length; i++)
                points[i] = new BoardPoint(bends[i].X, bends[i].Y);
            return new BoardRoute { Bends = points };
        }

        private static int Must(EditResult result)
        {
            if (!result.Done)
                throw new InvalidOperationException(result.Refusal);
            return result.Id;
        }

        /// <summary>One module being drawn: parts placed on the grid and wires between them, each edit made or its refusal thrown.</summary>
        private sealed class Drawing
        {
            private readonly BoardEditor _editor;
            private readonly string _module;

            public Drawing(BoardEditor editor, string module)
            {
                _editor = editor;
                _module = module;
            }

            public int Add(Part part, double x, double y) => Must(_editor.Add(_module, part, new Placement(x, y)));

            public int In(string name, double x, double y, int width = 1) => Add(new PortPart { Name = name, Width = width }, x, y);

            public int Out(string name, double x, double y, int width = 1) =>
                Add(new PortPart { Name = name, Direction = PortDirection.Output, Width = width }, x, y);

            /// <summary>A one-bit gate, its rise and fall both <paramref name="delay"/>.</summary>
            public int Gate(EngineGate kind, double x, double y, long delay) =>
                Add(new GatePart { Kind = kind, Inputs = kind == EngineGate.Not || kind == EngineGate.Buf ? 1 : 2, Rise = delay, Fall = delay }, x, y);

            /// <summary>
            /// A wire, routed automatically or through <paramref name="route"/>'s bends. A wire that
            /// taps another joins it <paramref name="tapAt"/> of the way along the tapped wire's own
            /// route -- a fraction of its length, so it is worked out from that route's bends.
            /// </summary>
            public int Wire(Endpoint from, Endpoint to, BoardRoute route = null, double tapAt = 0.5)
            {
                if (from.IsTap || to.IsTap)
                    route = (route ?? new BoardRoute()) with { TapAt = tapAt };
                return Must(_editor.Connect(_module, from, to, route));
            }
        }
    }
}
