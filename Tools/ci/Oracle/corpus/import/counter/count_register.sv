// count_register.sv: a 4-bit register with an enable and an asynchronous, active-low reset, in the
// course's always_ff style. Part of a three-module counter for BitSorter's import tests (Part 13b);
// not taken from any course material.
module count_register (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       en,
    input  logic [3:0] d,
    output logic [3:0] q
);
    always_ff @(posedge clk or negedge rst_n)
        if (!rst_n)
            q <= 4'd0;
        else if (en)
            q <= d;
endmodule
