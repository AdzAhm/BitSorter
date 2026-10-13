// Memories: a RAM whose image is loaded with $readmemh, written by always_ff and read by a continuous
// assignment; and a testbench that loads arrays declared every way round -- [0:7], [7:0], [8] -- with
// and without a range, from a file with @ addresses, x and z digits and both kinds of comment, and
// reads the RAM's words through the hierarchy. memories.hex and memories.bin are its images. Written
// for BitSorter's oracle; not taken from any course material.

module memories_ram #(
    parameter W = 8,
    parameter DEPTH = 16
) (
    input  logic         clk,
    input  logic         we,      // write wdata at waddr on the rising edge
    input  logic [3:0]   waddr,
    input  logic [3:0]   raddr,
    input  logic [W-1:0] wdata,
    output logic [W-1:0] rdata
);
    logic [W-1:0] mem [0:DEPTH-1];

    initial $readmemh("memories.hex", mem);

    always_ff @(posedge clk)
        if (we) mem[waddr] <= wdata;

    assign rdata = mem[raddr];
endmodule

module memories_tb;
    logic       clk = 1'b0, we = 1'b0;
    logic [3:0] waddr = 4'd0, raddr = 4'd0;
    logic [7:0] wdata = 8'd0, rdata;
    logic [7:0] down [7:0];
    logic [7:0] sized [8];
    logic [7:0] ranged [0:15];
    logic [7:0] bits [0:3];
    integer     i;

    memories_ram ram (.clk, .we, .waddr, .raddr, .wdata, .rdata);

    always #5 clk = ~clk;

    initial begin
        $readmemh("memories.hex", down);
        $readmemh("memories.hex", sized);
        $readmemh("memories.hex", ranged, 2, 9);
        $readmemb("memories.bin", bits);

        for (i = 0; i < 8; i++)
            $display("down[%0d]=%h sized[%0d]=%h", i, down[i], i, sized[i]);
        for (i = 0; i < 16; i++)
            $write("%h ", ranged[i]);
        $display;
        $display("bits %b %b %b", bits[0], bits[1], bits[2]);

        for (i = 0; i < 16; i++) begin
            raddr = i;
            #1 $display("ram[%0d]=%h through the hierarchy %h", i, rdata, ram.mem[i]);
        end

        @(negedge clk) begin we = 1'b1; waddr = 4'd3; wdata = 8'hA5; end
        @(negedge clk) begin waddr = 4'd12; wdata = 8'h5A; end
        @(negedge clk) we = 1'b0;

        raddr = 4'd3;
        #1 $display("after the writes ram[3]=%h", rdata);
        raddr = 4'd12;
        #1 $display("after the writes ram[12]=%h", rdata);
        $finish;
    end
endmodule
