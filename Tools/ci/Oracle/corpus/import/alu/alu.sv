// alu.sv: an 8-bit behavioural ALU in the course's style -- an always_comb case on the operation, its
// codes from an included file, and a zero flag. An import keeps it as written code and the include as
// one of the project's files. Written for BitSorter's import tests (Part 13b); not taken from any
// course material.
module alu (
    input  logic [7:0] a,
    input  logic [7:0] b,
    input  logic [2:0] op,
    output logic [7:0] y,
    output logic       zero
);
    `include "alu_ops.svh"

    always_comb begin
        case (op)
            OP_ADD:  y = a + b;
            OP_SUB:  y = a - b;
            OP_AND:  y = a & b;
            OP_OR:   y = a | b;
            OP_XOR:  y = a ^ b;
            OP_SLT:  y = {7'b0, $signed(a) < $signed(b)};
            OP_SHL:  y = a << b[2:0];
            default: y = a >> b[2:0];
        endcase
    end

    assign zero = (y == 8'd0);
endmodule
