// The procedural continuous assignment, M4 (IEEE 1800, 10.6.1): assign and deassign inside a process. An assign makes a
// variable follow its value -- every other write to it ignored -- until a deassign, which leaves it the value it had; a
// second assign takes the first one's place; a force lies over an assign until it is released, and then the assign shows
// again; a concatenation of variables is assigned whole; and an assign inside a clocked always -- the classic slip, where
// q <= a + 1 was meant -- keeps its variable following from the first edge on. ModelSim runs it as the standard says.
// Icarus 12 reads an assign's right side once, as it runs ("not yet fully supported"), so this design is held to ModelSim,
// and to the standard by BitSorter's own tests (Docs/oracle-notes.md). Written for BitSorter; not taken from any course
// material.

module procedural_assign_tb;
    logic [3:0] a, b, y, r, s, q;
    logic clk;

    // The slip: from the first edge q follows a + 1, whatever happens at the edges after.
    always @(posedge clk)
        assign q = a + 4'd1;

    initial begin
        a = 4'd1; b = 4'd2; y = 4'd0; r = 4'd0; s = 4'd0;
        assign y = a + b;        #1 $display("%0t assigned: y=%0d", $time, y);
        a = 4'd5;                #1 $display("%0t follows a: y=%0d", $time, y);
        y = 4'd9;                #1 $display("%0t a write ignored: y=%0d", $time, y);
        assign y = a - b;        #1 $display("%0t assigned again: y=%0d", $time, y);
        force y = 4'd15;         #1 $display("%0t forced: y=%0d", $time, y);
        a = 4'd8;                #1 $display("%0t still forced: y=%0d", $time, y);
        release y;               #1 $display("%0t released, the assign again: y=%0d", $time, y);
        deassign y;              #1 a = 4'd1;
                                 #1 $display("%0t deassigned, kept: y=%0d", $time, y);
        y = 4'd2;                #1 $display("%0t written: y=%0d", $time, y);
        assign {r, s} = {a, b};  #1 b = 4'd3;
                                 #1 $display("%0t a concatenation: r=%0d s=%0d", $time, r, s);
        force y = 4'd4;
        assign y = 4'd7;         #1 $display("%0t an assign under a force: y=%0d", $time, y);
        release y;               #1 $display("%0t shown on release: y=%0d", $time, y);
        force y = 4'd1;
        deassign y;              #1 $display("%0t deassigned under a force: y=%0d", $time, y);
        release y;               #1 $display("%0t released, kept: y=%0d", $time, y);
        deassign r;
        deassign s;
        b = 4'd5;                #1 $display("%0t deassigned: r=%0d s=%0d", $time, r, s);
    end

    initial begin
        clk = 0;
        #50 clk = 1;             // the first edge: q follows a + 1 from here
        #2  a = 4'd6;
        #1  $display("%0t after the first edge: q=%0d", $time, q);
        #2  clk = 0;
        #5  clk = 1;             // a second edge assigns it again
        #2  a = 4'd2;
        #1  $display("%0t after the second edge: q=%0d", $time, q);
        #7  $finish;
    end
endmodule
