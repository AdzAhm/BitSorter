// Written by hand where it says what it is; the strings are the corpus's files, copied, which
// ProcessorSampleTests holds them to character for character.
using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The start page's three RV32I processors (Part 15h-6, Ahmad's choice, 2026-10-29): single-cycle, multicycle and
    /// pipelined with forwarding, each running the one demo program -- the sum of 1 to 10, the first ten Fibonacci numbers
    /// and every instruction they run once, checking itself (asked 2026-11-01: "all of these examples") -- so the three
    /// can be compared on it: 408 cycles single-cycle, 1,568 multicycle, 482 pipelined.
    /// </summary>
    /// <remarks>
    /// Written for BitSorter's oracle from the published RISC-V specification, never from any course material, and held
    /// to Icarus Verilog on every signal by <c>BehaviouralOracleTests</c>. The text here is the corpus's own,
    /// <c>Tools/ci/Oracle/corpus/processor</c>, kept the same by <c>ProcessorSampleTests</c>: a change there is copied
    /// here, or that test fails. mcpu.sv holds its own testbench too, the oracle's, with its own program; the sample
    /// runs the demo's and leaves the other out.
    /// </remarks>
    public static class WorkbenchProcessors
    {
        /// <summary>The single-cycle processor: every instruction in one long clock cycle.</summary>
        public static Project SingleCycle() => SingleCycleOnce.Value;

        /// <summary>The multicycle processor: a state machine takes each instruction through three to five short cycles.</summary>
        public static Project Multicycle() => MulticycleOnce.Value;

        /// <summary>The pipelined processor: five stages at once, forwarding, a stall after a load and a flush after a jump.</summary>
        public static Project Pipelined() => PipelinedOnce.Value;

        // Imported once each: an import runs the testbench to find where it stops, a third of a second, and a project is
        // never changed in place, so every opening can share one (the review of #121).
        private static readonly Lazy<Project> SingleCycleOnce = new Lazy<Project>(() => Imported("scpu_tb", ("scpu.sv", Scpu)));
        private static readonly Lazy<Project> MulticycleOnce = new Lazy<Project>(() => Imported("mcpu_demo_tb", ("mcpu.sv", Mcpu)));
        private static readonly Lazy<Project> PipelinedOnce = new Lazy<Project>(() => Imported("pcpu_tb", ("pcpu.sv", Pcpu)));

        /// <summary>Every file the samples are made from, by its corpus name: what ProcessorSampleTests compares.</summary>
        public static readonly IReadOnlyList<(string File, string Text)> Files = new[]
        {
            ("scpu.sv", Scpu),
            ("mcpu.sv", Mcpu),
            ("pcpu.sv", Pcpu),
            ("rv32i.svh", Defs),
            ("rv_sum.hex", SumProgram),
            ("rv_sum_expected.hex", SumExpected),
            ("rv_fib.hex", FibProgram),
            ("rv_fib_expected.hex", FibExpected),
            ("rv_isa.hex", TestProgram),
            ("rv_isa_expected.hex", TestExpected),
            ("rv_data.hex", Data),
        };

        /// <summary>
        /// The processor's file with the definitions and the images, imported as FILE, IMPORT would, running
        /// <paramref name="bench"/>; any other testbench the file holds left out of the project.
        /// </summary>
        private static Project Imported(string bench, (string File, string Text) processor)
        {
            var sources = new List<SourceFile>
            {
                new SourceFile(processor.File, processor.Text),
                new SourceFile("rv32i.svh", Defs),
            };
            // Every program's images, so the testbench's PROGRAM can choose any of them (Part 15h-6c).
            foreach ((string name, string text) in Files)
                if (name.EndsWith(".hex", StringComparison.Ordinal))
                    sources.Add(new SourceFile(name, text));
            ProjectImport import = ProjectImporter.Import(sources, bench);
            if (!import.Succeeded || import.Project.Bench != bench)
                throw new InvalidOperationException("a processor sample did not import: " + string.Join("; ", import.Notes));
            Project project = import.Project;
            var used = new HashSet<string>(StringComparer.Ordinal);
            Collect(project, project.Find(bench), used);
            project = project with { Modules = project.Modules.Where(m => used.Contains(m.Name)).ToArray() };
            foreach (KeyValuePair<string, IReadOnlyDictionary<string, Placement>> laid in Arranged)
                if (project.Find(laid.Key) is Module module)
                    project = project.WithModule(module with { Arrangement = laid.Value, ArrangedFor = module.Source });
            return project;
        }

        /// <summary><paramref name="module"/> and every module it makes, all the way down.</summary>
        private static void Collect(Project project, Module module, HashSet<string> used)
        {
            if (module == null || !used.Add(module.Name))
                return;
            foreach (Module child in WrittenModules.UsesOf(project, module))
                Collect(project, child, used);
        }

        /// <summary>
        /// Where each datapath's parts stand, laid out by hand as the textbook draws one (Part 15h-6, Ahmad: "it doesnt
        /// look so beatiful and orgnized"): the PC on the left, decode in the middle, execute, write-back on the right, the
        /// next PC and the write-back fed back over the top -- by the keys the drawing names its parts with
        /// (<see cref="RtlDrawing.KeyOf"/>), kept against the code they were laid out for. ProcessorSampleTests holds every
        /// key to a part of the drawing.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Placement>> Arranged =
            new Dictionary<string, IReadOnlyDictionary<string, Placement>>
            {
                ["scpu_datapath"] = new Dictionary<string, Placement>
                {
                    ["sig:pc_next"] = new Placement(4.0, 0.0),
                    ["port:pc_src"] = new Placement(4.75, -4.0),
                    ["cell:MuxPart:0"] = new Placement(8.0, 0.0),
                    ["cell:ConstantPart:1"] = new Placement(6.5, -2.75),
                    ["cell:BoxPart:3"] = new Placement(8.0, 3.0),
                    ["port:rst"] = new Placement(4.0, 3.0),
                    ["reg:pc"] = new Placement(12.0, 0.0),
                    ["port:clk"] = new Placement(12.0, -3.0),
                    ["cell:ConstantPart:0"] = new Placement(13.5, 5.0),
                    ["sig:pc_plus4"] = new Placement(17.0, 4.5),
                    ["port:pc"] = new Placement(16.5, 0.0),
                    ["port:instr"] = new Placement(16.25, -6.5),
                    ["cell:BoxPart:0"] = new Placement(20.0, -5.0),
                    ["cell:BoxPart:1"] = new Placement(20.0, -6.5),
                    ["cell:BoxPart:2"] = new Placement(20.0, -8.0),
                    ["port:rf_we"] = new Placement(25.25, -2.0),
                    ["inst:regfile"] = new Placement(25.0, -6.5),
                    ["inst:immgen"] = new Placement(25.0, -11.0),
                    ["sig:opcode"] = new Placement(20.0, -13.0),
                    ["sig:funct3"] = new Placement(20.0, -14.5),
                    ["sig:funct7_5"] = new Placement(20.0, -16.0),
                    ["port:opcode"] = new Placement(24.25, -13.0),
                    ["port:funct3"] = new Placement(24.25, -14.5),
                    ["port:funct7_5"] = new Placement(24.5, -16.0),
                    ["sig:target"] = new Placement(31.0, 4.5),
                    ["inst:compare"] = new Placement(31.0, -2.0),
                    ["port:taken"] = new Placement(35.75, -2.0),
                    ["sig:alu_a"] = new Placement(31.0, -6.0),
                    ["port:alu_a_pc"] = new Placement(31.5, -9.0),
                    ["sig:alu_b"] = new Placement(34.0, -9.0),
                    ["port:alu_b_imm"] = new Placement(34.5, -12.0),
                    ["inst:alu"] = new Placement(38.0, -7.0),
                    ["port:alu_op"] = new Placement(38.75, -10.5),
                    ["port:data_addr"] = new Placement(42.0, -7.0),
                    ["port:write_data"] = new Placement(42.0, -14.0),
                    ["cell:BoxPart:4"] = new Placement(42.0, -2.5),
                    ["cell:ConstantPart:2"] = new Placement(42.25, -4.0),
                    ["cell:BoxPart:5"] = new Placement(45.5, -3.25),
                    ["port:read_data"] = new Placement(44.0, -10.5),
                    ["sig:wb"] = new Placement(49.0, -8.0),
                    ["port:wb_src"] = new Placement(49.75, -12.0),
                },
                ["mcpu_datapath"] = new Dictionary<string, Placement>
                {
                    ["sig:pc_next"] = new Placement(4.0, 0.0),
                    ["port:pc_src"] = new Placement(4.75, -4.0),
                    ["cell:MuxPart:0"] = new Placement(8.0, 0.0),
                    ["cell:BoxPart:4"] = new Placement(8.0, 3.0),
                    ["port:pc_we"] = new Placement(4.25, 3.0),
                    ["cell:MuxPart:1"] = new Placement(12.0, 0.0),
                    ["cell:BoxPart:3"] = new Placement(12.0, 3.0),
                    ["port:rst"] = new Placement(12.0, 5.5),
                    ["cell:ConstantPart:0"] = new Placement(10.5, -2.75),
                    ["reg:pc"] = new Placement(15.5, 0.0),
                    ["port:clk"] = new Placement(15.5, -3.0),
                    ["port:pc"] = new Placement(19.0, 1.5),
                    ["port:instr"] = new Placement(18.75, -6.5),
                    ["cell:MuxPart:2"] = new Placement(22.0, -6.5),
                    ["cell:MuxPart:3"] = new Placement(22.0, -2.5),
                    ["cell:BoxPart:5"] = new Placement(22.0, -10.0),
                    ["port:ir_we"] = new Placement(18.25, -10.0),
                    ["reg:ir"] = new Placement(25.5, -6.5),
                    ["reg:old_pc"] = new Placement(25.5, -2.5),
                    ["cell:BoxPart:0"] = new Placement(30.0, -5.0),
                    ["cell:BoxPart:1"] = new Placement(30.0, -6.5),
                    ["cell:BoxPart:2"] = new Placement(30.0, -8.0),
                    ["port:rf_we"] = new Placement(35.25, -3.5),
                    ["inst:regfile"] = new Placement(35.0, -6.5),
                    ["inst:immgen"] = new Placement(35.0, -11.0),
                    ["sig:opcode"] = new Placement(30.0, -13.0),
                    ["sig:funct3"] = new Placement(30.0, -14.5),
                    ["sig:funct7_5"] = new Placement(30.0, -16.0),
                    ["port:opcode"] = new Placement(34.25, -13.0),
                    ["port:funct3"] = new Placement(34.25, -14.5),
                    ["port:funct7_5"] = new Placement(34.5, -16.0),
                    ["reg:a"] = new Placement(40.0, -5.5),
                    ["reg:b"] = new Placement(40.0, -8.0),
                    ["inst:compare"] = new Placement(44.5, -1.0),
                    ["port:taken"] = new Placement(48.75, -1.0),
                    ["sig:alu_a"] = new Placement(45.0, -5.0),
                    ["port:alu_a_sel"] = new Placement(46.0, -8.75),
                    ["cell:ConstantPart:1"] = new Placement(45.5, -11.5),
                    ["sig:alu_b"] = new Placement(48.5, -9.0),
                    ["port:alu_b_sel"] = new Placement(49.5, -12.75),
                    ["inst:alu"] = new Placement(52.5, -6.5),
                    ["port:alu_op"] = new Placement(53.25, -10.0),
                    ["reg:alu_out"] = new Placement(56.5, -6.5),
                    ["port:data_addr"] = new Placement(60.0, -6.5),
                    ["port:write_data"] = new Placement(60.0, -15.0),
                    ["cell:ConstantPart:2"] = new Placement(53.0, -2.0),
                    ["cell:OperatorPart:0"] = new Placement(56.5, -1.5),
                    ["port:read_data"] = new Placement(57.5, -10.5),
                    ["reg:mdr"] = new Placement(60.5, -10.5),
                    ["sig:wb"] = new Placement(65.0, -8.0),
                    ["port:wb_src"] = new Placement(65.75, -12.0),
                },
            };

        private const string Scpu = @"// scpu: an original single-cycle processor -- each instruction fetched, decoded, executed, given its memory access and
// written back within one clock cycle, so the clock has to be as slow as the slowest instruction, a load. A decoder
// that is combinational logic, a datapath with the PC as its only register outside the register file, and separate
// instruction and data memories of 1,024 words each, loaded from images with $readmemh. It runs the RV32I instructions
// a program needs: lui, auipc, jal, jalr, every branch, lw and sw, and every arithmetic, logic and shift operation, by
// register and by immediate. No bytes or halfwords, no system instructions.
// Its datapath is drawn as the textbook draws one: the PC, the register file, an immediate generator and a branch
// comparator as blocks of their own, the ALU, and a mux for each choice -- the next PC, the ALU's a and b, what is
// written back. scpu_tb runs one of three programs on rv_data.hex -- its PROGRAM: rv_sum.hex, rv_fib.hex or
// rv_isa.hex -- until the program stores to 0x7fc, then checks its results against the program's own expected image.
// Written for BitSorter's oracle from the published RISC-V specification; not taken from any course material.

`include ""rv32i.svh""

// The ALU: the arithmetic and logic unit. It adds, subtracts, ANDs, ORs, XORs, shifts and compares its two operands as
// op says, and passes b through unchanged for lui.
module scpu_alu (
    input  logic [31:0] a,   // the first operand
    input  logic [31:0] b,   // the second operand; a shift takes its low five bits as the amount
    input  logic [3:0]  op,  // which operation: one of the ALU_ codes of rv32i.svh
    output logic [31:0] y    // the result
);
    always_comb begin
        // The operation: op chooses which result is y -- a + b, a - b, a AND b, a OR b, a XOR b, a shifted left, a
        // shifted right (logically, or arithmetically so the sign is kept), 1 or 0 for a signed or an unsigned
        // less-than, or b itself for lui. A code the processor never uses gives 0.
        case (op)
            ALU_ADD:   y = a + b;
            ALU_SUB:   y = a - b;
            ALU_AND:   y = a & b;
            ALU_OR:    y = a | b;
            ALU_XOR:   y = a ^ b;
            ALU_SLL:   y = a << b[4:0];
            ALU_SRL:   y = a >> b[4:0];
            ALU_SRA:   y = $signed(a) >>> b[4:0];
            ALU_SLT:   y = {31'd0, $signed(a) < $signed(b)};
            ALU_SLTU:  y = {31'd0, a < b};
            ALU_PASSB: y = b;
            default:   y = 32'd0;
        endcase
    end
endmodule

// The register file: 32 registers of 32 bits, x0 to x31, with two read ports and one write port. A read is immediate; a
// write lands on the rising edge. x0 always reads 0.
module scpu_regfile (
    input  logic        clk,  // the clock: a write lands on its rising edge
    input  logic        we,   // write wd to register wa on the rising edge
    input  logic [4:0]  ra1,  // the first register to read: rs1
    input  logic [4:0]  ra2,  // the second register to read: rs2
    input  logic [4:0]  wa,   // the register to write: rd (a write to x0 is ignored)
    input  logic [31:0] wd,   // the value to write
    output logic [31:0] rd1,  // the value of register ra1 (0 for x0)
    output logic [31:0] rd2   // the value of register ra2 (0 for x0)
);
    logic [31:0] regs [0:31];  // the registers x0 to x31 (x0's cell is never written)

    // The write port: when we is set, register wa takes wd on the rising edge. x0 is never written.
    always_ff @(posedge clk)
        if (we && wa != 5'd0) regs[wa] <= wd;

    // Read port 1: x0 reads 0 whatever is written to it, any other register reads what it holds.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : regs[ra1];
    // Read port 2: the same for ra2.
    assign rd2 = ra2 == 5'd0 ? 32'd0 : regs[ra2];
endmodule

// A memory of 1,024 words of 32 bits (4 KiB), loaded at time 0 from the IMAGE file with $readmemh. A read is immediate;
// a write lands on the rising edge. Addresses are byte addresses: the low two bits are dropped, so a word is always
// read or written whole, and an address past 4 KiB wraps around.
module scpu_memory #(
    parameter IMAGE = ""rv_sum.hex""   // the image loaded at time 0
) (
    input  logic        clk,    // the clock: a write lands on its rising edge
    input  logic        we,     // write wdata on the rising edge
    input  logic [31:0] addr,   // a byte address; words are aligned
    input  logic [31:0] wdata,  // the word to write
    output logic [31:0] rdata   // the word at addr, available at once
);
    logic [31:0] mem [0:1023];  // the 1,024 words

    // Fill the memory from the image file before the first clock.
    initial $readmemh(IMAGE, mem);

    // The write port: when we is set, the word at addr takes wdata on the rising edge.
    always_ff @(posedge clk)
        if (we) mem[addr[11:2]] <= wdata;

    // The read port: the word addr points at, with no clock needed.
    assign rdata = mem[addr[11:2]];
endmodule

// The control logic: what the instruction asks of the datapath, worked out from its opcode and funct fields alone --
// and, for a branch, from the comparison the datapath makes of its two registers.
module scpu_control (
    input  logic [6:0] opcode,     // instruction bits 6:0: the kind of instruction
    input  logic [2:0] funct3,     // instruction bits 14:12: which operation within a kind
    input  logic       funct7_5,   // instruction bit 30: tells sub from add, and sra from srl
    input  logic       taken,      // from the datapath's branch comparator: the branch's condition holds
    output logic [1:0] pc_src,     // the next PC: 0 PC + 4, 1 PC + immediate, 2 the ALU's sum, bit 0 cleared (jalr)
    output logic       rf_we,      // write the register file: the instruction has a result to keep
    output logic       mem_we,     // write the data memory: a store
    output logic       alu_a_pc,   // the ALU's a: 0 register rs1, 1 the PC (auipc)
    output logic       alu_b_imm,  // the ALU's b: 0 register rs2, 1 the immediate
    output logic [3:0] alu_op,     // which ALU operation: an ALU_ code
    output logic [1:0] wb_src      // what is written back: 0 the ALU, 1 the data memory, 2 PC + 4 (the link)
);
    logic [3:0] alu_function;

    // What the instruction's funct fields ask of the ALU.
    always_comb begin
        // funct3 names the operation of an arithmetic or logic instruction; instruction bit 30 separates the pairs add
        // and sub, srl and sra.
        case (funct3)
            // 000 is add -- or subtract, for a register-register instruction with bit 30 set (there is no subtract by
            // an immediate).
            3'b000:  alu_function = opcode == OP_REG && funct7_5 ? ALU_SUB : ALU_ADD;
            3'b001:  alu_function = ALU_SLL;
            3'b010:  alu_function = ALU_SLT;
            3'b011:  alu_function = ALU_SLTU;
            3'b100:  alu_function = ALU_XOR;
            // 101 is a right shift: arithmetic when bit 30 is set, else logical.
            3'b101:  alu_function = funct7_5 ? ALU_SRA : ALU_SRL;
            3'b110:  alu_function = ALU_OR;
            default: alu_function = ALU_AND;
        endcase
    end

    // The controls: every one starts at its quiet value, and the instruction's kind raises what it needs.
    always_comb begin
        pc_src = 2'd0;
        rf_we = 1'b0;
        mem_we = 1'b0;
        alu_a_pc = 1'b0;
        alu_b_imm = 1'b0;
        alu_op = ALU_ADD;
        wb_src = 2'd0;

        // The instruction's kind, by opcode, sets the controls it needs: register-register and immediate arithmetic
        // write the register file with the ALU's result; lui passes the immediate through; auipc adds it to the PC; jal
        // and jalr jump and write back PC + 4; a branch jumps if taken; a load reads the data memory; a store writes
        // it.
        case (opcode)
            OP_REG: begin
                rf_we = 1'b1;
                alu_op = alu_function;
            end
            OP_IMM: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                alu_op = alu_function;
            end
            OP_LUI: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                alu_op = ALU_PASSB;
            end
            OP_AUIPC: begin
                rf_we = 1'b1;
                alu_a_pc = 1'b1;
                alu_b_imm = 1'b1;
            end
            OP_JAL: begin
                pc_src = 2'd1;
                rf_we = 1'b1;
                wb_src = 2'd2;
            end
            OP_JALR: begin
                pc_src = 2'd2;
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                wb_src = 2'd2;
            end
            // A branch goes to the PC plus the immediate when the comparator says its condition holds, else on to the
            // next instruction.
            OP_BRANCH: pc_src = taken ? 2'd1 : 2'd0;
            OP_LOAD: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                wb_src = 2'd1;
            end
            OP_STORE: begin
                mem_we = 1'b1;
                alu_b_imm = 1'b1;
            end
            default: ;
        endcase
    end
endmodule

// The immediate generator: the instruction's immediate, its bits gathered and its sign extended as its format says.
module scpu_immgen (
    input  logic [31:0] instr,  // the whole instruction
    output logic [31:0] imm     // the immediate its format carries, as 32 bits (0 for a kind with none)
);
    always_comb begin
        // The immediate's format, by opcode: I (immediate arithmetic, loads, jalr), S (stores), B (branches), U (lui,
        // auipc) or J (jal). Each gathers its scattered bits, and all but U sign-extend them from instruction bit 31.
        case (instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            // A kind with no immediate, such as register-register arithmetic: 0.
            default:                  imm = 32'd0;
        endcase
    end
endmodule

// The branch comparator: whether a branch's condition holds of its two registers, by funct3.
module scpu_branch (
    input  logic [31:0] a,       // register rs1's value
    input  logic [31:0] b,       // register rs2's value
    input  logic [2:0]  funct3,  // which comparison: 000 beq, 001 bne, 100 blt, 101 bge, 110 bltu, 111 bgeu
    output logic        taken    // 1 when the branch's condition holds
);
    always_comb begin
        // The condition funct3 names: equal, not equal, then less than and greater or equal as signed numbers, then the
        // same two as unsigned. A funct3 no branch uses is never taken.
        case (funct3)
            3'b000:  taken = a == b;
            3'b001:  taken = a != b;
            3'b100:  taken = $signed(a) < $signed(b);
            3'b101:  taken = $signed(a) >= $signed(b);
            3'b110:  taken = a < b;
            3'b111:  taken = a >= b;
            default: taken = 1'b0;
        endcase
    end
endmodule

// The datapath: the PC, the register file, the immediate generator, the branch comparator, the ALU, and the muxes
// that choose each one's inputs -- the PC's next value, the ALU's a and b, and what is written back.
module scpu_datapath (
    input  logic        clk,         // the clock: the PC and the register file change on its rising edge
    input  logic        rst,         // asynchronous, active high: the PC to 0
    input  logic [1:0]  pc_src,      // from the control: the next PC -- 0 PC + 4, 1 PC + immediate, 2 the ALU's sum
    input  logic        rf_we,       // from the control: write the register file
    input  logic        alu_a_pc,    // from the control: the ALU's a is the PC (auipc), else register rs1
    input  logic        alu_b_imm,   // from the control: the ALU's b is the immediate, else register rs2
    input  logic [3:0]  alu_op,      // from the control: which ALU operation
    input  logic [1:0]  wb_src,      // from the control: what is written back -- 0 the ALU, 1 the data memory, 2 PC + 4
    input  logic [31:0] instr,       // the instruction at the PC, from the instruction memory
    input  logic [31:0] read_data,   // the word the data memory reads at data_addr, from the data memory
    output logic [31:0] pc,          // the address of the instruction being run, to the instruction memory
    output logic [31:0] data_addr,   // the ALU's result: the address of a load or store, to the data memory
    output logic [31:0] write_data,  // register rs2's value: what a store writes, to the data memory
    output logic [6:0]  opcode,      // instruction bits 6:0, to the control
    output logic [2:0]  funct3,      // instruction bits 14:12, to the control and the branch comparator
    output logic        funct7_5,    // instruction bit 30, to the control
    output logic        taken        // the branch comparator's answer, to the control
);
    logic [31:0] rd1, rd2, imm, alu_a, alu_b, alu_y, pc_plus4, target, pc_next, wb;

    assign opcode = instr[6:0];    // the kind of instruction, for the control
    assign funct3 = instr[14:12];  // which operation within a kind
    assign funct7_5 = instr[30];   // bit 30: sub and sra
    assign data_addr = alu_y;      // a load's or store's address is the ALU's sum
    assign write_data = rd2;       // a store writes register rs2

    // The PC: the address of the instruction being run. It takes the next PC on every rising edge; reset sets it to 0
    // at once, without waiting for a clock edge.
    always_ff @(posedge clk, posedge rst)
        // Reset sends the PC back to 0; otherwise it takes the next PC.
        if (rst) pc <= 32'd0;
        else pc <= pc_next;

    // PC + 4: the next instruction's address, and the link that jal and jalr write back.
    assign pc_plus4 = pc + 32'd4;
    // The target of a branch and of jal: the PC plus the immediate.
    assign target = pc + imm;

    always_comb
        // The next PC: the target (PC + the immediate) for a jal or a taken branch, the ALU's sum with bit 0 cleared
        // for a jalr, else the next instruction, PC + 4.
        case (pc_src)
            2'd1:    pc_next = target;
            2'd2:    pc_next = {alu_y[31:1], 1'b0};
            default: pc_next = pc_plus4;
        endcase

    // The register file: reads registers rs1 and rs2 (instruction bits 19:15 and 24:20), and writes register rd (bits
    // 11:7) with what is written back when rf_we is set.
    scpu_regfile regfile (
        .clk(clk),
        .we(rf_we),
        .ra1(instr[19:15]),
        .ra2(instr[24:20]),
        .wa(instr[11:7]),
        .wd(wb),
        .rd1(rd1),
        .rd2(rd2)
    );

    // The immediate generator: gathers the instruction's immediate and sign-extends it.
    scpu_immgen immgen (.instr(instr), .imm(imm));

    // The branch comparator: compares registers rs1 and rs2 as the branch's funct3 asks, and says whether it is taken.
    scpu_branch compare (.a(rd1), .b(rd2), .funct3(funct3), .taken(taken));

    // The ALU's a: the PC for auipc, else register rs1.
    assign alu_a = alu_a_pc ? pc : rd1;
    // The ALU's b: the immediate for immediate arithmetic, loads, stores and jalr, else register rs2.
    assign alu_b = alu_b_imm ? imm : rd2;

    // The ALU: the result of an arithmetic instruction, the address of a load or store, the target of a jalr.
    scpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    always_comb
        // What is written back to the register file: the data memory's word for a load, PC + 4 for jal and jalr, else
        // the ALU's result.
        case (wb_src)
            2'd1:    wb = read_data;
            2'd2:    wb = pc_plus4;
            default: wb = alu_y;
        endcase
endmodule

// The single-cycle processor: the two memories, the control and the datapath. Every instruction is fetched, run and
// written back within one clock cycle.
module scpu #(
    parameter PROGRAM = ""rv_sum.hex"",   // the instruction memory's image
    parameter DATA    = ""rv_data.hex""       // the data memory's image
) (
    input  logic clk,  // the clock: one instruction completes on each rising edge
    input  logic rst   // asynchronous, active high: the program starts again from address 0
);
    logic        rf_we, mem_we, alu_a_pc, alu_b_imm, funct7_5, taken;
    logic [1:0]  pc_src, wb_src;
    logic [3:0]  alu_op;
    logic [6:0]  opcode;
    logic [2:0]  funct3;
    logic [31:0] pc, instr, data_addr, write_data, read_data;

    // The instruction memory: read at the PC and never written, so the instruction at the PC is there at once.
    scpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    // The data memory: a load reads the word at the ALU's address; a store writes it there when mem_we is set.
    scpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we),
        .addr(data_addr),
        .wdata(write_data),
        .rdata(read_data)
    );

    // The control: turns the instruction, and a branch's comparison, into the datapath's control signals.
    scpu_control control (.*);

    // The datapath: the PC, the register file, the immediate generator, the branch comparator and the ALU, steered by
    // the control.
    scpu_datapath datapath (.*);
endmodule

// The testbench: it resets scpu, runs the program chosen by PROGRAM until it stores to 0x7fc, and checks the results it
// left against the program's expected image.
module scpu_tb;
    // Which program it runs: SUM, FIBONACCI or ISA (rv32i.svh). In the body, not a #( ) list, where a project's module
    // sees what it includes: a simulator's command line sets it all the same, as -P does, and so does an instantiation.
    parameter rv_program_t PROGRAM = SUM;

    // The program's images. Their names are of one length, so a ?: between them is a file name in every simulator:
    // Icarus pads a shorter one with a NUL it does not strip.
    localparam IMAGE    = PROGRAM == SUM ? ""rv_sum.hex"" : PROGRAM == FIBONACCI ? ""rv_fib.hex"" : ""rv_isa.hex"";
    localparam EXPECTED = PROGRAM == SUM ? ""rv_sum_expected.hex"" : PROGRAM == FIBONACCI ? ""rv_fib_expected.hex""
                                         : ""rv_isa_expected.hex"";

    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    scpu #(.PROGRAM(IMAGE), .DATA(""rv_data.hex"")) dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(EXPECTED, expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; every result is in memory by then.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            case (PROGRAM)
                SUM:
                    $display(""the sum of 1 to 10: %0d"", dut.dmem.mem[64]);
                FIBONACCI: begin
                    $write(""ten Fibonacci numbers:"");
                    for (i = 0; i < 10; i++)
                        $write("" %0d"", dut.dmem.mem[65 + i]);
                    $display;
                end
                default:
                    $display(""every instruction once: %0d of 41 checks held"", dut.dmem.mem[126]);
            endcase

            for (i = 0; i < 64; i++)
                if (dut.dmem.mem[64 + i] !== expected[i]) begin
                    $display(""FAIL: the word at %h is %h, not %h"", 32'h100 + 4 * i, dut.dmem.mem[64 + i], expected[i]);
                    failures++;
                end

            if (failures == 0)
                $display(""PASS: every result as expected"");
            else
                $display(""FAIL: %0d results wrong"", failures);
            $finish;
        end

    initial begin
        #100000;
        $display(""TIMEOUT: the program never said it was done"");
        $finish;
    end
endmodule
";

        private const string Mcpu = @"// mcpu: an original multicycle processor in the shape of the course's third simulation exercise -- a controller that is
// a state machine, a datapath with registers between the steps, and separate instruction and data memories of 1,024
// words each, loaded from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal, jalr,
// every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by immediate. No bytes or
// halfwords, no system instructions. Each instruction takes three to five clock cycles, one short step in each.
// Two testbenches are here. mcpu_tb is the oracle's own: it runs mcpu_program.hex, a program that sorts eight words, on
// mcpu_data.hex until the program stores to 0x7fc, then checks its results against mcpu_expected.hex. mcpu_demo_tb is
// the one the start page's sample runs: it takes a PROGRAM -- rv_sum.hex, rv_fib.hex or rv_isa.hex -- on rv_data.hex,
// and checks the results against that program's own expected image, as scpu_tb and pcpu_tb do. Written for BitSorter's
// oracle from the published RISC-V specification; not taken from any course material.

`include ""rv32i.svh""

// The ALU: the arithmetic and logic unit. It adds, subtracts, ANDs, ORs, XORs, shifts and compares its two operands as
// op says, and passes b through unchanged for lui.
module mcpu_alu (
    input  logic [31:0] a,   // the first operand
    input  logic [31:0] b,   // the second operand; a shift takes its low five bits as the amount
    input  logic [3:0]  op,  // which operation: one of the ALU_ codes of rv32i.svh
    output logic [31:0] y    // the result
);
    always_comb begin
        // The operation: op chooses which result is y -- a + b, a - b, a AND b, a OR b, a XOR b, a shifted left, a
        // shifted right (logically, or arithmetically so the sign is kept), 1 or 0 for a signed or an unsigned
        // less-than, or b itself for lui. A code the processor never uses gives 0.
        case (op)
            ALU_ADD:   y = a + b;
            ALU_SUB:   y = a - b;
            ALU_AND:   y = a & b;
            ALU_OR:    y = a | b;
            ALU_XOR:   y = a ^ b;
            ALU_SLL:   y = a << b[4:0];
            ALU_SRL:   y = a >> b[4:0];
            ALU_SRA:   y = $signed(a) >>> b[4:0];
            ALU_SLT:   y = {31'd0, $signed(a) < $signed(b)};
            ALU_SLTU:  y = {31'd0, a < b};
            ALU_PASSB: y = b;
            default:   y = 32'd0;
        endcase
    end
endmodule

// The register file: 32 registers of 32 bits, x0 to x31, with two read ports and one write port. A read is immediate; a
// write lands on the rising edge. x0 always reads 0.
module mcpu_regfile (
    input  logic        clk,  // the clock: a write lands on its rising edge
    input  logic        we,   // write wd to register wa on the rising edge
    input  logic [4:0]  ra1,  // the first register to read: rs1
    input  logic [4:0]  ra2,  // the second register to read: rs2
    input  logic [4:0]  wa,   // the register to write: rd (a write to x0 is ignored)
    input  logic [31:0] wd,   // the value to write
    output logic [31:0] rd1,  // the value of register ra1 (0 for x0)
    output logic [31:0] rd2   // the value of register ra2 (0 for x0)
);
    logic [31:0] regs [0:31];  // the registers x0 to x31 (x0's cell is never written)

    // The write port: when we is set, register wa takes wd on the rising edge. x0 is never written.
    always_ff @(posedge clk)
        if (we && wa != 5'd0) regs[wa] <= wd;

    // Read port 1: x0 reads 0 whatever is written to it, any other register reads what it holds.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : regs[ra1];
    // Read port 2: the same for ra2.
    assign rd2 = ra2 == 5'd0 ? 32'd0 : regs[ra2];
endmodule

// A memory of 1,024 words of 32 bits (4 KiB), loaded at time 0 from the IMAGE file with $readmemh. A read is immediate;
// a write lands on the rising edge. Addresses are byte addresses: the low two bits are dropped, so a word is always
// read or written whole, and an address past 4 KiB wraps around.
module mcpu_memory #(
    parameter IMAGE = ""mcpu_program.hex""   // the image loaded at time 0
) (
    input  logic        clk,    // the clock: a write lands on its rising edge
    input  logic        we,     // write wdata on the rising edge
    input  logic [31:0] addr,   // a byte address; words are aligned
    input  logic [31:0] wdata,  // the word to write
    output logic [31:0] rdata   // the word at addr, available at once
);
    logic [31:0] mem [0:1023];  // the 1,024 words

    // Fill the memory from the image file before the first clock.
    initial $readmemh(IMAGE, mem);

    // The write port: when we is set, the word at addr takes wdata on the rising edge.
    always_ff @(posedge clk)
        if (we) mem[addr[11:2]] <= wdata;

    // The read port: the word addr points at, with no clock needed.
    assign rdata = mem[addr[11:2]];
endmodule

// The immediate generator: the instruction's immediate, its bits gathered and its sign extended as its format says.
module mcpu_immgen (
    input  logic [31:0] instr,  // the whole instruction
    output logic [31:0] imm     // the immediate its format carries, as 32 bits (0 for a kind with none)
);
    always_comb begin
        // The immediate's format, by opcode: I (immediate arithmetic, loads, jalr), S (stores), B (branches), U (lui,
        // auipc) or J (jal). Each gathers its scattered bits, and all but U sign-extend them from instruction bit 31.
        case (instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            // A kind with no immediate, such as register-register arithmetic: 0.
            default:                  imm = 32'd0;
        endcase
    end
endmodule

// The branch comparator: whether a branch's condition holds of its two registers, by funct3.
module mcpu_branch (
    input  logic [31:0] a,       // register A: rs1's value, kept from the register file
    input  logic [31:0] b,       // register B: rs2's value, kept from the register file
    input  logic [2:0]  funct3,  // which comparison: 000 beq, 001 bne, 100 blt, 101 bge, 110 bltu, 111 bgeu
    output logic        taken    // 1 when the branch's condition holds
);
    always_comb begin
        // The condition funct3 names: equal, not equal, then less than and greater or equal as signed numbers, then the
        // same two as unsigned. A funct3 no branch uses is never taken.
        case (funct3)
            3'b000:  taken = a == b;
            3'b001:  taken = a != b;
            3'b100:  taken = $signed(a) < $signed(b);
            3'b101:  taken = $signed(a) >= $signed(b);
            3'b110:  taken = a < b;
            3'b111:  taken = a >= b;
            default: taken = 1'b0;
        endcase
    end
endmodule

// The control: a state machine that takes each instruction through its steps -- fetch, decode, then execute, memory
// access and write-back as the instruction needs them -- and says, in every step, what the datapath does. An
// instruction takes three to five cycles.
module mcpu_control (
    input  logic       clk,        // the clock: the state changes on its rising edge
    input  logic       rst,        // asynchronous, active high: the machine goes back to FETCH
    input  logic [6:0] opcode,     // instruction bits 6:0: the kind of instruction, from the instruction register
    input  logic [2:0] funct3,     // instruction bits 14:12: which operation within a kind
    input  logic       funct7_5,   // instruction bit 30: tells sub from add, and sra from srl
    input  logic       taken,      // from the datapath's branch comparator: the branch's condition holds
    output logic       pc_we,      // the PC loads the next PC at the end of this cycle
    output logic       ir_we,      // the instruction register loads the fetched instruction
    output logic       rf_we,      // the register file writes register rd at the end of this cycle
    output logic       mem_we,     // the data memory writes register B at the end of this cycle
    output logic [1:0] alu_a_sel,  // the ALU's a: 0 register A, 1 the PC, 2 the instruction's own address (old_pc)
    output logic [1:0] alu_b_sel,  // the ALU's b: 0 register B, 1 the immediate, 2 the constant four
    output logic [3:0] alu_op,     // which ALU operation: an ALU_ code
    output logic [1:0] pc_src,     // the next PC: 0 the ALU, 1 ALUOut, 2 the ALU's result, bit 0 cleared (jalr)
    output logic [1:0] wb_src      // what is written back: 0 ALUOut, 1 MDR (a loaded word), 2 the PC (the link)
);
    // The steps of an instruction. FETCH reads it; DECODE reads its registers; EXECUTE and ALU_WB run and write back an
    // arithmetic instruction; JUMP, JUMP_REG and BRANCH finish a jal, a jalr and a branch; MEM_ADDR forms a load's or
    // store's address, MEM_READ and MEM_WB finish the load, MEM_WRITE the store.
    typedef enum logic [3:0] {
        FETCH, DECODE, EXECUTE, ALU_WB, JUMP, JUMP_REG, BRANCH, MEM_ADDR, MEM_READ, MEM_WB, MEM_WRITE
    } state_t;

    state_t state, state_next;
    logic [3:0] alu_function;

    // The machine's state: which step of its instruction the processor is on. Reset puts it back at FETCH at once;
    // otherwise every rising edge moves it on to the state chosen below.
    always_ff @(posedge clk, posedge rst)
        // Reset starts at FETCH; otherwise the machine takes the next state.
        if (rst) state <= FETCH;
        else state <= state_next;

    // What the instruction's funct fields ask of the ALU.
    always_comb begin
        // funct3 names the operation of an arithmetic or logic instruction; instruction bit 30 separates the pairs add
        // and sub, srl and sra.
        case (funct3)
            // 000 is add -- or subtract, for a register-register instruction with bit 30 set (there is no subtract by
            // an immediate).
            3'b000:  alu_function = opcode == OP_REG && funct7_5 ? ALU_SUB : ALU_ADD;
            3'b001:  alu_function = ALU_SLL;
            3'b010:  alu_function = ALU_SLT;
            3'b011:  alu_function = ALU_SLTU;
            3'b100:  alu_function = ALU_XOR;
            // 101 is a right shift: arithmetic when bit 30 is set, else logical.
            3'b101:  alu_function = funct7_5 ? ALU_SRA : ALU_SRL;
            3'b110:  alu_function = ALU_OR;
            default: alu_function = ALU_AND;
        endcase
    end

    // What each step asks of the datapath. Every control starts at its quiet value and the state raises what it needs;
    // state_next starts as FETCH, so an instruction ends by going back to it.
    always_comb begin
        state_next = FETCH;
        pc_we = 1'b0;
        ir_we = 1'b0;
        rf_we = 1'b0;
        mem_we = 1'b0;
        alu_a_sel = 2'd0;
        alu_b_sel = 2'd0;
        alu_op = ALU_ADD;
        pc_src = 2'd0;
        wb_src = 2'd0;

        // Every control is chosen by the state: each arm below raises the controls its step needs -- all the others
        // keep their quiet value -- and names the state that follows.
        case (state)
            // FETCH: the instruction at the PC goes into the instruction register, and the PC steps to the next word --
            // the ALU adds four to the PC.
            FETCH: begin
                ir_we = 1'b1;
                pc_we = 1'b1;
                alu_a_sel = 2'd1;
                alu_b_sel = 2'd2;
                state_next = DECODE;
            end
            // DECODE: the registers the instruction names are read into A and B, and its kind chooses the next state.
            DECODE: begin
                // The target a branch, jal or auipc wants, worked out while the kind is decided.
                alu_a_sel = 2'd2;
                alu_b_sel = 2'd1;
                // The kind of instruction picks the next step: arithmetic and lui go to EXECUTE; auipc, whose result
                // the ALU already has, straight to ALU_WB; jal to JUMP; jalr to JUMP_REG; a branch to BRANCH; a load or
                // store to MEM_ADDR.
                case (opcode)
                    OP_REG, OP_IMM, OP_LUI: state_next = EXECUTE;
                    OP_AUIPC:               state_next = ALU_WB;
                    OP_JAL:                 state_next = JUMP;
                    OP_JALR:                state_next = JUMP_REG;
                    OP_BRANCH:              state_next = BRANCH;
                    OP_LOAD, OP_STORE:      state_next = MEM_ADDR;
                    // An opcode the processor does not run just starts the next fetch.
                    default:                state_next = FETCH;
                endcase
            end
            // EXECUTE: the ALU works on register A and register B, or the immediate, and the result goes into ALUOut.
            EXECUTE: begin
                // The second operand: register B for register-register arithmetic, else the immediate.
                alu_b_sel = opcode == OP_REG ? 2'd0 : 2'd1;
                // lui passes the immediate through; any other kind lets funct3 and bit 30 choose the operation.
                alu_op = opcode == OP_LUI ? ALU_PASSB : alu_function;
                state_next = ALU_WB;
            end
            // ALU_WB: the result kept in ALUOut is written to the register file.
            ALU_WB: begin
                rf_we = 1'b1;
                state_next = FETCH;
            end
            // JUMP, for jal: the link is written to rd -- the PC, already stepped past the instruction -- and the PC
            // loads the target kept in ALUOut.
            JUMP: begin
                rf_we = 1'b1;
                wb_src = 2'd2;
                pc_we = 1'b1;
                pc_src = 2'd1;
                state_next = FETCH;
            end
            // JUMP_REG, for jalr: the link is written to rd, and the PC loads register A plus the immediate with bit 0
            // cleared.
            JUMP_REG: begin
                rf_we = 1'b1;
                wb_src = 2'd2;
                alu_b_sel = 2'd1;
                pc_we = 1'b1;
                pc_src = 2'd2;
                state_next = FETCH;
            end
            // BRANCH: the PC loads the target kept in ALUOut if the condition holds; if not it already points at the
            // next instruction.
            BRANCH: begin
                pc_we = taken;
                pc_src = 2'd1;
                state_next = FETCH;
            end
            // MEM_ADDR, for a load or a store: the ALU adds the immediate to register A, and the sum, kept in ALUOut,
            // is the data address.
            MEM_ADDR: begin
                alu_b_sel = 2'd1;
                // A load goes on to read the memory; a store to write it.
                if (opcode == OP_LOAD)
                    state_next = MEM_READ;
                else
                    state_next = MEM_WRITE;
            end
            // MEM_READ: the data memory is read at the address in ALUOut and the word is kept in MDR. b stays the
            // immediate, so ALUOut keeps holding the address.
            MEM_READ: begin
                alu_b_sel = 2'd1;
                state_next = MEM_WB;
            end
            // MEM_WB: the word kept in MDR is written to the register file.
            MEM_WB: begin
                rf_we = 1'b1;
                wb_src = 2'd1;
                state_next = FETCH;
            end
            // MEM_WRITE: the data memory takes register B at the address in ALUOut. b stays the immediate, so ALUOut
            // keeps holding the address.
            MEM_WRITE: begin
                alu_b_sel = 2'd1;
                mem_we = 1'b1;
                state_next = FETCH;
            end
            // A state the machine is never in; it starts over at FETCH.
            default: state_next = FETCH;
        endcase
    end
endmodule

// The datapath: the PC, the instruction register, the register file, one ALU, and the registers that carry a step's
// result to the next -- A and B (the register file's outputs), ALUOut and the memory data register MDR. The control
// steers one ALU through every step by choosing what it works on.
module mcpu_datapath (
    input  logic        clk,         // the clock: every register changes on its rising edge
    input  logic        rst,         // asynchronous, active high: the PC to 0
    input  logic        pc_we,       // from the control: the PC loads the next PC
    input  logic        ir_we,       // from the control: the instruction register loads the fetched instruction
    input  logic        rf_we,       // from the control: write the register file
    input  logic [1:0]  alu_a_sel,   // from the control: the ALU's a -- 0 register A, 1 the PC, 2 old_pc
    input  logic [1:0]  alu_b_sel,   // from the control: the ALU's b -- 0 register B, 1 the immediate, 2 four
    input  logic [3:0]  alu_op,      // from the control: which ALU operation
    input  logic [1:0]  pc_src,      // from the control: the next PC -- 0 the ALU, 1 ALUOut, 2 the ALU, bit 0 cleared
    input  logic [1:0]  wb_src,      // from the control: what is written back -- 0 ALUOut, 1 MDR, 2 the PC
    input  logic [31:0] instr,       // the instruction at the PC, from the instruction memory
    input  logic [31:0] read_data,   // the word the data memory reads at data_addr, from the data memory
    output logic [31:0] pc,          // the address of the next instruction to fetch, to the instruction memory
    output logic [31:0] data_addr,   // ALUOut: the address of a load or store, to the data memory
    output logic [31:0] write_data,  // register B: what a store writes, to the data memory
    output logic [6:0]  opcode,      // instruction bits 6:0 of the instruction register, to the control
    output logic [2:0]  funct3,      // instruction bits 14:12, to the control and the branch comparator
    output logic        funct7_5,    // instruction bit 30, to the control
    output logic        taken        // the branch comparator's answer, to the control
);
    logic [31:0] ir, old_pc, a, b, alu_out, mdr;
    logic [31:0] rd1, rd2, imm, alu_a, alu_b, alu_y, pc_next, wb;

    assign opcode = ir[6:0];     // the kind of instruction, for the control
    assign funct3 = ir[14:12];   // which operation within a kind
    assign funct7_5 = ir[30];    // bit 30: sub and sra
    assign data_addr = alu_out;  // a load's or store's address is ALUOut, the ALU's sum from the step before
    assign write_data = b;       // a store writes register B

    // The register file: reads registers rs1 and rs2 of the instruction register (bits 19:15 and 24:20), which A and B
    // then keep, and writes register rd (bits 11:7) with what is written back when rf_we is set.
    mcpu_regfile regfile (
        .clk(clk),
        .we(rf_we),
        .ra1(ir[19:15]),
        .ra2(ir[24:20]),
        .wa(ir[11:7]),
        .wd(wb),
        .rd1(rd1),
        .rd2(rd2)
    );

    // The one ALU, used for everything: stepping the PC, a branch's or jal's target, the arithmetic itself, and a
    // load's or store's address.
    mcpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    // The PC: the address of the next instruction to fetch. It loads the next PC only when pc_we is set -- FETCH steps
    // it on by four, a jump or a taken branch loads its target -- and reset sets it to 0 at once.
    always_ff @(posedge clk, posedge rst)
        // Reset sends the PC back to 0.
        if (rst) pc <= 32'd0;
        // Otherwise the PC takes the next PC when pc_we is set, and holds when it is not.
        else if (pc_we) pc <= pc_next;

    // The instruction register, and the address the instruction came from. They load only in FETCH (ir_we) and then
    // hold for the rest of the instruction, while the PC has already stepped on; old_pc is what the branch, jal and
    // auipc targets are worked out from.
    always_ff @(posedge clk)
        // Load the fetched instruction and its address when ir_we is set; hold them otherwise.
        if (ir_we) begin
            ir <= instr;
            old_pc <= pc;
        end

    // The registers between the steps take a new value every cycle: A and B the register file's two outputs, ALUOut the
    // ALU's result and MDR the word the data memory reads -- so each step's result is there for the next.
    always_ff @(posedge clk) begin
        a <= rd1;
        b <= rd2;
        alu_out <= alu_y;
        mdr <= read_data;
    end

    // The immediate generator: gathers the instruction register's immediate and sign-extends it.
    mcpu_immgen immgen (.instr(ir), .imm(imm));

    // The branch comparator: compares registers A and B as the branch's funct3 asks, and says whether it is taken.
    mcpu_branch compare (.a(a), .b(b), .funct3(funct3), .taken(taken));

    always_comb begin
        // The ALU's a: register A (0), the PC (1, to step it by four in FETCH), or the instruction's own address old_pc
        // (2, to work out the target of a branch, jal or auipc).
        case (alu_a_sel)
            2'd1:    alu_a = pc;
            2'd2:    alu_a = old_pc;
            default: alu_a = a;
        endcase

        // The ALU's b: register B (0), the immediate (1), or the constant four (2, to step the PC).
        case (alu_b_sel)
            2'd1:    alu_b = imm;
            2'd2:    alu_b = 32'd4;
            default: alu_b = b;
        endcase

        // The next PC: the ALU's result (0, the PC plus four in FETCH), ALUOut (1, the target of a branch or jal,
        // worked out in DECODE), or the ALU's result with bit 0 cleared (2, a jalr's address).
        case (pc_src)
            2'd1:    pc_next = alu_out;
            2'd2:    pc_next = alu_y & ~32'd1;
            default: pc_next = alu_y;
        endcase

        // What is written back to the register file: ALUOut (0, an arithmetic result), MDR (1, a loaded word) or the PC
        // (2, the link for jal and jalr: FETCH already stepped the PC past the instruction).
        case (wb_src)
            2'd1:    wb = mdr;
            2'd2:    wb = pc;
            default: wb = alu_out;
        endcase
    end
endmodule

// The multicycle processor: the two memories, the control (a state machine) and the datapath. Each instruction takes
// three to five clock cycles, one step in each.
module mcpu #(
    parameter PROGRAM = ""mcpu_program.hex"",   // the instruction memory's image
    parameter DATA    = ""mcpu_data.hex""       // the data memory's image
) (
    input  logic clk,  // the clock: each rising edge ends one step of an instruction
    input  logic rst   // asynchronous, active high: the program starts again from address 0
);
    logic        pc_we, ir_we, rf_we, mem_we, funct7_5, taken;
    logic [1:0]  alu_a_sel, alu_b_sel, pc_src, wb_src;
    logic [3:0]  alu_op;
    logic [6:0]  opcode;
    logic [2:0]  funct3;
    logic [31:0] pc, instr, data_addr, write_data, read_data;

    // The instruction memory: read at the PC and never written; FETCH keeps the instruction it gives.
    mcpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    // The data memory: a load reads the word at the address in ALUOut; a store writes register B there when mem_we is
    // set.
    mcpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we),
        .addr(data_addr),
        .wdata(write_data),
        .rdata(read_data)
    );

    // The control: a state machine that sequences the steps of each instruction and raises the datapath's controls in
    // each.
    mcpu_control control (.*);

    // The datapath: the PC, the instruction register, the register file and the one ALU, with the registers between the
    // steps.
    mcpu_datapath datapath (.*);
endmodule

// The oracle's own testbench: it resets mcpu, runs mcpu_program.hex until it stores to 0x7fc, and checks that the eight
// words it sorted are in order and that its results match mcpu_expected.hex.
module mcpu_tb;
    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:22];

    mcpu dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(""mcpu_expected.hex"", expected);
        rst = 1'b1;
        repeat (4) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; that store lands on the next rising edge,
    // after every result is in memory.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            $write(""sorted:"");
            for (i = 0; i < 8; i++)
                $write("" %0d"", $signed(dut.dmem.mem[64 + i]));
            $display;

            for (i = 0; i < 7; i++)
                if ($signed(dut.dmem.mem[64 + i]) > $signed(dut.dmem.mem[65 + i])) begin
                    $display(""FAIL: words %0d and %0d are out of order"", i, i + 1);
                    failures++;
                end

            for (i = 0; i < 23; i++)
                if (dut.dmem.mem[128 + i] !== expected[i]) begin
                    $display(""FAIL: result %0d at %h is %h, not %h"", i, 32'h200 + 4 * i, dut.dmem.mem[128 + i], expected[i]);
                    failures++;
                end

            if (failures == 0)
                $display(""PASS: every result as expected"");
            else
                $display(""FAIL: %0d results wrong"", failures);
            $finish;
        end

    initial begin
        #200000;
        $display(""TIMEOUT: the program never said it was done"");
        $finish;
    end
endmodule

// mcpu running the programs scpu and pcpu run too -- its PROGRAM: the sum of 1 to 10, ten Fibonacci numbers, or
// every instruction once -- so the three processors can be compared on each: the start page's multicycle sample.
module mcpu_demo_tb;
    // Which program it runs: SUM, FIBONACCI or ISA (rv32i.svh). In the body, not a #( ) list, where a project's module
    // sees what it includes: a simulator's command line sets it all the same, as -P does, and so does an instantiation.
    parameter rv_program_t PROGRAM = SUM;

    // The program's images. Their names are of one length, so a ?: between them is a file name in every simulator:
    // Icarus pads a shorter one with a NUL it does not strip.
    localparam IMAGE    = PROGRAM == SUM ? ""rv_sum.hex"" : PROGRAM == FIBONACCI ? ""rv_fib.hex"" : ""rv_isa.hex"";
    localparam EXPECTED = PROGRAM == SUM ? ""rv_sum_expected.hex"" : PROGRAM == FIBONACCI ? ""rv_fib_expected.hex""
                                         : ""rv_isa_expected.hex"";

    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    mcpu #(.PROGRAM(IMAGE), .DATA(""rv_data.hex"")) dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(EXPECTED, expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; that store lands on the next rising edge,
    // after every result is in memory.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            case (PROGRAM)
                SUM:
                    $display(""the sum of 1 to 10: %0d"", dut.dmem.mem[64]);
                FIBONACCI: begin
                    $write(""ten Fibonacci numbers:"");
                    for (i = 0; i < 10; i++)
                        $write("" %0d"", dut.dmem.mem[65 + i]);
                    $display;
                end
                default:
                    $display(""every instruction once: %0d of 41 checks held"", dut.dmem.mem[126]);
            endcase

            for (i = 0; i < 64; i++)
                if (dut.dmem.mem[64 + i] !== expected[i]) begin
                    $display(""FAIL: the word at %h is %h, not %h"", 32'h100 + 4 * i, dut.dmem.mem[64 + i], expected[i]);
                    failures++;
                end

            if (failures == 0)
                $display(""PASS: every result as expected"");
            else
                $display(""FAIL: %0d results wrong"", failures);
            $finish;
        end

    initial begin
        #100000;
        $display(""TIMEOUT: the program never said it was done"");
        $finish;
    end
endmodule
";

        private const string Pcpu = @"// pcpu: an original pipelined processor -- five stages, fetch, decode, execute, memory and write-back,
// with a register between each two, so five instructions are under way at once and the clock need only
// be as slow as the slowest stage. Drawn as the textbook draws one: a module a stage (pcpu_fetch,
// pcpu_decode, pcpu_execute, and the data memory for the memory stage) and a module a register between
// them (pcpu_if_id, pcpu_id_ex, pcpu_ex_mem, pcpu_mem_wb). Hazards are handled as the textbook does:
//   - forwarding: a result still in the execute/memory or memory/write-back register goes straight to
//     the ALU's operands, rather than waiting to be written to the register file;
//   - a stall: an instruction that needs a word a load has not yet read waits one cycle in decode, with
//     a bubble let into execute behind the load;
//   - a flush: a taken branch or a jump is decided in execute, and the two instructions fetched after it
//     are turned into bubbles.
// The register file passes a value being written back straight through to a read of the same register in that cycle --
// the textbook's ""written in the first half, read in the second"" -- so decode never reads a register the instruction
// leaving write-back is about to change.
// Separate instruction and data memories of 1,024 words each, loaded from images with $readmemh. It runs the RV32I
// instructions a program needs: lui, auipc, jal, jalr, every branch, lw and sw, and every arithmetic, logic and shift
// operation, by register and by immediate. No bytes or halfwords, no system instructions. pcpu_tb runs one of three
// programs on rv_data.hex -- its PROGRAM: rv_sum.hex, rv_fib.hex or rv_isa.hex -- until the program stores to 0x7fc,
// then checks its results against the program's own expected image. Written for BitSorter's oracle from the published
// RISC-V specification; not taken from any course material.

`include ""rv32i.svh""

// The ALU: the arithmetic and logic unit. It adds, subtracts, ANDs, ORs, XORs, shifts and compares its two operands as
// op says, and passes b through unchanged for lui.
module pcpu_alu (
    input  logic [31:0] a,   // the first operand
    input  logic [31:0] b,   // the second operand; a shift takes its low five bits as the amount
    input  logic [3:0]  op,  // which operation: one of the ALU_ codes of rv32i.svh
    output logic [31:0] y    // the result
);
    always_comb begin
        // The operation: op chooses which result is y -- a + b, a - b, a AND b, a OR b, a XOR b, a shifted left, a
        // shifted right (logically, or arithmetically so the sign is kept), 1 or 0 for a signed or an unsigned
        // less-than, or b itself for lui. A code the processor never uses gives 0.
        case (op)
            ALU_ADD:   y = a + b;
            ALU_SUB:   y = a - b;
            ALU_AND:   y = a & b;
            ALU_OR:    y = a | b;
            ALU_XOR:   y = a ^ b;
            ALU_SLL:   y = a << b[4:0];
            ALU_SRL:   y = a >> b[4:0];
            ALU_SRA:   y = $signed(a) >>> b[4:0];
            ALU_SLT:   y = {31'd0, $signed(a) < $signed(b)};
            ALU_SLTU:  y = {31'd0, a < b};
            ALU_PASSB: y = b;
            default:   y = 32'd0;
        endcase
    end
endmodule

// The register file: 32 registers of 32 bits, x0 to x31, with two read ports and one write port. A read is immediate,
// and a register being written in the same cycle reads the value on its way in; the write itself lands on the rising
// edge. x0 always reads 0.
module pcpu_regfile (
    input  logic        clk,  // the clock: a write lands on its rising edge
    input  logic        we,   // write wd to register wa on the rising edge
    input  logic [4:0]  ra1,  // the first register to read: rs1
    input  logic [4:0]  ra2,  // the second register to read: rs2
    input  logic [4:0]  wa,   // the register to write: rd (a write to x0 is ignored)
    input  logic [31:0] wd,   // the value to write
    output logic [31:0] rd1,  // the value of register ra1: 0 for x0, wd if it is being written now
    output logic [31:0] rd2   // the value of register ra2: 0 for x0, wd if it is being written now
);
    logic [31:0] regs [0:31];  // the registers x0 to x31 (x0's cell is never written)

    // The write port: when we is set, register wa takes wd on the rising edge. x0 is never written.
    always_ff @(posedge clk)
        if (we && wa != 5'd0) regs[wa] <= wd;

    // Read port 1: x0 reads 0 whatever is written to it; a register being written this cycle reads what is being
    // written, passed straight through -- the textbook's write in the first half and read in the second; any other
    // register reads what it holds.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : we && wa == ra1 ? wd : regs[ra1];
    // Read port 2: the same for ra2 -- x0, a register being written now, or the register it names.
    assign rd2 = ra2 == 5'd0 ? 32'd0 : we && wa == ra2 ? wd : regs[ra2];
endmodule

// A memory of 1,024 words of 32 bits (4 KiB), loaded at time 0 from the IMAGE file with $readmemh. A read is immediate;
// a write lands on the rising edge. Addresses are byte addresses: the low two bits are dropped, so a word is always
// read or written whole, and an address past 4 KiB wraps around.
module pcpu_memory #(
    parameter IMAGE = ""rv_sum.hex""   // the image loaded at time 0
) (
    input  logic        clk,    // the clock: a write lands on its rising edge
    input  logic        we,     // write wdata on the rising edge
    input  logic [31:0] addr,   // a byte address; words are aligned
    input  logic [31:0] wdata,  // the word to write
    output logic [31:0] rdata   // the word at addr, available at once
);
    logic [31:0] mem [0:1023];  // the 1,024 words

    // Fill the memory from the image file before the first clock.
    initial $readmemh(IMAGE, mem);

    // The write port: when we is set, the word at addr takes wdata on the rising edge.
    always_ff @(posedge clk)
        if (we) mem[addr[11:2]] <= wdata;

    // The read port: the word addr points at, with no clock needed.
    assign rdata = mem[addr[11:2]];
endmodule

// The control: turns an instruction's opcode and funct fields into the controls it needs further down the pipeline --
// whether it writes a register or memory, reads memory, is a branch or a jump, and what the ALU does. Pure logic; it
// works in the decode stage.
module pcpu_control (
    input  logic [6:0] opcode,     // instruction bits 6:0: the kind of instruction
    input  logic [2:0] funct3,     // instruction bits 14:12: which operation within a kind
    input  logic       funct7_5,   // instruction bit 30: tells sub from add, and sra from srl
    output logic       rf_we,      // the instruction writes a register
    output logic       mem_we,     // a store: the instruction writes the data memory
    output logic       mem_read,   // a load: what it writes back comes late, from the data memory
    output logic       alu_a_pc,   // the ALU's a: 0 register rs1, 1 the PC (auipc)
    output logic       alu_b_imm,  // the ALU's b: 0 register rs2, 1 the immediate
    output logic [3:0] alu_op,     // which ALU operation: an ALU_ code
    output logic       branch,     // a conditional branch: execute makes the comparison
    output logic       jump,       // jal: jump to the PC plus the immediate
    output logic       jump_reg,   // jalr: jump to the ALU's sum, bit 0 cleared
    output logic       link        // jal and jalr write back PC + 4, the return address
);
    logic [3:0] alu_function;

    // What the instruction's funct fields ask of the ALU.
    always_comb begin
        // funct3 names the operation of an arithmetic or logic instruction; instruction bit 30 separates the pairs add
        // and sub, srl and sra.
        case (funct3)
            // 000 is add -- or subtract, for a register-register instruction with bit 30 set (there is no subtract by
            // an immediate).
            3'b000:  alu_function = opcode == OP_REG && funct7_5 ? ALU_SUB : ALU_ADD;
            3'b001:  alu_function = ALU_SLL;
            3'b010:  alu_function = ALU_SLT;
            3'b011:  alu_function = ALU_SLTU;
            3'b100:  alu_function = ALU_XOR;
            // 101 is a right shift: arithmetic when bit 30 is set, else logical.
            3'b101:  alu_function = funct7_5 ? ALU_SRA : ALU_SRL;
            3'b110:  alu_function = ALU_OR;
            default: alu_function = ALU_AND;
        endcase
    end

    // The controls: every one starts at its quiet value, and the instruction's kind raises what it needs.
    always_comb begin
        rf_we = 1'b0;
        mem_we = 1'b0;
        mem_read = 1'b0;
        alu_a_pc = 1'b0;
        alu_b_imm = 1'b0;
        alu_op = ALU_ADD;
        branch = 1'b0;
        jump = 1'b0;
        jump_reg = 1'b0;
        link = 1'b0;

        // The instruction's kind, by opcode, sets the controls it needs: register-register and immediate arithmetic
        // write a register; lui passes the immediate through; auipc adds it to the PC; jal and jalr jump and link; a
        // branch only says it is one, since execute makes the comparison; a load reads the data memory and writes a
        // register; a store writes the data memory.
        case (opcode)
            OP_REG: begin
                rf_we = 1'b1;
                alu_op = alu_function;
            end
            OP_IMM: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                alu_op = alu_function;
            end
            OP_LUI: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                alu_op = ALU_PASSB;
            end
            OP_AUIPC: begin
                rf_we = 1'b1;
                alu_a_pc = 1'b1;
                alu_b_imm = 1'b1;
            end
            OP_JAL: begin
                rf_we = 1'b1;
                jump = 1'b1;
                link = 1'b1;
            end
            OP_JALR: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                jump_reg = 1'b1;
                link = 1'b1;
            end
            OP_BRANCH: branch = 1'b1;
            OP_LOAD: begin
                rf_we = 1'b1;
                mem_read = 1'b1;
                alu_b_imm = 1'b1;
            end
            OP_STORE: begin
                mem_we = 1'b1;
                alu_b_imm = 1'b1;
            end
            default: ;
        endcase
    end
endmodule

// The immediate generator: the instruction's immediate, its bits gathered and its sign extended as its format says.
module pcpu_immgen (
    input  logic [31:0] instr,  // the whole instruction
    output logic [31:0] imm     // the immediate its format carries, as 32 bits (0 for a kind with none)
);
    always_comb begin
        // The immediate's format, by opcode: I (immediate arithmetic, loads, jalr), S (stores), B (branches), U (lui,
        // auipc) or J (jal). Each gathers its scattered bits, and all but U sign-extend them from instruction bit 31.
        case (instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            // A kind with no immediate, such as register-register arithmetic: 0.
            default:                  imm = 32'd0;
        endcase
    end
endmodule

// The branch comparator: whether a branch's condition holds of its two registers, by funct3.
module pcpu_branch (
    input  logic [31:0] a,       // rs1's value, forwarded where a newer one is on its way
    input  logic [31:0] b,       // rs2's value, forwarded where a newer one is on its way
    input  logic [2:0]  funct3,  // which comparison: 000 beq, 001 bne, 100 blt, 101 bge, 110 bltu, 111 bgeu
    output logic        taken    // 1 when the branch's condition holds
);
    always_comb begin
        // The condition funct3 names: equal, not equal, then less than and greater or equal as signed numbers, then the
        // same two as unsigned. A funct3 no branch uses is never taken.
        case (funct3)
            3'b000:  taken = a == b;
            3'b001:  taken = a != b;
            3'b100:  taken = $signed(a) < $signed(b);
            3'b101:  taken = $signed(a) >= $signed(b);
            3'b110:  taken = a < b;
            3'b111:  taken = a >= b;
            default: taken = 1'b0;
        endcase
    end
endmodule

// Where each of the ALU's register operands comes from: 0 the register file, by way of decode; 1 the
// memory/write-back register, a result two instructions back; 2 the execute/memory register, a result
// one instruction back -- the newer one winning when both write the register.
module pcpu_forward (
    input  logic [4:0] rs1,        // the first register the instruction in execute reads
    input  logic [4:0] rs2,        // the second register it reads
    input  logic       mem_rf_we,  // the instruction in memory, one ahead, writes a register
    input  logic [4:0] mem_rd,     // the register it writes
    input  logic       wb_rf_we,   // the instruction in write-back, two ahead, writes a register
    input  logic [4:0] wb_rd,      // the register it writes
    output logic [1:0] forward_a,  // operand A from: 0 decode's value, 1 memory/write-back, 2 execute/memory
    output logic [1:0] forward_b   // operand B from: 0 decode's value, 1 memory/write-back, 2 execute/memory
);
    always_comb begin
        // Operand A: the newest result wins. If the instruction one ahead, in the execute/memory register, writes the
        // register this one reads (and it is not x0), take its result.
        if (mem_rf_we && mem_rd != 5'd0 && mem_rd == rs1)
            forward_a = 2'd2;
        // Otherwise, if the instruction two ahead, in the memory/write-back register, writes it, take that value.
        else if (wb_rf_we && wb_rd != 5'd0 && wb_rd == rs1)
            forward_a = 2'd1;
        else
            forward_a = 2'd0;

        // Operand B: the same choice for the second register -- the execute/memory register's result first.
        if (mem_rf_we && mem_rd != 5'd0 && mem_rd == rs2)
            forward_b = 2'd2;
        // Otherwise the memory/write-back register's value, if it writes the register.
        else if (wb_rf_we && wb_rd != 5'd0 && wb_rd == rs2)
            forward_b = 2'd1;
        else
            forward_b = 2'd0;
    end
endmodule

// A load in execute whose register the instruction in decode reads: its word is not read until the
// memory stage, too late to forward, so decode waits a cycle.
module pcpu_hazard (
    input  logic       ex_mem_read,  // the instruction in execute is a load
    input  logic [4:0] ex_rd,        // the register the load writes
    input  logic [4:0] id_rs1,       // the first register the instruction in decode reads
    input  logic [4:0] id_rs2,       // the second register it reads
    output logic       stall         // decode waits a cycle: fetch and IF/ID hold, and a bubble goes into execute
);
    // A stall: the instruction in execute is a load of a register (not x0) that the instruction in decode reads.
    assign stall = ex_mem_read && ex_rd != 5'd0 && (ex_rd == id_rs1 || ex_rd == id_rs2);
endmodule

// ---------------------------------------------------------------- the stages, and the registers between them

// Fetch: the PC, which steps on by four, holds while decode waits, and takes a branch's or a jump's target.
module pcpu_fetch (
    input  logic        clk,       // the clock: the PC changes on its rising edge
    input  logic        rst,       // asynchronous, active high: the PC to 0
    input  logic        stall,     // decode waits a cycle: the PC holds
    input  logic        redirect,  // a taken branch or a jump in execute
    input  logic [31:0] target,    // where it goes
    output logic [31:0] pc         // the address of the instruction being fetched, to the instruction memory
);
    // The PC: the address of the instruction being fetched. It steps on by four each cycle, holds while decode waits,
    // and loads a branch's or jump's target; reset sets it to 0 at once.
    always_ff @(posedge clk, posedge rst)
        // Reset sends the PC back to 0.
        if (rst) pc <= 32'd0;
        // A branch or jump taken in execute loads its target.
        else if (redirect) pc <= target;
        // Otherwise the PC steps to the next word, unless decode is waiting, when it holds.
        else if (!stall) pc <= pc + 32'd4;
endmodule

// The IF/ID register, between fetch and decode: the instruction fetched and its address. A flush makes it a bubble, a
// stall holds it.
module pcpu_if_id (
    input  logic        clk,      // the clock: the register changes on its rising edge
    input  logic        rst,      // asynchronous, active high: both fields to 0
    input  logic        stall,    // decode waits a cycle: hold what is here
    input  logic        flush,    // a branch or jump was taken in execute: drop the instruction fetched behind it
    input  logic [31:0] pc,       // the address of the instruction being fetched
    input  logic [31:0] instr,    // that instruction, from the instruction memory
    output logic [31:0] id_pc,    // the address of the instruction now in decode
    output logic [31:0] id_instr  // the instruction now in decode (0, a bubble: opcode 0 does nothing)
);
    // The IF/ID register: the instruction in decode, id_instr, and the address it came from, id_pc. Reset and a flush
    // clear both to 0, a bubble; a stall leaves them as they are; otherwise they take what fetch gives.
    always_ff @(posedge clk, posedge rst)
        if (rst) begin  // reset clears both fields: a bubble
            id_pc <= 32'd0;
            id_instr <= 32'd0;
        end else if (flush) begin  // a flush clears them too: the instruction wrongly fetched becomes a bubble
            id_pc <= 32'd0;
            id_instr <= 32'd0;
        end else if (!stall) begin  // no stall: they take the instruction fetched; a stall keeps them
            id_pc <= pc;
            id_instr <= instr;
        end
endmodule

// Decode: what the instruction asks for, the registers it reads, its immediate, and whether it must wait for a load.
module pcpu_decode (
    input  logic        clk,          // the clock: the register file's write lands on its rising edge
    input  logic [31:0] instr,        // the instruction in decode, from the IF/ID register
    input  logic        wb_rf_we,     // the instruction in write-back writes the register file
    input  logic [4:0]  wb_rd,        // the register it writes
    input  logic [31:0] wb_value,     // the value it writes
    input  logic        ex_mem_read,  // the instruction in execute is a load (for the load hazard)
    input  logic [4:0]  ex_rd,        // the register that load writes
    output logic        stall,        // a load in execute writes a register this instruction reads: wait a cycle
    output logic        rf_we,        // the instruction writes a register
    output logic        mem_we,       // a store: it writes the data memory
    output logic        mem_read,     // a load: it reads the data memory
    output logic        alu_a_pc,     // the ALU's a is the PC (auipc), else register rs1
    output logic        alu_b_imm,    // the ALU's b is the immediate, else register rs2
    output logic [3:0]  alu_op,       // which ALU operation
    output logic        branch,       // a conditional branch
    output logic        jump,         // jal
    output logic        jump_reg,     // jalr
    output logic        link,         // jal or jalr: it writes back PC + 4
    output logic [2:0]  funct3,       // instruction bits 14:12: which comparison, for a branch
    output logic [4:0]  rs1,          // the first register it reads (bits 19:15)
    output logic [4:0]  rs2,          // the second register it reads (bits 24:20)
    output logic [4:0]  rd,           // the register it writes (bits 11:7)
    output logic [31:0] rd1,          // register rs1's value, read from the register file
    output logic [31:0] rd2,          // register rs2's value, read from the register file
    output logic [31:0] imm           // its immediate, sign-extended
);
    assign funct3 = instr[14:12];  // which operation within a kind
    assign rs1 = instr[19:15];     // the first register to read
    assign rs2 = instr[24:20];     // the second register to read
    assign rd = instr[11:7];       // the register to write

    // The control: decodes the instruction into its controls.
    pcpu_control control (
        .opcode(instr[6:0]),
        .funct3(instr[14:12]),
        .funct7_5(instr[30]),
        .rf_we(rf_we),
        .mem_we(mem_we),
        .mem_read(mem_read),
        .alu_a_pc(alu_a_pc),
        .alu_b_imm(alu_b_imm),
        .alu_op(alu_op),
        .branch(branch),
        .jump(jump),
        .jump_reg(jump_reg),
        .link(link)
    );

    // The register file: reads registers rs1 and rs2 for this instruction; its write port belongs to the instruction in
    // write-back, so wb_rf_we, wb_rd and wb_value come from there.
    pcpu_regfile regfile (
        .clk(clk),
        .we(wb_rf_we),
        .ra1(rs1),
        .ra2(rs2),
        .wa(wb_rd),
        .wd(wb_value),
        .rd1(rd1),
        .rd2(rd2)
    );

    // The hazard unit: a stall if the instruction in execute is a load whose register this one reads.
    pcpu_hazard hazard (
        .ex_mem_read(ex_mem_read),
        .ex_rd(ex_rd),
        .id_rs1(rs1),
        .id_rs2(rs2),
        .stall(stall)
    );

    // The immediate generator: gathers the instruction's immediate and sign-extends it.
    pcpu_immgen immgen (.instr(instr), .imm(imm));
endmodule

// Between decode and execute. A stall or a flush lets a bubble through: its controls cleared, so it writes nothing
// and jumps nowhere, whatever its other fields hold.
module pcpu_id_ex (
    input  logic        clk,           // the clock: the register changes on its rising edge
    input  logic        rst,           // asynchronous, active high: every field to 0, a bubble
    input  logic        stall,         // decode is waiting for a load: let a bubble through
    input  logic        flush,         // a branch or jump was taken in execute: let a bubble through
    input  logic        rf_we,         // from decode: writes a register
    input  logic        mem_we,        // from decode: a store
    input  logic        mem_read,      // from decode: a load
    input  logic        alu_a_pc,      // from decode: the ALU's a is the PC
    input  logic        alu_b_imm,     // from decode: the ALU's b is the immediate
    input  logic [3:0]  alu_op,        // from decode: the ALU operation
    input  logic        branch,        // from decode: a conditional branch
    input  logic        jump,          // from decode: jal
    input  logic        jump_reg,      // from decode: jalr
    input  logic        link,          // from decode: writes back PC + 4
    input  logic [2:0]  funct3,        // from decode: instruction bits 14:12, for a branch's comparison
    input  logic [4:0]  rs1,           // from decode: the first register it reads
    input  logic [4:0]  rs2,           // from decode: the second register it reads
    input  logic [4:0]  rd,            // from decode: the register it writes
    input  logic [31:0] pc,            // from decode: its address
    input  logic [31:0] rd1,           // from decode: register rs1's value as decode read it
    input  logic [31:0] rd2,           // from decode: register rs2's value as decode read it
    input  logic [31:0] imm,           // from decode: its immediate
    output logic        ex_rf_we,      // to execute: writes a register
    output logic        ex_mem_we,     // to execute: a store
    output logic        ex_mem_read,   // to execute: a load
    output logic        ex_alu_a_pc,   // to execute: the ALU's a is the PC
    output logic        ex_alu_b_imm,  // to execute: the ALU's b is the immediate
    output logic [3:0]  ex_alu_op,     // to execute: the ALU operation
    output logic        ex_branch,     // to execute: a conditional branch
    output logic        ex_jump,       // to execute: jal
    output logic        ex_jump_reg,   // to execute: jalr
    output logic        ex_link,       // to execute: writes back PC + 4
    output logic [2:0]  ex_funct3,     // to execute: instruction bits 14:12, for a branch's comparison
    output logic [4:0]  ex_rs1,        // to execute: the first register it reads
    output logic [4:0]  ex_rs2,        // to execute: the second register it reads
    output logic [4:0]  ex_rd,         // to execute: the register it writes
    output logic [31:0] ex_pc,         // to execute: its address
    output logic [31:0] ex_rd1,        // to execute: register rs1's value as decode read it
    output logic [31:0] ex_rd2,        // to execute: register rs2's value as decode read it
    output logic [31:0] ex_imm         // to execute: its immediate
);
    logic bubble;
    // A bubble: a stall (decode is waiting for a load) or a flush (a branch or jump was taken in execute).
    assign bubble = stall || flush;

    // The ID/EX register: the instruction in execute -- its controls, funct3, the registers it reads and writes, its
    // address, the two register values decode read and its immediate. Reset clears every field.
    always_ff @(posedge clk, posedge rst)
        if (rst) begin  // reset clears every field, so the stage starts as a bubble; otherwise it takes decode's value
            ex_rf_we <= 1'b0;
            ex_mem_we <= 1'b0;
            ex_mem_read <= 1'b0;
            ex_alu_a_pc <= 1'b0;
            ex_alu_b_imm <= 1'b0;
            ex_alu_op <= ALU_ADD;
            ex_branch <= 1'b0;
            ex_jump <= 1'b0;
            ex_jump_reg <= 1'b0;
            ex_link <= 1'b0;
            ex_funct3 <= 3'd0;
            ex_rs1 <= 5'd0;
            ex_rs2 <= 5'd0;
            ex_rd <= 5'd0;
            ex_pc <= 32'd0;
            ex_rd1 <= 32'd0;
            ex_rd2 <= 32'd0;
            ex_imm <= 32'd0;
        end else begin
            // A bubble clears the controls that could change anything -- a register write, a memory write or read -- so
            // it does nothing as it passes through.
            ex_rf_we <= rf_we && !bubble;
            ex_mem_we <= mem_we && !bubble;
            ex_mem_read <= mem_read && !bubble;
            // The ALU's choices pass on as they are.
            ex_alu_a_pc <= alu_a_pc;
            ex_alu_b_imm <= alu_b_imm;
            ex_alu_op <= alu_op;
            // A bubble is no branch and no jump either, so it redirects nothing.
            ex_branch <= branch && !bubble;
            ex_jump <= jump && !bubble;
            ex_jump_reg <= jump_reg && !bubble;
            // What execute needs of the instruction passes on: whether it links, its funct3, the registers it reads and
            // writes, its address, the two register values and its immediate.
            ex_link <= link;
            ex_funct3 <= funct3;
            ex_rs1 <= rs1;
            ex_rs2 <= rs2;
            ex_rd <= rd;
            ex_pc <= pc;
            ex_rd1 <= rd1;
            ex_rd2 <= rd2;
            ex_imm <= imm;
        end
endmodule

// Execute: the operands, forwarded where a newer value is on its way; the ALU; and whether a branch or a jump goes.
module pcpu_execute (
    input  logic        alu_a_pc,    // the ALU's a is the PC (auipc), else operand A
    input  logic        alu_b_imm,   // the ALU's b is the immediate, else operand B
    input  logic [3:0]  alu_op,      // which ALU operation
    input  logic        branch,      // a conditional branch
    input  logic        jump,        // jal
    input  logic        jump_reg,    // jalr
    input  logic        link,        // jal or jalr: the result is PC + 4
    input  logic [2:0]  funct3,      // which comparison a branch makes
    input  logic [4:0]  rs1,         // the first register it reads: forwarding looks for it in later stages
    input  logic [4:0]  rs2,         // the second register it reads
    input  logic [31:0] pc,          // the instruction's own address
    input  logic [31:0] rd1,         // register rs1's value as decode read it, perhaps out of date
    input  logic [31:0] rd2,         // register rs2's value as decode read it
    input  logic [31:0] imm,         // its immediate
    input  logic        mem_rf_we,   // the instruction in memory, one ahead, writes a register
    input  logic [4:0]  mem_rd,      // the register it writes
    input  logic [31:0] mem_result,  // its result, from the execute/memory register
    input  logic        wb_rf_we,    // the instruction in write-back, two ahead, writes a register
    input  logic [4:0]  wb_rd,       // the register it writes
    input  logic [31:0] wb_value,    // the value it writes: a loaded word, else its result
    output logic        redirect,    // fetch takes target, and the two instructions behind are flushed
    output logic [31:0] target,      // where a taken branch or a jump goes
    output logic [31:0] result,      // the ALU's result (a load's or store's address), or for a jump the PC and four
    output logic [31:0] store_data   // what a store writes
);
    logic [1:0]  forward_a, forward_b;
    logic [31:0] src_a, src_b, alu_a, alu_b, alu_y;
    logic        taken;

    // The forwarding unit: finds, for each register operand, whether a newer value is waiting in the memory or
    // write-back stage.
    pcpu_forward forward (
        .rs1(rs1),
        .rs2(rs2),
        .mem_rf_we(mem_rf_we),
        .mem_rd(mem_rd),
        .wb_rf_we(wb_rf_we),
        .wb_rd(wb_rd),
        .forward_a(forward_a),
        .forward_b(forward_b)
    );

    always_comb begin
        // Operand A: the value decode read (0), or a newer one forwarded from the instruction in write-back (1) or from
        // the one in memory (2).
        case (forward_a)
            2'd2:    src_a = mem_result;
            2'd1:    src_a = wb_value;
            default: src_a = rd1;
        endcase
        // Operand B: chosen the same way for the second register.
        case (forward_b)
            2'd2:    src_b = mem_result;
            2'd1:    src_b = wb_value;
            default: src_b = rd2;
        endcase
    end

    // The ALU's a: the PC for auipc, else operand A.
    assign alu_a = alu_a_pc ? pc : src_a;
    // The ALU's b: the immediate for immediate arithmetic, loads, stores and jalr, else operand B.
    assign alu_b = alu_b_imm ? imm : src_b;

    // The ALU: the result of an arithmetic instruction, the address of a load or store, the target of a jalr.
    pcpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    // The branch comparator: compares the two operands as the branch's funct3 asks, and says whether it is taken.
    pcpu_branch compare (.a(src_a), .b(src_b), .funct3(funct3), .taken(taken));

    // Fetch must change course if this instruction is a jal or jalr, or a branch whose condition holds.
    assign redirect = jump || jump_reg || (branch && taken);
    // Where it goes: a jalr to the ALU's sum with bit 0 cleared, a jal or a branch to its own address plus the
    // immediate.
    assign target = jump_reg ? alu_y & ~32'd1 : pc + imm;
    // What the instruction passes on: PC + 4 for a jal or jalr (the link), else the ALU's result -- an arithmetic
    // result, or the address of a load or store.
    assign result = link ? pc + 32'd4 : alu_y;
    // What a store writes: operand B, forwarded.
    assign store_data = src_b;
endmodule

// The EX/MEM register, between execute and memory: whether the instruction writes a register, writes memory or reads
// memory, its destination register, its result and the data a store writes.
module pcpu_ex_mem (
    input  logic        clk,            // the clock: the register changes on its rising edge
    input  logic        rst,            // asynchronous, active high: every field to 0, a bubble
    input  logic        rf_we,          // from execute: the instruction writes a register
    input  logic        mem_we,         // from execute: it is a store
    input  logic        mem_read,       // from execute: it is a load
    input  logic [4:0]  rd,             // from execute: the register it writes
    input  logic [31:0] result,         // from execute: its result -- also a load's or store's address
    input  logic [31:0] store_data,     // from execute: what a store writes
    output logic        mem_rf_we,      // to memory: the instruction writes a register
    output logic        mem_mem_we,     // to memory: it is a store, so the data memory writes
    output logic        mem_mem_read,   // to memory: it is a load
    output logic [4:0]  mem_rd,         // to memory: the register it writes
    output logic [31:0] mem_result,     // to memory: its result, which is also the address of a load or store
    output logic [31:0] mem_store_data  // to memory: what a store writes
);
    // The EX/MEM register: the instruction in memory -- its register-write, store and load controls, its destination
    // register, its result and a store's data. Reset clears every field.
    always_ff @(posedge clk, posedge rst)
        if (rst) begin  // reset: every field cleared, a bubble; otherwise they take what execute gives
            mem_rf_we <= 1'b0;
            mem_mem_we <= 1'b0;
            mem_mem_read <= 1'b0;
            mem_rd <= 5'd0;
            mem_result <= 32'd0;
            mem_store_data <= 32'd0;
        end else begin
            mem_rf_we <= rf_we;
            mem_mem_we <= mem_we;
            mem_mem_read <= mem_read;
            mem_rd <= rd;
            mem_result <= result;
            mem_store_data <= store_data;
        end
endmodule

// The MEM/WB register, between memory and write-back: what is written back -- the word read for a load, else the
// result -- and which register it goes to.
module pcpu_mem_wb (
    input  logic        clk,        // the clock: the register changes on its rising edge
    input  logic        rst,        // asynchronous, active high: every field to 0, a bubble
    input  logic        rf_we,      // from memory: the instruction writes a register
    input  logic        mem_read,   // from memory: it is a load
    input  logic [4:0]  rd,         // from memory: the register it writes
    input  logic [31:0] result,     // from memory: its result
    input  logic [31:0] read_data,  // from memory: the word the data memory reads
    output logic        wb_rf_we,   // to write-back: the instruction writes the register file
    output logic [4:0]  wb_rd,      // to write-back: the register it writes
    output logic [31:0] wb_value    // to write-back: the value it writes
);
    // The MEM/WB register: the instruction in write-back -- whether it writes the register file, its destination and
    // the value it writes. Reset clears every field.
    always_ff @(posedge clk, posedge rst)
        if (rst) begin  // reset: every field cleared, a bubble; otherwise they take what memory gives
            wb_rf_we <= 1'b0;
            wb_rd <= 5'd0;
            wb_value <= 32'd0;
        end else begin
            wb_rf_we <= rf_we;
            wb_rd <= rd;
            // The value to write back: the word the data memory read for a load, else the instruction's result.
            wb_value <= mem_read ? read_data : result;
        end
endmodule

// The pipelined processor: the instruction memory, the stages fetch, decode, execute, memory (the data memory) and
// write-back (the register file's write, fed from MEM/WB), and the registers between them -- IF/ID, ID/EX, EX/MEM and
// MEM/WB.
module pcpu #(
    parameter PROGRAM = ""rv_sum.hex"",   // the instruction memory's image
    parameter DATA    = ""rv_data.hex""       // the data memory's image
) (
    input  logic clk,  // the clock: every pipeline register changes on its rising edge
    input  logic rst   // asynchronous, active high: the PC to 0, every stage a bubble
);
    logic [31:0] pc, instr, id_pc, id_instr;
    logic        stall, redirect;
    logic [31:0] target;

    logic        rf_we, mem_we_id, mem_read, alu_a_pc, alu_b_imm, branch, jump, jump_reg, link;
    logic [3:0]  alu_op;
    logic [2:0]  funct3;
    logic [4:0]  rs1, rs2, rd;
    logic [31:0] rd1, rd2, imm;

    logic        ex_rf_we, ex_mem_we, ex_mem_read, ex_alu_a_pc, ex_alu_b_imm, ex_branch, ex_jump, ex_jump_reg, ex_link;
    logic [3:0]  ex_alu_op;
    logic [2:0]  ex_funct3;
    logic [4:0]  ex_rs1, ex_rs2, ex_rd;
    logic [31:0] ex_pc, ex_rd1, ex_rd2, ex_imm, ex_result, store_data;

    logic        mem_rf_we, mem_we, mem_mem_read;
    logic [4:0]  mem_rd;
    logic [31:0] mem_result, write_data, read_data, data_addr;

    logic        wb_rf_we;
    logic [4:0]  wb_rd;
    logic [31:0] wb_value;

    // The data memory's address in the memory stage, where the testbench watches for the store that ends a program.
    assign data_addr = mem_result;

    // The instruction memory: read at the PC fetch holds, so the instruction at that address arrives in the same cycle.
    pcpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    // Fetch: the PC, which steps on by four, holds while decode waits, and loads the target of a taken branch or a
    // jump.
    pcpu_fetch fetch (
        .clk(clk),
        .rst(rst),
        .stall(stall),
        .redirect(redirect),
        .target(target),
        .pc(pc)
    );

    // The IF/ID register: carries the fetched instruction and its address into decode; a stall holds it and a redirect
    // empties it.
    pcpu_if_id if_id (
        .clk(clk),
        .rst(rst),
        .stall(stall),
        .flush(redirect),
        .pc(pc),
        .instr(instr),
        .id_pc(id_pc),
        .id_instr(id_instr)
    );

    // Decode: works out the instruction's controls, reads its registers, forms its immediate, writes back what
    // write-back gives, and detects a load hazard.
    pcpu_decode decode (
        .clk(clk),
        .instr(id_instr),
        .wb_rf_we(wb_rf_we),
        .wb_rd(wb_rd),
        .wb_value(wb_value),
        .ex_mem_read(ex_mem_read),
        .ex_rd(ex_rd),
        .stall(stall),
        .rf_we(rf_we),
        .mem_we(mem_we_id),
        .mem_read(mem_read),
        .alu_a_pc(alu_a_pc),
        .alu_b_imm(alu_b_imm),
        .alu_op(alu_op),
        .branch(branch),
        .jump(jump),
        .jump_reg(jump_reg),
        .link(link),
        .funct3(funct3),
        .rs1(rs1),
        .rs2(rs2),
        .rd(rd),
        .rd1(rd1),
        .rd2(rd2),
        .imm(imm)
    );

    // The ID/EX register: carries decode's controls and values into execute; a stall or a redirect lets a bubble
    // through.
    pcpu_id_ex id_ex (
        .clk(clk),
        .rst(rst),
        .stall(stall),
        .flush(redirect),
        .rf_we(rf_we),
        .mem_we(mem_we_id),
        .mem_read(mem_read),
        .alu_a_pc(alu_a_pc),
        .alu_b_imm(alu_b_imm),
        .alu_op(alu_op),
        .branch(branch),
        .jump(jump),
        .jump_reg(jump_reg),
        .link(link),
        .funct3(funct3),
        .rs1(rs1),
        .rs2(rs2),
        .rd(rd),
        .pc(id_pc),
        .rd1(rd1),
        .rd2(rd2),
        .imm(imm),
        .ex_rf_we(ex_rf_we),
        .ex_mem_we(ex_mem_we),
        .ex_mem_read(ex_mem_read),
        .ex_alu_a_pc(ex_alu_a_pc),
        .ex_alu_b_imm(ex_alu_b_imm),
        .ex_alu_op(ex_alu_op),
        .ex_branch(ex_branch),
        .ex_jump(ex_jump),
        .ex_jump_reg(ex_jump_reg),
        .ex_link(ex_link),
        .ex_funct3(ex_funct3),
        .ex_rs1(ex_rs1),
        .ex_rs2(ex_rs2),
        .ex_rd(ex_rd),
        .ex_pc(ex_pc),
        .ex_rd1(ex_rd1),
        .ex_rd2(ex_rd2),
        .ex_imm(ex_imm)
    );

    // Execute: forwards newer operands, runs the ALU and the branch comparison, and decides whether fetch must be
    // redirected.
    pcpu_execute execute (
        .alu_a_pc(ex_alu_a_pc),
        .alu_b_imm(ex_alu_b_imm),
        .alu_op(ex_alu_op),
        .branch(ex_branch),
        .jump(ex_jump),
        .jump_reg(ex_jump_reg),
        .link(ex_link),
        .funct3(ex_funct3),
        .rs1(ex_rs1),
        .rs2(ex_rs2),
        .pc(ex_pc),
        .rd1(ex_rd1),
        .rd2(ex_rd2),
        .imm(ex_imm),
        .mem_rf_we(mem_rf_we),
        .mem_rd(mem_rd),
        .mem_result(mem_result),
        .wb_rf_we(wb_rf_we),
        .wb_rd(wb_rd),
        .wb_value(wb_value),
        .redirect(redirect),
        .target(target),
        .result(ex_result),
        .store_data(store_data)
    );

    // The EX/MEM register: carries execute's result and a store's data into the memory stage.
    pcpu_ex_mem ex_mem (
        .clk(clk),
        .rst(rst),
        .rf_we(ex_rf_we),
        .mem_we(ex_mem_we),
        .mem_read(ex_mem_read),
        .rd(ex_rd),
        .result(ex_result),
        .store_data(store_data),
        .mem_rf_we(mem_rf_we),
        .mem_mem_we(mem_we),
        .mem_mem_read(mem_mem_read),
        .mem_rd(mem_rd),
        .mem_result(mem_result),
        .mem_store_data(write_data)
    );

    // The data memory is the memory stage: a load reads the word at the address in the EX/MEM register, a store writes
    // it there when mem_we is set.
    pcpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we),
        .addr(mem_result),
        .wdata(write_data),
        .rdata(read_data)
    );

    // The MEM/WB register: carries what write-back writes -- the loaded word, else the result -- and its destination
    // register.
    pcpu_mem_wb mem_wb (
        .clk(clk),
        .rst(rst),
        .rf_we(mem_rf_we),
        .mem_read(mem_mem_read),
        .rd(mem_rd),
        .result(mem_result),
        .read_data(read_data),
        .wb_rf_we(wb_rf_we),
        .wb_rd(wb_rd),
        .wb_value(wb_value)
    );
endmodule

// The testbench: it resets pcpu, runs the program chosen by PROGRAM until it stores to 0x7fc, and checks the results it
// left against the program's expected image.
module pcpu_tb;
    // Which program it runs: SUM, FIBONACCI or ISA (rv32i.svh). In the body, not a #( ) list, where a project's module
    // sees what it includes: a simulator's command line sets it all the same, as -P does, and so does an instantiation.
    parameter rv_program_t PROGRAM = SUM;

    // The program's images. Their names are of one length, so a ?: between them is a file name in every simulator:
    // Icarus pads a shorter one with a NUL it does not strip.
    localparam IMAGE    = PROGRAM == SUM ? ""rv_sum.hex"" : PROGRAM == FIBONACCI ? ""rv_fib.hex"" : ""rv_isa.hex"";
    localparam EXPECTED = PROGRAM == SUM ? ""rv_sum_expected.hex"" : PROGRAM == FIBONACCI ? ""rv_fib_expected.hex""
                                         : ""rv_isa_expected.hex"";

    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    pcpu #(.PROGRAM(IMAGE), .DATA(""rv_data.hex"")) dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(EXPECTED, expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; every result is in memory by then.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            case (PROGRAM)
                SUM:
                    $display(""the sum of 1 to 10: %0d"", dut.dmem.mem[64]);
                FIBONACCI: begin
                    $write(""ten Fibonacci numbers:"");
                    for (i = 0; i < 10; i++)
                        $write("" %0d"", dut.dmem.mem[65 + i]);
                    $display;
                end
                default:
                    $display(""every instruction once: %0d of 41 checks held"", dut.dmem.mem[126]);
            endcase

            for (i = 0; i < 64; i++)
                if (dut.dmem.mem[64 + i] !== expected[i]) begin
                    $display(""FAIL: the word at %h is %h, not %h"", 32'h100 + 4 * i, dut.dmem.mem[64 + i], expected[i]);
                    failures++;
                end

            if (failures == 0)
                $display(""PASS: every result as expected"");
            else
                $display(""FAIL: %0d results wrong"", failures);
            $finish;
        end

    initial begin
        #100000;
        $display(""TIMEOUT: the program never said it was done"");
        $finish;
    end
endmodule
";

        private const string Defs = @"// The numbers the RV32I processors share -- mcpu's, scpu's and pcpu's: the opcodes they run and
// their ALU's operations. Included once, outside every module, so each sees them. Written for
// BitSorter's oracle from the published RISC-V specification; not taken from any course material.

`ifndef RV32I_SVH
`define RV32I_SVH

// The programs a processor's testbench can run, its PROGRAM (Part 15h-6c): the sum of 1 to 10, the first ten
// Fibonacci numbers, or the instruction set -- ISA, every instruction once -- checking itself.
typedef enum {SUM, FIBONACCI, ISA} rv_program_t;

localparam logic [6:0] OP_LUI    = 7'b0110111;
localparam logic [6:0] OP_AUIPC  = 7'b0010111;
localparam logic [6:0] OP_JAL    = 7'b1101111;
localparam logic [6:0] OP_JALR   = 7'b1100111;
localparam logic [6:0] OP_BRANCH = 7'b1100011;
localparam logic [6:0] OP_LOAD   = 7'b0000011;
localparam logic [6:0] OP_STORE  = 7'b0100011;
localparam logic [6:0] OP_IMM    = 7'b0010011;
localparam logic [6:0] OP_REG    = 7'b0110011;

localparam logic [3:0] ALU_ADD   = 4'd0;
localparam logic [3:0] ALU_SUB   = 4'd1;
localparam logic [3:0] ALU_AND   = 4'd2;
localparam logic [3:0] ALU_OR    = 4'd3;
localparam logic [3:0] ALU_XOR   = 4'd4;
localparam logic [3:0] ALU_SLL   = 4'd5;
localparam logic [3:0] ALU_SRL   = 4'd6;
localparam logic [3:0] ALU_SRA   = 4'd7;
localparam logic [3:0] ALU_SLT   = 4'd8;
localparam logic [3:0] ALU_SLTU  = 4'd9;
localparam logic [3:0] ALU_PASSB = 4'd10;

`endif
";

        private const string SumProgram = @"// rv_sum, one of the three programs BitSorter's RV32I processors run: the sum of 1 to 10, in a
// loop of add, addi and blt, left at 0x100.
// Assembled from the listing beside each word; word n is the instruction at byte address 4n.
// Written for BitSorter's oracle; not taken from any course material.
00000413  // 000: addi s0, zero, 0          1. the sum of 1 to 10
00100293  // 004: addi t0, zero, 1          i
00b00313  // 008: addi t1, zero, 11         one past the last
00540433  // 00c: add s0, s0, t0            <- sum_loop
00128293  // 010: addi t0, t0, 1
fe62cce3  // 014: blt t0, t1, sum_loop
10802023  // 018: sw s0, 0x100(zero)        55
00100293  // 01c: addi t0, zero, 1
7e502e23  // 020: sw t0, 0x7fc(zero)        done: the testbench watches for this store
0000006f  // 024: jal zero, end             and wait (end)
";

        private const string SumExpected = @"// What rv_sum leaves in data words 64 to 127 (bytes 0x100 to 0x1fc): 55 at 0x100, the rest 0.
// Worked out by an emulator written from the RISC-V specification.
// Written for BitSorter's oracle; not taken from any course material.
00000037  // 100
00000000  // 104
00000000  // 108
00000000  // 10c
00000000  // 110
00000000  // 114
00000000  // 118
00000000  // 11c
00000000  // 120
00000000  // 124
00000000  // 128
00000000  // 12c
00000000  // 130
00000000  // 134
00000000  // 138
00000000  // 13c
00000000  // 140
00000000  // 144
00000000  // 148
00000000  // 14c
00000000  // 150
00000000  // 154
00000000  // 158
00000000  // 15c
00000000  // 160
00000000  // 164
00000000  // 168
00000000  // 16c
00000000  // 170
00000000  // 174
00000000  // 178
00000000  // 17c
00000000  // 180
00000000  // 184
00000000  // 188
00000000  // 18c
00000000  // 190
00000000  // 194
00000000  // 198
00000000  // 19c
00000000  // 1a0
00000000  // 1a4
00000000  // 1a8
00000000  // 1ac
00000000  // 1b0
00000000  // 1b4
00000000  // 1b8
00000000  // 1bc
00000000  // 1c0
00000000  // 1c4
00000000  // 1c8
00000000  // 1cc
00000000  // 1d0
00000000  // 1d4
00000000  // 1d8
00000000  // 1dc
00000000  // 1e0
00000000  // 1e4
00000000  // 1e8
00000000  // 1ec
00000000  // 1f0
00000000  // 1f4
00000000  // 1f8
00000000  // 1fc
";

        private const string FibProgram = @"// rv_fib, one of the three programs BitSorter's RV32I processors run: the first ten Fibonacci
// numbers, each loaded back from the two before it, left from 0x104 -- a load used at once.
// Assembled from the listing beside each word; word n is the instruction at byte address 4n.
// Written for BitSorter's oracle; not taken from any course material.
10400513  // 000: addi a0, zero, 0x104      2. ten Fibonacci numbers, from 0x104
00a00593  // 004: addi a1, zero, 10
010000ef  // 008: jal ra, fib
00100293  // 00c: addi t0, zero, 1
7e502e23  // 010: sw t0, 0x7fc(zero)        done: the testbench watches for this store
0000006f  // 014: jal zero, end             and wait (end)
00052023  // 018: sw zero, 0(a0)            F0 = 0 (fib)
00100293  // 01c: addi t0, zero, 1
00552223  // 020: sw t0, 4(a0)              F1 = 1
00850393  // 024: addi t2, a0, 8            the next word
00259e13  // 028: slli t3, a1, 2
01c50e33  // 02c: add t3, a0, t3            one past the last
ff83ae83  // 030: lw t4, -8(t2)             the two before it, from memory (fib_loop)
ffc3af03  // 034: lw t5, -4(t2)
01ee8fb3  // 038: add t6, t4, t5            needs the load just before it
01f3a023  // 03c: sw t6, 0(t2)
00438393  // 040: addi t2, t2, 4
ffc3e6e3  // 044: bltu t2, t3, fib_loop
00008067  // 048: jalr zero, 0(ra)          return
";

        private const string FibExpected = @"// What rv_fib leaves in data words 64 to 127 (bytes 0x100 to 0x1fc): the first ten Fibonacci
// numbers from 0x104, the rest 0.
// Worked out by an emulator written from the RISC-V specification.
// Written for BitSorter's oracle; not taken from any course material.
00000000  // 100
00000000  // 104
00000001  // 108
00000001  // 10c
00000002  // 110
00000003  // 114
00000005  // 118
00000008  // 11c
0000000d  // 120
00000015  // 124
00000022  // 128
00000000  // 12c
00000000  // 130
00000000  // 134
00000000  // 138
00000000  // 13c
00000000  // 140
00000000  // 144
00000000  // 148
00000000  // 14c
00000000  // 150
00000000  // 154
00000000  // 158
00000000  // 15c
00000000  // 160
00000000  // 164
00000000  // 168
00000000  // 16c
00000000  // 170
00000000  // 174
00000000  // 178
00000000  // 17c
00000000  // 180
00000000  // 184
00000000  // 188
00000000  // 18c
00000000  // 190
00000000  // 194
00000000  // 198
00000000  // 19c
00000000  // 1a0
00000000  // 1a4
00000000  // 1a8
00000000  // 1ac
00000000  // 1b0
00000000  // 1b4
00000000  // 1b8
00000000  // 1bc
00000000  // 1c0
00000000  // 1c4
00000000  // 1c8
00000000  // 1cc
00000000  // 1d0
00000000  // 1d4
00000000  // 1d8
00000000  // 1dc
00000000  // 1e0
00000000  // 1e4
00000000  // 1e8
00000000  // 1ec
00000000  // 1f0
00000000  // 1f4
00000000  // 1f8
00000000  // 1fc
";

        private const string TestProgram = @"// rv_isa, one of the three programs BitSorter's RV32I processors run: every instruction they run
// once -- each branch both ways -- checking itself, the results from 0x140 and at 0x1f8 how many
// of its checks held.
// Assembled from the listing beside each word; word n is the instruction at byte address 4n.
// Written for BitSorter's oracle; not taken from any course material.
014000ef  // 000: jal ra, every             3. every instruction once, from 0x140
1fb02c23  // 004: sw s11, 0x1f8(zero)       how many of the checks held
00100293  // 008: addi t0, zero, 1
7e502e23  // 00c: sw t0, 0x7fc(zero)        done: the testbench watches for this store
0000006f  // 010: jal zero, end             and wait (end)
14000493  // 014: addi s1, zero, 0x140      where the results go (every)
00000d93  // 018: addi s11, zero, 0         checks held
f0f01637  // 01c: lui a2, 0xf0f01           a negative word
23460613  // 020: addi a2, a2, 564
0a500693  // 024: addi a3, zero, 165        a small positive one
00500713  // 028: addi a4, zero, 5          a shift
abcde2b7  // 02c: lui t0, 0xabcde
0054a023  // 030: sw t0, 0(s1)              lui
00448493  // 034: addi s1, s1, 4
abcdefb7  // 038: lui t6, 0xabcde
01f29463  // 03c: bne t0, t6, skip1
001d8d93  // 040: addi s11, s11, 1
00001297  // 044: auipc t0, 0x1             <- here
0054a023  // 048: sw t0, 0(s1)              auipc
00448493  // 04c: addi s1, s1, 4
00001fb7  // 050: lui t6, 0x1
044f8f93  // 054: addi t6, t6, 68
01f29463  // 058: bne t0, t6, skip2
001d8d93  // 05c: addi s11, s11, 1
00d602b3  // 060: add t0, a2, a3
0054a023  // 064: sw t0, 0(s1)              add
00448493  // 068: addi s1, s1, 4
f0f01fb7  // 06c: lui t6, 0xf0f01
2d9f8f93  // 070: addi t6, t6, 729
01f29463  // 074: bne t0, t6, skip3
001d8d93  // 078: addi s11, s11, 1
40d602b3  // 07c: sub t0, a2, a3
0054a023  // 080: sw t0, 0(s1)              sub
00448493  // 084: addi s1, s1, 4
f0f01fb7  // 088: lui t6, 0xf0f01
18ff8f93  // 08c: addi t6, t6, 399
01f29463  // 090: bne t0, t6, skip4
001d8d93  // 094: addi s11, s11, 1
00e692b3  // 098: sll t0, a3, a4
0054a023  // 09c: sw t0, 0(s1)              sll
00448493  // 0a0: addi s1, s1, 4
00001fb7  // 0a4: lui t6, 0x1
4a0f8f93  // 0a8: addi t6, t6, 1184
01f29463  // 0ac: bne t0, t6, skip5
001d8d93  // 0b0: addi s11, s11, 1
00d622b3  // 0b4: slt t0, a2, a3
0054a023  // 0b8: sw t0, 0(s1)              slt: the negative one is less
00448493  // 0bc: addi s1, s1, 4
00100f93  // 0c0: addi t6, zero, 1
01f29463  // 0c4: bne t0, t6, skip6
001d8d93  // 0c8: addi s11, s11, 1
00d632b3  // 0cc: sltu t0, a2, a3
0054a023  // 0d0: sw t0, 0(s1)              sltu: but not without its sign
00448493  // 0d4: addi s1, s1, 4
00000f93  // 0d8: addi t6, zero, 0
01f29463  // 0dc: bne t0, t6, skip7
001d8d93  // 0e0: addi s11, s11, 1
00d642b3  // 0e4: xor t0, a2, a3
0054a023  // 0e8: sw t0, 0(s1)              xor
00448493  // 0ec: addi s1, s1, 4
f0f01fb7  // 0f0: lui t6, 0xf0f01
291f8f93  // 0f4: addi t6, t6, 657
01f29463  // 0f8: bne t0, t6, skip8
001d8d93  // 0fc: addi s11, s11, 1
00e652b3  // 100: srl t0, a2, a4
0054a023  // 104: sw t0, 0(s1)              srl
00448493  // 108: addi s1, s1, 4
07878fb7  // 10c: lui t6, 0x7878
091f8f93  // 110: addi t6, t6, 145
01f29463  // 114: bne t0, t6, skip9
001d8d93  // 118: addi s11, s11, 1
40e652b3  // 11c: sra t0, a2, a4
0054a023  // 120: sw t0, 0(s1)              sra
00448493  // 124: addi s1, s1, 4
ff878fb7  // 128: lui t6, 0xff878
091f8f93  // 12c: addi t6, t6, 145
01f29463  // 130: bne t0, t6, skip10
001d8d93  // 134: addi s11, s11, 1
00d662b3  // 138: or t0, a2, a3
0054a023  // 13c: sw t0, 0(s1)              or
00448493  // 140: addi s1, s1, 4
f0f01fb7  // 144: lui t6, 0xf0f01
2b5f8f93  // 148: addi t6, t6, 693
01f29463  // 14c: bne t0, t6, skip11
001d8d93  // 150: addi s11, s11, 1
00d672b3  // 154: and t0, a2, a3
0054a023  // 158: sw t0, 0(s1)              and
00448493  // 15c: addi s1, s1, 4
02400f93  // 160: addi t6, zero, 36
01f29463  // 164: bne t0, t6, skip12
001d8d93  // 168: addi s11, s11, 1
f9c60293  // 16c: addi t0, a2, -100
0054a023  // 170: sw t0, 0(s1)              addi
00448493  // 174: addi s1, s1, 4
f0f01fb7  // 178: lui t6, 0xf0f01
1d0f8f93  // 17c: addi t6, t6, 464
01f29463  // 180: bne t0, t6, skip13
001d8d93  // 184: addi s11, s11, 1
0c86a293  // 188: slti t0, a3, 200
0054a023  // 18c: sw t0, 0(s1)              slti
00448493  // 190: addi s1, s1, 4
00100f93  // 194: addi t6, zero, 1
01f29463  // 198: bne t0, t6, skip14
001d8d93  // 19c: addi s11, s11, 1
0646b293  // 1a0: sltiu t0, a3, 100
0054a023  // 1a4: sw t0, 0(s1)              sltiu
00448493  // 1a8: addi s1, s1, 4
00000f93  // 1ac: addi t6, zero, 0
01f29463  // 1b0: bne t0, t6, skip15
001d8d93  // 1b4: addi s11, s11, 1
fff6c293  // 1b8: xori t0, a3, -1
0054a023  // 1bc: sw t0, 0(s1)              xori: not
00448493  // 1c0: addi s1, s1, 4
f5a00f93  // 1c4: addi t6, zero, -166
01f29463  // 1c8: bne t0, t6, skip16
001d8d93  // 1cc: addi s11, s11, 1
7006e293  // 1d0: ori t0, a3, 0x700
0054a023  // 1d4: sw t0, 0(s1)              ori
00448493  // 1d8: addi s1, s1, 4
7a500f93  // 1dc: addi t6, zero, 1957
01f29463  // 1e0: bne t0, t6, skip17
001d8d93  // 1e4: addi s11, s11, 1
3f067293  // 1e8: andi t0, a2, 0x3f0
0054a023  // 1ec: sw t0, 0(s1)              andi
00448493  // 1f0: addi s1, s1, 4
23000f93  // 1f4: addi t6, zero, 560
01f29463  // 1f8: bne t0, t6, skip18
001d8d93  // 1fc: addi s11, s11, 1
00769293  // 200: slli t0, a3, 7
0054a023  // 204: sw t0, 0(s1)              slli
00448493  // 208: addi s1, s1, 4
00005fb7  // 20c: lui t6, 0x5
280f8f93  // 210: addi t6, t6, 640
01f29463  // 214: bne t0, t6, skip19
001d8d93  // 218: addi s11, s11, 1
00965293  // 21c: srli t0, a2, 9
0054a023  // 220: sw t0, 0(s1)              srli
00448493  // 224: addi s1, s1, 4
00788fb7  // 228: lui t6, 0x788
809f8f93  // 22c: addi t6, t6, -2039
01f29463  // 230: bne t0, t6, skip20
001d8d93  // 234: addi s11, s11, 1
40965293  // 238: srai t0, a2, 9
0054a023  // 23c: sw t0, 0(s1)              srai
00448493  // 240: addi s1, s1, 4
fff88fb7  // 244: lui t6, 0xfff88
809f8f93  // 248: addi t6, t6, -2039
01f29463  // 24c: bne t0, t6, skip21
001d8d93  // 250: addi s11, s11, 1
00002283  // 254: lw t0, 0(zero)
0054a023  // 258: sw t0, 0(s1)              lw
00448493  // 25c: addi s1, s1, 4
5a5a1fb7  // 260: lui t6, 0x5a5a1
f0ff8f93  // 264: addi t6, t6, -241
01f29463  // 268: bne t0, t6, skip22
001d8d93  // 26c: addi s11, s11, 1
1ec02823  // 270: sw a2, 0x1f0(zero)
1f002283  // 274: lw t0, 0x1f0(zero)        read back at once
0054a023  // 278: sw t0, 0(s1)              sw
00448493  // 27c: addi s1, s1, 4
f0f01fb7  // 280: lui t6, 0xf0f01
234f8f93  // 284: addi t6, t6, 564
01f29463  // 288: bne t0, t6, skip23
001d8d93  // 28c: addi s11, s11, 1
00000293  // 290: addi t0, zero, 0
00d68463  // 294: beq a3, a3, over_beq_a3_a3  taken
00128293  // 298: addi t0, t0, 1            skipped when taken
00228293  // 29c: addi t0, t0, 2            <- over_beq_a3_a3
0054a023  // 2a0: sw t0, 0(s1)              beq, taken
00448493  // 2a4: addi s1, s1, 4
00200f93  // 2a8: addi t6, zero, 2
01f29463  // 2ac: bne t0, t6, skip24
001d8d93  // 2b0: addi s11, s11, 1
00000293  // 2b4: addi t0, zero, 0
00d60463  // 2b8: beq a2, a3, over_beq_a2_a3  not taken
00128293  // 2bc: addi t0, t0, 1            skipped when taken
00228293  // 2c0: addi t0, t0, 2            <- over_beq_a2_a3
0054a023  // 2c4: sw t0, 0(s1)              beq, not taken
00448493  // 2c8: addi s1, s1, 4
00300f93  // 2cc: addi t6, zero, 3
01f29463  // 2d0: bne t0, t6, skip25
001d8d93  // 2d4: addi s11, s11, 1
00000293  // 2d8: addi t0, zero, 0
00d69463  // 2dc: bne a3, a3, over_bne_a3_a3  not taken
00128293  // 2e0: addi t0, t0, 1            skipped when taken
00228293  // 2e4: addi t0, t0, 2            <- over_bne_a3_a3
0054a023  // 2e8: sw t0, 0(s1)              bne, not taken
00448493  // 2ec: addi s1, s1, 4
00300f93  // 2f0: addi t6, zero, 3
01f29463  // 2f4: bne t0, t6, skip26
001d8d93  // 2f8: addi s11, s11, 1
00000293  // 2fc: addi t0, zero, 0
00d61463  // 300: bne a2, a3, over_bne_a2_a3  taken
00128293  // 304: addi t0, t0, 1            skipped when taken
00228293  // 308: addi t0, t0, 2            <- over_bne_a2_a3
0054a023  // 30c: sw t0, 0(s1)              bne, taken
00448493  // 310: addi s1, s1, 4
00200f93  // 314: addi t6, zero, 2
01f29463  // 318: bne t0, t6, skip27
001d8d93  // 31c: addi s11, s11, 1
00000293  // 320: addi t0, zero, 0
00d64463  // 324: blt a2, a3, over_blt_a2_a3  taken
00128293  // 328: addi t0, t0, 1            skipped when taken
00228293  // 32c: addi t0, t0, 2            <- over_blt_a2_a3
0054a023  // 330: sw t0, 0(s1)              blt, taken
00448493  // 334: addi s1, s1, 4
00200f93  // 338: addi t6, zero, 2
01f29463  // 33c: bne t0, t6, skip28
001d8d93  // 340: addi s11, s11, 1
00000293  // 344: addi t0, zero, 0
00c6c463  // 348: blt a3, a2, over_blt_a3_a2  not taken
00128293  // 34c: addi t0, t0, 1            skipped when taken
00228293  // 350: addi t0, t0, 2            <- over_blt_a3_a2
0054a023  // 354: sw t0, 0(s1)              blt, not taken
00448493  // 358: addi s1, s1, 4
00300f93  // 35c: addi t6, zero, 3
01f29463  // 360: bne t0, t6, skip29
001d8d93  // 364: addi s11, s11, 1
00000293  // 368: addi t0, zero, 0
00d65463  // 36c: bge a2, a3, over_bge_a2_a3  not taken
00128293  // 370: addi t0, t0, 1            skipped when taken
00228293  // 374: addi t0, t0, 2            <- over_bge_a2_a3
0054a023  // 378: sw t0, 0(s1)              bge, not taken
00448493  // 37c: addi s1, s1, 4
00300f93  // 380: addi t6, zero, 3
01f29463  // 384: bne t0, t6, skip30
001d8d93  // 388: addi s11, s11, 1
00000293  // 38c: addi t0, zero, 0
00c6d463  // 390: bge a3, a2, over_bge_a3_a2  taken
00128293  // 394: addi t0, t0, 1            skipped when taken
00228293  // 398: addi t0, t0, 2            <- over_bge_a3_a2
0054a023  // 39c: sw t0, 0(s1)              bge, taken
00448493  // 3a0: addi s1, s1, 4
00200f93  // 3a4: addi t6, zero, 2
01f29463  // 3a8: bne t0, t6, skip31
001d8d93  // 3ac: addi s11, s11, 1
00000293  // 3b0: addi t0, zero, 0
00d66463  // 3b4: bltu a2, a3, over_bltu_a2_a3  not taken
00128293  // 3b8: addi t0, t0, 1            skipped when taken
00228293  // 3bc: addi t0, t0, 2            <- over_bltu_a2_a3
0054a023  // 3c0: sw t0, 0(s1)              bltu, not taken
00448493  // 3c4: addi s1, s1, 4
00300f93  // 3c8: addi t6, zero, 3
01f29463  // 3cc: bne t0, t6, skip32
001d8d93  // 3d0: addi s11, s11, 1
00000293  // 3d4: addi t0, zero, 0
00c6e463  // 3d8: bltu a3, a2, over_bltu_a3_a2  taken
00128293  // 3dc: addi t0, t0, 1            skipped when taken
00228293  // 3e0: addi t0, t0, 2            <- over_bltu_a3_a2
0054a023  // 3e4: sw t0, 0(s1)              bltu, taken
00448493  // 3e8: addi s1, s1, 4
00200f93  // 3ec: addi t6, zero, 2
01f29463  // 3f0: bne t0, t6, skip33
001d8d93  // 3f4: addi s11, s11, 1
00000293  // 3f8: addi t0, zero, 0
00d67463  // 3fc: bgeu a2, a3, over_bgeu_a2_a3  taken
00128293  // 400: addi t0, t0, 1            skipped when taken
00228293  // 404: addi t0, t0, 2            <- over_bgeu_a2_a3
0054a023  // 408: sw t0, 0(s1)              bgeu, taken
00448493  // 40c: addi s1, s1, 4
00200f93  // 410: addi t6, zero, 2
01f29463  // 414: bne t0, t6, skip34
001d8d93  // 418: addi s11, s11, 1
00000293  // 41c: addi t0, zero, 0
00c6f463  // 420: bgeu a3, a2, over_bgeu_a3_a2  not taken
00128293  // 424: addi t0, t0, 1            skipped when taken
00228293  // 428: addi t0, t0, 2            <- over_bgeu_a3_a2
0054a023  // 42c: sw t0, 0(s1)              bgeu, not taken
00448493  // 430: addi s1, s1, 4
00300f93  // 434: addi t6, zero, 3
01f29463  // 438: bne t0, t6, skip35
001d8d93  // 43c: addi s11, s11, 1
00002283  // 440: lw t0, 0(zero)
00128313  // 444: addi t1, t0, 1            the word just loaded, at once
0064a023  // 448: sw t1, 0(s1)              lw, then used at once
00448493  // 44c: addi s1, s1, 4
5a5a1fb7  // 450: lui t6, 0x5a5a1
f10f8f93  // 454: addi t6, t6, -240
01f31463  // 458: bne t1, t6, skip36
001d8d93  // 45c: addi s11, s11, 1
00000293  // 460: addi t0, zero, 0
1f002383  // 464: lw t2, 0x1f0(zero)
00c38463  // 468: beq t2, a2, over_loaded   compares the word just loaded
00128293  // 46c: addi t0, t0, 1            skipped
00228293  // 470: addi t0, t0, 2            <- over_loaded
0054a023  // 474: sw t0, 0(s1)              a branch on a word just loaded
00448493  // 478: addi s1, s1, 4
00200f93  // 47c: addi t6, zero, 2
01f29463  // 480: bne t0, t6, skip37
001d8d93  // 484: addi s11, s11, 1
00000293  // 488: addi t0, zero, 0
1f002383  // 48c: lw t2, 0x1f0(zero)
00c39463  // 490: bne t2, a2, over_unequal  not taken: the word just loaded is a2
00128293  // 494: addi t0, t0, 1
00228293  // 498: addi t0, t0, 2            <- over_unequal
0054a023  // 49c: sw t0, 0(s1)              a branch on a word just loaded, not taken
00448493  // 4a0: addi s1, s1, 4
00300f93  // 4a4: addi t6, zero, 3
01f29463  // 4a8: bne t0, t6, skip38
001d8d93  // 4ac: addi s11, s11, 1
0080036f  // 4b0: jal t1, after_jal         <- the_jal
f9cd8d93  // 4b4: addi s11, s11, -100       never runs
0064a023  // 4b8: sw t1, 0(s1)              jal links (after_jal)
00448493  // 4bc: addi s1, s1, 4
00000fb7  // 4c0: lui t6, 0x0
4b4f8f93  // 4c4: addi t6, t6, 1204
01f31463  // 4c8: bne t1, t6, skip39
001d8d93  // 4cc: addi s11, s11, 1
00000397  // 4d0: auipc t2, 0               <- the_auipc
01138367  // 4d4: jalr t1, 17(t2)           an odd target: bit 0 cleared
f9cd8d93  // 4d8: addi s11, s11, -100       never runs
f9cd8d93  // 4dc: addi s11, s11, -100       never runs
00000e17  // 4e0: auipc t3, 0               where it landed, which must be even (landed)
0064a023  // 4e4: sw t1, 0(s1)              jalr links
00448493  // 4e8: addi s1, s1, 4
00000fb7  // 4ec: lui t6, 0x0
4d8f8f93  // 4f0: addi t6, t6, 1240
01f31463  // 4f4: bne t1, t6, skip40
001d8d93  // 4f8: addi s11, s11, 1
01c4a023  // 4fc: sw t3, 0(s1)              jalr lands with bit 0 cleared
00448493  // 500: addi s1, s1, 4
00000fb7  // 504: lui t6, 0x0
4e0f8f93  // 508: addi t6, t6, 1248
01fe1463  // 50c: bne t3, t6, skip41
001d8d93  // 510: addi s11, s11, 1
00008067  // 514: jalr zero, 0(ra)          return
";

        private const string TestExpected = @"// What rv_isa leaves in data words 64 to 127 (bytes 0x100 to 0x1fc): 41 results of every
// instruction the processors run from 0x140, the word it stored to 0x1f0, and at 0x1f8 how many of
// its checks held.
// Worked out by an emulator written from the RISC-V specification.
// Written for BitSorter's oracle; not taken from any course material.
00000000  // 100
00000000  // 104
00000000  // 108
00000000  // 10c
00000000  // 110
00000000  // 114
00000000  // 118
00000000  // 11c
00000000  // 120
00000000  // 124
00000000  // 128
00000000  // 12c
00000000  // 130
00000000  // 134
00000000  // 138
00000000  // 13c
abcde000  // 140
00001044  // 144
f0f012d9  // 148
f0f0118f  // 14c
000014a0  // 150
00000001  // 154
00000000  // 158
f0f01291  // 15c
07878091  // 160
ff878091  // 164
f0f012b5  // 168
00000024  // 16c
f0f011d0  // 170
00000001  // 174
00000000  // 178
ffffff5a  // 17c
000007a5  // 180
00000230  // 184
00005280  // 188
00787809  // 18c
fff87809  // 190
5a5a0f0f  // 194
f0f01234  // 198
00000002  // 19c
00000003  // 1a0
00000003  // 1a4
00000002  // 1a8
00000002  // 1ac
00000003  // 1b0
00000003  // 1b4
00000002  // 1b8
00000003  // 1bc
00000002  // 1c0
00000002  // 1c4
00000003  // 1c8
5a5a0f10  // 1cc
00000002  // 1d0
00000003  // 1d4
000004b4  // 1d8
000004d8  // 1dc
000004e0  // 1e0
00000000  // 1e4
00000000  // 1e8
00000000  // 1ec
f0f01234  // 1f0
00000000  // 1f4
00000029  // 1f8
00000000  // 1fc
";

        private const string Data = @"// The data BitSorter's RV32I programs start from: word 0 for the test's lw, and words 1 to 127
// cleared, so every result word starts at 0.
// Written for BitSorter's oracle; not taken from any course material.
5a5a0f0f
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
00000000
";
    }
}
