// Functions and tasks as a testbench and a design use them: a function in a continuous assignment and
// in an always_comb, one reading a module signal it is not given, one with a loop and locals, an
// automatic one that calls itself, a static one that keeps a local between calls, the old style of
// argument, a default argument; a task that waits on the clock and gives values back, called from two
// places, and a void function that prints. Written for BitSorter; not taken from any course material.

module checksum #(parameter WIDTH = 8) (
    input  logic [WIDTH-1:0] data,
    input  logic             invert,
    output logic [3:0]       ones,
    output logic             parity,
    output logic [WIDTH-1:0] shown
);
    // How many bits are 1: a loop over the bits, with a local count.
    function automatic logic [3:0] count_ones(input logic [WIDTH-1:0] value);
        logic [3:0] n;
        n = 0;
        for (int i = 0; i < WIDTH; i++)
            n = n + value[i];
        return n;
    endfunction

    // The old style: the argument declared in the body, the result given by the function's own name.
    function odd;
        input [WIDTH-1:0] value;
        odd = ^value;
    endfunction

    // Reads invert, which it is not given: a call is worked out again when invert changes too.
    function logic [WIDTH-1:0] maybe_inverted(input logic [WIDTH-1:0] value);
        maybe_inverted = invert ? ~value : value;
    endfunction

    assign ones = count_ones(data);
    assign shown = maybe_inverted(data);

    always_comb
        parity = odd(data);
endmodule

module subroutines_tb;
    logic clk;
    logic [7:0] data;
    logic invert;
    logic [3:0] ones;
    logic parity;
    logic [7:0] shown;
    logic [7:0] got_a, got_b;

    checksum #(.WIDTH(8)) dut (.data(data), .invert(invert), .ones(ones), .parity(parity), .shown(shown));

    // An automatic function that calls itself.
    function automatic int factorial(input int n);
        if (n <= 1)
            return 1;
        return n * factorial(n - 1);
    endfunction

    // A static function: its local keeps its value from one call to the next.
    function int next_ticket();
        int issued;
        issued = issued + 1;
        return issued;
    endfunction

    // A default argument.
    function automatic logic [7:0] scaled(input logic [7:0] value, input int by = 2);
        return value * by;
    endfunction

    // Waits for the clock, puts a value on data, and gives back what came out after the next edge.
    task automatic apply(input logic [7:0] value, input logic flip, output logic [7:0] seen);
        @(negedge clk);
        data = value;
        invert = flip;
        @(posedge clk);
        #1 seen = shown;
    endtask

    function void report(input logic [7:0] value, input logic [3:0] count);
        $display("%0t value %b has %0d ones, parity %b", $time, value, count, ^value);
    endfunction

    initial begin
        clk = 0;
        forever #5 clk = ~clk;
    end

    initial begin
        data = 0;
        invert = 0;
        #1 report(data, ones);

        apply(8'b1011_0010, 0, got_a);
        report(data, ones);
        apply(8'b1111_0000, 1, got_b);
        report(data, ones);
        $display("got %b and %b, parity %b", got_a, got_b, parity);

        $display("factorials %0d %0d %0d", factorial(1), factorial(5), factorial(10));
        $display("tickets %0d %0d %0d", next_ticket(), next_ticket(), next_ticket());
        $display("scaled %0d %0d", scaled(8'd21), scaled(8'd21, 3));
        invert = 0;
        #1 $display("shown %b", shown);
        $finish;
    end
endmodule
