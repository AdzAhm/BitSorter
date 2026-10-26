// incrementer.sv: one more than a 4-bit value, with the carry out, as a single continuous assignment.
// Part of a three-module counter for BitSorter's import tests (Part 13b); not taken from any course
// material.
module incrementer (
    input  logic [3:0] d,
    output logic [3:0] q,
    output logic       carry
);
    assign {carry, q} = d + 5'd1;
endmodule
