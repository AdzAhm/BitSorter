// Which is bigger: a solution, from the one its level's own tests proved (Part 15c).

// which_is_bigger: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module which_is_bigger (
    input logic a,
    input logic b,
    output logic lt,
    output logic eq,
    output logic gt
);
    logic g1_y, g2_y;
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .Z(g1_y));
    INV #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(b), .Z(g2_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(b), .Z(lt));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(a), .B(g2_y), .Z(gt));
    NOR2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(lt), .B(gt), .Z(eq));
endmodule
