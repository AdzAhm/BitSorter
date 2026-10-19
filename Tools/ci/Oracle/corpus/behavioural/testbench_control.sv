// Testbench control, Part L5, as Icarus can run it as well: fork with join, join_any and join_none, a
// fork inside a fork, wait fork and disable fork; disable of a named block from inside it, of a named
// fork from outside it, and of a task another branch is running; named events triggered with -> and
// waited on with @; force and release of a wire, of bits of one and of a variable a clock writes; and
// immediate assertions with pass and fail actions. Where Icarus 12 runs otherwise or not at all -- the
// order of branches started in one moment, a force following its expression, ->>, e.triggered, an
// assertion's own message -- BitSorter's own tests hold it (Docs/oracle-notes.md). Written for BitSorter;
// not taken from any course material.

module testbench_control_tb;
    event go, done;
    logic [3:0] a, b;
    wire  [3:0] w = a ^ b;
    logic [3:0] q;
    logic clk;
    int count;

    task automatic slow(input int n);
        repeat (n) #2 count++;
        $display("%0t slow %0d done", $time, n);
    endtask

    always @(posedge clk)
        q <= a;

    initial begin
        clk = 0;
        forever #5 clk = ~clk;
    end

    initial begin
        a = 1;
        b = 2;
        count = 0;

        fork
            begin #5 $display("%0t branch A", $time); end
            begin #3 $display("%0t branch B", $time); end
        join
        $display("%0t after join", $time);

        fork
            #5 $display("%0t any A", $time);
            #2 $display("%0t any B", $time);
        join_any
        $display("%0t after join_any", $time);
        disable fork;

        fork
            #1 $display("%0t none", $time);
        join_none
        $display("%0t after join_none", $time);
        wait fork;
        $display("%0t after wait fork", $time);

        fork
            begin
                fork
                    #2 $display("%0t inner 1", $time);
                    #3 $display("%0t inner 2", $time);
                join
                $display("%0t inner joined", $time);
            end
            #1 $display("%0t outer", $time);
        join
        $display("%0t nested done", $time);

        begin : outer
            #2 $display("%0t in outer", $time);
            disable outer;
            $display("%0t never", $time);
        end
        $display("%0t after outer", $time);

        fork : watch
            #10 $display("%0t watch timed out", $time);
            #20 $display("%0t watch late", $time);
        join_none
        #3 disable watch;
        $display("%0t watch disabled", $time);

        fork
            slow(5);
            #5 disable slow;
        join
        $display("%0t count=%0d", $time, count);

        -> go;
        @(done) $display("%0t done seen", $time);

        force w[1] = 1'b0;
        #1 $display("%0t w=%b", $time, w);
        a = 4'b1111;
        #1 $display("%0t w=%b", $time, w);
        release w[1];
        #1 $display("%0t w=%b", $time, w);
        force w = 4'hA;
        #1 $display("%0t w=%h", $time, w);
        release w;
        force q = 4'b1010;
        #1 $display("%0t q=%b", $time, q);
        @(posedge clk) #1 $display("%0t q=%b forced over the clock", $time, q);
        release q;
        #1 $display("%0t q=%b kept", $time, q);
        @(posedge clk) #1 $display("%0t q=%b clocked", $time, q);

        assert (q == a) $display("%0t q follows a", $time);
        else $display("%0t q does not follow a", $time);
        assert (q != a) $display("%0t never passes", $time);
        else $display("%0t q is still a", $time);
        #1 $finish;
    end

    initial begin
        @(go) $display("%0t go seen", $time);
        #2 -> done;
    end
endmodule
