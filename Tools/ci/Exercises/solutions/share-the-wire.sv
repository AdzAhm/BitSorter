// Share the wire: a solution, written for BitSorter (Part 15c).

`timescale 1ns/1ns

// share_the_wire: drawn in BitSorter.

module share_the_wire (
    input logic a,
    input logic ea,
    input logic b,
    input logic eb,
    output tri logic bus
);
    bufif1 #(1, 1) g1 (bus, a, ea);
    bufif1 #(1, 1) g2 (bus, b, eb);
endmodule
