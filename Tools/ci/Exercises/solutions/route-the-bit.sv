// Route the bit: a solution, from the one its level's own tests proved (Part 15c).

// route_the_bit: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module route_the_bit (
    input logic in,
    output logic binOne,
    output logic binZero
);
    INV #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(in), .Z(binOne));
endmodule
