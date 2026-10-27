// Part 14f as Icarus runs it too: a shared bus. Two devices, each with an inout port onto one tri net, each driving it
// while its enable is high and reading it back; the testbench drives it as well, through a tri-state gate on one bit
// and an assignment on the rest. Where two drive different values the bus reads x; where they agree, the value; where
// none drives, z. One device sits inside a wrapper, so an inout passed on to an inout is one net all the way down.
// Written for BitSorter; not taken from any course material.

module device #(parameter W = 4) (
    input  logic         en,     // drive the bus
    input  logic [W-1:0] value,  // what to drive
    inout  tri   [W-1:0] bus,    // the shared bus
    output logic [W-1:0] seen    // what the bus reads
);
    assign #2 bus = en ? value : 'z;   // one delay: a vector's rise and fall are taken apart differently by Icarus (oracle-notes)
    assign seen = bus;
endmodule

module wrapper #(parameter W = 4) (
    input  logic         en,
    input  logic [W-1:0] value,
    inout  tri   [W-1:0] bus,
    output logic [W-1:0] seen
);
    device #(.W(W)) inner (.en(en), .value(value), .bus(bus), .seen(seen));
endmodule

module shared_bus_tb;
    logic ea, eb, et;
    logic [3:0] va, vb, vt;
    tri [3:0] bus;
    logic [3:0] sa, sb;

    device a (.en(ea), .value(va), .bus(bus), .seen(sa));
    wrapper b (.en(eb), .value(vb), .bus(bus), .seen(sb));

    bufif1 #(2, 2, 4) t0 (bus[0], vt[0], et);
    assign #2 bus[3:1] = et ? vt[3:1] : 3'bz;

    always @(bus)
        $strobe("%0t bus=%b a sees %b, b sees %b", $time, bus, sa, sb);   // at the end of the step, once what reads the bus has

    initial begin
        ea = 0; eb = 0; et = 0;
        va = 4'h3; vb = 4'hC; vt = 4'h5;
        #10 ea = 1;               // a alone: 0011
        #10 eb = 1;               // a and b disagree on every bit: xxxx
        #10 ea = 0;               // b alone: 1100
        #10 eb = 0; et = 1;       // the testbench alone: 0101
        #10 vb = 4'h5; eb = 1;    // b and the testbench agree: 0101
        #10 vb = 4'h4;            // and disagree on bit 0 only: 010x
        #10 et = 0; eb = 0;       // nobody: zzzz
        #10 $finish;
    end
endmodule
