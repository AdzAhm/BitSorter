// Find the slow path: a solution, written for BitSorter (Part 15c).

`timescale 1ns/1ns

// find_the_slow_path: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module find_the_slow_path (
    input logic a,
    input logic b,
    input logic c,
    input logic d,
    output logic odd
);
    logic g1_y, g2_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(c), .B(d), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(g2_y), .Z(odd));
endmodule
