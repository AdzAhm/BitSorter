// What $display and its kind print: the conversions testbenches use, on known values, signed ones
// and values with x and z bits; default widths, explicit ones and %0; $write continuing a line;
// $strobe and $monitor at the end of a time step, and $monitor switched off and on; %m and a name
// reached through the hierarchy. Written for BitSorter's oracle; not taken from any course material.

module printing_child (
    input  logic [3:0] a,
    output logic [3:0] y
);
    logic [3:0] next;

    assign next = a + 4'd1;
    assign y = next;

    initial #1 $display("child %m sees a=%0d", a);
endmodule

module printing;
    logic [3:0]        a = 4'd5, y;
    logic signed [7:0] s = -8'sd3;
    logic [7:0]        xs = 8'b1x0z_1010;
    logic [31:0]       word = 32'hDEAD_BEEF;
    integer            n = -42;
    logic [7:0]        ch = 8'd66;
    logic [3:0]        c = 4'd0;

    printing_child child (.a(a), .y(y));

    initial begin
        $display("d=%d 0d=%0d h=%h 0h=%0h b=%b o=%o x=%x", a, a, a, a, a, a, a);
        $display("signed d=%d 0d=%0d h=%h b=%b", s, s, s, s);
        $display("unknown d=%d h=%h b=%b o=%o", xs, xs, xs, xs);
        $display("all x %d, one x %d, all z %d", 8'bxxxx_xxxx, 8'b0000_x000, 8'bzzzz_zzzz);
        $display("word %h %0h %d", word, word, word);
        $display("integer %d %0d %h", n, n, n);
        $display("widths %5d|%-5d|%05d|%3h|%8b|", a, a, a, a, a);
        $display("char %c string %s %10s|", ch, "lit", "pad");
        $display("time %t %0t", $time, $time);
        $display("hierarchy %0d %0d", child.next, child.y);
        $display("args", a, " and ", 8'd7, " end");
        $display(a);
        $displayh("hex by default ", word);
        $displayb("binary by default ", a);
        $write("written ");
        $write("on one line %0d", a);
        $display;
        $display("percent %% done");
        #5 $display("after five, t=%0t", $time);
    end

    initial begin
        $monitor("monitor t=%0t c=%0d", $time, c);
        #10 c = 4'd1;
        c = 4'd2;
        #10 c = 4'd3;
        $strobe("strobe t=%0t c=%0d", $time, c);
        c = 4'd4;
        #5 $monitoroff;
        #5 c = 4'd5;
        #5 $monitoron;
        #10 $finish;
    end
endmodule
