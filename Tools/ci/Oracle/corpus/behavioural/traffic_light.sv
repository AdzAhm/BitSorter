// A behavioural Moore machine and its testbench, in the shape the course writes state machines:
// states in a typedef enum, the state held in always_ff with an asynchronous reset, the next state
// and the outputs worked out in always_comb with defaults first. A traffic light that holds green
// for a configurable number of cycles. Written for BitSorter; not taken from any course material.

module traffic_light #(
    parameter GREEN_CYCLES = 4
) (
    input  logic clk,
    input  logic reset,   // asynchronous, active high
    input  logic walk,    // a pedestrian pressed the button
    output logic red,
    output logic amber,
    output logic green
);
    typedef enum logic [1:0] {RED, RED_AMBER, GREEN, AMBER} light_t;

    light_t state, state_next;
    logic [3:0] count;

    always_ff @(posedge clk, posedge reset) begin
        if (reset)
            state <= RED;
        else
            state <= state_next;
    end

    always_ff @(posedge clk, posedge reset) begin
        if (reset)
            count <= 4'd0;
        else if (state != GREEN)
            count <= 4'd0;
        else
            count <= count + 4'd1;
    end

    always_comb begin
        state_next = state;
        red = 1'b0;
        amber = 1'b0;
        green = 1'b0;

        case (state)
            RED: begin
                red = 1'b1;
                if (!walk) state_next = RED_AMBER;
            end
            RED_AMBER: begin
                red = 1'b1;
                amber = 1'b1;
                state_next = GREEN;
            end
            GREEN: begin
                green = 1'b1;
                if (walk || count == GREEN_CYCLES - 1) state_next = AMBER;
            end
            default: begin
                amber = 1'b1;
                state_next = RED;
            end
        endcase
    end
endmodule

module traffic_light_tb;
    logic clk, reset, walk;
    logic red, amber, green;

    traffic_light #(.GREEN_CYCLES(3)) dut (
        .clk(clk),
        .reset(reset),
        .walk(walk),
        .red(red),
        .amber(amber),
        .green(green)
    );

    initial begin
        clk = 1'b0;
        forever #5 clk = ~clk;
    end

    initial begin
        reset = 1'b1;
        walk = 1'b0;
        #12 reset = 1'b0;
        repeat (8) @(posedge clk);
        walk = 1'b1;
        @(posedge clk);
        walk = 1'b0;
        repeat (6) @(posedge clk);
        $stop;
    end
endmodule
