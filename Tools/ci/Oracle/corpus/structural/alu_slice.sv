// One bit of an ALU: op chooses AND, OR, add or subtract (b inverted, the carry in set by the
// first slice). Built from continuous assigns and a full adder, so a slice is structural and
// carries its own delays. Written for BitSorter's oracle; not taken from any course material.
`ifndef BITSORTER_ALU_SLICE_SV
`define BITSORTER_ALU_SLICE_SV

`include "full_adder.sv"

module alu_slice (
    input  logic       a,
    input  logic       b,
    input  logic       cin,
    input  logic [1:0] op,   // 00 and, 01 or, 10 add, 11 subtract
    output logic       y,
    output logic       cout
);
    logic b_in, sum;

    assign b_in = op[0] & op[1] ? ~b : b;

    full_adder #(.TPD(1)) fa (.a(a), .b(b_in), .cin(cin), .sum(sum), .cout(cout));

    assign y = op[1] ? sum : (op[0] ? a | b : a & b);
endmodule

`endif
