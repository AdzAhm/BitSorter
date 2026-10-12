// Gate-delay probes: one question per block about how a delay behaves, each the same circuit as
// Tools/ci/Sim.Tests/Probes.cs builds in C#. Written for BitSorter's oracle; not taken from any
// course material. EngineOracleTests runs this under Icarus and compares its VCD, signal by signal,
// with BitSorter's own run of the same circuits. Docs/oracle-notes.md says what each one showed.

module probes;

  // P1: a 1-unit pulse into an AND with delay 3. Inertial: swallowed.
  logic a1 = 0, b1 = 1; wire y1;
  and #3 g1 (y1, a1, b1);
  initial begin #10 a1 = 1; #1 a1 = 0; end

  // P2: a pulse exactly as long as the delay. It passes: 1 at 13, 0 at 16.
  logic a2 = 0; wire y2;
  buf #3 g2 (y2, a2);
  initial begin #10 a2 = 1; #3 a2 = 0; end

  // P3: rise 5, fall 1, a 2-unit high pulse. The rise is cancelled.
  logic a3 = 0; wire y3;
  buf #(5, 1) g3 (y3, a3);
  initial begin #10 a3 = 1; #2 a3 = 0; end

  // P4: rise 1, fall 5, a 2-unit low pulse. The fall is cancelled.
  logic a4 = 1; wire y4;
  buf #(1, 5) g4 (y4, a4);
  initial begin #10 a4 = 0; #2 a4 = 1; end

  // P5: rise 2, fall 5; the input goes to x. To x takes the smaller delay.
  logic a5 = 0; wire y5;
  buf #(2, 5) g5 (y5, a5);
  initial begin #10 a5 = 1'bx; end

  // P6: delay 4; a 1 on its way is replaced by an x.
  logic a6 = 0; wire y6;
  buf #4 g6 (y6, a6);
  initial begin #10 a6 = 1; #1 a6 = 1'bx; end

  // P7: bufif1 turning off takes the turn-off delay.
  logic d7 = 1, e7 = 1; wire y7;
  bufif1 #(2, 3, 4) g7 (y7, d7, e7);
  initial begin #10 e7 = 0; end

  // P8: assign #3 swallows a short pulse as a gate does.
  logic a8 = 0, b8 = 1; wire y8;
  assign #3 y8 = a8 & b8;
  initial begin #10 a8 = 1; #1 a8 = 0; end

  // P9: the reference glitch, y = a AND (NOT a): NOT 3, AND 2. A pulse from 12 to 15.
  logic a9 = 0; wire n9, y9;
  not #3 g9n (n9, a9);
  and #2 g9a (y9, a9, n9);
  initial begin #10 a9 = 1; end

  // P10: NOT 1, AND 2. The 1-unit glitch is shorter than the AND's delay, and swallowed.
  logic a10 = 0; wire n10, y10;
  not #1 g10n (n10, a10);
  and #2 g10a (y10, a10, n10);
  initial begin #10 a10 = 1; end

  // P11: a gate output declared logic, as the course writes it: x until the inputs settle.
  logic a11, b11; logic y11;
  xor #(2, 3) g11 (y11, a11, b11);
  initial begin #1 a11 = 0; b11 = 0; end

  initial begin
    $dumpfile("probes.vcd");
    $dumpvars(0, probes);
    #40 $finish;
  end

endmodule
