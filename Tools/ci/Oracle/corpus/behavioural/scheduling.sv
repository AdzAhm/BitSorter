// Where processes run relative to each other: the questions the standard leaves to the simulator,
// answered as Icarus answers them, and the ones it settles, held to it. One block per question.
// Written for BitSorter's oracle; not taken from any course material. Docs/oracle-notes.md says what
// each one showed.

module scheduling_flop (
    input  logic clk,
    input  logic rst,
    input  logic d,
    output logic q
);
    always_ff @(posedge clk, posedge rst)
        if (rst) q <= 1'b0;
        else q <= d;
endmodule

module scheduling_waiter (
    input  logic r,
    output logic q
);
    always @(posedge r) q = 1'b1;
endmodule

module scheduling;
    // S1: an initial written before an always, in one module: the always is not waiting yet when
    // the initial raises its signal at time 0, so it never sees the edge.
    logic r1, q1;
    initial r1 = 1'b1;
    always @(posedge r1) q1 = 1'b1;

    // S2: the same written the other way round: the always is already waiting.
    logic r2, q2;
    always @(posedge r2) q2 = 1'b1;
    initial r2 = 1'b1;

    // S3: an instance written after the initial still starts first, so a reset raised at time 0
    // reaches its flip-flop.
    logic clk = 1'b0, r3, d3 = 1'b1, q3;
    initial r3 = 1'b1;
    scheduling_flop f3 (.clk(clk), .rst(r3), .d(d3), .q(q3));

    logic r4, q4;
    initial r4 = 1'b1;
    scheduling_waiter w4 (.r(r4), .q(q4));

    // S5: nonblocking assignments swap; blocking ones copy.
    logic s5a = 1'b0, s5b = 1'b1, b5a = 1'b0, b5b = 1'b1;
    always @(posedge clk) begin s5a <= s5b; s5b <= s5a; end
    always @(posedge clk) begin b5a = b5b; b5b = b5a; end
    initial #5 clk = 1'b1;

    // S6: always_comb runs once at time 0; always @* waits for a change.
    logic in6 = 1'b0, comb6, star6;
    always_comb comb6 = ~in6;
    always @* star6 = ~in6;
    initial #30 in6 = 1'b1;

    // S7: #0 lets the rest of the instant run first.
    logic h7, g7;
    initial begin #20 h7 = 1'b1; #0 g7 = h7; end
    initial begin #20 h7 = 1'b0; end

    // S8: an intra-assignment delay takes the value now and writes it later.
    logic b8 = 1'b0, a8, d8 = 1'b0, q8;
    initial a8 = #45 b8;
    initial q8 <= #43 d8;
    initial begin #41 b8 = 1'b1; d8 = 1'b1; end

    // S9: a delay that works out negative is read as an unsigned time (IEEE 1800, 9.4.1), so long it
    // never comes due: q9 stays 0.
    integer d9 = -5;
    logic q9 = 1'b0;
    initial #d9 q9 = 1'b1;

    // S10: two nonblocking writes to one variable in one time step land in turn, each its own update
    // (IEEE 1800, 10.4.2): the fall between wakes the negedge, the rise after it the posedge, and both
    // read the value the last write left. Writes to bits apart land together: no value between.
    logic a10 = 1'b1, neg10 = 1'b0, pos10 = 1'b0, saw10;
    always @(negedge a10) begin neg10 = 1'b1; saw10 = a10; end
    always @(posedge a10) pos10 = 1'b1;
    initial begin #12 a10 <= 1'b0; a10 <= 1'b1; end
    logic [1:0] v10;
    integer across10 = 0;
    always @(v10[1] ^ v10[0]) across10 = across10 + 1;
    initial begin #13 v10 = 2'b01; #1 v10[0] <= 1'b0; v10[1] <= 1'b1; end

    initial #50 $finish;
endmodule
