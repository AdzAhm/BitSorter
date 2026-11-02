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
            foreach ((string File, string Text) image in Files)
                if (image.File.EndsWith(".hex", StringComparison.Ordinal))
                    sources.Add(new SourceFile(image.File, image.Text));
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

        private const string Scpu = @"// scpu: an original single-cycle processor -- each instruction fetched, decoded, executed, given its
// memory access and written back within one clock cycle, so the clock has to be as slow as the slowest
// instruction, a load. A decoder that is combinational logic, a datapath with the PC as its only
// register outside the register file, and separate instruction and data memories of 1,024 words each,
// loaded from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal,
// jalr, every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by
// immediate. No bytes or halfwords, no system instructions. Its datapath is drawn as the textbook draws one: the PC,
// the register file, an immediate generator and a branch comparator as blocks of their own, the ALU, and a mux for
// each choice -- the next PC, the ALU's a and b, what is written back. scpu_tb runs one of three programs on rv_data.hex
// -- its PROGRAM: rv_sum.hex, rv_fib.hex or rv_isa.hex -- until the program stores to 0x7fc, then checks its
// results against the program's own expected image. Written for
// BitSorter's oracle from the published RISC-V specification; not taken from any course material.

`include ""rv32i.svh""

module scpu_alu (
    input  logic [31:0] a,
    input  logic [31:0] b,
    input  logic [3:0]  op,     // one of the ALU_ operations
    output logic [31:0] y
);
    always_comb begin
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

module scpu_regfile (
    input  logic        clk,
    input  logic        we,     // write wd to register wa on the rising edge
    input  logic [4:0]  ra1,
    input  logic [4:0]  ra2,
    input  logic [4:0]  wa,
    input  logic [31:0] wd,
    output logic [31:0] rd1,
    output logic [31:0] rd2
);
    logic [31:0] regs [0:31];

    always_ff @(posedge clk)
        if (we && wa != 5'd0) regs[wa] <= wd;

    // x0 reads 0 whatever is written to it.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : regs[ra1];
    assign rd2 = ra2 == 5'd0 ? 32'd0 : regs[ra2];
endmodule

module scpu_memory #(
    parameter IMAGE = ""rv_sum.hex""   // the image loaded at time 0
) (
    input  logic        clk,
    input  logic        we,       // write wdata on the rising edge
    input  logic [31:0] addr,     // a byte address; words are aligned
    input  logic [31:0] wdata,
    output logic [31:0] rdata
);
    logic [31:0] mem [0:1023];

    initial $readmemh(IMAGE, mem);

    always_ff @(posedge clk)
        if (we) mem[addr[11:2]] <= wdata;

    assign rdata = mem[addr[11:2]];
endmodule

// The control logic: what the instruction asks of the datapath, worked out from its opcode and funct fields alone --
// and, for a branch, from the comparison the datapath makes of its two registers.
module scpu_control (
    input  logic [6:0] opcode,
    input  logic [2:0] funct3,
    input  logic       funct7_5,    // instruction bit 30: sub and sra
    input  logic       taken,       // the branch comparator: the condition holds
    output logic [1:0] pc_src,      // the next PC: 0 the PC and four, 1 the PC and the immediate, 2 the ALU, bit 0 cleared
    output logic       rf_we,
    output logic       mem_we,
    output logic       alu_a_pc,    // the ALU's a: 0 the register, 1 the PC
    output logic       alu_b_imm,   // the ALU's b: 0 the register, 1 the immediate
    output logic [3:0] alu_op,
    output logic [1:0] wb_src       // what is written back: 0 the ALU, 1 the data memory, 2 the PC and four
);
    logic [3:0] alu_function;

    // What the instruction's funct fields ask of the ALU.
    always_comb begin
        case (funct3)
            3'b000:  alu_function = opcode == OP_REG && funct7_5 ? ALU_SUB : ALU_ADD;
            3'b001:  alu_function = ALU_SLL;
            3'b010:  alu_function = ALU_SLT;
            3'b011:  alu_function = ALU_SLTU;
            3'b100:  alu_function = ALU_XOR;
            3'b101:  alu_function = funct7_5 ? ALU_SRA : ALU_SRL;
            3'b110:  alu_function = ALU_OR;
            default: alu_function = ALU_AND;
        endcase
    end

    always_comb begin
        pc_src = 2'd0;
        rf_we = 1'b0;
        mem_we = 1'b0;
        alu_a_pc = 1'b0;
        alu_b_imm = 1'b0;
        alu_op = ALU_ADD;
        wb_src = 2'd0;

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
    input  logic [31:0] instr,
    output logic [31:0] imm
);
    always_comb begin
        case (instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            default:                  imm = 32'd0;
        endcase
    end
endmodule

// The branch comparator: whether a branch's condition holds of its two registers, by funct3.
module scpu_branch (
    input  logic [31:0] a,
    input  logic [31:0] b,
    input  logic [2:0]  funct3,
    output logic        taken
);
    always_comb begin
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
    input  logic        clk,
    input  logic        rst,          // asynchronous, active high: the PC to 0
    input  logic [1:0]  pc_src,
    input  logic        rf_we,
    input  logic        alu_a_pc,
    input  logic        alu_b_imm,
    input  logic [3:0]  alu_op,
    input  logic [1:0]  wb_src,
    input  logic [31:0] instr,        // from the instruction memory
    input  logic [31:0] read_data,    // from the data memory
    output logic [31:0] pc,
    output logic [31:0] data_addr,
    output logic [31:0] write_data,
    output logic [6:0]  opcode,
    output logic [2:0]  funct3,
    output logic        funct7_5,
    output logic        taken
);
    logic [31:0] rd1, rd2, imm, alu_a, alu_b, alu_y, pc_plus4, target, pc_next, wb;

    assign opcode = instr[6:0];
    assign funct3 = instr[14:12];
    assign funct7_5 = instr[30];
    assign data_addr = alu_y;
    assign write_data = rd2;

    always_ff @(posedge clk, posedge rst)
        if (rst) pc <= 32'd0;
        else pc <= pc_next;

    assign pc_plus4 = pc + 32'd4;
    assign target = pc + imm;

    always_comb
        case (pc_src)
            2'd1:    pc_next = target;
            2'd2:    pc_next = {alu_y[31:1], 1'b0};
            default: pc_next = pc_plus4;
        endcase

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

    scpu_immgen immgen (.instr(instr), .imm(imm));

    scpu_branch compare (.a(rd1), .b(rd2), .funct3(funct3), .taken(taken));

    assign alu_a = alu_a_pc ? pc : rd1;
    assign alu_b = alu_b_imm ? imm : rd2;

    scpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    always_comb
        case (wb_src)
            2'd1:    wb = read_data;
            2'd2:    wb = pc_plus4;
            default: wb = alu_y;
        endcase
endmodule

module scpu #(
    parameter PROGRAM = ""rv_sum.hex"",   // the instruction memory's image
    parameter DATA    = ""rv_data.hex""       // the data memory's image
) (
    input  logic clk,
    input  logic rst      // asynchronous, active high
);
    logic        rf_we, mem_we, alu_a_pc, alu_b_imm, funct7_5, taken;
    logic [1:0]  pc_src, wb_src;
    logic [3:0]  alu_op;
    logic [6:0]  opcode;
    logic [2:0]  funct3;
    logic [31:0] pc, instr, data_addr, write_data, read_data;

    scpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    scpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we),
        .addr(data_addr),
        .wdata(write_data),
        .rdata(read_data)
    );

    scpu_control control (.*);

    scpu_datapath datapath (.*);
endmodule

module scpu_tb;
    // Which program it runs: SUM, FIBONACCI or TEST (rv32i.svh). In the body, not a #( ) list, where a project's module
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

        private const string Mcpu = @"// mcpu: an original multicycle processor in the shape of the course's third simulation exercise -- a
// controller that is a state machine, a datapath with registers between the steps, and separate
// instruction and data memories of 1,024 words each, loaded from images with $readmemh. It runs the
// RV32I instructions a program needs: lui, auipc, jal, jalr, every branch, lw and sw, and every
// arithmetic, logic and shift operation, by register and by immediate. No bytes or halfwords, no
// system instructions. mcpu_tb runs mcpu_program.hex on mcpu_data.hex until the program stores to
// 0x7fc, then checks its results against mcpu_expected.hex. Written for BitSorter's oracle from the
// published RISC-V specification; not taken from any course material.

`include ""rv32i.svh""

