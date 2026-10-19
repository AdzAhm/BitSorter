// The data types of Part L4 that Icarus can run as well: packed structs -- one inside another, with an
// enum member -- a packed union, packed arrays of two and three dimensions and of structs, a memory of two
// dimensions, foreach over memories, and selects with indices known only as the run goes, inside and
// outside the range. Read and written, whole, by member, by element and by slice. Assignment patterns,
// a nested member printed by itself, foreach over a vector declared high to low, and an element of a
// signed packed array are where Icarus and the standard part (Docs/oracle-notes.md), and are held by
// BitSorter's own tests. Written for BitSorter; not taken from any course material.

module data_types_tb;
    typedef enum logic [1:0] {IDLE, LOAD, RUN, DONE} state_t;

    typedef struct packed {
        logic [3:0] op;
        logic [7:0] imm;
        state_t     st;
    } instr_t;

    typedef struct packed {
        instr_t head;
        logic   valid;
    } slot_t;

    typedef union packed {
        logic [13:0] raw;
        instr_t      ins;
    } word_u;

    instr_t a, b, c;
    slot_t  s;
    word_u  u;
    logic [3:0][7:0] bytes;
    logic [1:0][2:0][3:0] cube;
    instr_t [1:0] pair;
    logic [7:0] grid [0:2][0:3];
    logic [7:0] mem [4];
    int i, j, sum;
    logic [7:0] t8;
    logic [3:0] t4;
    logic signed [1:0][7:0] sb;
    wire [7:0] imm_w = a.imm;

    assign c = {a.op, 8'h00, IDLE};

    initial begin
        a = {4'h3, 8'hA5, RUN};
        b = {4'h1, 8'h0F, DONE};
        #1 $display("a=%h b=%h c=%h imm_w=%h", a, b, c, imm_w);
        $display("a.op=%h a.imm=%h a.st=%d", a.op, a.imm, a.st);
        a.imm = 8'h3C;
        a.st = LOAD;
        $display("a=%h", a);

        s.head = a;
        s.valid = 1;
        s.head.op = 4'hF;
        t8 = s.head.imm;
        $display("s=%h s.head.imm=%h", s, t8);

        u.raw = 14'h1234;
        t4 = u.ins.op;
        t8 = u.ins.imm;
        $display("u.ins.op=%h u.ins.imm=%h", t4, t8);

        bytes = 32'hDEADBEEF;
        $display("bytes[0]=%h bytes[3]=%h bytes[2:1]=%h", bytes[0], bytes[3], bytes[2:1]);
        bytes[1] = 8'h00;
        $display("bytes=%h", bytes);
        for (i = 0; i < 4; i++)
            bytes[i] = i * 17;
        $display("bytes=%h", bytes);
        i = 2;
        $display("bytes[i]=%h bytes[2][3:0]=%h bytes[2][1]=%b", bytes[i], bytes[2][3:0], bytes[2][1]);
        i = 5;
        $display("bytes[i] outside=%h", bytes[i]);
        bytes[i] = 8'hFF;
        $display("bytes=%h", bytes);

        cube = '0;
        cube[1][2] = 4'hA;
        cube[0][1][2] = 1'b1;
        $display("cube=%h", cube);

        pair[1] = a;
        pair[0] = b;
        $display("pair=%h pair[1].imm=%h", pair, pair[1].imm);
        pair[0].op = 4'h7;
        $display("pair=%h", pair);

        foreach (grid[r, k])
            grid[r][k] = r * 16 + k;
        $display("grid[1][2]=%h grid[2][3]=%h", grid[1][2], grid[2][3]);
        i = 2;
        j = 1;
        $display("grid[i][j]=%h", grid[i][j]);
        j = 7;
        $display("grid[i][j] outside=%h", grid[i][j]);
        grid[i][j] = 8'h77;
        sum = 0;
        foreach (grid[r, k])
            sum += grid[r][k];
        $display("sum=%0d", sum);

        foreach (mem[k])
            mem[k] = 8'hC0 + k;
        $display("mem=%h %h %h %h", mem[0], mem[1], mem[2], mem[3]);

        $display("bits=%0d %0d %0d", $bits(instr_t), $bits(slot_t), $bits(pair));
        sb = {8'hFF, 8'h80};
        $display("sb=%0d", sb);
        #1 $finish;
    end
endmodule
