// Four corners: a solution, from the one its level's own tests proved (Part 15c).

// four_corners: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module four_corners (
    input logic a,
    input logic b,
    input logic c,
    output logic out
);
    logic g1_y, g2_y, g3_y, g4_y, g5_y, g6_y, g7_y;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(b), .Z(g2_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(c), .Z(g3_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g1_y), .B(g2_y), .Z(g4_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(b), .B(g3_y), .Z(g5_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(a), .B(c), .Z(g6_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g7 (.A(g4_y), .B(g5_y), .Z(g7_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g8 (.A(g7_y), .B(g6_y), .Z(out));
endmodule
