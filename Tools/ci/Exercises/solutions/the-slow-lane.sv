// The slow lane: a solution, from the one its level's own tests proved (Part 15c).

// the_slow_lane: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module the_slow_lane (
    input logic a,
    input logic b,
    input logic c,
    output logic out
);
    logic g1_y, g2_y, g3_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(g1_y), .B(c), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g2_y), .B(a), .Z(g3_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g3_y), .B(b), .Z(out));
endmodule
