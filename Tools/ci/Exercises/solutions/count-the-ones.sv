// Count the ones: a solution, from the one its level's own tests proved (Part 15c).

// count_the_ones: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module count_the_ones (
    input logic in,
    input logic clk,
    output logic high,
    output logic low
);
    logic g1_y, g3_y, g2_y;
    logic r1_q = 1'b0;
    logic r2_q = 1'b0;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(in), .B(low), .Z(g1_y));
    assign low = r1_q;
    always_ff @(posedge clk)
        r1_q <= g1_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(high), .B(g3_y), .Z(g2_y));
    assign high = r2_q;
    always_ff @(posedge clk)
        r2_q <= g2_y;
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(in), .B(low), .Z(g3_y));
endmodule
