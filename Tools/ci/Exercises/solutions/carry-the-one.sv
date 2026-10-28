// Carry the one: a solution, from the one its level's own tests proved (Part 15c).

// carry_the_one: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module carry_the_one (
    input logic a,
    input logic b,
    input logic cin,
    output logic sum,
    output logic cout
);
    logic g1_y, g2_y, g4_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a), .B(b), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(cin), .Z(sum));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g1_y), .B(cin), .Z(g4_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(g2_y), .B(g4_y), .Z(cout));
endmodule
