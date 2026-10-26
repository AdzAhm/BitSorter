// gates.sv: a small gate library in the style of the ones a course hands out -- one module per gate,
// ports A, B and Z, a rise delay Tpdlh and a fall delay Tpdhl that default to 1. An import uses
// BitSorter's own cells in their place, which match them. Written for BitSorter's import tests
// (Part 13b); not taken from any course material.
module INV (
    input  logic A,
    output logic Z
);
    parameter Tpdlh = 1;
    parameter Tpdhl = 1;
    not #(Tpdlh, Tpdhl) g (Z, A);
endmodule

module NAND2 (
    input  logic A,
    input  logic B,
    output logic Z
);
    parameter Tpdlh = 1;
    parameter Tpdhl = 1;
    nand #(Tpdlh, Tpdhl) g (Z, A, B);
endmodule
