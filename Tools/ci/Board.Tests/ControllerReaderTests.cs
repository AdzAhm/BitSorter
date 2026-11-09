using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A controller read as the textbook draws it (Part 15h-6e): the three processors' controllers -- the multicycle one's
    /// eleven states, the single-cycle and pipelined ones' tables -- and the traffic light read as written; every corpus and
    /// exercise module read or saying why; and every reading held to the engine: for every state and every input, the next
    /// state the circuit works out is one the diagram has an arrow to, every arrow is taken, and every constant the diagram
    /// or a table shows is what the circuit outputs.
    /// </summary>
    public class ControllerReaderTests
    {
        [Test]
        public void TheMulticycleController_IsElevenStates_FromFetch_AndItsAluDecoder()
        {
            ControllerReading reading = Read("processor", "mcpu.sv", "mcpu_control");
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "FETCH", "DECODE", "EXECUTE", "ALU_WB", "JUMP", "JUMP_REG", "BRANCH", "MEM_ADDR", "MEM_READ", "MEM_WB", "MEM_WRITE" },
                machine.States);
            Assert.AreEqual("state", machine.Register);
            Assert.AreEqual("state_next", machine.Next);
            Assert.AreEqual("FETCH", machine.States[machine.Start]);
            Assert.IsTrue(reading.Whole, "nothing in it the reading leaves out");

            CollectionAssert.AreEqual(new[] { "DECODE" }, Out(machine, "FETCH").Select(a => a.To).ToArray(), "FETCH always to DECODE");
            Assert.IsNull(Out(machine, "FETCH").Single().Condition);
            CollectionAssert.AreEqual(new[]
            {
                "EXECUTE: opcode is OP_REG, OP_IMM or OP_LUI", "ALU_WB: opcode is OP_AUIPC", "JUMP: opcode is OP_JAL",
                "JUMP_REG: opcode is OP_JALR", "BRANCH: opcode is OP_BRANCH", "MEM_ADDR: opcode is OP_LOAD or OP_STORE", "FETCH: otherwise",
            }, Out(machine, "DECODE").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "MEM_READ: opcode == OP_LOAD", "MEM_WRITE: otherwise" },
                Out(machine, "MEM_ADDR").Select(a => a.To + ": " + a.Condition).ToArray());

            CollectionAssert.AreEqual(new[] { "pc_we = 1", "ir_we = 1", "alu_a_sel = 1", "alu_b_sel = 2" }, Settings(machine, "FETCH"));
            CollectionAssert.AreEqual(new[] { "pc_we = taken", "pc_src = 1" }, Settings(machine, "BRANCH"));
            CollectionAssert.AreEqual(new[] { "alu_a_sel = 2", "alu_b_sel = 1" }, Settings(machine, "DECODE"));

            TableReading alu = reading.Tables.Single();
            Assert.AreEqual("funct3", alu.Subject);
            CollectionAssert.AreEqual(new[] { "alu_function" }, alu.Columns);
            CollectionAssert.AreEqual(new[] { "3'b000", "3'b001", "3'b010", "3'b011", "3'b100", "3'b101", "3'b110", "otherwise" }, alu.Rows.Select(r => r.Label).ToArray());
            Assert.AreEqual("ALU_SLL", alu.Rows[1].Cells[0].Text);
            Assert.AreEqual(1, alu.RowOf(LogicVec.FromUInt64(1, 3)), "funct3 = 1 picks the second row");
            Assert.AreEqual(7, alu.RowOf(LogicVec.FromUInt64(7, 3)), "and 7 the default's");
        }

        [TestCase("scpu.sv", "scpu_control")]
        [TestCase("pcpu.sv", "pcpu_control")]
        public void ACombinationalController_IsItsTwoTables(string file, string module)
        {
            ControllerReading reading = Read("processor", file, module);
            Assert.IsNull(reading.Machine);
            Assert.IsTrue(reading.Whole);
            CollectionAssert.AreEqual(new[] { "funct3", "opcode" }, reading.Tables.Select(t => t.Subject).ToArray());
            TableReading main = reading.Tables[1];
            CollectionAssert.AreEqual(new[] { "OP_REG", "OP_IMM", "OP_LUI", "OP_AUIPC", "OP_JAL", "OP_JALR", "OP_BRANCH", "OP_LOAD", "OP_STORE", "otherwise" },
                main.Rows.Select(r => r.Label).ToArray());
            TableRow lui = main.Rows[2];
            Assert.AreEqual("ALU_PASSB", lui.Cells[main.Columns.ToList().IndexOf("alu_op")].Text);
            Assert.IsFalse(lui.Cells[main.Columns.ToList().IndexOf("alu_op")].FromDefault);
            TableRow reg = main.Rows[0];
            Assert.IsTrue(reg.Cells[main.Columns.ToList().IndexOf("alu_b_imm")].FromDefault, "left to what is set before the case");
            Assert.AreEqual(2, main.RowOf(LogicVec.FromUInt64(0b0110111, 7)), "OP_LUI's code picks its row");
        }

        [Test]
        public void TheTrafficLight_IsItsFourStates_ItsCounterLeftToTheDrawing()
        {
            ControllerReading reading = Read("behavioural", "traffic_light.sv", "traffic_light");
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "RED", "RED_AMBER", "GREEN", "AMBER" }, machine.States);
            Assert.AreEqual(0, machine.Start);
            CollectionAssert.AreEqual(new[] { "RED_AMBER: !walk", "RED: otherwise" }, Out(machine, "RED").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "RED" }, Out(machine, "AMBER").Select(a => a.To).ToArray(), "AMBER is the default arm");
            CollectionAssert.AreEqual(new[] { "red = 1", "amber = 1" }, Settings(machine, "RED_AMBER"));
            Assert.IsFalse(reading.Whole, "its counter is no part of the diagram");
        }

        [Test]
        public void TheMultipliersControl_IsAMealyMachine_ItsOutputsOnTheArrowsThatSetThem()
        {
            ControllerReading reading = Read("behavioural", "multiplier.sv", "mult_control");
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "IDLE", "RUN" }, machine.States);
            CollectionAssert.AreEqual(new[] { "RUN: start", "IDLE: otherwise" }, Out(machine, "IDLE").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "count_next = 0", "load = 1" },
                machine.Arrows.Single(a => a.From == 0 && a.To == 1).Outputs.Select(o => o.ToString()).ToArray(),
                "start / load = 1, as the textbook writes a Mealy output, in the order the code sets them before the case");
            CollectionAssert.IsEmpty(Settings(machine, "IDLE"), "nothing IDLE sets on every way out of it");
            CollectionAssert.AreEqual(new[] { "count_next = count + 4'd1", "step = 1" }, Settings(machine, "RUN"));
            Assert.IsTrue(reading.Whole, "its counter is kept by the state's own process");
        }

        [Test]
        public void ADatapath_IsNoController()
        {
            Assert.IsNull(ReadOrNull("processor", "scpu.sv", "scpu_datapath", out string why));
            Assert.IsNotEmpty(why);
        }

        private static IEnumerable<TestCaseData> Designs()
        {
            foreach (string id in ExerciseFiles.Ids())
                yield return new TestCaseData("exercise", id).SetName("exercise " + id);
            foreach (string folder in new[] { "structural", "behavioural", "processor", "system" })
                foreach (string path in Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder), "*.sv").OrderBy(p => p, StringComparer.Ordinal))
                    yield return new TestCaseData(folder, Path.GetFileName(path)).SetName(folder + " " + Path.GetFileName(path));
        }

        /// <summary>Every module read or saying why, never throwing; and every reading held to the engine.</summary>
        [TestCaseSource(nameof(Designs))]
        public void EveryModule_IsReadOrSaysWhy_AndEveryReadingIsWhatTheEngineMakes(string kind, string name)
        {
            SyntaxTree tree = Tree(kind, name);
            foreach (ModuleDeclaration module in tree.Modules)
            {
                ControllerReading reading = ControllerReader.Read(tree, module, out string why);
                if (reading == null)
                {
                    Assert.IsNotEmpty(why, $"{module.Name}: no reading says why");
                    continue;
                }
                TestContext.WriteLine($"{name} {module.Name}: {(reading.Machine != null ? reading.Machine.States.Count + " states, " : "")}{reading.Tables.Count} tables{(reading.Whole ? ", whole" : "")}");
                ControllerEngine.Hold(tree, module.Name, reading);
            }
        }

        /// <summary>The arrows out of <paramref name="state"/>, each to a state by its name.</summary>
        private static List<(string To, string Condition)> Out(MachineReading machine, string state)
        {
            int from = machine.States.ToList().IndexOf(state);
            return machine.Arrows.Where(a => a.From == from).Select(a => (machine.States[a.To], a.Condition)).ToList();
        }

        private static string[] Settings(MachineReading machine, string state) =>
            machine.Settings[machine.States.ToList().IndexOf(state)].Select(s => s.ToString()).ToArray();

        // ---- what the review of 15h-6e found, each held to the engine as well

        private const string Nested = @"
