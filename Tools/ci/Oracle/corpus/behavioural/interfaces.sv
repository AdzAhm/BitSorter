// Part L8 as Icarus runs it too: an interface with a parameter and a port, its own signals, an assign and an
// always block inside it, a function and a task, and modports; instances of it with parameters of their own,
// two in a generate loop, and a testbench reaching into them by name -- reading, writing, assigning, calling.
// Interface ports, which Icarus 12 does not read, are held to Icarus another way, in InterfaceOracleTests.
// Written for BitSorter; not taken from any course material.

interface counter_if #(parameter W = 4) (input logic clk, input logic rst);
    logic [W-1:0] count;
    logic step;
    logic wrapped;

    assign wrapped = &count;

    always @(posedge clk)
        if (rst) count <= 0;
        else if (step) count <= count + 1;

    function automatic logic [W:0] doubled();
        return count * 2;
    endfunction

    task automatic report(input int who);
        $display("%0t counter %0d at %0d, wrapped %b", $time, who, count, wrapped);
    endtask

    modport user (output step, input count, wrapped, clk);
    modport observer (input count, wrapped);
endinterface

module interfaces_tb;
    logic clk = 0, rst = 1;
    always #5 clk = ~clk;

    counter_if #(.W(2)) narrow (.clk(clk), .rst(rst));
    counter_if wide (clk, rst);

    // Two more, each stepping on its own condition: the first always, the second each time narrow wraps.
    genvar i;
    for (i = 0; i < 2; i++) begin : lane
        counter_if #(.W(3)) c (.clk(clk), .rst(rst));
        assign c.step = i == 0 || narrow.wrapped;
    end

    initial begin
        narrow.step = 1;
        wide.step = 0;
        #12 rst = 0;
        #40 narrow.report(1);
        wide.report(2);
        wide.step = 1;
        #20 $display("%0d %0d %0d %0d", narrow.count, wide.count, lane[0].c.count, lane[1].c.count);
        $display("%0d %0d %0d", narrow.doubled(), wide.doubled(), $bits(lane[1].c.count));
        lane[1].c.report(4);
        $finish;
    end
endmodule
