// Don't care: a solution, from the one its level's own tests proved (Part 15c).

// dont_care: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module dont_care (
    input logic a,
    input logic b,
    input logic c,
    input logic d,
    output logic out
);
    logic g1_y, g2_y, g3_y;
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(b), .B(c), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(b), .B(d), .Z(g2_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(g2_y), .Z(g3_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(a), .B(g3_y), .Z(out));
endmodule
