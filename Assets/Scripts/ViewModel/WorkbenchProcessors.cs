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
    /// and every instruction once, checking itself (asked 2026-11-01: "all of these examples") -- so the three can be
    /// compared on it: 323 cycles single-cycle, 1,244 multicycle, 386 pipelined.
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
        public static Project SingleCycle() => Imported("scpu_tb", ("scpu.sv", Scpu));

        /// <summary>The multicycle processor: a state machine takes each instruction through three to five short cycles.</summary>
        public static Project Multicycle() => Imported("mcpu_demo_tb", ("mcpu.sv", Mcpu));

        /// <summary>The pipelined processor: five stages at once, forwarding, a stall after a load and a flush after a jump.</summary>
        public static Project Pipelined() => Imported("pcpu_tb", ("pcpu.sv", Pcpu));

        /// <summary>Every file the samples are made from, by its corpus name: what ProcessorSampleTests compares.</summary>
        public static readonly IReadOnlyList<(string File, string Text)> Files = new[]
        {
            ("scpu.sv", Scpu),
            ("mcpu.sv", Mcpu),
            ("pcpu.sv", Pcpu),
            ("rv32i.svh", Defs),
            ("rv_program.hex", Program),
            ("rv_data.hex", Data),
            ("rv_expected.hex", Expected),
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
                new SourceFile("rv_program.hex", Program),
                new SourceFile("rv_data.hex", Data),
                new SourceFile("rv_expected.hex", Expected),
            };
            ProjectImport import = ProjectImporter.Import(sources, bench);
            if (!import.Succeeded || import.Project.Bench != bench)
                throw new InvalidOperationException("a processor sample did not import: " + string.Join("; ", import.Notes));
            Project project = import.Project;
            var used = new HashSet<string>(StringComparer.Ordinal);
            Collect(project, project.Find(bench), used);
            return project with { Modules = project.Modules.Where(m => used.Contains(m.Name)).ToArray() };
        }

        /// <summary><paramref name="module"/> and every module it makes, all the way down.</summary>
        private static void Collect(Project project, Module module, HashSet<string> used)
        {
            if (module == null || !used.Add(module.Name))
                return;
            foreach (Module child in WrittenModules.UsesOf(project, module))
                Collect(project, child, used);
        }

        private const string Scpu = @"// scpu: an original single-cycle processor -- each instruction fetched, decoded, executed, given its
// memory access and written back within one clock cycle, so the clock has to be as slow as the slowest
// instruction, a load. A decoder that is combinational logic, a datapath with the PC as its only
// register outside the register file, and separate instruction and data memories of 1,024 words each,
// loaded from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal,
// jalr, every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by
// immediate. No bytes or halfwords, no system instructions. scpu_tb runs rv_program.hex on rv_data.hex
// until the program stores to 0x7fc, then checks its results against rv_expected.hex. Written for
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
    parameter IMAGE = ""rv_program.hex""   // the image loaded at time 0
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

