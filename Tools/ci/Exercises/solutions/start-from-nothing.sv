// Start from nothing: a solution, written for BitSorter (Part 15c).

`timescale 1ns/1ns

// start_from_nothing: drawn in BitSorter.

module start_from_nothing (
    input logic clk,
    input logic rst,
    input logic d,
    output logic q
);
    always_ff @(posedge clk, posedge rst)
        if (rst)
            q <= 1'b0;
        else
            q <= d;
endmodule
