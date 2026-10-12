// BitSorter's own small cell library, in the style of the gate libraries a course hands out: one
// module per gate, each with a rise delay (Tpdlh, output low to high) and a fall delay (Tpdhl) that an
// instance can override with #(.Tpdlh(..), .Tpdhl(..)). Written for BitSorter's oracle; not taken
// from any course material. Unless an instance says otherwise every gate is ideal (delay 0).
`ifndef BITSORTER_CELLS_SV
`define BITSORTER_CELLS_SV

module INV (
    input  logic A,
    output logic Z
);
    parameter Tpdlh = 0;
    parameter Tpdhl = 0;
    not #(Tpdlh, Tpdhl) g (Z, A);
endmodule

module AND2 (
    input  logic A,
    input  logic B,
    output logic Z
);
    parameter Tpdlh = 0;
    parameter Tpdhl = 0;
    and #(Tpdlh, Tpdhl) g (Z, A, B);
endmodule

module OR2 (
    input  logic A,
    input  logic B,
    output logic Z
);
    parameter Tpdlh = 0;
    parameter Tpdhl = 0;
    or #(Tpdlh, Tpdhl) g (Z, A, B);
endmodule

module NAND2 (
    input  logic A,
    input  logic B,
    output logic Z
);
    parameter Tpdlh = 0;
    parameter Tpdhl = 0;
    nand #(Tpdlh, Tpdhl) g (Z, A, B);
endmodule

module NOR2 (
    input  logic A,
    input  logic B,
    output logic Z
);
    parameter Tpdlh = 0;
    parameter Tpdhl = 0;
    nor #(Tpdlh, Tpdhl) g (Z, A, B);
endmodule

module XOR2 (
    input  logic A,
    input  logic B,
    output logic Z
);
    parameter Tpdlh = 0;
    parameter Tpdhl = 0;
    xor #(Tpdlh, Tpdhl) g (Z, A, B);
endmodule

`endif