module scpu_control (
    input  logic [6:0] opcode,
    input  logic [2:0] funct3,
    input  logic       funct7_5,    // instruction bit 30: sub and sra
    output logic       rf_we,
    output logic       mem_we,
    output logic       alu_a_pc,    // the ALU's a: 0 the register, 1 the PC
    output logic       alu_b_imm,   // the ALU's b: 0 the register, 1 the immediate
    output logic [3:0] alu_op,
    output logic       branch,
    output logic       jump,        // jal: to the PC and the immediate
    output logic       jump_reg,    // jalr: to the ALU's sum, bit 0 cleared
    output logic [1:0] wb_src       // 0 the ALU, 1 the data memory, 2 the PC and four
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
        alu_a_pc = 1'b0;
        alu_b_imm = 1'b0;
        alu_op = ALU_ADD;
        branch = 1'b0;
        jump = 1'b0;
        jump_reg = 1'b0;
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
                rf_we = 1'b1;
                jump = 1'b1;
                wb_src = 2'd2;
            end
            OP_JALR: begin
                rf_we = 1'b1;
                alu_b_imm = 1'b1;
                jump_reg = 1'b1;
                wb_src = 2'd2;
            end
            OP_BRANCH: branch = 1'b1;
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

module scpu_datapath (
    input  logic        clk,
    input  logic        rst,          // asynchronous, active high: the PC to 0
    input  logic        rf_we,
    input  logic        alu_a_pc,
    input  logic        alu_b_imm,
    input  logic [3:0]  alu_op,
    input  logic        branch,
    input  logic        jump,
    input  logic        jump_reg,
    input  logic [1:0]  wb_src,
    input  logic [31:0] instr,        // from the instruction memory
    input  logic [31:0] read_data,    // from the data memory
    output logic [31:0] pc,
    output logic [31:0] data_addr,
    output logic [31:0] write_data,
    output logic [6:0]  opcode,
    output logic [2:0]  funct3,
    output logic        funct7_5
);
    logic [31:0] rd1, rd2, imm, alu_a, alu_b, alu_y, pc_plus4, target, pc_next, wb;
    logic        taken;

    assign opcode = instr[6:0];
    assign funct3 = instr[14:12];
    assign funct7_5 = instr[30];
    assign data_addr = alu_y;
    assign write_data = rd2;

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

    scpu_alu alu (.a(alu_a), .b(alu_b), .op(alu_op), .y(alu_y));

    always_ff @(posedge clk, posedge rst)
        if (rst) pc <= 32'd0;
        else pc <= pc_next;

    always_comb begin
        case (opcode)
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{instr[31]}}, instr[31:20]};
            OP_STORE:                 imm = {{20{instr[31]}}, instr[31:25], instr[11:7]};
            OP_BRANCH:                imm = {{19{instr[31]}}, instr[31], instr[7], instr[30:25], instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{instr[31]}}, instr[31], instr[19:12], instr[20], instr[30:21], 1'b0};
            default:                  imm = 32'd0;
        endcase
    end

    always_comb begin
        case (funct3)
            3'b000:  taken = rd1 == rd2;
            3'b001:  taken = rd1 != rd2;
            3'b100:  taken = $signed(rd1) < $signed(rd2);
            3'b101:  taken = $signed(rd1) >= $signed(rd2);
            3'b110:  taken = rd1 < rd2;
            3'b111:  taken = rd1 >= rd2;
            default: taken = 1'b0;
        endcase
    end

    assign alu_a = alu_a_pc ? pc : rd1;
    assign alu_b = alu_b_imm ? imm : rd2;
    assign pc_plus4 = pc + 32'd4;
    assign target = pc + imm;

    always_comb begin
        if (jump_reg)
            pc_next = alu_y & ~32'd1;
        else if (jump || (branch && taken))
            pc_next = target;
        else
            pc_next = pc_plus4;

        case (wb_src)
            2'd1:    wb = read_data;
            2'd2:    wb = pc_plus4;
            default: wb = alu_y;
        endcase
    end
endmodule

