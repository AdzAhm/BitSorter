// Pass it on: a solution, from the one its level's own tests proved (Part 15c).

// pass_it_on: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module pass_it_on (
    input logic a1,
    input logic a0,
    input logic b1,
    input logic b0,
    output logic s1,
    output logic s0,
    output logic cout
);
    logic g2_y, g3_y, g4_y, g6_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a0), .B(b0), .Z(s0));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a0), .B(b0), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(a1), .B(b1), .Z(g3_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(a1), .B(b1), .Z(g4_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(g3_y), .B(g2_y), .Z(s1));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(g3_y), .B(g2_y), .Z(g6_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g7 (.A(g6_y), .B(g4_y), .Z(cout));
endmodule
