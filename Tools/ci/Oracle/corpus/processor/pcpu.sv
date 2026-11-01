// pcpu: an original pipelined processor -- five stages, fetch, decode, execute, memory and write-back,
// with a register between each two, so five instructions are under way at once and the clock need only
// be as slow as the slowest stage. Hazards are handled as the textbook does:
//   - forwarding: a result still in the execute/memory or memory/write-back register goes straight to
//     the ALU's operands, rather than waiting to be written to the register file;
//   - a stall: an instruction that needs a word a load has not yet read waits one cycle in decode, with
//     a bubble let into execute behind the load;
//   - a flush: a taken branch or a jump is decided in execute, and the two instructions fetched after it
//     are turned into bubbles.
// The register file passes a value being written back straight through to a read of the same register
// in that cycle -- the textbook's "written in the first half, read in the second" -- so decode never
// reads a register the instruction leaving write-back is about to change. Separate instruction and data memories of 1,024 words each, loaded
// from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal, jalr,
// every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by
// immediate. No bytes or halfwords, no system instructions. pcpu_tb runs rv_program.hex on rv_data.hex
// until the program stores to 0x7fc, then checks its results against rv_expected.hex. Written for
// BitSorter's oracle from the published RISC-V specification; not taken from any course material.

`include "rv32i.svh"

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
    parameter PROGRAM = "rv_program.hex",   // the instruction memory's image
    parameter DATA    = "rv_data.hex"       // the data memory's image
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

    // A stall or a flush lets a bubble into execute: its controls cleared, so it writes nothing and jumps
    // nowhere, whatever its other fields hold.
    logic bubble;
    assign bubble = redirect || stall;

    always_ff @(posedge clk, posedge rst)
        if (rst) begin
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
            ex_rf_we <= rf_we && !bubble;
            ex_mem_we <= mem_we_id && !bubble;
            ex_mem_read <= mem_read && !bubble;
            ex_alu_a_pc <= alu_a_pc;
            ex_alu_b_imm <= alu_b_imm;
            ex_branch <= branch && !bubble;
            ex_jump <= jump && !bubble;
            ex_jump_reg <= jump_reg && !bubble;
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
            $display("every instruction once: %0d of 40 checks held", dut.dmem.mem[126]);

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
