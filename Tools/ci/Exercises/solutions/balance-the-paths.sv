// Balance the paths: a solution, from the one its level's own tests proved (Part 15c).

// balance_the_paths: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module balance_the_paths (
    input logic a,
    input logic b,
    output logic out
);
    logic g1_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(g1_y), .B(b), .Z(out));
endmodule
