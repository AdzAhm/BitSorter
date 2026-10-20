// Part L7 as Icarus runs it too: real variables, parameters -- one typed, one taking its value's type --
// and a function of reals; arithmetic mixing reals and whole numbers, comparisons, a real as a condition
// (-0.0 being false), conversion both ways with rounding half away from zero, $rtoi, $itor, $realtobits,
// $bitstoreal and the maths functions; %f, %e, %g, a real with no format, and %d of one. And delays in
// fractions of a unit, in two modules of different timescales: each module's delays scaled to the finest
// precision and rounded to its own, $time in its own units, $realtime with the fraction, %t in ticks.
// Written for BitSorter; not taken from any course material.

`timescale 1ns/1ps
module pulse_gen (
    output logic pulse   // high for a fraction of a nanosecond
);
    parameter real WIDTH = 0.25;

    initial begin
        pulse = 0;
        #1.5 pulse = 1;
        #WIDTH pulse = 0;
        #0.0004 pulse = 1;   // under a picosecond: rounds to no time at all
        #0.0006 pulse = 0;   // over half of one: a whole picosecond
    end
endmodule

`timescale 10ns/1ns
module reals_tb;
    parameter SCALE = 2.5;

    real r, s, sum;
    int i;
    logic [7:0] b;
    logic pulse;

    pulse_gen gen (.pulse(pulse));

    function automatic real average(input real a, input real c);
        return (a + c) / 2;
    endfunction

    initial begin
        r = SCALE;
        s = 1.0 / 3;
        $display(r, " ", s);
        $display("%f %e %g %0.2f %d %10.3f|", r, s, s, s, r, r);
        i = r;
        $display("%0d", i);
        i = -2.5;
        $display("%0d", i);
        r = 7 / 2;
        $display("%f", r);
        r = 7 / 2.0;
        $display("%f %f", r, average(r, 1));
        $display("%0d %f", $rtoi(3.9), $itor(5));
        $display("%h %f", $realtobits(1.0), $bitstoreal(64'h3FF8000000000000));
        $display("%f %f %f %f", $sqrt(2.0), $pow(2.0, 10), $ln(1.0), $floor(-1.5));
        b = 200.7;
        $display("%0d", b);
        r = -0.0;
        $display("%0d %0d %0d", r ? 1 : 0, r > s, 3.5 == 7.0 / 2);
        sum = 0;
        for (i = 0; i < 4; i++)
            sum = sum + i * 0.5;
        $display("%g", sum);

        #1.25 $display("%t %0d %f", $time, $time, $realtime);
        #0.04 $display("%t %0d %f", $realtime, $time, $realtime);
        #0.06 $display("%t %0d %f", $realtime, $time, $realtime);
        #1 $finish;
    end
endmodule
