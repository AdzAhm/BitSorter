// A 2:1 multiplexer drawn from the cell library: s chooses b when 1 and a when 0. The NAND-NAND
// form, with the inverter slower than the rest, so a change of s alone can glitch the output: the
// static hazard the course's lecture 3a draws. Written for BitSorter's oracle; not taken from any
// course material.
`include "cells.sv"

module mux2 (
    input  logic a, // chosen when s is 0
    input  logic b, // chosen when s is 1
    input  logic s, // the select
    output logic y  // a or b
);
    logic s_n, pick_a, pick_b;

    INV #(.Tpdlh(3), .Tpdhl(3)) g1 (.A(s), .Z(s_n));
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a), .B(s_n), .Z(pick_a));
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(b), .B(s), .Z(pick_b));
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(pick_a), .B(pick_b), .Z(y));
endmodule