module scpu #(
    parameter PROGRAM = ""rv_program.hex"",   // the instruction memory's image
    parameter DATA    = ""rv_data.hex""       // the data memory's image
) (
    input  logic clk,
    input  logic rst      // asynchronous, active high
);
    logic        rf_we, mem_we, alu_a_pc, alu_b_imm, branch, jump, jump_reg, funct7_5;
    logic [1:0]  wb_src;
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
    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    scpu dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(""rv_expected.hex"", expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; every result is in memory by then.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            $display(""the sum of 1 to 10: %0d"", dut.dmem.mem[64]);
            $write(""ten Fibonacci numbers:"");
            for (i = 0; i < 10; i++)
                $write("" %0d"", dut.dmem.mem[65 + i]);
            $display;
            $display(""every instruction once: %0d of 31 checks held"", dut.dmem.mem[126]);

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

    always_comb begin
        case (opcode)
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{ir[31]}}, ir[31:20]};
            OP_STORE:                 imm = {{20{ir[31]}}, ir[31:25], ir[11:7]};
            OP_BRANCH:                imm = {{19{ir[31]}}, ir[31], ir[7], ir[30:25], ir[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {ir[31:12], 12'd0};
            OP_JAL:                   imm = {{11{ir[31]}}, ir[31], ir[19:12], ir[20], ir[30:21], 1'b0};
            default:                  imm = 32'd0;
        endcase
    end

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

// mcpu running rv_demo, the program scpu and pcpu run too -- the sum of 1 to 10, ten Fibonacci numbers and
// every instruction once -- so the three processors can be compared on one program: the start page's
// multicycle sample (Part 15h-6).
module mcpu_demo_tb;
    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    mcpu #(.PROGRAM(""rv_program.hex""), .DATA(""rv_data.hex"")) dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(""rv_expected.hex"", expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; that store lands on the next rising edge,
    // after every result is in memory.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            $display(""the sum of 1 to 10: %0d"", dut.dmem.mem[64]);
            $write(""ten Fibonacci numbers:"");
            for (i = 0; i < 10; i++)
                $write("" %0d"", dut.dmem.mem[65 + i]);
            $display;
            $display(""every instruction once: %0d of 31 checks held"", dut.dmem.mem[126]);

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
// be as slow as the slowest stage. Hazards are handled as the textbook does:
//   - forwarding: a result still in the execute/memory or memory/write-back register goes straight to
//     the ALU's operands, rather than waiting to be written to the register file;
//   - a stall: an instruction that needs a word a load has not yet read waits one cycle in decode, with
//     a bubble let into execute behind the load;
//   - a flush: a taken branch or a jump is decided in execute, and the two instructions fetched after it
//     are turned into bubbles.
// The register file is written in the first half of a cycle and read in the second, so a value being
// written back is read the same cycle. Separate instruction and data memories of 1,024 words each, loaded
// from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal, jalr,
// every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by
// immediate. No bytes or halfwords, no system instructions. pcpu_tb runs rv_program.hex on rv_data.hex
// until the program stores to 0x7fc, then checks its results against rv_expected.hex. Written for
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
    // written, as though written in the cycle's first half and read in its second.
    assign rd1 = ra1 == 5'd0 ? 32'd0 : we && wa == ra1 ? wd : regs[ra1];
    assign rd2 = ra2 == 5'd0 ? 32'd0 : we && wa == ra2 ? wd : regs[ra2];
endmodule

module pcpu_memory #(
    parameter IMAGE = ""rv_program.hex""   // the image loaded at time 0
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

module pcpu #(
    parameter PROGRAM = ""rv_program.hex"",   // the instruction memory's image
    parameter DATA    = ""rv_data.hex""       // the data memory's image
) (
    input  logic clk,
    input  logic rst      // asynchronous, active high: the PC to 0, every stage a bubble
);
    // ---- fetch
    logic [31:0] pc, pc_next, instr;
    logic        stall, redirect;
    logic [31:0] target;

    // ---- fetch/decode
    logic [31:0] id_pc, id_instr;

    // ---- decode
    logic        rf_we, mem_we_id, mem_read, alu_a_pc, alu_b_imm, branch, jump, jump_reg, link;
    logic [3:0]  alu_op;
    logic [31:0] rd1, rd2, imm;

    // ---- decode/execute
    logic        ex_rf_we, ex_mem_we, ex_mem_read, ex_alu_a_pc, ex_alu_b_imm, ex_branch, ex_jump, ex_jump_reg, ex_link;
    logic [3:0]  ex_alu_op;
    logic [2:0]  ex_funct3;
    logic [4:0]  ex_rs1, ex_rs2, ex_rd;
    logic [31:0] ex_pc, ex_rd1, ex_rd2, ex_imm;

    // ---- execute
    logic [1:0]  forward_a, forward_b;
    logic [31:0] src_a, src_b, alu_a, alu_b, alu_y, ex_result;
    logic        taken;

    // ---- execute/memory
    logic        mem_rf_we, mem_mem_read;
    logic [4:0]  mem_rd;
    logic [31:0] mem_result, write_data, read_data, data_addr;
    logic        mem_we_stage;

    // ---- memory/write-back
    logic        wb_rf_we;
    logic [4:0]  wb_rd;
    logic [31:0] wb_value;

    // The testbench watches the memory stage's store.
    logic        mem_we;
    assign mem_we = mem_we_stage;
    assign data_addr = mem_result;

    // ================================================================ fetch
    pcpu_memory #(.IMAGE(PROGRAM)) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    assign pc_next = redirect ? target : pc + 32'd4;

    always_ff @(posedge clk, posedge rst)
        if (rst) pc <= 32'd0;
        else if (redirect || !stall) pc <= pc_next;

    always_ff @(posedge clk, posedge rst)
        if (rst) begin
            id_pc <= 32'd0;
            id_instr <= 32'd0;      // a bubble: opcode 0 does nothing
        end else if (redirect) begin
            id_pc <= 32'd0;
            id_instr <= 32'd0;
        end else if (!stall) begin
            id_pc <= pc;
            id_instr <= instr;
        end

    // ================================================================ decode
    pcpu_control control (
        .opcode(id_instr[6:0]),
        .funct3(id_instr[14:12]),
        .funct7_5(id_instr[30]),
        .rf_we(rf_we),
        .mem_we(mem_we_id),
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
        .ra1(id_instr[19:15]),
        .ra2(id_instr[24:20]),
        .wa(wb_rd),
        .wd(wb_value),
        .rd1(rd1),
        .rd2(rd2)
    );

    pcpu_hazard hazard (
        .ex_mem_read(ex_mem_read),
        .ex_rd(ex_rd),
        .id_rs1(id_instr[19:15]),
        .id_rs2(id_instr[24:20]),
        .stall(stall)
    );

    always_comb begin
        case (id_instr[6:0])
            OP_IMM, OP_LOAD, OP_JALR: imm = {{20{id_instr[31]}}, id_instr[31:20]};
            OP_STORE:                 imm = {{20{id_instr[31]}}, id_instr[31:25], id_instr[11:7]};
            OP_BRANCH:                imm = {{19{id_instr[31]}}, id_instr[31], id_instr[7], id_instr[30:25], id_instr[11:8], 1'b0};
            OP_LUI, OP_AUIPC:         imm = {id_instr[31:12], 12'd0};
            OP_JAL:                   imm = {{11{id_instr[31]}}, id_instr[31], id_instr[19:12], id_instr[20], id_instr[30:21], 1'b0};
            default:                  imm = 32'd0;
        endcase
    end

    // A stall or a flush lets a bubble into execute: nothing written, nothing jumped to.
    always_ff @(posedge clk, posedge rst)
        if (rst || redirect || stall) begin
            ex_rf_we <= 1'b0;
            ex_mem_we <= 1'b0;
            ex_mem_read <= 1'b0;
            ex_alu_a_pc <= 1'b0;
            ex_alu_b_imm <= 1'b0;
            ex_branch <= 1'b0;
            ex_jump <= 1'b0;
            ex_jump_reg <= 1'b0;
            ex_link <= 1'b0;
            ex_alu_op <= ALU_ADD;
            ex_funct3 <= 3'd0;
            ex_rs1 <= 5'd0;
            ex_rs2 <= 5'd0;
            ex_rd <= 5'd0;
            ex_pc <= 32'd0;
            ex_rd1 <= 32'd0;
            ex_rd2 <= 32'd0;
            ex_imm <= 32'd0;
        end else begin
            ex_rf_we <= rf_we;
            ex_mem_we <= mem_we_id;
            ex_mem_read <= mem_read;
            ex_alu_a_pc <= alu_a_pc;
            ex_alu_b_imm <= alu_b_imm;
            ex_branch <= branch;
            ex_jump <= jump;
            ex_jump_reg <= jump_reg;
            ex_link <= link;
            ex_alu_op <= alu_op;
            ex_funct3 <= id_instr[14:12];
            ex_rs1 <= id_instr[19:15];
            ex_rs2 <= id_instr[24:20];
            ex_rd <= id_instr[11:7];
            ex_pc <= id_pc;
            ex_rd1 <= rd1;
            ex_rd2 <= rd2;
            ex_imm <= imm;
        end

    // ================================================================ execute
    pcpu_forward forward (
        .rs1(ex_rs1),
        .rs2(ex_rs2),
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
            default: src_a = ex_rd1;
        endcase
        case (forward_b)
            2'd2:    src_b = mem_result;
            2'd1:    src_b = wb_value;
            default: src_b = ex_rd2;
        endcase
    end

    assign alu_a = ex_alu_a_pc ? ex_pc : src_a;
    assign alu_b = ex_alu_b_imm ? ex_imm : src_b;

    pcpu_alu alu (.a(alu_a), .b(alu_b), .op(ex_alu_op), .y(alu_y));

    always_comb begin
        case (ex_funct3)
            3'b000:  taken = src_a == src_b;
            3'b001:  taken = src_a != src_b;
            3'b100:  taken = $signed(src_a) < $signed(src_b);
            3'b101:  taken = $signed(src_a) >= $signed(src_b);
            3'b110:  taken = src_a < src_b;
            3'b111:  taken = src_a >= src_b;
            default: taken = 1'b0;
        endcase
    end

    assign redirect = ex_jump || ex_jump_reg || (ex_branch && taken);
    assign target = ex_jump_reg ? alu_y & ~32'd1 : ex_pc + ex_imm;
    assign ex_result = ex_link ? ex_pc + 32'd4 : alu_y;

    always_ff @(posedge clk, posedge rst)
        if (rst) begin
            mem_rf_we <= 1'b0;
            mem_we_stage <= 1'b0;
            mem_mem_read <= 1'b0;
            mem_rd <= 5'd0;
            mem_result <= 32'd0;
            write_data <= 32'd0;
        end else begin
            mem_rf_we <= ex_rf_we;
            mem_we_stage <= ex_mem_we;
            mem_mem_read <= ex_mem_read;
            mem_rd <= ex_rd;
            mem_result <= ex_result;
            write_data <= src_b;
        end

    // ================================================================ memory
    pcpu_memory #(.IMAGE(DATA)) dmem (
        .clk(clk),
        .we(mem_we_stage),
        .addr(mem_result),
        .wdata(write_data),
        .rdata(read_data)
    );

    always_ff @(posedge clk, posedge rst)
        if (rst) begin
            wb_rf_we <= 1'b0;
            wb_rd <= 5'd0;
            wb_value <= 32'd0;
        end else begin
            wb_rf_we <= mem_rf_we;
            wb_rd <= mem_rd;
            wb_value <= mem_mem_read ? read_data : mem_result;
        end

    // ================================================================ write-back: the register file, above
endmodule

module pcpu_tb;
    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    pcpu dut (.clk(clk), .rst(rst));

    always #5 clk = ~clk;
    always @(posedge clk) cycles <= cycles + 1;

    initial begin
        $readmemh(""rv_expected.hex"", expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; every result is in memory by then.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display(""done after %0d cycles, at %0t"", cycles, $time);
            $display(""the sum of 1 to 10: %0d"", dut.dmem.mem[64]);
            $write(""ten Fibonacci numbers:"");
            for (i = 0; i < 10; i++)
                $write("" %0d"", dut.dmem.mem[65 + i]);
            $display;
            $display(""every instruction once: %0d of 31 checks held"", dut.dmem.mem[126]);

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

        private const string Program = @"// rv_demo, the program BitSorter's RV32I processors run: the sum of 1 to 10, the first ten
// Fibonacci numbers, and every instruction once, checking itself. Assembled from the listing
// beside each word; word n is the instruction at byte address 4n.
// Written for BitSorter's oracle; not taken from any course material.
00000413  // 000: addi s0, zero, 0          1. the sum of 1 to 10
00100293  // 004: addi t0, zero, 1          i
00b00313  // 008: addi t1, zero, 11         one past the last
00540433  // 00c: add s0, s0, t0            <- sum_loop
00128293  // 010: addi t0, t0, 1
fe62cce3  // 014: blt t0, t1, sum_loop
10802023  // 018: sw s0, 0x100(zero)        55
10400513  // 01c: addi a0, zero, 0x104      2. ten Fibonacci numbers, from 0x104
00a00593  // 020: addi a1, zero, 10
018000ef  // 024: jal ra, fib
048000ef  // 028: jal ra, every             3. every instruction once, from 0x140
1fb02c23  // 02c: sw s11, 0x1f8(zero)       how many of the checks held
00100293  // 030: addi t0, zero, 1
7e502e23  // 034: sw t0, 0x7fc(zero)        done: the testbench watches for this store
0000006f  // 038: jal zero, end             and wait (end)
00052023  // 03c: sw zero, 0(a0)            F0 = 0 (fib)
00100293  // 040: addi t0, zero, 1
00552223  // 044: sw t0, 4(a0)              F1 = 1
00850393  // 048: addi t2, a0, 8            the next word
00259e13  // 04c: slli t3, a1, 2
01c50e33  // 050: add t3, a0, t3            one past the last
ff83ae83  // 054: lw t4, -8(t2)             the two before it, from memory (fib_loop)
ffc3af03  // 058: lw t5, -4(t2)
01ee8fb3  // 05c: add t6, t4, t5            needs the load just before it
01f3a023  // 060: sw t6, 0(t2)
00438393  // 064: addi t2, t2, 4
ffc3e6e3  // 068: bltu t2, t3, fib_loop
00008067  // 06c: jalr zero, 0(ra)          return
14000493  // 070: addi s1, zero, 0x140      where the results go (every)
00000d93  // 074: addi s11, zero, 0         checks held
f0f01637  // 078: lui a2, 0xf0f01           a negative word
23460613  // 07c: addi a2, a2, 564
0a500693  // 080: addi a3, zero, 165        a small positive one
00500713  // 084: addi a4, zero, 5          a shift
abcde2b7  // 088: lui t0, 0xabcde
0054a023  // 08c: sw t0, 0(s1)              lui
00448493  // 090: addi s1, s1, 4
abcdefb7  // 094: lui t6, 0xabcde
01f29463  // 098: bne t0, t6, skip1
001d8d93  // 09c: addi s11, s11, 1
00001297  // 0a0: auipc t0, 0x1             <- here
0054a023  // 0a4: sw t0, 0(s1)              auipc
00448493  // 0a8: addi s1, s1, 4
00001fb7  // 0ac: lui t6, 0x1
0a0f8f93  // 0b0: addi t6, t6, 160
01f29463  // 0b4: bne t0, t6, skip2
001d8d93  // 0b8: addi s11, s11, 1
00d602b3  // 0bc: add t0, a2, a3
0054a023  // 0c0: sw t0, 0(s1)              add
00448493  // 0c4: addi s1, s1, 4
f0f01fb7  // 0c8: lui t6, 0xf0f01
2d9f8f93  // 0cc: addi t6, t6, 729
01f29463  // 0d0: bne t0, t6, skip3
001d8d93  // 0d4: addi s11, s11, 1
40d602b3  // 0d8: sub t0, a2, a3
0054a023  // 0dc: sw t0, 0(s1)              sub
00448493  // 0e0: addi s1, s1, 4
f0f01fb7  // 0e4: lui t6, 0xf0f01
18ff8f93  // 0e8: addi t6, t6, 399
01f29463  // 0ec: bne t0, t6, skip4
001d8d93  // 0f0: addi s11, s11, 1
00e692b3  // 0f4: sll t0, a3, a4
0054a023  // 0f8: sw t0, 0(s1)              sll
00448493  // 0fc: addi s1, s1, 4
00001fb7  // 100: lui t6, 0x1
4a0f8f93  // 104: addi t6, t6, 1184
01f29463  // 108: bne t0, t6, skip5
001d8d93  // 10c: addi s11, s11, 1
00d622b3  // 110: slt t0, a2, a3
0054a023  // 114: sw t0, 0(s1)              slt: the negative one is less
00448493  // 118: addi s1, s1, 4
00100f93  // 11c: addi t6, zero, 1
01f29463  // 120: bne t0, t6, skip6
001d8d93  // 124: addi s11, s11, 1
00d632b3  // 128: sltu t0, a2, a3
0054a023  // 12c: sw t0, 0(s1)              sltu: but not without its sign
00448493  // 130: addi s1, s1, 4
00000f93  // 134: addi t6, zero, 0
01f29463  // 138: bne t0, t6, skip7
001d8d93  // 13c: addi s11, s11, 1
00d642b3  // 140: xor t0, a2, a3
0054a023  // 144: sw t0, 0(s1)              xor
00448493  // 148: addi s1, s1, 4
f0f01fb7  // 14c: lui t6, 0xf0f01
291f8f93  // 150: addi t6, t6, 657
01f29463  // 154: bne t0, t6, skip8
001d8d93  // 158: addi s11, s11, 1
00e652b3  // 15c: srl t0, a2, a4
0054a023  // 160: sw t0, 0(s1)              srl
00448493  // 164: addi s1, s1, 4
07878fb7  // 168: lui t6, 0x7878
091f8f93  // 16c: addi t6, t6, 145
01f29463  // 170: bne t0, t6, skip9
001d8d93  // 174: addi s11, s11, 1
40e652b3  // 178: sra t0, a2, a4
0054a023  // 17c: sw t0, 0(s1)              sra
00448493  // 180: addi s1, s1, 4
ff878fb7  // 184: lui t6, 0xff878
091f8f93  // 188: addi t6, t6, 145
01f29463  // 18c: bne t0, t6, skip10
001d8d93  // 190: addi s11, s11, 1
00d662b3  // 194: or t0, a2, a3
0054a023  // 198: sw t0, 0(s1)              or
00448493  // 19c: addi s1, s1, 4
f0f01fb7  // 1a0: lui t6, 0xf0f01
2b5f8f93  // 1a4: addi t6, t6, 693
01f29463  // 1a8: bne t0, t6, skip11
001d8d93  // 1ac: addi s11, s11, 1
00d672b3  // 1b0: and t0, a2, a3
0054a023  // 1b4: sw t0, 0(s1)              and
00448493  // 1b8: addi s1, s1, 4
02400f93  // 1bc: addi t6, zero, 36
01f29463  // 1c0: bne t0, t6, skip12
001d8d93  // 1c4: addi s11, s11, 1
f9c60293  // 1c8: addi t0, a2, -100
0054a023  // 1cc: sw t0, 0(s1)              addi
00448493  // 1d0: addi s1, s1, 4
f0f01fb7  // 1d4: lui t6, 0xf0f01
1d0f8f93  // 1d8: addi t6, t6, 464
01f29463  // 1dc: bne t0, t6, skip13
001d8d93  // 1e0: addi s11, s11, 1
0c86a293  // 1e4: slti t0, a3, 200
0054a023  // 1e8: sw t0, 0(s1)              slti
00448493  // 1ec: addi s1, s1, 4
00100f93  // 1f0: addi t6, zero, 1
01f29463  // 1f4: bne t0, t6, skip14
001d8d93  // 1f8: addi s11, s11, 1
0646b293  // 1fc: sltiu t0, a3, 100
0054a023  // 200: sw t0, 0(s1)              sltiu
00448493  // 204: addi s1, s1, 4
00000f93  // 208: addi t6, zero, 0
01f29463  // 20c: bne t0, t6, skip15
001d8d93  // 210: addi s11, s11, 1
fff6c293  // 214: xori t0, a3, -1
0054a023  // 218: sw t0, 0(s1)              xori: not
00448493  // 21c: addi s1, s1, 4
f5a00f93  // 220: addi t6, zero, -166
01f29463  // 224: bne t0, t6, skip16
001d8d93  // 228: addi s11, s11, 1
7006e293  // 22c: ori t0, a3, 0x700
0054a023  // 230: sw t0, 0(s1)              ori
00448493  // 234: addi s1, s1, 4
7a500f93  // 238: addi t6, zero, 1957
01f29463  // 23c: bne t0, t6, skip17
001d8d93  // 240: addi s11, s11, 1
3f067293  // 244: andi t0, a2, 0x3f0
0054a023  // 248: sw t0, 0(s1)              andi
00448493  // 24c: addi s1, s1, 4
23000f93  // 250: addi t6, zero, 560
01f29463  // 254: bne t0, t6, skip18
001d8d93  // 258: addi s11, s11, 1
00769293  // 25c: slli t0, a3, 7
0054a023  // 260: sw t0, 0(s1)              slli
00448493  // 264: addi s1, s1, 4
00005fb7  // 268: lui t6, 0x5
280f8f93  // 26c: addi t6, t6, 640
01f29463  // 270: bne t0, t6, skip19
001d8d93  // 274: addi s11, s11, 1
00965293  // 278: srli t0, a2, 9
0054a023  // 27c: sw t0, 0(s1)              srli
00448493  // 280: addi s1, s1, 4
00788fb7  // 284: lui t6, 0x788
809f8f93  // 288: addi t6, t6, -2039
01f29463  // 28c: bne t0, t6, skip20
001d8d93  // 290: addi s11, s11, 1
40965293  // 294: srai t0, a2, 9
0054a023  // 298: sw t0, 0(s1)              srai
00448493  // 29c: addi s1, s1, 4
fff88fb7  // 2a0: lui t6, 0xfff88
809f8f93  // 2a4: addi t6, t6, -2039
01f29463  // 2a8: bne t0, t6, skip21
001d8d93  // 2ac: addi s11, s11, 1
00002283  // 2b0: lw t0, 0(zero)
0054a023  // 2b4: sw t0, 0(s1)              lw
00448493  // 2b8: addi s1, s1, 4
5a5a1fb7  // 2bc: lui t6, 0x5a5a1
f0ff8f93  // 2c0: addi t6, t6, -241
01f29463  // 2c4: bne t0, t6, skip22
001d8d93  // 2c8: addi s11, s11, 1
1ec02823  // 2cc: sw a2, 0x1f0(zero)
1f002283  // 2d0: lw t0, 0x1f0(zero)        read back at once
0054a023  // 2d4: sw t0, 0(s1)              sw
00448493  // 2d8: addi s1, s1, 4
f0f01fb7  // 2dc: lui t6, 0xf0f01
234f8f93  // 2e0: addi t6, t6, 564
01f29463  // 2e4: bne t0, t6, skip23
001d8d93  // 2e8: addi s11, s11, 1
00000293  // 2ec: addi t0, zero, 0
00d68463  // 2f0: beq a3, a3, over_beq      taken
00128293  // 2f4: addi t0, t0, 1            skipped when taken
00228293  // 2f8: addi t0, t0, 2            <- over_beq
0054a023  // 2fc: sw t0, 0(s1)              beq
00448493  // 300: addi s1, s1, 4
00200f93  // 304: addi t6, zero, 2
01f29463  // 308: bne t0, t6, skip24
001d8d93  // 30c: addi s11, s11, 1
00000293  // 310: addi t0, zero, 0
00d69463  // 314: bne a3, a3, over_bne      not taken
00128293  // 318: addi t0, t0, 1            skipped when taken
00228293  // 31c: addi t0, t0, 2            <- over_bne
0054a023  // 320: sw t0, 0(s1)              bne
00448493  // 324: addi s1, s1, 4
00300f93  // 328: addi t6, zero, 3
01f29463  // 32c: bne t0, t6, skip25
001d8d93  // 330: addi s11, s11, 1
00000293  // 334: addi t0, zero, 0
00d64463  // 338: blt a2, a3, over_blt      taken
00128293  // 33c: addi t0, t0, 1            skipped when taken
00228293  // 340: addi t0, t0, 2            <- over_blt
0054a023  // 344: sw t0, 0(s1)              blt
00448493  // 348: addi s1, s1, 4
00200f93  // 34c: addi t6, zero, 2
01f29463  // 350: bne t0, t6, skip26
001d8d93  // 354: addi s11, s11, 1
00000293  // 358: addi t0, zero, 0
00d65463  // 35c: bge a2, a3, over_bge      not taken
00128293  // 360: addi t0, t0, 1            skipped when taken
00228293  // 364: addi t0, t0, 2            <- over_bge
0054a023  // 368: sw t0, 0(s1)              bge
00448493  // 36c: addi s1, s1, 4
00300f93  // 370: addi t6, zero, 3
01f29463  // 374: bne t0, t6, skip27
001d8d93  // 378: addi s11, s11, 1
00000293  // 37c: addi t0, zero, 0
00d66463  // 380: bltu a2, a3, over_bltu    not taken
00128293  // 384: addi t0, t0, 1            skipped when taken
00228293  // 388: addi t0, t0, 2            <- over_bltu
0054a023  // 38c: sw t0, 0(s1)              bltu
00448493  // 390: addi s1, s1, 4
00300f93  // 394: addi t6, zero, 3
01f29463  // 398: bne t0, t6, skip28
001d8d93  // 39c: addi s11, s11, 1
00000293  // 3a0: addi t0, zero, 0
00d67463  // 3a4: bgeu a2, a3, over_bgeu    taken
00128293  // 3a8: addi t0, t0, 1            skipped when taken
00228293  // 3ac: addi t0, t0, 2            <- over_bgeu
0054a023  // 3b0: sw t0, 0(s1)              bgeu
00448493  // 3b4: addi s1, s1, 4
00200f93  // 3b8: addi t6, zero, 2
01f29463  // 3bc: bne t0, t6, skip29
001d8d93  // 3c0: addi s11, s11, 1
0080036f  // 3c4: jal t1, after_jal         <- the_jal
f9cd8d93  // 3c8: addi s11, s11, -100       never runs
0064a023  // 3cc: sw t1, 0(s1)              jal links (after_jal)
00448493  // 3d0: addi s1, s1, 4
00000fb7  // 3d4: lui t6, 0x0
3c8f8f93  // 3d8: addi t6, t6, 968
01f31463  // 3dc: bne t1, t6, skip30
001d8d93  // 3e0: addi s11, s11, 1
00000397  // 3e4: auipc t2, 0               <- the_auipc
01038367  // 3e8: jalr t1, 16(t2)
f9cd8d93  // 3ec: addi s11, s11, -100       never runs
f9cd8d93  // 3f0: addi s11, s11, -100       never runs
0064a023  // 3f4: sw t1, 0(s1)              jalr links
00448493  // 3f8: addi s1, s1, 4
00000fb7  // 3fc: lui t6, 0x0
3ecf8f93  // 400: addi t6, t6, 1004
01f31463  // 404: bne t1, t6, skip31
001d8d93  // 408: addi s11, s11, 1
00008067  // 40c: jalr zero, 0(ra)          return
";

        private const string Data = @"// rv_demo's data: word 0 for its lw, and words 1 to 127 cleared, so every result word starts at 0.
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

        private const string Expected = @"// What rv_demo leaves in data words 64 to 127 (bytes 0x100 to 0x1fc): the sum, ten Fibonacci
// numbers, 31 results of every instruction once, the word it stored to 0x1f0, and at 0x1f8
// how many of its checks held. Worked out by an emulator written from the RISC-V specification.
// Written for BitSorter's oracle; not taken from any course material.
00000037  // 100
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
abcde000  // 140
000010a0  // 144
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
00000002  // 1a4
00000003  // 1a8
00000003  // 1ac
00000002  // 1b0
000003c8  // 1b4
000003ec  // 1b8
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
f0f01234  // 1f0
00000000  // 1f4
0000001f  // 1f8
00000000  // 1fc
";
    }
}
