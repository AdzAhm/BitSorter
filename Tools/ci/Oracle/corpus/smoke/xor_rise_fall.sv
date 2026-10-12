// An XOR gate whose output rises in 2 and falls in 3, and a testbench that prints every change of
// its output. Written for BitSorter's oracle harness; not taken from any course material.
//
// Expected, and asserted by IcarusSmokeTests:
//   0  x   nothing has reached the output yet
//   3  0   the inputs start at 0 and 0, so the output falls: the fall delay, 3
//   12 1   a rises at 10, so the output rises: the rise delay, 2
//   23 0   a falls at 20, so the output falls again: 3

module xor_rise_fall_tb;

  logic a;
  logic b;
  logic y;

  xor #(2, 3) g1 (y, a, b);

  initial begin
    $monitor("%0t %b", $time, y);
    a = 1'b0;
    b = 1'b0;
    #10 a = 1'b1;
    #10 a = 1'b0;
    #10 $finish;
  end

endmodule
