// Keep in step: a solution, written for BitSorter (Part 15c).

`timescale 1ns/1ns

// keep_in_step: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module keep_in_step (
    input logic clk,
    input logic a,
    input logic b,
    output logic out
);
    logic r1_q = 1'b0;
    logic r2_q = 1'b0;
    logic r4_q = 1'b0;
    logic r3_q = 1'b0;
    always_ff @(posedge clk)
        r1_q <= a;
    always_ff @(posedge clk)
        r2_q <= r1_q;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(r2_q), .B(r4_q), .Z(out));
    always_ff @(posedge clk)
        r3_q <= b;
    always_ff @(posedge clk)
        r4_q <= r3_q;
endmodule
