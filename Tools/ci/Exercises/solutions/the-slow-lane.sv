// The slow lane: a solution (Part 15g). out = (((a ^ b) ^ c) & a) | b simplifies to b | (a & ~c), and a & ~c is
// a & (a ^ c): three gates in a row where the expression as written takes four.

// the_slow_lane: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module the_slow_lane (
    input logic a,
    input logic b,
    input logic c,
    output logic out
);
    logic g1_y, g3_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(c), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(a), .Z(g3_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g3_y), .B(b), .Z(out));
endmodule
