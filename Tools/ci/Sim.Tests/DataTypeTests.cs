using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part L4's data types. <c>corpus/behavioural/data_types.sv</c> holds what Icarus can run as well against
    /// it; these hold, by the standard, what Icarus 12 cannot read or reads otherwise (Docs/oracle-notes.md):
    /// assignment patterns, a nested member by itself, <c>foreach</c> over a range declared high to low, an
    /// element of a signed packed array, selects past a member or an element with an index known only as
    /// the run goes -- and what is refused.
    /// </summary>
    public class DataTypeTests
    {
        private static Elaboration Elaborate(string text)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, null);
        }

        private static string[] Printed(string text, long until = 100)
        {
            Elaboration e = Elaborate(text);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(until);
            return sim.History.Messages.Where(m => m.Severity == Severity.Info && !m.Text.StartsWith("$")).Select(m => m.Text).ToArray();
        }

        // -----------------------------------------------------------------
        // Assignment patterns (IEEE 1800, 10.9)
        // -----------------------------------------------------------------

        [Test]
        public void APattern_FillsAStruct_InOrder_ByName_AndByDefault_AndAPackedArray_ByIndex()
        {
            CollectionAssert.AreEqual(new[] { "0e96 043f 0000 3c05", "1003", "1a2b3c4d", "bb5555aa", "ff" }, Printed(@"
module t;
    typedef struct packed { logic [3:0] op; logic [7:0] imm; logic [1:0] st; } instr_t;
    typedef struct packed { instr_t head; logic valid; } slot_t;
    instr_t a, b, c, d;
    slot_t s;
    logic [3:0][7:0] bytes;
    logic [7:0] v;
    initial begin
        a = '{4'h3, 8'hA5, 2'd2};
        b = '{imm: 8'h0F, st: 2'd3, op: 4'h1};
        c = '{default: 0};
        d = '{op: 4'hF, default: 1};
        s = '{'{4'h2, 8'h00, 2'd1}, 1'b1};
        bytes = '{8'h1A, 8'h2B, 8'h3C, 8'h4D};
        $display(""%h %h %h %h"", a, b, c, d);
        $display(""%h"", s);
        $display(""%h"", bytes);
        bytes = '{0: 8'hAA, 3: 8'hBB, default: 8'h55};
        $display(""%h"", bytes);
        v = '{default: 1'b1};
        $display(""%h"", v);
    end
