// Half adder: a solution, from the one its level's own tests proved (Part 15c).

// half_adder: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module half_adder (
    input logic a,
    input logic b,
    output logic sum,
    output logic carry
);
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(sum));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a), .B(b), .Z(carry));
endmodule
