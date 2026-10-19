// The expressions of Part L3 that Icarus can run as well: ==? and !=?, the bit-counting system functions,
// casts to the built-in types and to an enum, an enum's methods -- name() printed with %s, first(),
// last(), num(), next() and prev() round the ends and from a value that is no member -- and $random with a
// seed of the testbench's own and without one. inside and case inside, which Icarus cannot read, are held
// by BitSorter's own tests. Written for BitSorter; not taken from any course material.

module expressions_tb;
    typedef enum logic [1:0] {IDLE, RUN, DONE} state_t;
    typedef enum {RED, GREEN, BLUE} colour_t;

    state_t state, other;
    colour_t colour;
    logic [3:0] v, w;
    logic [7:0] b;
    integer seed, r;
    int i;

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
        #1 $finish;
    end
endmodule