module nested (input logic clk, rst, go, done, err, output logic busy);
    typedef enum logic [1:0] {IDLE, RUN, ERR} st_t;
    st_t state, nxt;
    always_ff @(posedge clk, posedge rst)
        if (rst) state <= IDLE;
        else state <= nxt;
    always_comb begin
        nxt = state;
        busy = 1'b0;
        case (state)
            IDLE: if (go) nxt = RUN;
            RUN: begin
                busy = 1'b1;
                if (done) begin
                    if (err) nxt = ERR;
                    else nxt = IDLE;
                end
            end
            default: nxt = IDLE;
        endcase
    end
endmodule
";

        /// <summary>An else inside an if keeps the if's condition: done and !err, never a bare "done" (the review of 15h-6e).</summary>
        [Test]
        public void AnElseInsideAnIf_KeepsTheIfsCondition_OnItsArrow()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(Nested, "nested");
            CollectionAssert.AreEqual(new[] { "ERR: done and err", "IDLE: done and !err", "RUN: otherwise" },
                Out(reading.Machine, "RUN").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "busy = 1" }, Settings(reading.Machine, "RUN"));
            Assert.AreEqual(1, reading.Machine.Settings[1].Single().Width, "busy is one bit");
            ControllerEngine.Hold(tree, "nested", reading);
        }

        private const string Numbered = @"
