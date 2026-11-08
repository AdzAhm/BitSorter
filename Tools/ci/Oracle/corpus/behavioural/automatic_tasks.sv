// Automatic tasks: each call with variables of its own (IEEE 1800, 13.3.1) -- two branches of a fork
// running one at once, each keeping its own across its waits; a task that calls itself, twice in its
// body, with a block's variable of its own and an output given back to a local of the call above;
// and two tasks that call each other. Written for BitSorter's oracle; not taken from any course
// material. Docs/oracle-notes.md, "Automatic tasks", says what both simulators print.

module automatic_tasks_tb;
    // Two at once: each call's id, product and wait are its own.
    task automatic hold(input int id, input int wait_for);
        int seen;
        seen = id * 10;
        #(wait_for);
        $display("%0t hold id=%0d seen=%0d wait_for=%0d", $time, id, seen, wait_for);
    endtask

    // A tree of calls: the sum of 1 to n, split in two halves, a step of time at each level.
    task automatic sum(input int low, input int high, output int total);
        int left, right, middle;
        if (low == high) total = low;
        else begin
            middle = (low + high) / 2;
            #1 sum(low, middle, left);
            begin : other_half
                int got;
                sum(middle + 1, high, got);
                right = got;
            end
            total = left + right;
        end
    endtask

    // Two tasks that call each other: n down to 0, each level adding its own n.
    task automatic ping(input int n, output int total);
        int below;
        if (n == 0) total = 0;
        else begin
            pong(n - 1, below);
            total = below + n;
        end
    endtask

    task automatic pong(input int n, output int total);
        int below;
        if (n == 0) total = 0;
        else begin
            #1 ping(n - 1, below);
            total = below + 2 * n;
        end
    endtask

    int s, p;
    initial begin
        fork
            hold(1, 5);
            hold(2, 3);
        join
        sum(1, 10, s);
        $display("%0t sum=%0d", $time, s);
        ping(6, p);
        $display("%0t ping=%0d", $time, p);
        $finish;
    end
endmodule
