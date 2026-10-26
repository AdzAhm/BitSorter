// alu_ops.svh: the ALU's operation codes, included inside the module that uses them, the way a course
// keeps shared constants in a file of their own. Written for BitSorter's import tests (Part 13b); not
// taken from any course material.
localparam logic [2:0] OP_ADD = 3'd0;
localparam logic [2:0] OP_SUB = 3'd1;
localparam logic [2:0] OP_AND = 3'd2;
localparam logic [2:0] OP_OR  = 3'd3;
localparam logic [2:0] OP_XOR = 3'd4;
localparam logic [2:0] OP_SLT = 3'd5;
localparam logic [2:0] OP_SHL = 3'd6;
localparam logic [2:0] OP_SHR = 3'd7;