module numbered (input logic clk, rst, x, output logic y);
    typedef enum logic [1:0] {A = 2'd1, B = 2'd2, C = 2'd3} st_t;
    st_t state, nxt;
    always_ff @(posedge clk, posedge rst)
        if (rst) state <= A;
        else state <= nxt;
    always_comb begin
        nxt = A;
        y = 1'b0;
        case (state)
            2'd1: nxt = B;
            B: nxt = x ? C : A;
            default: begin nxt = A; y = 1'b1; end
        endcase
    end
endmodule
";

        /// <summary>A label written as the state's value takes the state as its name would (the review: A = 1 and 2'd1: read A -> A).</summary>
        [Test]
        public void ALabelWrittenAsTheStatesValue_TakesTheState()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(Numbered, "numbered");
            CollectionAssert.AreEqual(new[] { "B" }, Out(reading.Machine, "A").Select(a => a.To).ToArray(), "A's arm is the 2'd1: one");
            CollectionAssert.AreEqual(new[] { "C: x", "A: otherwise" }, Out(reading.Machine, "B").Select(a => a.To + ": " + a.Condition).ToArray());
            ControllerEngine.Hold(tree, "numbered", reading);
        }

        /// <summary>A case of signals is a mux on the data, drawn and never tabled (the review: case (sel) 0: y = a read as a table).</summary>
        [Test]
        public void ACaseOfSignals_IsAMux_NoTable()
        {
            const string mux = @"
module pick (input logic [1:0] sel, input logic [7:0] a, b, c, d, output logic [7:0] y);
    always_comb
        case (sel)
            2'd0: y = a;
            2'd1: y = b;
            2'd2: y = c;
            default: y = d;
        endcase
endmodule
";
            SyntaxTree tree = SyntaxReader.ReadText(mux, "pick.sv");
            Assert.IsNull(ControllerReader.Read(tree, tree.Modules.Single(), out string why));
            Assert.IsNotEmpty(why);
        }

        /// <summary>A register process that keeps more than the state, or a wire given a value, is not the whole module.</summary>
        [TestCase("    always_ff @(posedge clk) acc <= acc + 8'd1;\n", TestName = "ARegisterBesideTheMachine_IsNotWhole")]
        [TestCase("    wire spare = state == RUN;\n", TestName = "AWireGivenAValue_IsNotWhole")]
        [TestCase("    function logic f(input logic a); return a; endfunction\n", TestName = "AFunction_IsNotWhole")]
        public void SomethingTheDiagramDoesNotShow_MakesItNotWhole(string extra)
        {
            string text = Nested.Replace("    st_t state, nxt;\n", "    st_t state, nxt;\n    logic [7:0] acc;\n" + extra);
            (SyntaxTree _, ControllerReading reading) = ReadText(text, "nested");
            Assert.IsFalse(reading.Whole);
        }

        [Test]
        public void TheMachineAlone_IsWhole() => Assert.IsTrue(ReadText(Nested, "nested").Reading.Whole);

        /// <summary>A state machine in one process -- the case on the state in the always_ff itself -- with a reset and a registered output.</summary>
        private const string OneProcess = @"
