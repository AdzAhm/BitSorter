// Out of step: a solution, from the one its level's own tests proved (Part 15c).

// out_of_step: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module out_of_step (
    input logic a,
    input logic b,
    input logic c,
    input logic d,
    output logic out
);
    logic g1_y, g2_y, g3_y;
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(c), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g2_y), .B(d), .Z(g3_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g1_y), .B(g3_y), .Z(out));
endmodule
