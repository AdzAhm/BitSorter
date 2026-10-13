// A sequential multiplier in the shape of the course's second simulation exercise: a Mealy controller
// and a datapath joined in a top module, and a testbench with a clock generator, a reset held for four
// cycles, and a wait on the falling edge of busy after each product. Unsigned operands, a product twice
// as wide, one add-and-shift step a clock. The testbench drives its inputs on the falling edge, so
// nothing it does races the flip-flops, and busy is written once each time the controller's logic
// runs, so it never dips for an instant: Docs/oracle-notes.md says why a busy given a default and then
// set behaves differently in Icarus. Written for BitSorter; not taken from any course material.

module mult_control #(
    parameter STEPS = 8
) (
    input  logic clk,
    input  logic rst,     // asynchronous, active high
    input  logic start,   // begin a multiplication
    output logic load,    // the datapath takes its operands
    output logic step,    // the datapath adds and shifts once
    output logic busy     // a multiplication is under way
);
    typedef enum logic {IDLE, RUN} state_t;

    state_t state, state_next;
    logic [3:0] count, count_next;

    always_ff @(posedge clk, posedge rst) begin
        if (rst) begin
            state <= IDLE;
            count <= 4'd0;
        end else begin
            state <= state_next;
            count <= count_next;
        end
    end

    // A Mealy machine: load answers start in the cycle it arrives. busy is a Moore output, written
    // once from the state rather than given a default and then set.
    always_comb begin
        state_next = state;
        count_next = count;
        load = 1'b0;
        step = 1'b0;
        busy = state == RUN;

        case (state)
            IDLE: begin
                if (start) begin
                    load = 1'b1;
                    count_next = 4'd0;
                    state_next = RUN;
                end
            end
            RUN: begin
                step = 1'b1;
                count_next = count + 4'd1;
                if (count == STEPS - 1)
                    state_next = IDLE;
            end
        endcase
    end
endmodule

module mult_datapath #(
    parameter W = 8
) (
    input  logic           clk,
    input  logic           load,      // take a and b
    input  logic           step,      // add if the lowest multiplier bit is 1, then shift right
    input  logic [W-1:0]   a,         // the multiplicand
    input  logic [W-1:0]   b,         // the multiplier
    output logic [2*W-1:0] product
);
    logic [W-1:0] multiplicand;
    logic [W:0]   high;   // the running sum, with room for its carry
    logic [W-1:0] low;    // the multiplier, shifted out as the product shifts in
    logic [W:0]   sum;

    assign sum = low[0] ? high + {1'b0, multiplicand} : high;
    assign product = {high[W-1:0], low};

    always_ff @(posedge clk) begin
        if (load) begin
            multiplicand <= a;
            high <= '0;
            low <= b;
        end else if (step) begin
            {high, low} <= {1'b0, sum, low[W-1:1]};
        end
    end
endmodule

module multiplier #(
    parameter W = 8
) (
    input  logic           clk,
    input  logic           rst,
    input  logic           start,
    input  logic [W-1:0]   a,
    input  logic [W-1:0]   b,
    output logic           busy,
    output logic [2*W-1:0] product
);
    logic load, step;

    mult_control #(.STEPS(W)) control (
        .clk(clk),
        .rst(rst),
        .start(start),
        .load(load),
        .step(step),
        .busy(busy)
    );

    mult_datapath #(.W(W)) datapath (
        .clk(clk),
        .load(load),
        .step(step),
        .a(a),
        .b(b),
        .product(product)
    );
endmodule

module multiplier_tb;
    logic        clk, rst, start;
    logic [7:0]  a, b;
    logic        busy;
    logic [15:0] product;

    multiplier #(.W(8)) dut (
        .clk(clk),
        .rst(rst),
        .start(start),
        .a(a),
        .b(b),
        .busy(busy),
        .product(product)
    );

    initial clk = 1'b0;
    always #5 clk = ~clk;

    initial begin
        rst = 1'b1;
        start = 1'b0;
        a = 8'd0;
        b = 8'd0;
        repeat (4) @(posedge clk);
        @(negedge clk) rst = 1'b0;

        @(negedge clk) begin a = 8'd13; b = 8'd11; start = 1'b1; end
        @(negedge clk) start = 1'b0;
        @(negedge busy);

        @(negedge clk) begin a = 8'd255; b = 8'd255; start = 1'b1; end
        @(negedge clk) start = 1'b0;
        @(negedge busy);

        @(negedge clk) begin a = 8'd170; b = 8'd0; start = 1'b1; end
        @(negedge clk) start = 1'b0;
        @(negedge busy);

        @(negedge clk) begin a = 8'd1; b = 8'd200; start = 1'b1; end
        @(negedge clk) start = 1'b0;
        @(negedge busy);

        repeat (2) @(posedge clk);
        $finish;
    end
endmodule
