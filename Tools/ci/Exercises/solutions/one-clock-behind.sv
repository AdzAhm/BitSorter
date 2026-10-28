// One clock behind: a solution, from the one its level's own tests proved (Part 15c).

// one_clock_behind: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module one_clock_behind (
    input logic in,
    input logic clk,
    output logic out
);
    logic r1_q = 1'b0;
    logic g1_y, g2_y;
    logic r2_q = 1'b0;
    always_ff @(posedge clk)
        r1_q <= in;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(r1_q), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(in), .B(g1_y), .Z(g2_y));
    assign out = r2_q;
    always_ff @(posedge clk)
        r2_q <= g2_y;
endmodule