module one (input logic clk, rst, go, output logic busy);
    typedef enum logic [1:0] {IDLE, RUN, DONE} st_t;
    st_t state;
    always_ff @(posedge clk)
        if (rst) begin
            state <= IDLE;
            busy <= 1'b0;
        end else
            case (state)
                IDLE: begin busy <= 1'b0; if (go) state <= RUN; end
                RUN: begin busy <= 1'b1; state <= DONE; end
                default: begin busy <= 1'b0; state <= IDLE; end
            endcase
endmodule
";

        /// <summary>
        /// A machine written in one process is held to the engine as one in two is: its arms read with the reset let go, and an
        /// output it sets compared with what it is about to take, not what it holds -- whatever the reset's edge or its sense.
        /// And the check still refuses such a reading that the engine does not bear out (the 16d sweep: the reset, folded into
        /// the register's next value, took every state to IDLE where no arrow went, so no such diagram was ever shown).
        /// </summary>
        [TestCase("@(posedge clk)", "rst", "rst", TestName = "AOneProcessMachine_WithASynchronousReset_IsHeldToTheEngine")]
        [TestCase("@(posedge clk, posedge rst)", "rst", "rst", TestName = "AOneProcessMachine_WithAnAsynchronousReset_IsHeldToTheEngine")]
        [TestCase("@(posedge clk, negedge rst_n)", "rst_n", "!rst_n", TestName = "AOneProcessMachine_WithAnActiveLowReset_IsHeldToTheEngine")]
        public void AOneProcessMachine_IsHeldToTheEngine(string events, string reset, string condition)
        {
            string text = OneProcess.Replace("@(posedge clk)", events).Replace("input logic clk, rst,", "input logic clk, " + reset + ",")
                .Replace("if (rst) begin", "if (" + condition + ") begin");
            (SyntaxTree tree, ControllerReading reading) = ReadText(text, "one");
            MachineReading machine = reading.Machine;
            Assert.AreEqual(machine.Register, machine.Next, "sanity: one process");
            CollectionAssert.AreEqual(new[] { "RUN: go", "IDLE: otherwise" }, Out(machine, "IDLE").Select(a => a.To + ": " + a.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { "busy = 1" }, Settings(machine, "RUN"));
            Assert.IsTrue(reading.Whole);
            ControllerEngine.Hold(tree, "one", reading);

            Elaboration elaboration = Elaborator.Elaborate(tree, "one");
            Design design = elaboration.Design;
            string top = design.Scopes.Single(s => s.Kind == ScopeKind.Top).Path;
            var fewer = new MachineReading(machine.Register, machine.Next, machine.States, machine.Codes, machine.Start,
                machine.Arrows.Skip(1).ToList(), machine.Settings);
            StringAssert.Contains("no arrow", ControllerCheck.Why(new ControllerReading(reading.Module, fewer, reading.Tables, reading.Whole), design,
                RtlExtractor.Of(design, top), top), "an arrow taken away is still refused");
        }

        /// <summary>A machine beside a memory it reads -- a ROM looked up by the state -- with the memory's words in it.</summary>
        private static readonly string NestedWithARom = Nested
            .Replace("output logic busy);", "output logic busy, output logic [7:0] out);")
            .Replace("    st_t state, nxt;\n", "    st_t state, nxt;\n    logic [7:0] rom [0:3];\n    assign out = rom[state];\n");

        /// <summary>
        /// A machine beside a memory is held to the engine with the memory's words given values, as every other signal the
        /// logic reads is (the 16d sweep: they were left with none, and the check threw from the first word it read).
        /// </summary>
        [Test]
        public void AMachineBesideAMemory_IsHeldToTheEngine_ItsWordsGivenValues()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(NestedWithARom, "nested");
            Assert.IsNotNull(reading.Machine, "the machine is read beside the ROM");
            Assert.IsFalse(reading.Whole, "and the assign is not of it");
            Assert.DoesNotThrow(() => ControllerEngine.Hold(tree, "nested", reading));
        }

        /// <summary>Two decoders, the second's case on what the first sets: a subject the module works out itself.</summary>
        private const string Chained = @"
