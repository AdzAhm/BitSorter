// mux2_tb.sv: a testbench for mux2.sv that walks the inputs through each case, long enough apart for
// every gate to settle, and stops. The select falling while a and b are both 1 is where the gates'
// delays show. Written for BitSorter's import tests (Part 13b); not taken from any course material.
module mux2_tb;
    logic a, b, s;
    logic y;

    mux2 dut (.a(a), .b(b), .s(s), .y(y));

    initial begin
        a = 0;
        b = 0;
        s = 0;
        #10 a = 1;
        #10 s = 1;
        #10 b = 1;
        #10 s = 0;
        #10 a = 0;
        #10 s = 1;
        #10 b = 0;
        #10 $stop;
    end
endmodule
