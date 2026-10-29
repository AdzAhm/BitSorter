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
                ("Written adder",
                    "A four-bit adder written as SystemVerilog rather than drawn. Double-click it to edit its code: the waves follow as you type.",
                    WrittenAdder),
                ("Traffic light",
                    "A traffic light written as code: four states, the road green until a car waits on the side street. STATES draws its machine; double-click u_lights to step inside.",
                    TrafficLight),
                ("Two-bit ALU",
                    "A 2-bit ALU written as code, its adder two full adders: LOGIC tables all 64 inputs, GATES opens its operators, and a double-click on adder steps inside.",
                    TwoBitAlu),
                ("Is it prime?",
                    "Whether a 4-bit number is prime, written as one case: LOGIC shows its truth table, its K-map and the smallest sum of products.",
                    IsItPrime),
                ("Sequence detector",
                    "Finds 1 0 1 in a stream of bits, overlapping, written as code: a Mealy machine, its output reading the input too -- STATES draws its three states.",
                    SequenceDetector),
                ("Seven segments",
                    "A hex digit to a seven-segment display, written as one case: four inputs, seven outputs -- LOGIC gives each segment its K-map.",
                    SevenSegments),
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

        /// <summary>How long the written adder's testbench holds each pair of inputs.</summary>
        public const long WrittenAdderStep = 8;

        /// <summary>The written adder's text: a parameter for its width, and its sum and carry three units after its operands.</summary>
        // -----------------------------------------------------------------
        // Written as code, as a student's would be (Part 15h-4d, asked 2026-10-29: "a couple more samples")
        // -----------------------------------------------------------------

        /// <summary>
        /// A project written as SystemVerilog files, imported as FILE's IMPORT would read them -- its testbench the run, its
        /// design shown as the drawing of its code. Each a few lines, written for BitSorter, nothing from the course.
        /// </summary>
        private static Project Written(params (string File, string Text)[] files)
        {
            var sources = new SourceFile[files.Length];
            for (int i = 0; i < files.Length; i++)
                sources[i] = new SourceFile(files[i].File, files[i].Text);
            ProjectImport import = ProjectImporter.Import(sources);
            if (!import.Succeeded)
                throw new InvalidOperationException("a written sample did not import");
            return import.Project;
        }

        /// <summary>A traffic light where a side street meets a main road: four states, a Moore machine -- for STATES.</summary>
        public static Project TrafficLight() => Written(("traffic.sv", TrafficSource), ("lights.sv", LightsSource), ("traffic_tb.sv", TrafficBenchSource));

        /// <summary>A 2-bit ALU whose adder is two full adders, a block inside a block -- for LOGIC, GATES and stepping inside.</summary>
        public static Project TwoBitAlu() => Written(("fa.sv", FullAdderGatesSource), ("add2.sv", Add2Source), ("alu2.sv", Alu2Source), ("alu2_tb.sv", Alu2BenchSource));

        /// <summary>Whether a 4-bit number is prime: four inputs -- for LOGIC's truth table, K-map and smallest sum of products.</summary>
        public static Project IsItPrime() => Written(("prime4.sv", Prime4Source), ("prime4_tb.sv", Prime4BenchSource));

        /// <summary>A 1-0-1 detector, overlapping: three states, a Mealy machine -- for STATES beside the traffic light's Moore.</summary>
        public static Project SequenceDetector() => Written(("seq101.sv", Seq101Source), ("seq101_tb.sv", Seq101BenchSource));

        /// <summary>A hex digit to seven segments: four inputs, seven outputs -- a K-map a segment.</summary>
        public static Project SevenSegments() => Written(("seg7.sv", Seg7Source), ("seg7_tb.sv", Seg7BenchSource));

        private const string Seq101Source =
            "// Says 1 the moment the last three bits in were 1, 0, 1 -- overlapping, so\n" +
            "// 1 0 1 0 1 finds two. A Mealy machine: its output reads the input as well\n" +
            "// as the state.\n" +
            "module seq101 (\n" +
            "    input  logic clk,\n" +
            "    input  logic reset,\n" +
            "    input  logic x,       // a bit in, each clock\n" +
            "    output logic found    // 1 while x completes 1 0 1\n" +
            ");\n" +
            "    localparam logic [1:0] NONE = 2'd0,   // nothing of it yet\n" +
            "                           ONE  = 2'd1,   // seen 1\n" +
            "                           ONE0 = 2'd2;   // seen 1 0\n" +
            "\n" +
            "    logic [1:0] state, next;\n" +
            "\n" +
            "    always_ff @(posedge clk or posedge reset)\n" +
            "        if (reset) state <= NONE;\n" +
            "        else       state <= next;\n" +
            "\n" +
            "    always_comb begin\n" +
            "        next  = NONE;\n" +
            "        found = 1'b0;\n" +
            "        case (state)\n" +
            "            NONE: next = x ? ONE : NONE;\n" +
            "            ONE:  next = x ? ONE : ONE0;\n" +
            "            ONE0: begin\n" +
            "                next  = x ? ONE : NONE;\n" +
            "                found = x;\n" +
            "            end\n" +
            "            default: next = NONE;\n" +
            "        endcase\n" +
            "    end\n" +
            "endmodule\n";

        private const string Seq101BenchSource =
            "// Feeds the detector the bits 1 0 1 0 1 1 0 1 0 0 1, one a clock.\n" +
            "module seq101_tb;\n" +
            "    logic clk = 1'b0, reset = 1'b1, x = 1'b0;\n" +
            "    logic found;\n" +
            "    logic [10:0] bits = 11'b10101101001;\n" +
            "\n" +
            "    seq101 dut (.clk(clk), .reset(reset), .x(x), .found(found));\n" +
            "\n" +
            "    always #5 clk = ~clk;\n" +
            "\n" +
            "    initial begin\n" +
            "        #7 reset = 1'b0;\n" +
            "        for (int i = 10; i >= 0; i--) begin\n" +
            "            x = bits[i];\n" +
            "            #10;\n" +
            "        end\n" +
            "        $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private const string Seg7Source =
            "// A hex digit to the seven segments of a display, a lit segment a 1:\n" +
            "//      a\n" +
            "//    f   b\n" +
            "//      g\n" +
            "//    e   c\n" +
            "//      d\n" +
            "// Four inputs and seven outputs: LOGIC gives each segment its truth table,\n" +
            "// its K-map and its smallest sum of products.\n" +
            "module seg7 (\n" +
            "    input  logic [3:0] digit,\n" +
            "    output logic [6:0] seg      // {a, b, c, d, e, f, g}\n" +
            ");\n" +
            "    always_comb\n" +
            "        case (digit)\n" +
            "            4'h0:    seg = 7'b1111110;\n" +
            "            4'h1:    seg = 7'b0110000;\n" +
            "            4'h2:    seg = 7'b1101101;\n" +
            "            4'h3:    seg = 7'b1111001;\n" +
            "            4'h4:    seg = 7'b0110011;\n" +
            "            4'h5:    seg = 7'b1011011;\n" +
            "            4'h6:    seg = 7'b1011111;\n" +
            "            4'h7:    seg = 7'b1110000;\n" +
            "            4'h8:    seg = 7'b1111111;\n" +
            "            4'h9:    seg = 7'b1111011;\n" +
            "            4'hA:    seg = 7'b1110111;\n" +
            "            4'hB:    seg = 7'b0011111;\n" +
            "            4'hC:    seg = 7'b1001110;\n" +
            "            4'hD:    seg = 7'b0111101;\n" +
            "            4'hE:    seg = 7'b1001111;\n" +
            "            default: seg = 7'b1000111;   // F\n" +
            "        endcase\n" +
            "endmodule\n";

        private const string Seg7BenchSource =
            "// Counts the digit through all sixteen values, 10 ps apart.\n" +
            "module seg7_tb;\n" +
            "    logic [3:0] digit = 4'h0;\n" +
            "    logic [6:0] seg;\n" +
            "\n" +
            "    seg7 dut (.digit(digit), .seg(seg));\n" +
            "\n" +
            "    initial begin\n" +
            "        repeat (16) #10 digit = digit + 4'h1;\n" +
            "        $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private const string TrafficSource =
            "// A traffic light where a quiet side street meets a main road.\n" +
            "// The road stays green until a car waits on the side street. Then the road\n" +
            "// turns yellow, the side street goes green for as long as cars keep coming,\n" +
            "// turns yellow, and the road goes green again.\n" +
            "// Four states, a Moore machine: the lights depend on the state alone.\n" +
            "module traffic (\n" +
            "    input  logic       clk,\n" +
            "    input  logic       reset,   // back to ROAD_GO\n" +
            "    input  logic       car,     // a car waiting on the side street\n" +
            "    output logic [1:0] road,    // 2 = green, 1 = yellow, 0 = red\n" +
            "    output logic [1:0] side\n" +
            ");\n" +
            "    localparam logic [1:0] ROAD_GO   = 2'd0,\n" +
            "                           ROAD_SLOW = 2'd1,\n" +
            "                           SIDE_GO   = 2'd2,\n" +
            "                           SIDE_SLOW = 2'd3;\n" +
            "\n" +
            "    logic [1:0] state, next;\n" +
            "\n" +
            "    // The state register: a flip-flop per bit, reset to ROAD_GO.\n" +
            "    always_ff @(posedge clk or posedge reset)\n" +
            "        if (reset) state <= ROAD_GO;\n" +
            "        else       state <= next;\n" +
            "\n" +
            "    // The next state.\n" +
            "    always_comb\n" +
            "        case (state)\n" +
            "            ROAD_GO:   next = car ? ROAD_SLOW : ROAD_GO;\n" +
            "            ROAD_SLOW: next = SIDE_GO;\n" +
            "            SIDE_GO:   next = car ? SIDE_GO : SIDE_SLOW;\n" +
            "            default:   next = ROAD_GO;\n" +
            "        endcase\n" +
            "\n" +
            "    // The lights, from the state alone.\n" +
            "    lights u_lights (.state(state), .road(road), .side(side));\n" +
            "endmodule\n";

        private const string LightsSource =
            "// Which light each street shows in each state of the traffic light.\n" +
            "module lights (\n" +
            "    input  logic [1:0] state,\n" +
            "    output logic [1:0] road,   // 2 = green, 1 = yellow, 0 = red\n" +
            "    output logic [1:0] side\n" +
            ");\n" +
            "    always_comb\n" +
            "        case (state)\n" +
            "            2'd0:    begin road = 2'd2; side = 2'd0; end   // ROAD_GO\n" +
            "            2'd1:    begin road = 2'd1; side = 2'd0; end   // ROAD_SLOW\n" +
            "            2'd2:    begin road = 2'd0; side = 2'd2; end   // SIDE_GO\n" +
            "            default: begin road = 2'd0; side = 2'd1; end   // SIDE_SLOW\n" +
            "        endcase\n" +
            "endmodule\n";

        private const string TrafficBenchSource =
            "// Drives the traffic light: a clock every 10 ps, a reset, and cars coming and going.\n" +
            "module traffic_tb;\n" +
            "    logic clk = 1'b0, reset = 1'b1, car = 1'b0;\n" +
            "    logic [1:0] road, side;\n" +
            "\n" +
            "    traffic dut (.clk(clk), .reset(reset), .car(car), .road(road), .side(side));\n" +
            "\n" +
            "    always #5 clk = ~clk;\n" +
            "\n" +
            "    initial begin\n" +
            "        #12 reset = 1'b0;\n" +
            "        #20 car = 1'b1;     // a car arrives on the side street\n" +
            "        #40 car = 1'b0;     // the side street empties\n" +
            "        #40 car = 1'b1;     // another car\n" +
            "        #10 car = 1'b0;\n" +
            "        #50 $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private const string FullAdderGatesSource =
            "// A full adder from gates: the sum is the odd parity, the carry the majority.\n" +
            "module fa (\n" +
            "    input  logic a, b, cin,\n" +
            "    output logic s, cout\n" +
            ");\n" +
            "    assign s    = a ^ b ^ cin;\n" +
            "    assign cout = (a & b) | (cin & (a ^ b));\n" +
            "endmodule\n";

        private const string Add2Source =
            "// A 2-bit ripple adder: two full adders, the carry handed from one to the next.\n" +
            "module add2 (\n" +
            "    input  logic [1:0] a, b,\n" +
            "    input  logic       cin,\n" +
            "    output logic [1:0] s,\n" +
            "    output logic       cout\n" +
            ");\n" +
            "    logic c1;\n" +
            "\n" +
            "    fa bit0 (.a(a[0]), .b(b[0]), .cin(cin), .s(s[0]), .cout(c1));\n" +
            "    fa bit1 (.a(a[1]), .b(b[1]), .cin(c1),  .s(s[1]), .cout(cout));\n" +
            "endmodule\n";

        private const string Alu2Source =
            "// A 2-bit ALU: op chooses a + b, a - b, a AND b or a OR b.\n" +
            "// Subtraction is a + ~b + 1: the same adder, b inverted and a carry in.\n" +
            "module alu2 (\n" +
            "    input  logic [1:0] a, b,\n" +
            "    input  logic [1:0] op,      // 0 add, 1 subtract, 2 AND, 3 OR\n" +
            "    output logic [1:0] y,\n" +
            "    output logic       carry\n" +
            ");\n" +
            "    logic       sub;\n" +
            "    logic [1:0] bx, sum;\n" +
            "    logic       c;\n" +
            "\n" +
            "    assign sub = (op == 2'd1);\n" +
            "    assign bx  = sub ? ~b : b;\n" +
            "\n" +
            "    add2 adder (.a(a), .b(bx), .cin(sub), .s(sum), .cout(c));\n" +
            "\n" +
            "    always_comb\n" +
            "        case (op)\n" +
            "            2'd0, 2'd1: begin y = sum;   carry = c;    end\n" +
            "            2'd2:       begin y = a & b; carry = 1'b0; end\n" +
            "            default:    begin y = a | b; carry = 1'b0; end\n" +
            "        endcase\n" +
            "endmodule\n";

        private const string Alu2BenchSource =
            "// Tries the ALU on every op, a and b: 64 combinations, 10 ps apart.\n" +
            "module alu2_tb;\n" +
            "    logic [1:0] a = 2'd0, b = 2'd0, op = 2'd0;\n" +
            "    logic [1:0] y;\n" +
            "    logic       carry;\n" +
            "\n" +
            "    alu2 dut (.a(a), .b(b), .op(op), .y(y), .carry(carry));\n" +
            "\n" +
            "    initial begin\n" +
            "        for (int i = 0; i < 64; i++) begin\n" +
            "            {op, a, b} = i;\n" +
            "            #10;\n" +
            "        end\n" +
            "        $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private const string Prime4Source =
            "// Is a 4-bit number prime? 2, 3, 5, 7, 11 and 13 are.\n" +
            "// Four inputs, so the LOGIC tab shows its truth table, its K-map and the\n" +
            "// smallest sum of products.\n" +
            "module prime4 (\n" +
            "    input  logic [3:0] n,\n" +
            "    output logic       prime\n" +
            ");\n" +
            "    always_comb\n" +
            "        case (n)\n" +
            "            4'd2, 4'd3, 4'd5, 4'd7, 4'd11, 4'd13: prime = 1'b1;\n" +
            "            default:                            prime = 1'b0;\n" +
            "        endcase\n" +
            "endmodule\n";

        private const string Prime4BenchSource =
            "// Counts n through all sixteen values, 10 ps apart.\n" +
            "module prime4_tb;\n" +
            "    logic [3:0] n = 4'd0;\n" +
            "    logic       prime;\n" +
            "\n" +
            "    prime4 dut (.n(n), .prime(prime));\n" +
            "\n" +
            "    initial begin\n" +
            "        repeat (16) #10 n = n + 4'd1;\n" +
            "        $stop;\n" +
            "    end\n" +
            "endmodule\n";

        public const string AdderSource =
            "// A four-bit adder, written rather than drawn: its sum and its carry\n" +
            "// three time units after its operands.\n" +
            "module add4 #(\n" +
            "    parameter N = 4\n" +
            ") (\n" +
            "    input  logic [N-1:0] a,     // the first operand\n" +
            "    input  logic [N-1:0] b,     // the second\n" +
            "    output logic [N-1:0] s,     // their sum\n" +
            "    output logic         cout   // and its carry out\n" +
            ");\n" +
            "    assign #3 {cout, s} = a + b;\n" +
            "endmodule\n";

        /// <summary>
        /// A block written as code (Part 12d): <see cref="AdderSource"/>, inside a drawn top with a and b in and s and cout
        /// out. Its testbench gives eight pairs of inputs, <see cref="WrittenAdderStep"/> apart.
        /// </summary>
        public static Project WrittenAdder()
        {
            var editor = new BoardEditor(Project.New("adder"));
            Must(editor.AddWrittenModule("add4", AdderSource));

            var top = new Drawing(editor, "adder");
            int a = top.In("a", 1, 0.5, 4), b = top.In("b", 1, -0.5, 4);
            int s = top.Out("s", 10, 0.5, 4), cout = top.Out("cout", 10, -0.5);
            int add = top.Add(new BlockPart { Name = "add", Module = "add4" }, 5.5, 0);
            top.Wire(At(a, "out"), At(add, "a"));
            top.Wire(At(b, "out"), At(add, "b"));
            top.Wire(At(add, "s"), At(s, "in"));
            top.Wire(At(add, "cout"), At(cout, "in"));

            ulong[] aValues = { 0x0, 0x5, 0xA, 0xF, 0x3, 0xC, 0x9, 0xF };
            ulong[] bValues = { 0x0, 0x3, 0x5, 0x1, 0xF, 0xC, 0x6, 0xF };
            var aSteps = new Step[aValues.Length];
            var bSteps = new Step[bValues.Length];
            for (int i = 0; i < aValues.Length; i++)
            {
                aSteps[i] = new Step(i * WrittenAdderStep, Bits(aValues[i], 4));
                bSteps[i] = new Step(i * WrittenAdderStep, Bits(bValues[i], 4));
            }

            Must(editor.SetStimulus(new BoardStimulus
            {
                Tracks = new[] { new BoardTrack { Port = "a", Steps = aSteps }, new BoardTrack { Port = "b", Steps = bSteps } },
                End = aValues.Length * WrittenAdderStep,
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