module mcpu_alu (
    input  logic [31:0] a,
    input  logic [31:0] b,
    input  logic [3:0]  op,     // one of the ALU_ operations
    output logic [31:0] y
);
    always_comb begin
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

module mcpu_regfile (
    input  logic        clk,
    input  logic        we,     // write wd to register wa on the rising edge
    input  logic [4:0]  ra1,
    input  logic [4:0]  ra2,
    input  logic [4:0]  wa,
    input  logic [31:0] wd,
    output logic [31:0] rd1,
    output logic [31:0] rd2
);
    logic [31:0] regs [0:31];

    always_ff @(posedge clk)
        if (we && wa != 5'd0) regs[wa] <= wd;

    // x0 reads 0 whatever is written to it.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : regs[ra1];
    assign rd2 = ra2 == 5'd0 ? 32'd0 : regs[ra2];
endmodule

module mcpu_memory #(
    parameter IMAGE = ""mcpu_program.hex""   // the image loaded at time 0
) (
    input  logic        clk,
    input  logic        we,       // write wdata on the rising edge
    input  logic [31:0] addr,     // a byte address; words are aligned
    input  logic [31:0] wdata,
    output logic [31:0] rdata
);
    logic [31:0] mem [0:1023];

    initial $readmemh(IMAGE, mem);

    always_ff @(posedge clk)
        if (we) mem[addr[11:2]] <= wdata;

    assign rdata = mem[addr[11:2]];
