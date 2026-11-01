// scpu: an original single-cycle processor -- each instruction fetched, decoded, executed, given its
// memory access and written back within one clock cycle, so the clock has to be as slow as the slowest
// instruction, a load. A decoder that is combinational logic, a datapath with the PC as its only
// register outside the register file, and separate instruction and data memories of 1,024 words each,
// loaded from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal,
// jalr, every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by
// immediate. No bytes or halfwords, no system instructions. scpu_tb runs rv_program.hex on rv_data.hex
// until the program stores to 0x7fc, then checks its results against rv_expected.hex. Written for
// BitSorter's oracle from the published RISC-V specification; not taken from any course material.

`include "rv32i.svh"

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
    parameter IMAGE = "rv_program.hex"   // the image loaded at time 0
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
    parameter PROGRAM = "rv_program.hex",   // the instruction memory's image
    parameter DATA    = "rv_data.hex"       // the data memory's image
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
        $readmemh("rv_expected.hex", expected);
        rst = 1'b1;
        repeat (2) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; every result is in memory by then.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display("done after %0d cycles, at %0t", cycles, $time);
            $display("the sum of 1 to 10: %0d", dut.dmem.mem[64]);
            $write("ten Fibonacci numbers:");
            for (i = 0; i < 10; i++)
                $write(" %0d", dut.dmem.mem[65 + i]);
            $display;
            $display("every instruction once: %0d of 41 checks held", dut.dmem.mem[126]);

            for (i = 0; i < 64; i++)
                if (dut.dmem.mem[64 + i] !== expected[i]) begin
                    $display("FAIL: the word at %h is %h, not %h", 32'h100 + 4 * i, dut.dmem.mem[64 + i], expected[i]);
                    failures++;
                end

            if (failures == 0)
                $display("PASS: every result as expected");
            else
                $display("FAIL: %0d results wrong", failures);
            $finish;
        end

    initial begin
        #100000;
        $display("TIMEOUT: the program never said it was done");
        $finish;
    end
endmodule
