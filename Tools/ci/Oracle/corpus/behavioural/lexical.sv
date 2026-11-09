// How text is read where the 16d sweep found the reader astray: a string's escapes -- octal and hex codes, a
// tab, a backslash and a quote, the control characters, a string continued onto the next line -- a size and
// its base read apart, a macro's value or argument the size or the base on the next line, a delay's number
// never taken for the size of the value after it, an unnamed gate delayed by a parameter, a `define's
// comment ending in a backslash, which continues the define, and another directive's, which does not, and
// `__LINE__. Run by BehaviouralOracleTests in Icarus and ModelSim alike. Written for BitSorter; not taken
// from any course material.

`timescale 1ns/1ns // set by hand in C:\work\
`define W 8
`define ZERO(w) w'b0
`define DLY #1
`define T 2
`define TWO 1 + // the second term is on the next line \
    1

module lexical_tb;
    parameter D = 3;
    reg [7:0] q;
    reg a, b;
    wire y1, y2, n1;

    and #D (y1, a, b);
    or #D g2 (y2, a, b);
    not #(D) (n1, a);

    initial begin
        $display("A\101B\x42C\tD \\ \"quoted\"");
        $display("%0d %h %h", $bits("\101"), "\033\377", "\v\f\a");
        $display("line %0d", `__LINE__);
        $display("two %0d", `TWO);
        // Icarus 12 does not count the line a string is continued onto, so a `__LINE__ after this is one short
        // there (Docs/oracle-notes.md); none is.
        $display("cont\
inued");

        q = `W'hA5;
        $display("sized by a macro %h", q);
        q = 8
            'h3C;
        $display("base on the next line %h", q);
        q = `ZERO(6) | 8'h81;
        $display("sized by an argument %h", q);

        q = #2 'b1;
        $display("%0t delay then value %h", $time, q);
        q = #2'b11;
        $display("%0t delay then value %h", $time, q);
        q = `DLY 'b0;
        $display("%0t a macro's delay %h", $time, q);
        q = #`T 'hD;
        $display("%0t a macro's number %h", $time, q);
        q = #2
            'hE;
        $display("%0t value on the next line %h", $time, q);
        #(2 'b1) q = 8'h77;
        $display("%0t in brackets one literal %h", $time, q);
        q <= `DLY 'hC;
        #2 $display("%0t nonblocking %h", $time, q);

        a = 1;
        b = 1;
        #1 $display("%0t gates %b %b %b", $time, y1, y2, n1);
        #(D) $display("%0t gates %b %b %b", $time, y1, y2, n1);
        $finish;
    end
endmodule
