// The expressions of Part L3 that Icarus can run as well: ==? and !=?, the bit-counting system functions,
// casts to the built-in types, to a size worked out in parentheses and to an enum, an enum's methods --
// name() printed with %s, first(), last(), num(), next() and prev() round the ends and from a value that
// is no member -- and $random with a seed of the testbench's own and without one, moving the seed on only
// where it is worked out: not in a ?: arm passed over or the right of a decided && or ||, and in both arms
// of an x condition. inside and case inside, which Icarus cannot read, are held by BitSorter's own tests.
// Written for BitSorter; not taken from any course material.

// casts to the built-in types and to an enum, an enum's methods -- name() printed with %s, first(),
// last(), num(), next() and prev() round the ends and from a value that is no member -- and $random with a
// seed of the testbench's own and without one; and a replication of 0 inside a concatenation, which is left
// out (IEEE 1800, 11.4.12.1), so a parameterised extension is written for its identity case too. inside and
// case inside, which Icarus cannot read, are held by BitSorter's own tests. Written for BitSorter; not taken
// from any course material.

module extend #(parameter W = 8, N = 8) (input logic [N-1:0] a, output logic [W-1:0] zeros, signs);
    assign zeros = {{(W-N){1'b0}}, a};
    assign signs = {{(W-N){a[N-1]}}, a};
endmodule

module expressions_tb;
    typedef enum logic [1:0] {IDLE, RUN, DONE} state_t;
    typedef enum {RED, GREEN, BLUE} colour_t;
    localparam W = 4;

    state_t state, other;
    colour_t colour;
    logic [3:0] v, w;
    logic [7:0] b;
    integer seed, r;
    int i;
    int a, c, d, e, f, x;
    logic on = 1, off = 0, unknown = 1'bx;

    logic [7:0] narrow = 8'hA5, same_zeros, same_signs;
    logic [11:0] wide_zeros, wide_signs;

    extend #(.W(8), .N(8)) same (.a(narrow), .zeros(same_zeros), .signs(same_signs));
    extend #(.W(12), .N(8)) wider (.a(narrow), .zeros(wide_zeros), .signs(wide_signs));

    initial begin
        v = 4'b0101;
        w = 4'b01x1;
        $display("wildcard %b %b %b %b", v ==? 4'b0x01, w ==? 4'b0101, v !=? 4'b1x0x, v ==? 4'bzz01);

        b = 8'b1011_0x10;
        $display("counting %0d %b %b %b %b", $countones(b), $onehot(8'b0001_0000), $onehot0(8'b0), $isunknown(b), $isunknown(v));

        $display("casts %0d %0d %0d %0d %0d %0d %0d", int'(8'hF0), 4'(8'h1F), 16'(-8'sd2), byte'(300), bit'(1'bx), int'(4'b1x01), shortint'(-1));

        state = RUN;
        other = state.next();
        $display("enum [%s] [%s] %0d %0d %0d", state.name(), other.name(), state.first(), state.last(), state.num());
        state = DONE;
        other = state.next();
        $display("round [%s] [%s]", state.name(), other.name());
        other = state.prev(2);
        $display("back [%s] %0d", other.name(), other);
        other = state.next(4);
        $display("four on [%s] %0d", other.name(), other);
        state = state_t'(3);
        other = state.next();
        $display("no member [%s] %0d %b", state.name(), state, other);

        colour = BLUE;
        $display("padded [%s] [%10s] [%-6s]", colour.name(), colour.name(), colour.name());

        seed = 7;
        for (i = 0; i < 3; i++) begin
            r = $random(seed);
            $display("seeded %0d %0d", r, seed);
        end

        for (i = 0; i < 3; i++)
            $display("unseeded %0d", $random);

        seed = 3;
        $display("in an expression %0d %0d", $random(seed) % 10, seed);

        seed = 7;
        a = off ? $random(seed) : 5;
        c = on ? 6 : $random(seed);
        d = (off && $random(seed)) ? 1 : 2;
        e = (on || $random(seed)) ? 3 : 4;
        f = on ? $random(seed) : 9;
        $display("passed over %0d %0d %0d %0d %0d %0d", a, c, d, e, f, seed);
        x = unknown ? $random(seed) : 1;
        $display("both arms of x %0d %0d", x, seed);

        // Last, so the lines ModelSim prints otherwise keep their places (BehaviouralOracleTests.ModelSimDiffers).
        $display("sized by an expression %0d %0d %0d %0d", (W+1)'(4'd9), $bits((W+1)'(v)), (W-1)'(4'd13), (2*W)'(v) + 8'd1);

        #1 $display("extended %h %h %h %h %h", same_zeros, same_signs, wide_zeros, wide_signs, {2{{0{narrow[0]}}, narrow[3:0]}});
        #1 $finish;
    end
endmodule
