// pcpu: an original pipelined processor -- five stages, fetch, decode, execute, memory and write-back,
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
// the textbook's "written in the first half, read in the second" -- so decode never reads a register the instruction
// leaving write-back is about to change.
// Separate instruction and data memories of 1,024 words each, loaded from images with $readmemh. It runs the RV32I
// instructions a program needs: lui, auipc, jal, jalr, every branch, lw and sw, and every arithmetic, logic and shift
// operation, by register and by immediate. No bytes or halfwords, no system instructions. pcpu_tb runs one of three
// programs on rv_data.hex -- its PROGRAM: rv_sum.hex, rv_fib.hex or rv_isa.hex -- until the program stores to 0x7fc,
// then checks its results against the program's own expected image. Written for BitSorter's oracle from the published
// RISC-V specification; not taken from any course material.

`include "rv32i.svh"

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
    output logic [31:0] result,      // what the instruction writes back, a load's address or the PC and four
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
    parameter PROGRAM = "rv_sum.hex",   // the instruction memory's image
    parameter DATA    = "rv_data.hex"       // the data memory's image
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

    // The memory stage's store, which the testbench watches.
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
    localparam IMAGE    = PROGRAM == SUM ? "rv_sum.hex" : PROGRAM == FIBONACCI ? "rv_fib.hex" : "rv_isa.hex";
    localparam EXPECTED = PROGRAM == SUM ? "rv_sum_expected.hex" : PROGRAM == FIBONACCI ? "rv_fib_expected.hex"
                                         : "rv_isa_expected.hex";

    logic        clk = 1'b0;
    logic        rst;
    integer      cycles = 0;
    integer      failures = 0;
    integer      i;
    logic [31:0] expected [0:63];

    pcpu #(.PROGRAM(IMAGE), .DATA("rv_data.hex")) dut (.clk(clk), .rst(rst));

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