module chain (input logic [1:0] op, output logic [1:0] kind, output logic [3:0] y);
    always_comb begin
        kind = 2'd0;
        case (op)
            2'd0: kind = 2'd1;
            2'd1: kind = 2'd2;
            default: kind = 2'd3;
        endcase
    end
    always_comb begin
        y = 4'd0;
        case (kind)
            2'd1: y = 4'd5;
            2'd2: y = 4'd6;
            2'd3: y = 4'd7;
            default: y = 4'd9;
        endcase
    end
endmodule
";

        /// <summary>The same reading with one of its table's constants made otherwise: what the check must refuse.</summary>
        private static ControllerReading WithCell(ControllerReading reading, int table, int row, int column, LogicVec constant)
        {
            var tables = reading.Tables.ToList();
            TableReading t = tables[table];
            var rows = t.Rows.ToList();
            var cells = rows[row].Cells.ToList();
            cells[column] = new TableCell(constant.ToString(), constant, false);
            rows[row] = new TableRow(rows[row].Label, rows[row].Matches, rows[row].Otherwise, cells);
            tables[table] = new TableReading(t.Subject, t.Columns, rows);
            return new ControllerReading(reading.Module, reading.Machine, tables, reading.Whole);
        }

        private static string Why(SyntaxTree tree, string module, ControllerReading reading)
        {
            Design design = Elaborator.Elaborate(tree, module).Design;
            string top = design.Scopes.Single(s => s.Kind == ScopeKind.Top).Path;
            return ControllerCheck.Why(reading, design, RtlExtractor.Of(design, top), top);
        }

        /// <summary>
        /// A decoder whose case is on a signal the module works out itself -- the second of two chained decoders -- is held to the
        /// engine with its subject held at each row on its own net, whatever the inputs would work it out as; and a constant read
        /// wrong in it is still refused (the 16d sweep: the subject was forced on its signal alone, the netlist worked it out
        /// again regardless, and the rows seldom matched, so such a table was never shown).
        /// </summary>
        [Test]
        public void ADecoderOnASignalTheModuleWorksOut_IsHeldToTheEngine()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(Chained, "chain");
            Assert.AreEqual(2, reading.Tables.Count, "sanity: both decoders");
            Assert.AreEqual("kind", reading.Tables[1].Subject);
            Assert.IsTrue(reading.Whole);
            ControllerEngine.Hold(tree, "chain", reading);

            StringAssert.Contains("where the table says", Why(tree, "chain", WithCell(reading, 1, 1, 0, LogicVec.FromUInt64(15, 4))),
                "kind = 2'd2's y read as 15, where the circuit gives 6");
        }

        /// <summary>
        /// The chain again, its first decoder on a 7-bit opcode, as a processor's is: <c>kind</c> is 1 at one opcode in 128,
        /// and 0 at none.
        /// </summary>
        private const string ChainedOnAnOpcode = @"
module chain (input logic [6:0] op, output logic [1:0] kind, output logic [3:0] y);
    always_comb
        case (op)
            7'b0110011: kind = 2'd1;
            7'b0010011: kind = 2'd2;
            7'b0000011: kind = 2'd3;
            default:    kind = 2'd0;
        endcase
    always_comb
        case (kind)
            2'd1: y = 4'd5;
            2'd2: y = 4'd6;
            2'd3: y = 4'd7;
            default: y = 4'd9;
        endcase
