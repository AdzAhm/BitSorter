// Flip on one: a solution, from the one its level's own tests proved (Part 15c).

// flip_on_one: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module flip_on_one (
    input logic in,
    input logic clk,
    output logic out
);
    logic r1_q = 1'b0;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(in), .B(r1_q), .Z(out));
    always_ff @(posedge clk)
        r1_q <= out;
endmodule