endmodule

// The immediate generator: the instruction's immediate, its bits gathered and its sign extended as its format says.
module mcpu_immgen (
    input  logic [31:0] instr,
    output logic [31:0] imm
);
    always_comb begin
        case (instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            default:                  imm = 32'd0;
        endcase
    end
endmodule

// The branch comparator: whether a branch's condition holds of its two registers, by funct3.
module mcpu_branch (
    input  logic [31:0] a,
    input  logic [31:0] b,
    input  logic [2:0]  funct3,
    output logic        taken
);
    always_comb begin
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

module mcpu_control (
    input  logic       clk,
    input  logic       rst,         // asynchronous, active high
    input  logic [6:0] opcode,
    input  logic [2:0] funct3,
    input  logic       funct7_5,    // instruction bit 30: sub and sra
    input  logic       taken,       // the branch condition holds
    output logic       pc_we,
    output logic       ir_we,
    output logic       rf_we,
    output logic       mem_we,
    output logic [1:0] alu_a_sel,   // 0 A, 1 PC, 2 the instruction's own PC
    output logic [1:0] alu_b_sel,   // 0 B, 1 the immediate, 2 four
    output logic [3:0] alu_op,
    output logic [1:0] pc_src,      // 0 the ALU, 1 ALUOut, 2 the ALU with bit 0 cleared
    output logic [1:0] wb_src       // 0 ALUOut, 1 MDR, 2 PC
);
    typedef enum logic [3:0] {
        FETCH, DECODE, EXECUTE, ALU_WB, JUMP, JUMP_REG, BRANCH, MEM_ADDR, MEM_READ, MEM_WB, MEM_WRITE
    } state_t;

    state_t state, state_next;
    logic [3:0] alu_function;

    always_ff @(posedge clk, posedge rst)
        if (rst) state <= FETCH;
        else state <= state_next;

    // What the instruction's funct fields ask of the ALU.
    always_comb begin
        case (funct3)
            3'b000:  alu_function = opcode == OP_REG && funct7_5 ? ALU_SUB : ALU_ADD;
            3'b001:  alu_function = ALU_SLL;
            3'b010:  alu_function = ALU_SLT;
            3'b011:  alu_function = ALU_SLTU;
            3'b100:  alu_function = ALU_XOR;
            3'b101:  alu_function = funct7_5 ? ALU_SRA : ALU_SRL;
            3'b110:  alu_function = ALU_OR;
            default: alu_function = ALU_AND;
        endcase
    end

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

        case (state)
            FETCH: begin
                ir_we = 1'b1;
                pc_we = 1'b1;
                alu_a_sel = 2'd1;
                alu_b_sel = 2'd2;
                state_next = DECODE;
            end
            DECODE: begin
                // The target a branch, jal or auipc wants, worked out while the kind is decided.
                alu_a_sel = 2'd2;
                alu_b_sel = 2'd1;
                case (opcode)
                    OP_REG, OP_IMM, OP_LUI: state_next = EXECUTE;
                    OP_AUIPC:               state_next = ALU_WB;
                    OP_JAL:                 state_next = JUMP;
                    OP_JALR:                state_next = JUMP_REG;
                    OP_BRANCH:              state_next = BRANCH;
                    OP_LOAD, OP_STORE:      state_next = MEM_ADDR;
                    default:                state_next = FETCH;
                endcase
            end
            EXECUTE: begin
                alu_b_sel = opcode == OP_REG ? 2'd0 : 2'd1;
                alu_op = opcode == OP_LUI ? ALU_PASSB : alu_function;
                state_next = ALU_WB;
            end
            ALU_WB: begin
                rf_we = 1'b1;
                state_next = FETCH;
            end
            JUMP: begin
                rf_we = 1'b1;
                wb_src = 2'd2;
                pc_we = 1'b1;
                pc_src = 2'd1;
                state_next = FETCH;
            end
            JUMP_REG: begin
                rf_we = 1'b1;
                wb_src = 2'd2;
                alu_b_sel = 2'd1;
                pc_we = 1'b1;
                pc_src = 2'd2;
                state_next = FETCH;
            end
            BRANCH: begin
                pc_we = taken;
                pc_src = 2'd1;
                state_next = FETCH;
            end
            MEM_ADDR: begin
                alu_b_sel = 2'd1;
                if (opcode == OP_LOAD)
                    state_next = MEM_READ;
                else
                    state_next = MEM_WRITE;
            end
            MEM_READ: begin
                alu_b_sel = 2'd1;
                state_next = MEM_WB;
            end
            MEM_WB: begin
                rf_we = 1'b1;
                wb_src = 2'd1;
                state_next = FETCH;
            end
            MEM_WRITE: begin
                alu_b_sel = 2'd1;
                mem_we = 1'b1;
                state_next = FETCH;
            end
            default: state_next = FETCH;
        endcase
    end
endmodule

module mcpu_datapath (
    input  logic        clk,
    input  logic        rst,          // asynchronous, active high: the PC to 0
    input  logic        pc_we,
    input  logic        ir_we,
    input  logic        rf_we,
    input  logic [1:0]  alu_a_sel,
    input  logic [1:0]  alu_b_sel,
    input  logic [3:0]  alu_op,
    input  logic [1:0]  pc_src,
    input  logic [1:0]  wb_src,
    input  logic [31:0] instr,        // from the instruction memory
    input  logic [31:0] read_data,    // from the data memory
    output logic [31:0] pc,
    output logic [31:0] data_addr,
    output logic [31:0] write_data,
    output logic [6:0]  opcode,
    output logic [2:0]  funct3,
    output logic        funct7_5,
    output logic        taken
);
    logic [31:0] ir, old_pc, a, b, alu_out, mdr;
    logic [31:0] rd1, rd2, imm, alu_a, alu_b, alu_y, pc_next, wb;

    assign opcode = ir[6:0];
    assign funct3 = ir[14:12];
    assign funct7_5 = ir[30];
    assign data_addr = alu_out;
    assign write_data = b;

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

    mcpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    always_ff @(posedge clk, posedge rst)
        if (rst) pc <= 32'd0;
        else if (pc_we) pc <= pc_next;

    always_ff @(posedge clk)
        if (ir_we) begin
            ir <= instr;
            old_pc <= pc;
        end

    // The registers between the steps take a new value every cycle.
    always_ff @(posedge clk) begin
        a <= rd1;
        b <= rd2;
        alu_out <= alu_y;
        mdr <= read_data;
    end

    mcpu_immgen immgen (.instr(ir), .imm(imm));

    mcpu_branch compare (.a(a), .b(b), .funct3(funct3), .taken(taken));

    always_comb begin
        case (alu_a_sel)
            2'd1:    alu_a = pc;
            2'd2:    alu_a = old_pc;
            default: alu_a = a;
        endcase

        case (alu_b_sel)
            2'd1:    alu_b = imm;
            2'd2:    alu_b = 32'd4;
            default: alu_b = b;
        endcase

        case (pc_src)
            2'd1:    pc_next = alu_out;
            2'd2:    pc_next = alu_y & ~32'd1;
            default: pc_next = alu_y;
        endcase

        case (wb_src)
            2'd1:    wb = mdr;
            2'd2:    wb = pc;
            default: wb = alu_out;
        endcase
    end
endmodule

module mcpu #(
    parameter PROGRAM = ""mcpu_program.hex"",   // the instruction memory's image
    parameter DATA    = ""mcpu_data.hex""       // the data memory's image
) (
    input  logic clk,
    input  logic rst      // asynchronous, active high
);
    logic        pc_we, ir_we, rf_we, mem_we, funct7_5, taken;
    logic [1:0]  alu_a_sel, alu_b_sel, pc_src, wb_src;
    logic [3:0]  alu_op;
    logic [6:0]  opcode;
    logic [2:0]  funct3;
    logic [31:0] pc, instr, data_addr, write_data, read_data;

    mcpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    mcpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we),
        .addr(data_addr),
        .wdata(write_data),
        .rdata(read_data)
    );

    mcpu_control control (.*);

    mcpu_datapath datapath (.*);
