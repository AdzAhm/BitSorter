// counter.sv: a 4-bit counter made of the two modules beside it, wired together by name, which says
// when it wraps. An import draws it, with the written register placed in it as a block. Written for
// BitSorter's import tests (Part 13b); not taken from any course material.
module counter (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       en,
    output logic [3:0] count,
    output logic       carry
);
    logic [3:0] next;

    incrementer inc (.d(count), .q(next), .carry(carry));
    count_register r (.clk(clk), .rst_n(rst_n), .en(en), .d(next), .q(count));
endmodule
