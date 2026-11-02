// scpu: an original single-cycle processor -- each instruction fetched, decoded, executed, given its memory access and
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

`include "rv32i.svh"

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
    parameter IMAGE = "rv_sum.hex"   // the image loaded at time 0
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
    parameter PROGRAM = "rv_sum.hex",   // the instruction memory's image
    parameter DATA    = "rv_data.hex"       // the data memory's image
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
    localparam IMAGE    = PROGRAM == SUM ? "rv_sum.hex" : PROGRAM == FIBONACCI ? "rv_fib.hex" : "rv_isa.hex";
    localparam EXPECTED = PROGRAM == SUM ? "rv_sum_expected.hex" : PROGRAM == FIBONACCI ? "rv_fib_expected.hex"
                                         : "rv_isa_expected.hex";

    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    scpu #(.PROGRAM(IMAGE), .DATA("rv_data.hex")) dut (.clk(clk), .rst(rst));

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
            $display("done after %0d cycles, at %0t", cycles, $time);
            case (PROGRAM)
                SUM:
                    $display("the sum of 1 to 10: %0d", dut.dmem.mem[64]);
                FIBONACCI: begin
                    $write("ten Fibonacci numbers:");
                    for (i = 0; i < 10; i++)
                        $write(" %0d", dut.dmem.mem[65 + i]);
                    $display;
                end
                default:
                    $display("every instruction once: %0d of 41 checks held", dut.dmem.mem[126]);
            endcase

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
