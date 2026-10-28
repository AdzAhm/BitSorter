// One of four: a solution, from the one its level's own tests proved (Part 15c).

// one_of_four: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module one_of_four (
    input logic a,
    input logic b,
    output logic d0,
    output logic d1,
    output logic d2,
    output logic d3
);
    logic g1_y, g2_y;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(b), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(g2_y), .Z(d0));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g1_y), .B(b), .Z(d1));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(a), .B(g2_y), .Z(d2));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(a), .B(b), .Z(d3));
endmodule
