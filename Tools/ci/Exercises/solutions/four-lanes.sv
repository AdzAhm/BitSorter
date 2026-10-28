// Four lanes: a solution, from the one its level's own tests proved (Part 15c).

// four_lanes: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module four_lanes (
    input logic d0,
    input logic d1,
    input logic d2,
    input logic d3,
    input logic s1,
    input logic s0,
    output logic out
);
    logic g1_y, g2_y, g3_y, g4_y, g5_y, g6_y, g7_y, g8_y, g9_y, g10_y;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(s0), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(s1), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(d0), .B(g1_y), .Z(g3_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(d1), .B(s0), .Z(g4_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(d2), .B(g1_y), .Z(g5_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(d3), .B(s0), .Z(g6_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g7 (.A(g3_y), .B(g4_y), .Z(g7_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g8 (.A(g5_y), .B(g6_y), .Z(g8_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g9 (.A(g7_y), .B(g2_y), .Z(g9_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g10 (.A(g8_y), .B(s1), .Z(g10_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g11 (.A(g9_y), .B(g10_y), .Z(out));
endmodule
