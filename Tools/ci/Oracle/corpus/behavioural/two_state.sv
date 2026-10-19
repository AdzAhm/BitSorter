// The 2-state types beside the 4-state ones: a bit, a byte, an int, and a state machine whose
// `typedef enum {...}` names no base type and is therefore an int -- so its state reads its first
// name, not x, before the reset arrives. Then x and z written into each, which a 2-state type holds
// as 0, set against a logic that keeps them. Written for BitSorter; not taken from any course material.

module parity_watch (
    input  logic clk,
    input  logic reset,   // asynchronous, active high
    input  logic in,
    output logic odd
);
    typedef enum {EVEN, ODD} parity_t;

    parity_t state, state_next;

    always_ff @(posedge clk, posedge reset)
        if (reset) state <= EVEN;
        else       state <= state_next;

    always_comb begin
        state_next = state;
        if (in) state_next = (state == EVEN) ? ODD : EVEN;
    end

    assign odd = state == ODD;
endmodule

module two_state_tb;
    logic clk, reset, in, odd;
    bit       flag;
    bit [3:0] nibble;
    byte      octet;
    int       count;
    logic [3:0] four;

    parity_watch dut (.clk(clk), .reset(reset), .in(in), .odd(odd));

    initial begin
        clk = 0;
        forever #5 clk = ~clk;
    end

    initial begin
        reset = 0; in = 1;
        // Before any reset: the int-based state is EVEN (0), the bit types are 0, the logic is x.
        #1 $display("before %0d %b %b %b %0d %0d %b", dut.state, odd, flag, nibble, octet, count, four);

        nibble = 4'b1x0z; four = 4'b1x0z;
        octet = 8'bxxxx_0110; count = 'z; flag = 1'bx;
        #1 $display("x and z %b %b %b %0d %b", flag, nibble, octet, count, four);

        #2 reset = 1;
        #3 reset = 0;
        repeat (4) @(negedge clk) begin
            count = count + 1;
            $display("%0d %0d %b", count, dut.state, odd);
        end
        $finish;
    end
endmodule
