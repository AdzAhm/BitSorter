// Nothing but NAND: a solution, from the one its level's own tests proved (Part 15c).

// nothing_but_nand: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module nothing_but_nand (
    input logic a,
    input logic b,
    output logic out
);
    logic g1_y, g2_y, g3_y;
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a), .B(g1_y), .Z(g2_y));
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(b), .B(g1_y), .Z(g3_y));
    NAND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g2_y), .B(g3_y), .Z(out));
endmodule
