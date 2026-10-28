// One clock late: a solution, from the one its level's own tests proved (Part 15c).

// one_clock_late: drawn in BitSorter.

module one_clock_late (
    input logic in,
    input logic clk,
    output logic out
);
    logic r1_q = 1'b0;
    assign out = r1_q;
    always_ff @(posedge clk)
        r1_q <= in;
endmodule
