// What waits on a function's call: a function that reads a signal it is not given, called from an
// always_comb, an always @* and a continuous assignment. When that signal changes, only the always_comb
// is evaluated again; the other two wait on the call's arguments alone (IEEE 1800, 9.2.2.2.1), so they
// keep their old value until an argument changes. Written for BitSorter; not taken from any course material.

module function_sensitivity;
    logic       flip;
    logic [3:0] v;
    logic [3:0] by_comb, by_star, by_assign;

    function logic [3:0] maybe_flipped(input logic [3:0] x);
        maybe_flipped = flip ? ~x : x;
    endfunction

    always_comb by_comb = maybe_flipped(v);
    always @*   by_star = maybe_flipped(v);
    assign by_assign = maybe_flipped(v);

    initial begin
        flip = 0;
        v = 4'b0011;
        #1 $display("%b %b %b", by_comb, by_star, by_assign);
        flip = 1;
        #1 $display("%b %b %b", by_comb, by_star, by_assign);
        v = 4'b0101;
        #1 $display("%b %b %b", by_comb, by_star, by_assign);
        $finish;
    end
endmodule