endmodule"));
        }

        [Test]
        public void APattern_GivesAParameter_AVariablesStartingValue_AndAnAssign_Their_Values()
        {
            CollectionAssert.AreEqual(new[] { "c3 12 21" }, Printed(@"
module t;
    typedef struct packed { logic [3:0] hi; logic [3:0] lo; } pair_t;
    localparam pair_t P = '{hi: 4'hC, lo: 4'h3};
    pair_t q = '{4'h1, 4'h2};
    pair_t w;
    assign w = '{lo: q.hi, hi: q.lo};
    initial #1 $display(""%h %h %h"", P, q, w);
endmodule"));
        }

        [TestCase("a = '{4'h3, imm: 8'h1, st: 2'd0};", ElaborationCodes.BadSelect)]
        [TestCase("a = '{4'h3, 8'h1};", ElaborationCodes.WidthMismatch)]
        [TestCase("a = '{opp: 4'h3, default: 0};", ElaborationCodes.UnknownName)]
        [TestCase("a = '{op: 4'h3};", ElaborationCodes.BadSelect)]
        [TestCase("a = '{op: 4'h3, op: 4'h4, default: 0};", ElaborationCodes.Duplicate)]
        [TestCase("{x, y} = '{1'b1, 1'b0};", ElaborationCodes.BadSelect)]
        [TestCase("x = '{1'b1} + 1'b1;", ElaborationCodes.BadSelect)]
        [TestCase("u = '{8'h1};", ElaborationCodes.NotYet)]
        public void APatternThatDoesNotFit_IsRefused_WithWhy(string statement, string code)
        {
            Elaboration e = Elaborate(@"
module t;
    typedef struct packed { logic [3:0] op; logic [7:0] imm; logic [1:0] st; } instr_t;
    typedef union packed { logic [7:0] b; logic signed [7:0] s; } u_t;
    instr_t a;
    u_t u;
    logic x, y;
    initial begin
        " + statement + @"
    end
endmodule");
            CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
        }

        // -----------------------------------------------------------------
        // Members, elements and selects
        // -----------------------------------------------------------------

        [Test]
        public void ANestedMember_IsItsOwnWidth()
        {
            // Icarus 12 sizes s.head.imm as the whole of s (Docs/oracle-notes.md); it is eight bits.
            CollectionAssert.AreEqual(new[] { "3c 8 14" }, Printed(@"
module t;
    typedef struct packed { logic [3:0] op; logic [7:0] imm; logic [1:0] st; } instr_t;
    typedef struct packed { instr_t head; logic valid; } slot_t;
    slot_t s;
    initial begin
        s = 15'h79e3;
        $display(""%h %0d %0d"", s.head.imm, $bits(s.head.imm), $bits(s.head));
    end
endmodule"));
        }

        [Test]
        public void AnElementOfASignedPackedArray_IsUnsigned_UnlessItsOwnTypeIsSigned()
        {
            // IEEE 1800, 7.4.1; Icarus 12 reads sb[1] as signed.
            CollectionAssert.AreEqual(new[] { "255 -128 -1 65408" }, Printed(@"
module t;
    typedef logic signed [7:0] sbyte_t;
    logic signed [1:0][7:0] sb;
    sbyte_t [1:0] tb;
    initial begin
        sb = 16'hFF80;
        tb = 16'hFF80;
        $display(""%0d %0d %0d %0d"", sb[1], sb, tb[1], tb);
    end
endmodule"));
        }

        [Test]
        public void ASelectPastAnElementPickedAsTheRunGoes_ReadsAndWritesItsOwnBits_AndNothingOutside()
        {
            CollectionAssert.AreEqual(new[] { "05a000 a 05", "00000000 x", "00000008 1", "00000008 xx" }, Printed(@"
module t;
    typedef struct packed { logic [3:0] op; logic [3:0] arg; } i_t;
    i_t [2:0] prog;
    logic [3:0][7:0] bytes;
    int i, j;
    initial begin
        prog = 0;
        i = 1;
        prog[i].op = 4'hA;
        prog[i + 1].arg = 4'h5;
        $display(""%h %h %h"", prog, prog[i].op, prog[i + 1]);
        bytes = 32'h0;
        i = 0;
        j = 9;
        bytes[i][j] = 1'b1;
        $display(""%h %b"", bytes, bytes[i][j]);
        j = 3;
        bytes[i][j] = 1'b1;
        $display(""%h %b"", bytes, bytes[i][j]);
        i = 4;
        bytes[i] = 8'hFF;
        $display(""%h %h"", bytes, bytes[i]);
    end
endmodule"));
        }

        [Test]
        public void AVariablePartSelect_OfAVectorDeclaredLowToHigh_ReadsAndWrites()
        {
            CollectionAssert.AreEqual(new[] { "001 10", "11111010" }, Printed(@"
module t;
    logic [0:7] v;
    int i;
    initial begin
        v = 8'b1100_1010;
        i = 2;
        $display(""%b %b"", v[i +: 3], v[i -: 2]);
        v[i +: 2] = 2'b11;
        $display(""%b"", v);
    end
endmodule"));
        }

        [Test]
        public void AWordOfATwoDimensionalMemory_PickedOutside_IsXAndIsNotWritten()
        {
            CollectionAssert.AreEqual(new[] { "00 xx 48", "12" }, Printed(@"
module t;
    logic [7:0] g [0:1][0:2];
    int r, c;
    initial begin
        foreach (g[a, b])
            g[a][b] = 0;
        r = 0;
        c = 3;
        g[r][c] = 8'hEE;
        $display(""%h %h %0d"", g[1][0], g[r][c], $bits(g));
        r = 1;
        c = 0;
        g[r][c] = 8'h12;
        $display(""%h"", g[1][0]);
    end
endmodule"));
        }

        [Test]
        public void AUnion_SharesItsBits_AndASignedStruct_ReadsSignedAsAWhole()
        {
            CollectionAssert.AreEqual(new[] { "-12 f 4", "f1", "-128 8" }, Printed(@"
module t;
    typedef struct packed {
        logic [3:0] hi;
        logic [3:0] lo;
    } nibbles_t;
    typedef union packed {
        logic [7:0]        b;
        logic signed [7:0] s;
        nibbles_t          n;
    } u_t;
    typedef struct packed signed {
        logic [3:0] a;
        logic [3:0] b;
    } ss_t;
    u_t u;
    ss_t x;
    initial begin
        u.b = 8'hF4;
        $display(""%0d %h %h"", u.s, u.n.hi, u.n.lo);
        u.n.lo = 4'h1;
        $display(""%h"", u.b);
        x = 8'h80;
        $display(""%0d %0d"", x, x.a);
    end
endmodule"));
        }

        [Test]
        public void AnEnumMember_AndAWordOfEnums_HaveTheEnumsMethods()
        {
            CollectionAssert.AreEqual(new[] { "RUN LOAD LOAD" }, Printed(@"
module t;
    typedef enum logic [1:0] {IDLE, LOAD, RUN} st_t;
    typedef struct packed { st_t st; logic f; } r_t;
    r_t r;
    st_t trail [2];
    initial begin
        r.st = RUN;
        trail[1] = LOAD;
        $display(""%s %s %s"", r.st.name(), trail[1].name(), r.st.prev().name());
    end
endmodule"));
        }

        [Test]
        public void AMember_IsReachedThroughTheHierarchy()
        {
            CollectionAssert.AreEqual(new[] { "5 5a" }, Printed(@"
module inner;
    typedef struct packed { logic [3:0] a; logic [3:0] b; } p_t;
    p_t s;
    initial s = 8'h5A;
endmodule

module t;
    inner dut ();
    initial #1 $display(""%h %h"", dut.s.a, dut.s);
endmodule"));
        }

        [Test]
        public void AStructAndAPackedArray_AreWholePorts()
        {
            CollectionAssert.AreEqual(new[] { "5 7" }, Printed(@"
typedef struct packed { logic [3:0] op; logic [3:0] arg; } i_t;

module dec (input i_t ins, input logic [1:0][3:0] pair, output logic [3:0] op, output logic [3:0] sum);
    assign op = ins.op;
    assign sum = pair[1] + pair[0];
endmodule

module t;
    i_t ins;
    logic [1:0][3:0] pair;
    logic [3:0] op, sum;
    dec d (.ins(ins), .pair(pair), .op(op), .sum(sum));
    initial begin
        ins = 8'h5C;
        pair = 8'h34;
        #1 $display(""%h %h"", op, sum);
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // foreach (IEEE 1800, 12.7.3)
        // -----------------------------------------------------------------

        [Test]
        public void Foreach_WalksEachDimensionFromItsLeftBoundToItsRight_AndSkipsAnEmptyPlace()
        {
            // [3:0] counts down, as declared; Icarus 12 counts every range up (Docs/oracle-notes.md).
            CollectionAssert.AreEqual(new[] { "3210 12 210210 210 2" }, Printed(@"
module t;
    logic [3:0] down;
    logic [0:2] up;
    logic [7:0] grid [0:1][2:0];
    int a, b, c, d, n;
    initial begin
        a = 0; b = 0; c = 0; d = 0; n = 0;
        foreach (down[i]) a = a * 10 + i;
        foreach (up[i]) b = b * 10 + i;
        foreach (grid[r, k]) c = c * 10 + k;
        foreach (grid[, k]) d = d * 10 + k;
        foreach (down[i]) begin
            if (i == 1)
                break;
            n++;
        end
        $display(""%0d %0d %0d %0d %0d"", a, b, c, d, n);
    end
endmodule"));
        }

        // -----------------------------------------------------------------
        // What is refused
        // -----------------------------------------------------------------

        [TestCase("p_t s; initial s.nope = 1;", ElaborationCodes.UnknownName)]
        [TestCase("p_t s; logic y; assign y = s.nope;", ElaborationCodes.UnknownName)]
        [TestCase("logic [3:0] v; logic y; assign y = v.x;", ElaborationCodes.UnknownName)]
        [TestCase("logic [7:0] g [2][3]; logic [7:0] y; initial y = g[1];", ElaborationCodes.BadSelect)]
        [TestCase("logic [7:0] g [2][3]; initial $readmemh(\"f.hex\", g);", ElaborationCodes.NotYet)]
        [TestCase("union packed { logic [3:0] a; logic [7:0] b; } u;", ElaborationCodes.WidthMismatch)]
        [TestCase("struct packed { logic a; logic a; } d;", ElaborationCodes.Duplicate)]
        [TestCase("logic [3:0] v; initial foreach (v[i, j]) v[i] = 0;", ElaborationCodes.BadSelect)]
        [TestCase("p_t s; logic y; assign y = s.a[1]; assign s.b = 4'h1;", null)]
        public void AMemberOrASelectThatIsNotThere_IsRefused_WithWhy(string items, string code)
        {
            Elaboration e = Elaborate(@"
module t;
    typedef struct packed { logic [3:0] a; logic [3:0] b; } p_t;
    " + items + @"
endmodule");
            if (code == null)
                Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            else
                CollectionAssert.Contains(e.Diagnostics.Select(d => d.Code).ToArray(), code, string.Join("\n", e.Diagnostics));
        }

        [Test]
        public void AMissingMember_NamesTheMembersThereAre()
        {
            Elaboration e = Elaborate(@"
module t;
    typedef struct packed { logic [3:0] a; logic [3:0] b; logic c; } p_t;
    p_t s;
    logic y;
    assign y = s.nope;
endmodule");
            StringAssert.Contains("its members are a, b and c", string.Join("\n", e.Diagnostics));
        }
    }
}
