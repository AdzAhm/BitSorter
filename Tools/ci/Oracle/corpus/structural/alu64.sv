// A 64-bit ALU from 64 slices in a generate loop, the carry rippling through a 65-bit vector
// (carry[64] is the carry out), as wide as anything the course writes. Written for BitSorter's
// oracle; not taken from any course material.
`include "alu_slice.sv"

module alu64 #(
    parameter N = 64
) (
    input  logic [N-1:0] a,
    input  logic [N-1:0] b,
    input  logic [1:0]   op,
    output logic [N-1:0] y,
    output logic         carry_out
);
    logic [N:0] carry;

    // Subtracting adds the inverse of b plus one.
    assign carry[0] = op[1] & op[0];

    genvar i;
    generate
        for (i = 0; i < N; i++) begin : slice
            alu_slice s (
                .a(a[i]),
                .b(b[i]),
                .cin(carry[i]),
                .op(op),
                .y(y[i]),
                .cout(carry[i + 1])
            );
        end
    endgenerate

    assign carry_out = carry[N];
endmodule
