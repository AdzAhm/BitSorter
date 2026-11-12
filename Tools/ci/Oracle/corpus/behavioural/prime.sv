// A combinational always_comb with a for loop run a fixed number of times, set by a parameter, and
// % and * in the test inside it: whether n is prime, for any width. The workbench's "Is it prime?"
// sample is the module below (SampleTests holds the two the same); here at six bits, every value
// from 0 to 63. Written for BitSorter; not taken from any course material.

// Is it prime? A number is prime when it is more than 1 and no number from
// 2 up to its square root divides it, so this works it out for any width of
// n: set N in the testbench to try wider numbers. At four bits the LOGIC tab
// shows its truth table, its K-map and the smallest sum of products.
module prime #(
    parameter N = 4         // how many bits n has
) (
    input  logic [N-1:0] n,
    output logic         is_prime
);
    // Hardware has no loop that runs while n says so: this one runs a fixed
    // number of times, every i below 2**((N+1)/2) -- at least the square root
    // of the largest n -- and is built as that many copies of its body.
    always_comb begin
        is_prime = (n > 1);
        for (int i = 2; i < 2 ** ((N + 1) / 2); i++)
            if (i * i <= n && n % i == 0)
                is_prime = 1'b0;
    end
endmodule

module prime_tb;
    localparam N = 6;
    logic [N-1:0] n = '0;
    logic         is_prime;

    prime #(.N(N)) dut (.n(n), .is_prime(is_prime));

    initial begin
        repeat (2 ** N) #10 n = n + 1'b1;
        $stop;
    end
endmodule
