// Highest wins: a solution, from the one its level's own tests proved (Part 15c).

// highest_wins: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module highest_wins (
    input logic i3,
    input logic i2,
    input logic i1,
    input logic i0,
    output logic y1,
    output logic y0,
    output logic v
);
    logic g2_y, g6_y, g5_y;
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(i3), .B(i2), .Z(y1));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(i1), .B(i0), .Z(g2_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(y1), .B(g2_y), .Z(v));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(i3), .B(g6_y), .Z(y0));
    INV #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(i2), .Z(g5_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(g5_y), .B(i1), .Z(g6_y));
endmodule
