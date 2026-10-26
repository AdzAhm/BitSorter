// alu_tb.sv: a testbench for alu.sv in the course's style -- operands set one after another with a
// delay between, each result printed, and $stop at the end. It says its timescale, which an import
// takes as the project's unit. Written for BitSorter's import tests (Part 13b); not taken from any
// course material.
`timescale 1ns/1ns

module alu_tb;
    logic [7:0] a, b;
    logic [2:0] op;
    logic [7:0] y;
    logic       zero;

    alu uut (.a(a), .b(b), .op(op), .y(y), .zero(zero));

    initial begin
        a = 8'd23;
        b = 8'd19;
        for (int i = 0; i < 8; i++) begin
            op = i[2:0];
            #10 $display("%0d: op %0d gives %0d, zero %b", $time, op, y, zero);
        end
        a = 8'd5;
        b = 8'd5;
        op = 3'd1;
        #10 $display("%0d: 5 - 5 = %0d, zero %b", $time, y, zero);
        a = 8'hf0;
        b = 8'h0f;
        op = 3'd2;
        #10 $display("%0d: f0 & 0f = %h, zero %b", $time, y, zero);
        $stop;
    end
endmodule
