// Add as you go: a solution, from the one its level's own tests proved (Part 15c).

// add_as_you_go: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module add_as_you_go (
    input logic a,
    input logic b,
    input logic clk,
    output logic sum
);
    logic g1_y, g3_y, g4_y, g5_y;
    logic r1_q = 1'b0;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(g1_y), .B(r1_q), .Z(sum));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(a), .B(b), .Z(g3_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g1_y), .B(r1_q), .Z(g4_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(g3_y), .B(g4_y), .Z(g5_y));
    always_ff @(posedge clk)
        r1_q <= g5_y;
endmodule