endmodule
";

        /// <summary>
        /// Every row of a decoder on a signal the module works out is held to the engine, however seldom the inputs bring its
        /// subject there -- or never, as nothing here sets <c>kind</c> to 0: its case is tried at each label with the subject
        /// held there, so a constant read wrong in any row is refused (the review of the 16d fixes: the check took whichever
        /// row the random opcodes gave, all but always the default, and passed <c>kind = 2'd1</c>'s y read as 15).
        /// </summary>
        [Test]
        public void EveryRowOfADecoderOnASignalTheModuleWorksOut_IsHeld_HoweverSeldomItsSubjectGetsThere()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(ChainedOnAnOpcode, "chain");
            int second = reading.Tables.ToList().FindIndex(t => t.Subject == "kind");
            Assert.GreaterOrEqual(second, 0, "sanity: the decoder on kind");
            ControllerEngine.Hold(tree, "chain", reading);

            for (int row = 0; row < reading.Tables[second].Rows.Count; row++)
            {
                string label = reading.Tables[second].Rows[row].Label;
                string why = Why(tree, "chain", WithCell(reading, second, row, 0, LogicVec.FromUInt64(15, 4)));
                Assert.IsNotNull(why, $"kind = {label}'s y read as 15");
                StringAssert.Contains("kind = " + label + " gives y", why);
            }
        }

        /// <summary>
        /// A table's default row is held to the engine as every other row is, at values of its subject no label takes, so a
        /// default read wrong is refused (the 16d sweep: the default row had no label to force, and its constants were shown
        /// unchecked). The chain's second default is no case the circuit ever comes to -- the first decoder never sets kind to
        /// 0 -- so nothing the circuit does bears it out or not.
        /// </summary>
        [Test]
        public void ADecodersDefaultRow_IsHeldToTheEngine_AsEveryOtherRowIs()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(Chained, "chain");
            ControllerEngine.Hold(tree, "chain", reading);
            int otherwise = reading.Tables[0].Rows.ToList().FindIndex(r => r.Otherwise);
            Assert.AreEqual(2, otherwise, "sanity: op's default row");
            string why = Why(tree, "chain", WithCell(reading, 0, otherwise, 0, LogicVec.FromUInt64(0, 2)));
            Assert.IsNotNull(why, "op's default read as kind = 0, where the circuit gives 3");
            StringAssert.Contains("otherwise gives kind", why);
        }

        /// <summary>A decoder whose default leaves its outputs free, as the textbook writes one: x for every bit.</summary>
        private const string FreeDefault = @"
module dec (input logic [2:0] op, output logic [1:0] a, output logic b);
    always_comb
        case (op)
            3'd0: begin a = 2'd1; b = 1'b0; end
            3'd1: begin a = 2'd2; b = 1'b1; end
            3'd2: begin a = 2'd3; b = 1'b1; end
            default: begin a = 2'bxx; b = 1'bx; end
        endcase
endmodule
";

        /// <summary>
        /// A default row of x -- <c>default: a = 2'bxx;</c>, the textbook's way to leave a decoder's outputs free -- is borne out
        /// by the circuit giving x there, bit for bit, and still refused where it gives otherwise (the review of the 16d fixes:
        /// once the default row was tried, an x the table showed never matched, and such a table was refused saying "gives
        /// a = 2'bxx, where the table says 2'bxx").
        /// </summary>
        [Test]
        public void ADecodersDefaultOfX_IsBorneOut_XForX()
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(FreeDefault, "dec");
            Assert.AreEqual(1, reading.Tables.Count, "sanity: one decoder");
            int otherwise = reading.Tables[0].Rows.ToList().FindIndex(r => r.Otherwise);
            Assert.AreEqual(3, otherwise, "sanity: its default row");
            ControllerEngine.Hold(tree, "dec", reading);

            StringAssert.Contains("otherwise gives a", Why(tree, "dec", WithCell(reading, 0, otherwise, 0, LogicVec.FromUInt64(0, 2))),
                "the default read as a = 0, where the circuit gives x");
            StringAssert.Contains("3'd0 gives a", Why(tree, "dec", WithCell(reading, 0, 0, 0, LogicVec.AllX(2))),
                "and a row read as x, where the circuit gives 1");
        }

        /// <summary>A machine whose way out of IDLE is taken only at <c>COND</c>.</summary>
        private const string Waiting = @"
module waiting (input logic clk, rst, valid, go, ready, input logic [7:0] data, input logic [15:0] count, output logic busy);
    typedef enum logic [1:0] {IDLE, RUN, DONE} st_t;
    st_t state, nxt;
    always_ff @(posedge clk, posedge rst)
        if (rst) state <= IDLE;
        else state <= nxt;
    always_comb begin
        nxt = state;
        busy = 1'b0;
        case (state)
            IDLE: if (COND) nxt = RUN;
            RUN: begin busy = 1'b1; nxt = DONE; end
            default: nxt = IDLE;
        endcase
    end
