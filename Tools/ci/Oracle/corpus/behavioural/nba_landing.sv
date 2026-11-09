// The NBA region's updates landing one at a time (IEEE 1800, 10.4.2): two nonblocking writes to one variable in one
// moment make a value between, and what waits on it is or is not woken by it. A wait on a variable, a select of one by
// constant bounds, or a concatenation of them is woken by the value between; a wait on any other expression -- an
// operator, a cast, a memory's word -- and a wait statement see only what the region left. Writes to bits apart land
// together, whatever order they are written in. The standard leaves open which waits see a value between, and Icarus 12
// settles seven of these otherwise -- it wakes none of the selects, and four of the expressions (Docs/oracle-notes.md) --
// so this design is held to ModelSim, as BitSorter runs it.
// Written for BitSorter; not taken from any course material.

module nba_landing_tb;
    logic [7:0] v, s, w, u;
    logic [3:0] count;
    logic [3:0] m [0:3];
    logic a, b, c, e, p, q, r;
    int i;
    logic vx = 0, ab = 0, sel = 0, selpos = 0, part = 0, whole = 0, lsb = 0, cat = 0;
    logic eq = 0, word = 0, wordi = 0, mixed = 0, cast = 0, inv = 0, waited = 0, pr = 0, qany = 0, qneg = 0;

    // Each waits from 7, after the values are set, and marks whether the nonblocking writes at 10 woke it.
    initial #7 @(v[3] ^ v[0]) vx = 1;
    initial #7 @(a ^ b) ab = 1;
    initial #7 @(s[3]) sel = 1;
    initial #7 @(posedge s[3]) selpos = 1;
    initial #7 @(w[3:2]) part = 1;
    initial #7 @(s) whole = 1;
    initial #7 @(posedge u) lsb = 1;
    initial #7 @({c, e}) cat = 1;
    initial #7 @(count == 4'd5) eq = 1;
    initial #7 @(m[1]) word = 1;
    initial #7 @(m[i]) wordi = 1;
    initial #7 @(posedge e or (a ^ b)) mixed = 1;
    initial #7 @($signed(w)) cast = 1;
    initial #7 @(~c) inv = 1;
    initial #7 @(p ^ r) pr = 1;
    initial #7 @(q) qany = 1;
    initial #7 @(negedge q) qneg = 1;
    initial #7 wait (a == 0) waited = 1;

    initial begin
        #5 v = 8'b0000_0001; s = 8'h08; w = 8'h08; u = 8'h01; count = 4'd5; m[1] = 4'h3; i = 1;
        a = 1; b = 0; c = 1; e = 1; p = 0; q = 1; r = 0;

        // Bits apart, the last between the first two; a value and back; a whole value and one bit of it.
        #5 v[0] <= 1'b0; v[7] <= 1'b0; v[3] <= 1'b1;
        a <= 0; a <= 1;
        s <= 8'h00; s[3] <= 1'b1;
        w <= 8'h00; w[3] <= 1'b1;
        u <= 8'h00; u <= 8'h01;
        count <= 4'd0; count <= 4'd5;
        m[1] <= 4'h0; m[1] <= 4'h3;
        c <= 0; c <= 1;
        p <= 1; q <= 0; q <= 1;

        #10 $display("between: sel=%b selpos=%b part=%b whole=%b lsb=%b cat=%b qany=%b qneg=%b",
                      sel, selpos, part, whole, lsb, cat, qany, qneg);
        $display("left: vx=%b ab=%b eq=%b word=%b wordi=%b mixed=%b cast=%b inv=%b waited=%b pr=%b",
                 vx, ab, eq, word, wordi, mixed, cast, inv, waited, pr);
        $display("v=%b s=%h w=%h u=%h count=%0d m1=%h", v, s, w, u, count, m[1]);
        $finish;
    end
endmodule
