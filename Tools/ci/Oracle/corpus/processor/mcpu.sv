// mcpu: an original multicycle processor in the shape of the course's third simulation exercise -- a controller that is
// a state machine, a datapath with registers between the steps, and separate instruction and data memories of 1,024
// words each, loaded from images with $readmemh. It runs the RV32I instructions a program needs: lui, auipc, jal, jalr,
// every branch, lw and sw, and every arithmetic, logic and shift operation, by register and by immediate. No bytes or
// halfwords, no system instructions. Each instruction takes three to five clock cycles, one short step in each.
// Two testbenches are here. mcpu_tb is the oracle's own: it runs mcpu_program.hex, a program that sorts eight words, on
// mcpu_data.hex until the program stores to 0x7fc, then checks its results against mcpu_expected.hex. mcpu_demo_tb is
// the one the start page's sample runs: it takes a PROGRAM -- rv_sum.hex, rv_fib.hex or rv_isa.hex -- on rv_data.hex,
// and checks the results against that program's own expected image, as scpu_tb and pcpu_tb do. Written for BitSorter's
// oracle from the published RISC-V specification; not taken from any course material.

`include "rv32i.svh"

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
    parameter IMAGE = "mcpu_program.hex"   // the image loaded at time 0
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
    parameter PROGRAM = "mcpu_program.hex",   // the instruction memory's image
    parameter DATA    = "mcpu_data.hex"       // the data memory's image
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

// mcpu running the programs scpu and pcpu run too -- its PROGRAM: the sum of 1 to 10, ten Fibonacci numbers, or
// every instruction once -- so the three processors can be compared on each: the start page's multicycle sample.
module mcpu_demo_tb;
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

    mcpu #(.PROGRAM(IMAGE), .DATA("rv_data.hex")) dut (.clk(clk), .rst(rst));

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
