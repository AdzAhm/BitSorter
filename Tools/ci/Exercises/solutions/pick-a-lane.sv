// Pick a lane: a solution, from the one its level's own tests proved (Part 15c).

// pick_a_lane: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module pick_a_lane (
    input logic a,
    input logic b,
    input logic s,
    output logic out
);
    logic g1_y, g2_y, g3_y;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(s), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a), .B(g1_y), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(b), .B(s), .Z(g3_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g2_y), .B(g3_y), .Z(out));
endmodule
