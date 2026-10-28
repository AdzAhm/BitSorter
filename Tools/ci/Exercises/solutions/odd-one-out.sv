// Odd one out: a solution, from the one its level's own tests proved (Part 15c).

// odd_one_out: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module odd_one_out (
    input logic a,
    input logic b,
    input logic c,
    input logic d,
    output logic odd
);
    logic g1_y, g2_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(c), .B(d), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(g2_y), .Z(odd));
endmodule
