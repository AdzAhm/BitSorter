// Balance the paths: a solution (Part 15g). The buffer delays b, the early path, so both reach the AND together and
// out does not flicker; it still settles in 2 ns.

// balance_the_paths: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module balance_the_paths (
    input logic a,
    input logic b,
    output logic out
);
    logic g1_y, g3_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    BUF #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(b), .Z(g3_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(g1_y), .B(g3_y), .Z(out));
endmodule
