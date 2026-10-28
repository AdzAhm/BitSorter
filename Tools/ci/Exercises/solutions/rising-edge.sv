// Rising edge: a solution, from the one its level's own tests proved (Part 15c).

// rising_edge: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module rising_edge (
    input logic in,
    input logic clk,
    output logic out
);
    logic r1_q = 1'b0;
    logic g1_y;
    always_ff @(posedge clk)
        r1_q <= in;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(r1_q), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(in), .B(g1_y), .Z(out));
endmodule
