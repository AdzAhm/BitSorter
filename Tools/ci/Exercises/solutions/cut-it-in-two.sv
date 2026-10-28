// Cut it in two: a solution, written for BitSorter (Part 15c).

`timescale 1ns/1ns

// cut_it_in_two: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module cut_it_in_two (
    input logic clk,
    input logic a,
    input logic b,
    output logic out
);
    logic g1_y, g2_y, g3_y, g4_y;
    logic r1_q = 1'b0;
    logic r2_q = 1'b0;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(g1_y), .Z(g2_y));
    always_ff @(posedge clk)
        r1_q <= g2_y;
    INV #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(r1_q), .Z(g3_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g3_y), .Z(g4_y));
    assign out = r2_q;
    always_ff @(posedge clk)
        r2_q <= g4_y;
endmodule
