// mcpu: an original multicycle processor in the shape of the course's third simulation exercise -- a
// controller that is a state machine, a datapath with registers between the steps, and separate
// instruction and data memories of 1,024 words each, loaded from images with $readmemh. It runs the
// RV32I instructions a program needs: lui, auipc, jal, jalr, every branch, lw and sw, and every
// arithmetic, logic and shift operation, by register and by immediate. No bytes or halfwords, no
// system instructions. mcpu_tb runs mcpu_program.hex on mcpu_data.hex until the program stores to
// 0x7fc, then checks its results against mcpu_expected.hex. Written for BitSorter's oracle from the
// published RISC-V specification; not taken from any course material.

`include "mcpu_defs.svh"

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
    parameter IMAGE = "mcpu_program.hex"   // the image loaded at time 0
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

module mcpu (
    input  logic clk,
    input  logic rst      // asynchronous, active high
);
    logic        pc_we, ir_we, rf_we, mem_we, funct7_5, taken;
    logic [1:0]  alu_a_sel, alu_b_sel, pc_src, wb_src;
    logic [3:0]  alu_op;
    logic [6:0]  opcode;
    logic [2:0]  funct3;
    logic [31:0] pc, instr, data_addr, write_data, read_data;

    mcpu_memory #(.IMAGE("mcpu_program.hex")) imem (
        .clk(clk),
        .we(1'b0),
        .addr(pc),
        .wdata(32'd0),
        .rdata(instr)
    );

    mcpu_memory #(.IMAGE("mcpu_data.hex")) dmem (
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
        $readmemh("mcpu_expected.hex", expected);
        rst = 1'b1;
        repeat (4) @(posedge clk);
        @(negedge clk) rst = 1'b0;
    end

    // The program says it is done by storing to 0x7fc; that store lands on the next rising edge,
    // after every result is in memory.
    always @(negedge clk)
        if (dut.mem_we && dut.data_addr == 32'h0000_07fc) begin
            $display("done after %0d cycles, at %0t", cycles, $time);
            $write("sorted:");
            for (i = 0; i < 8; i++)
                $write(" %0d", $signed(dut.dmem.mem[64 + i]));
            $display;

            for (i = 0; i < 7; i++)
                if ($signed(dut.dmem.mem[64 + i]) > $signed(dut.dmem.mem[65 + i])) begin
                    $display("FAIL: words %0d and %0d are out of order", i, i + 1);
                    failures++;
                end

            for (i = 0; i < 23; i++)
                if (dut.dmem.mem[128 + i] !== expected[i]) begin
                    $display("FAIL: result %0d at %h is %h, not %h", i, 32'h200 + 4 * i, dut.dmem.mem[128 + i], expected[i]);
                    failures++;
                end

            if (failures == 0)
                $display("PASS: every result as expected");
            else
                $display("FAIL: %0d results wrong", failures);
            $finish;
        end

    initial begin
        #200000;
        $display("TIMEOUT: the program never said it was done");
        $finish;
    end
endmodule
