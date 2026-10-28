// Carry it further: a solution, from the one its level's own tests proved (Part 15c).

// carry_it_further: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module carry_it_further (
    input logic a2,
    input logic a1,
    input logic a0,
    input logic b2,
    input logic b1,
    input logic b0,
    output logic s2,
    output logic s1,
    output logic s0,
    output logic cout
);
    logic g2_y, g6_y, g4_y, g11_y, g7_y, g8_y, g9_y, g10_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a0), .B(b0), .Z(s0));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a1), .B(b1), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g2_y), .B(g6_y), .Z(s1));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(a2), .B(b2), .Z(g4_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(g4_y), .B(g11_y), .Z(s2));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(a0), .B(b0), .Z(g6_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g7 (.A(a1), .B(b1), .Z(g7_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g8 (.A(g2_y), .B(g6_y), .Z(g8_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g9 (.A(a2), .B(b2), .Z(g9_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g10 (.A(g4_y), .B(g11_y), .Z(g10_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g11 (.A(g8_y), .B(g7_y), .Z(g11_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g12 (.A(g10_y), .B(g9_y), .Z(cout));
endmodule