endmodule

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
// every instruction once -- so the three processors can be compared on each: the start page's multicycle sample
// (Part 15h-6, the three programs Part 15h-6c).
module mcpu_demo_tb;
    // Which program it runs: SUM, FIBONACCI or TEST (rv32i.svh). In the body, not a #( ) list, where a project's module
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
// The register file passes a value being written back straight through to a read of the same register
// in that cycle -- the textbook's ""written in the first half, read in the second"" -- so decode never
// reads a register the instruction leaving write-back is about to change. Separate instruction and data memories of 1,024 words each, loaded
// from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal, jalr,
// every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by
// immediate. No bytes or halfwords, no system instructions. pcpu_tb runs one of three programs on rv_data.hex
// -- its PROGRAM: rv_sum.hex, rv_fib.hex or rv_isa.hex -- until the program stores to 0x7fc, then checks its
// results against the program's own expected image. Written for
// BitSorter's oracle from the published RISC-V specification; not taken from any course material.

`include ""rv32i.svh""

module pcpu_alu (
    input  logic [31:0] a,
    input  logic [31:0] b,
    input  logic [3:0]  op,     // one of the ALU_ operations
    output logic [31:0] y
);
    always_comb begin
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

module pcpu_regfile (
    input  logic        clk,
    input  logic        we,     // write wd to register wa on the rising edge
    input  logic [4:0]  ra1,
    input  logic [4:0]  ra2,
    input  logic [4:0]  wa,
    input  logic [31:0] wd,
    output logic [31:0] rd1,
    output logic [31:0] rd2
);
    logic [31:0] regs [0:31];

    always_ff @(posedge clk)
        if (we && wa != 5'd0) regs[wa] <= wd;

    // x0 reads 0 whatever is written to it; a register being written this cycle reads what is being
    // written, passed straight through: the textbook's write in the first half and read in the second.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : we && wa == ra1 ? wd : regs[ra1];
    assign rd2 = ra2 == 5'd0 ? 32'd0 : we && wa == ra2 ? wd : regs[ra2];
endmodule

module pcpu_memory #(
    parameter IMAGE = ""rv_sum.hex""   // the image loaded at time 0
) (
    input  logic        clk,
    input  logic        we,       // write wdata on the rising edge
    input  logic [31:0] addr,     // a byte address; words are aligned
    input  logic [31:0] wdata,
    output logic [31:0] rdata
);
    logic [31:0] mem [0:1023];

    initial $readmemh(IMAGE, mem);

    always_ff @(posedge clk)
        if (we) mem[addr[11:2]] <= wdata;

    assign rdata = mem[addr[11:2]];
endmodule

module pcpu_control (
    input  logic [6:0] opcode,
    input  logic [2:0] funct3,
    input  logic       funct7_5,    // instruction bit 30: sub and sra
    output logic       rf_we,
    output logic       mem_we,
    output logic       mem_read,    // a load: what it writes back comes late, from the data memory
    output logic       alu_a_pc,    // the ALU's a: 0 the register, 1 the PC
    output logic       alu_b_imm,   // the ALU's b: 0 the register, 1 the immediate
    output logic [3:0] alu_op,
    output logic       branch,
    output logic       jump,        // jal: to the PC and the immediate
    output logic       jump_reg,    // jalr: to the ALU's sum, bit 0 cleared
    output logic       link         // jal and jalr write back the PC and four
);
    logic [3:0] alu_function;

    // What the instruction's funct fields ask of the ALU.
    always_comb begin
        case (funct3)
            3'b000:  alu_function = opcode == OP_REG && funct7_5 ? ALU_SUB : ALU_ADD;
            3'b001:  alu_function = ALU_SLL;
            3'b010:  alu_function = ALU_SLT;
            3'b011:  alu_function = ALU_SLTU;
            3'b100:  alu_function = ALU_XOR;
            3'b101:  alu_function = funct7_5 ? ALU_SRA : ALU_SRL;
            3'b110:  alu_function = ALU_OR;
            default: alu_function = ALU_AND;
        endcase
    end

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
    input  logic [31:0] instr,
    output logic [31:0] imm
);
    always_comb begin
        case (instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            default:                  imm = 32'd0;
        endcase
    end
endmodule

// The branch comparator: whether a branch's condition holds of its two registers, by funct3.
module pcpu_branch (
    input  logic [31:0] a,
    input  logic [31:0] b,
    input  logic [2:0]  funct3,
    output logic        taken
);
    always_comb begin
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
    input  logic [4:0] rs1,          // the registers the instruction in execute reads
    input  logic [4:0] rs2,
    input  logic       mem_rf_we,    // the instruction in memory, one ahead
    input  logic [4:0] mem_rd,
    input  logic       wb_rf_we,     // the instruction in write-back, two ahead
    input  logic [4:0] wb_rd,
    output logic [1:0] forward_a,
    output logic [1:0] forward_b
);
    always_comb begin
        if (mem_rf_we && mem_rd != 5'd0 && mem_rd == rs1)
            forward_a = 2'd2;
        else if (wb_rf_we && wb_rd != 5'd0 && wb_rd == rs1)
            forward_a = 2'd1;
        else
            forward_a = 2'd0;

        if (mem_rf_we && mem_rd != 5'd0 && mem_rd == rs2)
            forward_b = 2'd2;
        else if (wb_rf_we && wb_rd != 5'd0 && wb_rd == rs2)
            forward_b = 2'd1;
        else
            forward_b = 2'd0;
    end
endmodule

// A load in execute whose register the instruction in decode reads: its word is not read until the
// memory stage, too late to forward, so decode waits a cycle.
module pcpu_hazard (
    input  logic       ex_mem_read,   // the instruction in execute is a load
    input  logic [4:0] ex_rd,
    input  logic [4:0] id_rs1,        // the registers the instruction in decode reads
    input  logic [4:0] id_rs2,
    output logic       stall
);
    assign stall = ex_mem_read && ex_rd != 5'd0 && (ex_rd == id_rs1 || ex_rd == id_rs2);
endmodule

// ---------------------------------------------------------------- the stages, and the registers between them

// Fetch: the PC, which steps on by four, holds while decode waits, and takes a branch's or a jump's target.
module pcpu_fetch (
    input  logic        clk,
    input  logic        rst,        // asynchronous, active high: the PC to 0
    input  logic        stall,      // decode waits a cycle: the PC holds
    input  logic        redirect,   // a taken branch or a jump in execute
    input  logic [31:0] target,     // where it goes
    output logic [31:0] pc
);
    always_ff @(posedge clk, posedge rst)
        if (rst) pc <= 32'd0;
        else if (redirect) pc <= target;
        else if (!stall) pc <= pc + 32'd4;
endmodule

// Between fetch and decode: the instruction and its PC. A flush makes it a bubble, a stall holds it.
module pcpu_if_id (
    input  logic        clk,
    input  logic        rst,
    input  logic        stall,
    input  logic        flush,
    input  logic [31:0] pc,
    input  logic [31:0] instr,
    output logic [31:0] id_pc,
    output logic [31:0] id_instr    // 0, a bubble: opcode 0 does nothing
);
    always_ff @(posedge clk, posedge rst)
        if (rst) begin
            id_pc <= 32'd0;
            id_instr <= 32'd0;
        end else if (flush) begin
            id_pc <= 32'd0;
            id_instr <= 32'd0;
        end else if (!stall) begin
            id_pc <= pc;
            id_instr <= instr;
        end
endmodule

// Decode: what the instruction asks for, the registers it reads, its immediate, and whether it must wait for a load.
module pcpu_decode (
    input  logic        clk,
    input  logic [31:0] instr,
    input  logic        wb_rf_we,       // write-back writes the register file
    input  logic [4:0]  wb_rd,
    input  logic [31:0] wb_value,
    input  logic        ex_mem_read,    // the instruction in execute, for the load hazard
    input  logic [4:0]  ex_rd,
    output logic        stall,
    output logic        rf_we,
    output logic        mem_we,
    output logic        mem_read,
    output logic        alu_a_pc,
    output logic        alu_b_imm,
    output logic [3:0]  alu_op,
    output logic        branch,
    output logic        jump,
    output logic        jump_reg,
    output logic        link,
    output logic [2:0]  funct3,
    output logic [4:0]  rs1,
    output logic [4:0]  rs2,
    output logic [4:0]  rd,
    output logic [31:0] rd1,
    output logic [31:0] rd2,
    output logic [31:0] imm
);
    assign funct3 = instr[14:12];
    assign rs1 = instr[19:15];
    assign rs2 = instr[24:20];
    assign rd = instr[11:7];

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

    pcpu_hazard hazard (
        .ex_mem_read(ex_mem_read),
        .ex_rd(ex_rd),
        .id_rs1(rs1),
        .id_rs2(rs2),
        .stall(stall)
    );

    pcpu_immgen immgen (.instr(instr), .imm(imm));
endmodule

// Between decode and execute. A stall or a flush lets a bubble through: its controls cleared, so it writes nothing
// and jumps nowhere, whatever its other fields hold.
module pcpu_id_ex (
    input  logic        clk,
    input  logic        rst,
    input  logic        stall,
    input  logic        flush,
    input  logic        rf_we,
    input  logic        mem_we,
    input  logic        mem_read,
    input  logic        alu_a_pc,
    input  logic        alu_b_imm,
    input  logic [3:0]  alu_op,
    input  logic        branch,
    input  logic        jump,
    input  logic        jump_reg,
    input  logic        link,
    input  logic [2:0]  funct3,
    input  logic [4:0]  rs1,
    input  logic [4:0]  rs2,
    input  logic [4:0]  rd,
    input  logic [31:0] pc,
    input  logic [31:0] rd1,
    input  logic [31:0] rd2,
    input  logic [31:0] imm,
    output logic        ex_rf_we,
    output logic        ex_mem_we,
    output logic        ex_mem_read,
    output logic        ex_alu_a_pc,
    output logic        ex_alu_b_imm,
    output logic [3:0]  ex_alu_op,
    output logic        ex_branch,
    output logic        ex_jump,
    output logic        ex_jump_reg,
    output logic        ex_link,
    output logic [2:0]  ex_funct3,
    output logic [4:0]  ex_rs1,
    output logic [4:0]  ex_rs2,
    output logic [4:0]  ex_rd,
    output logic [31:0] ex_pc,
    output logic [31:0] ex_rd1,
    output logic [31:0] ex_rd2,
    output logic [31:0] ex_imm
);
    logic bubble;
    assign bubble = stall || flush;

    always_ff @(posedge clk, posedge rst)
        if (rst) begin
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
            ex_rf_we <= rf_we && !bubble;
            ex_mem_we <= mem_we && !bubble;
            ex_mem_read <= mem_read && !bubble;
            ex_alu_a_pc <= alu_a_pc;
            ex_alu_b_imm <= alu_b_imm;
            ex_alu_op <= alu_op;
            ex_branch <= branch && !bubble;
            ex_jump <= jump && !bubble;
            ex_jump_reg <= jump_reg && !bubble;
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
    input  logic        alu_a_pc,
    input  logic        alu_b_imm,
    input  logic [3:0]  alu_op,
    input  logic        branch,
    input  logic        jump,
    input  logic        jump_reg,
    input  logic        link,
    input  logic [2:0]  funct3,
    input  logic [4:0]  rs1,
    input  logic [4:0]  rs2,
    input  logic [31:0] pc,
    input  logic [31:0] rd1,
    input  logic [31:0] rd2,
    input  logic [31:0] imm,
    input  logic        mem_rf_we,      // the instruction in memory, one ahead
    input  logic [4:0]  mem_rd,
    input  logic [31:0] mem_result,
    input  logic        wb_rf_we,       // the instruction in write-back, two ahead
    input  logic [4:0]  wb_rd,
    input  logic [31:0] wb_value,
    output logic        redirect,       // fetch takes target, and the two instructions behind are flushed
    output logic [31:0] target,
    output logic [31:0] result,         // what the instruction writes back, a load's address or the PC and four
    output logic [31:0] store_data      // what a store writes
);
    logic [1:0]  forward_a, forward_b;
    logic [31:0] src_a, src_b, alu_a, alu_b, alu_y;
    logic        taken;

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
        case (forward_a)
            2'd2:    src_a = mem_result;
            2'd1:    src_a = wb_value;
            default: src_a = rd1;
        endcase
        case (forward_b)
            2'd2:    src_b = mem_result;
            2'd1:    src_b = wb_value;
            default: src_b = rd2;
        endcase
    end

    assign alu_a = alu_a_pc ? pc : src_a;
    assign alu_b = alu_b_imm ? imm : src_b;

    pcpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    pcpu_branch compare (.a(src_a), .b(src_b), .funct3(funct3), .taken(taken));

    assign redirect = jump || jump_reg || (branch && taken);
    assign target = jump_reg ? alu_y & ~32'd1 : pc + imm;
    assign result = link ? pc + 32'd4 : alu_y;
    assign store_data = src_b;
endmodule

// Between execute and memory.
module pcpu_ex_mem (
    input  logic        clk,
    input  logic        rst,
    input  logic        rf_we,
    input  logic        mem_we,
    input  logic        mem_read,
    input  logic [4:0]  rd,
    input  logic [31:0] result,
    input  logic [31:0] store_data,
    output logic        mem_rf_we,
    output logic        mem_mem_we,
    output logic        mem_mem_read,
    output logic [4:0]  mem_rd,
    output logic [31:0] mem_result,
    output logic [31:0] mem_store_data
);
    always_ff @(posedge clk, posedge rst)
        if (rst) begin
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

// Between memory and write-back: what is written back -- the word read for a load, else the result.
module pcpu_mem_wb (
    input  logic        clk,
    input  logic        rst,
    input  logic        rf_we,
    input  logic        mem_read,
    input  logic [4:0]  rd,
    input  logic [31:0] result,
    input  logic [31:0] read_data,
    output logic        wb_rf_we,
    output logic [4:0]  wb_rd,
    output logic [31:0] wb_value
);
    always_ff @(posedge clk, posedge rst)
        if (rst) begin
            wb_rf_we <= 1'b0;
            wb_rd <= 5'd0;
            wb_value <= 32'd0;
        end else begin
            wb_rf_we <= rf_we;
            wb_rd <= rd;
            wb_value <= mem_read ? read_data : result;
        end
endmodule

// The processor: the two memories, the three stages that do the work, and the four registers between the five.
module pcpu #(
    parameter PROGRAM = ""rv_sum.hex"",   // the instruction memory's image
    parameter DATA    = ""rv_data.hex""       // the data memory's image
) (
    input  logic clk,
    input  logic rst      // asynchronous, active high: the PC to 0, every stage a bubble
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

    // The memory stage's store, which the testbench watches.
    assign data_addr = mem_result;

    pcpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    pcpu_fetch fetch (
        .clk(clk),
        .rst(rst),
        .stall(stall),
        .redirect(redirect),
        .target(target),
        .pc(pc)
    );

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

    pcpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we),
        .addr(mem_result),
        .wdata(write_data),
        .rdata(read_data)
    );

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

module pcpu_tb;
    // Which program it runs: SUM, FIBONACCI or TEST (rv32i.svh). In the body, not a #( ) list, where a project's module
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
// Fibonacci numbers, or every instruction once, checking itself.
typedef enum {SUM, FIBONACCI, TEST} rv_program_t;

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
