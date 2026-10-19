// Part L6 as Icarus can run it as well: a package of a parameter, an enum, a struct, a function and a
// task, imported whole, by name and in a module's header, and named as pkg::name, one package importing
// another; macros with arguments -- a default, one use inside another, a use over two lines, `` and `"`";
// strings joined, measured, cut, compared and turned to numbers and back, $sformatf and $sformat; and a
// file written with $fopen, $fdisplay, $fwrite and $fstrobe, a channel of a multichannel descriptor with
// the transcript beside it, and a file added to after it is closed. Where Icarus 12 cannot -- a string's
// toupper, getc, compare and putc, a task called as pkg::t -- BitSorter's own tests hold it
// (Docs/oracle-notes.md). Written for BitSorter; not taken from any course material.

`define MAX(a, b = 0) ((a) > (b) ? (a) : (b))
`define REG(name, w = 4) logic [w-1:0] name``_q;
`define SHOW(x) $display(`"x = %0d`", x)

package sizes;
    parameter int WORD = 8;
endpackage

package alu_pkg;
    import sizes::*;

    typedef enum logic [1:0] {ADD, SUB, AND_OP, OR_OP} op_t;
    typedef struct packed { op_t op; logic [WORD-1:0] a; } req_t;
    localparam logic [WORD-1:0] ONES = '1;

    function automatic logic [WORD-1:0] apply(input op_t op, input logic [WORD-1:0] a, b);
        case (op)
            ADD: return a + b;
            SUB: return a - b;
            AND_OP: return a & b;
            default: return a | b;
        endcase
    endfunction

    task automatic show(input logic [WORD-1:0] v);
        $display("show %h", v);
    endtask
endpackage

module unit_alu import alu_pkg::*; (
    input  req_t       req,   // what to do, and the first operand
    input  logic [7:0] b,     // the second operand
    output logic [7:0] y      // the result
);
    assign y = apply(req.op, req.a, b);
endmodule

module library_tb;
    import alu_pkg::req_t, alu_pkg::show;

    req_t req;
    logic [7:0] b, y;
    `REG(count)
    `REG(wide, 8)
    string name = "adder";
    string s, line;
    logic [8*12-1:0] packed_line;
    integer fd, channel, again;

    unit_alu u (.req(req), .b(b), .y(y));

    initial begin
        req = {alu_pkg::SUB, 8'h10};
        b = 8'h03;
        #1 $display("%h %0d %h", y, sizes::WORD, alu_pkg::ONES);
        show(y);
        req.op = alu_pkg::OR_OP;
        #1 $display("%h %h", y, alu_pkg::apply(alu_pkg::AND_OP, 8'hF0, 8'h3C));

        count_q = `MAX(3, 9);
        wide_q = `MAX(count_q,
                      200);
        `SHOW(count_q);
        `SHOW(wide_q);
        $display("%0d %0d", `MAX(`MAX(1, 2), 3), `MAX(5));

        s = {name, "_", "test"};
        $display("%s %0d", s, s.len());
        $display(s);
        $display("[%s] %c", s.substr(2, 4), s[0]);
        $display("%0d %0d %0d", s == "adder_test", s < "b", "abc" > "abd");
        line = $sformatf("n=%0d hex=%h name=%s", wide_q, wide_q, name);
        $display("%s", line);
        $sformat(packed_line, "%0d-%0d", count_q, count_q + 1);
        $display("[%s]", packed_line);
        s = "123abc";
        $display("%0d", s.atoi());
        s.itoa(-77);
        $display("%s %0d", s, s.len());

        fd = $fopen("results.txt", "w");
        channel = $fopen("channel.txt");
        $fdisplay(fd, "y=%h at %0t", y, $time);
        $fwrite(fd, "no newline ");
        $fwrite(fd, "then one\n");
        $fdisplay(channel | 1, "to the channel and the transcript: %0d", count_q);
        #2 b = b + 1;
        $fstrobe(fd, "strobed %h", b);
        b = b + 1;
        #1 $fclose(fd);
        $fclose(channel);
        again = $fopen("results.txt", "a");
        $fdisplay(again, "added %0d", again != 0);
        $fclose(again);
        #1 $finish;
    end
endmodule