endmodule
";

        /// <summary>
        /// An arrow taken only at two inputs' values together, three's, or one wide input's value the logic compares with, is
        /// found taken, and the correct machine shown (the 16d sweep: one input was tried at a time, the rest at random, so
        /// such a machine passed or failed by the seed -- about half of those of an 8-bit compare and a valid -- and a 16-bit
        /// compare's arrow was almost never taken in sixteen random tries).
        /// </summary>
        [TestCase("data == 8'd200 && valid", TestName = "AnArrowOfTwoInputsAtOnce_IsFoundTaken")]
        [TestCase("valid && go && ready && data[0]", TestName = "AnArrowOfFourInputsAtOnce_IsFoundTaken")]
        [TestCase("count == 16'd1000", TestName = "AnArrowOfAWideInputsValue_IsFoundTaken")]
        [TestCase("count == 16'd1000 && valid", TestName = "AnArrowOfAWideInputsValueAndAnother_IsFoundTaken")]
        public void AnArrowTakenOnlyAtSomeInputsTogether_IsFoundTaken(string condition)
        {
            (SyntaxTree tree, ControllerReading reading) = ReadText(Waiting.Replace("COND", condition), "waiting");
            CollectionAssert.AreEqual(new[] { "RUN", "IDLE" }, Out(reading.Machine, "IDLE").Select(a => a.To).ToArray(), "sanity: read");
            Assert.IsNull(Why(tree, "waiting", reading), condition);

            MachineReading machine = reading.Machine;
            var more = new MachineReading(machine.Register, machine.Next, machine.States, machine.Codes, machine.Start,
                machine.Arrows.Append(new MachineArrow(0, 2, "never")).ToList(), machine.Settings);
            Assert.AreEqual("No input takes IDLE to DONE.", Why(tree, "waiting", new ControllerReading(reading.Module, more, reading.Tables, reading.Whole)),
                "an arrow no input takes is still refused, after the second look");
        }

        /// <summary>
        /// A register of an enum of fewer than two members is no state machine, as a case of fewer than two labels is not (the
        /// 16d sweep: an empty enum, half written, read as a machine of no states, and its diagram's layout threw).
        /// </summary>
        [TestCase("", TestName = "AnEnumOfNoMembers_IsNoStateMachine")]
        [TestCase("IDLE", TestName = "AnEnumOfOneMember_IsNoStateMachine")]
        public void AnEnumOfFewerThanTwoMembers_IsNoStateMachine(string members)
        {
            string text = $@"
module skeleton (input logic clk, output logic y);
    typedef enum logic [1:0] {{{members}}} st_t;
    st_t state, nxt;
    always_ff @(posedge clk) state <= nxt;
    always_comb begin
        nxt = state;
        y = 1'b0;
        case (state)
            default: nxt = state;
        endcase
    end
endmodule
";
            SyntaxTree tree = SyntaxReader.ReadText(text, "skeleton.sv");
            if (tree.HasErrors)
                Assert.Pass("refused as it is read, which is as good: " + tree.Diagnostics.First());
            ControllerReading reading = ControllerReader.Read(tree, tree.Modules.Single(), out string why);
            Assert.IsNull(reading?.Machine, "no machine of " + (reading?.Machine?.States.Count ?? 0) + " states");
            StringAssert.Contains("fewer than two states", why ?? "");
        }

        /// <summary>A decoder whose labels are an enum's members lights the row of the member's value (the review: every value read "otherwise").</summary>
        [Test]
        public void ADecodersEnumLabels_PickTheirRows()
        {
            const string decoder = @"
typedef enum logic [1:0] {P, Q, R} op_t;
module dec (input op_t op, output logic [3:0] y);
    always_comb begin
        y = 4'd0;
        case (op)
            P: y = 4'd3;
            Q: y = 4'd5;
            default: y = 4'd9;
        endcase
    end
endmodule
";
            SyntaxTree tree = SyntaxReader.ReadText(decoder, "dec.sv");
            ControllerReading reading = ControllerReader.Read(tree, tree.Modules.Single(), out string why);
            Assert.IsNotNull(reading, why);
            TableReading table = reading.Tables.Single();
            Assert.AreEqual(0, table.RowOf(LogicVec.FromUInt64(0, 2)));
            Assert.AreEqual(1, table.RowOf(LogicVec.FromUInt64(1, 2)));
            Assert.AreEqual(2, table.RowOf(LogicVec.FromUInt64(2, 2)), "R: the default's");
            ControllerEngine.Hold(tree, "dec", reading);
        }

        /// <summary>A start the code does not say is no start, never the first state; one declared with a value starts there.</summary>
        [Test]
        public void TheStart_IsTheResets_OrTheDeclaredValue_OrNone()
        {
            string late = Nested.Replace("        if (rst) state <= IDLE;\n        else state <= nxt;\n", "        begin state <= nxt; if (rst) state <= RUN; end\n");
            (SyntaxTree _, ControllerReading noStart) = ReadText(late, "nested");
            Assert.AreEqual(-1, noStart.Machine.Start, "a reset after the next value is no reset this reads");
            Assert.IsFalse(noStart.Whole);

            string declared = Nested.Replace("    st_t state, nxt;\n", "    st_t state = RUN, nxt;\n")
                .Replace("        if (rst) state <= IDLE;\n        else state <= nxt;\n", "        state <= nxt;\n");
            (SyntaxTree _, ControllerReading fromDeclaration) = ReadText(declared, "nested");
            Assert.AreEqual("RUN", fromDeclaration.Machine.States[fromDeclaration.Machine.Start]);
        }

        /// <summary>always @(*) reads as always_comb does; a compound default is no plain one; a copy is the value it copies.</summary>
        [Test]
        public void AlwaysStar_Reads_ACompoundDefaultDoesNot_ACopyIsItsValue()
        {
            string star = Nested.Replace("    always_comb begin\n        nxt = state;", "    always @(*) begin\n        nxt = state;");
            (SyntaxTree starTree, ControllerReading starred) = ReadText(star, "nested");
            Assert.AreEqual(3, starred.Machine.States.Count);
            ControllerEngine.Hold(starTree, "nested", starred);

            string compound = Nested.Replace("        busy = 1'b0;\n", "        busy = 1'b0;\n        busy |= 1'b0;\n");
            SyntaxTree tree = SyntaxReader.ReadText(compound, "nested.sv");
            Assert.IsNull(ControllerReader.Read(tree, tree.Modules.Single(), out _), "busy |= is no plain default");

            string copy = Nested.Replace("                busy = 1'b1;\n", "                nxt = RUN;\n                busy = nxt == RUN;\n");
            (SyntaxTree _, ControllerReading copied) = ReadText(copy, "nested");
            Assert.IsNotNull(copied);
        }

        private static (SyntaxTree Tree, ControllerReading Reading) ReadText(string text, string module)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, module + ".sv");
            Assert.IsFalse(tree.HasErrors, string.Join("\n", tree.Diagnostics));
            ControllerReading reading = ControllerReader.Read(tree, tree.Modules.Single(m => m.Name == module), out string why);
            Assert.IsNotNull(reading, why);
            return (tree, reading);
        }

        private static ControllerReading Read(string kind, string file, string module)
        {
            ControllerReading reading = ReadOrNull(kind, file, module, out string why);
            Assert.IsNotNull(reading, why);
            return reading;
        }

        private static ControllerReading ReadOrNull(string kind, string file, string module, out string why)
        {
            SyntaxTree tree = Tree(kind, file);
            return ControllerReader.Read(tree, tree.Modules.Single(m => m.Name == module), out why);
        }

        private static SyntaxTree Tree(string kind, string name)
        {
            if (kind == "exercise")
            {
                Project solution = ExerciseFiles.Solution(ExerciseFiles.Load(name));
                return BoardCompiler.Compile(solution).Tree;
            }
            string path = RepoRoot.Combine("Tools/ci/Oracle/corpus/" + kind + "/" + name);
            var files = new MemorySourceFiles();
            foreach (string other in Directory.GetFiles(Path.GetDirectoryName(path)))
                files.Add(Path.GetFileName(other), File.ReadAllText(other));
            return SyntaxReader.ReadText(File.ReadAllText(path), name, files);
        }
    }
}
