// Hold when told: a solution, from the one its level's own tests proved (Part 15c).

// hold_when_told: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module hold_when_told (
    input logic d,
    input logic en,
    input logic clk,
    output logic out
);
    logic g1_y, g2_y, g3_y, g4_y;
    logic r1_q = 1'b0;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(en), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(d), .B(en), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(out), .B(g1_y), .Z(g3_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g2_y), .B(g3_y), .Z(g4_y));
    assign out = r1_q;
    always_ff @(posedge clk)
        r1_q <= g4_y;
endmodule
