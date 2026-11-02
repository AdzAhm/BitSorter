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
    logic [3:0] c, d, u, v, f, g, h, count;
    wire  [3:0] fw = c ^ d;
    logic clk;

    // The slip: from the first edge q follows a + 1, whatever happens at the edges after.
    always @(posedge clk)
        assign q = a + 4'd1;

    // The slip that reads itself: one more at each edge, never a loop -- what it changes does not set it going again.
    always @(posedge clk)
        assign count = count + 4'd1;

    // In one moment: an assign follows what it reads at once, a force a delta later; a deassign or an ignored write keeps
    // what the assign has; nonblocking writes are ignored as blocking ones are; and two assigns reading each other settle.
    initial begin
        c = 4'd1; d = 4'd2; u = 4'd0; v = 4'd0; f = 4'd0; g = 4'd0; h = 4'd0; count = 4'd0;
        #20 assign u = c + d;
        assign v = c + d;
        force f = c + d;
        force fw = c + d;
        #1 $display("%0t u=%0d v=%0d f=%0d fw=%0d", $time, u, v, f, fw);
        c = 4'd5;
        $display("%0t at once: u=%0d, a force not yet: f=%0d fw=%0d", $time, u, f, fw);
        deassign u;
        v = 4'd9;
        v <= 4'd10;
        $display("%0t deassigned: u=%0d, a write ignored: v=%0d", $time, u, v);
        #1 $display("%0t next: u=%0d v=%0d f=%0d fw=%0d", $time, u, v, f, fw);
        release f;
        release fw;
        assign g = h + 4'd1;
        assign h = g + 4'd1;
        #1 $display("%0t two that read each other: g=%0d h=%0d", $time, g, h);
        deassign v;
        deassign g;
        deassign h;
    end

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
        #1  $display("%0t after the first edge: q=%0d count=%0d", $time, q, count);
        #2  clk = 0;
        #5  clk = 1;             // a second edge assigns it again
        #2  a = 4'd2;
        #1  $display("%0t after the second edge: q=%0d count=%0d", $time, q, count);
        #7  $finish;
    end
endmodule
