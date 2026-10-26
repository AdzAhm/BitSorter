// counter_tb.sv: a testbench for counter.sv with a free-running clock, a reset at the start, the
// enable dropped for a while part of the way, and a line printed at every wrap. Written for BitSorter's
// import tests (Part 13b); not taken from any course material.
module counter_tb;
    logic       clk = 0;
    logic       rst_n;
    logic       en;
    logic [3:0] count;
    logic       carry;

    counter dut (.clk(clk), .rst_n(rst_n), .en(en), .count(count), .carry(carry));

    always #5 clk = ~clk;

    always @(posedge clk)
        if (en && carry)
            $display("%0d: wraps from %0d", $time, count);

    initial begin
        rst_n = 0;
        en = 0;
        #12 rst_n = 1;
        en = 1;
        #100 en = 0;
        #40 en = 1;
        #200 $stop;
    end
endmodule
