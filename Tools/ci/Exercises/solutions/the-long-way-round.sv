// The long way round: a solution, from the one its level's own tests proved (Part 15c).

// the_long_way_round: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module the_long_way_round (
    input logic a,
    input logic b,
    output logic out
);
    logic g1_y;
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(g1_y), .Z(out));
endmodule
