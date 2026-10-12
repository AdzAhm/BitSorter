// A full adder from two XORs, two ANDs and an OR, with the carry path shorter than the sum path so
// the two settle at different times. Written for BitSorter's oracle; not taken from any course
// material.
`ifndef BITSORTER_FULL_ADDER_SV
`define BITSORTER_FULL_ADDER_SV

`include "cells.sv"

module full_adder #(
    parameter TPD = 2 // every gate's delay, both ways
) (
    input  logic a,
    input  logic b,
    input  logic cin,
    output logic sum,
    output logic cout
);
    logic half, carry_ab, carry_c;

    XOR2 #(.Tpdlh(TPD), .Tpdhl(TPD)) g1 (.A(a), .B(b), .Z(half));
    XOR2 #(.Tpdlh(TPD), .Tpdhl(TPD)) g2 (.A(half), .B(cin), .Z(sum));
    AND2 #(.Tpdlh(TPD), .Tpdhl(TPD)) g3 (.A(a), .B(b), .Z(carry_ab));
    AND2 #(.Tpdlh(TPD), .Tpdhl(TPD)) g4 (.A(half), .B(cin), .Z(carry_c));
    OR2 #(.Tpdlh(TPD), .Tpdhl(TPD)) g5 (.A(carry_ab), .B(carry_c), .Z(cout));
endmodule

`endif
