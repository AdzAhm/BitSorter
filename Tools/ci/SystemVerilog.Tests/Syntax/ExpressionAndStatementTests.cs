using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using static BitSorter.SystemVerilog.Tests.Syntax.SyntaxKit;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>Expressions by the course's precedence table, and every statement form.</summary>
    public class ExpressionAndStatementTests
    {
        // -----------------------------------------------------------------
        // Precedence, shown by where the printer puts parentheses on a tree with none
        // -----------------------------------------------------------------

        private static string Grouped(string text) => Show(Expr(text));

        /// <summary>The tree with every operator's operands in brackets: how it was grouped.</summary>
        private static string Show(Expression e)
        {
            switch (e)
            {
                case BinaryExpression b: return "[" + Show(b.Left) + " " + b.Operator + " " + Show(b.Right) + "]";
                case UnaryExpression u: return "[" + u.Operator + Show(u.Operand) + "]";
                case ConditionalExpression c: return "[" + Show(c.Condition) + " ? " + Show(c.WhenTrue) + " : " + Show(c.WhenFalse) + "]";
                case ParenthesizedExpression p: return Show(p.Inner);
                default: return Printer.Print(e);
            }
        }

        [TestCase("a | b & c", "[a | [b & c]]")]
        [TestCase("a & b ^ c", "[[a & b] ^ c]")]
        [TestCase("a ^ b | c", "[[a ^ b] | c]")]
        [TestCase("a || b && c", "[a || [b && c]]")]
        [TestCase("a == b & c", "[[a == b] & c]")]
        [TestCase("a < b == c", "[[a < b] == c]")]
        [TestCase("a + b << 2", "[[a + b] << 2]")]
        [TestCase("a * b + c", "[[a * b] + c]")]
        [TestCase("a - b - c", "[[a - b] - c]")]
        [TestCase("a ** 2 * b", "[[a ** 2] * b]")]
        [TestCase("~a & b", "[[~a] & b]")]
        [TestCase("&a | b", "[[&a] | b]")]
        [TestCase("!a && b", "[[!a] && b]")]
        [TestCase("s ? a : t ? b : c", "[s ? a : [t ? b : c]]")]
        [TestCase("a & b ? c : d", "[[a & b] ? c : d]")]
        [TestCase("(A & B) | C", "[[A & B] | C]")]
        [TestCase("a <= b", "[a <= b]")]
        [TestCase("a >>> 1 ~^ b", "[[a >>> 1] ~^ b]")]
        public void Operators_BindAsTheCoursesTableSays(string text, string grouped)
        {
            Assert.AreEqual(grouped, Grouped(text));
        }

        [Test]
        public void Primaries_OfEveryKind()
        {
            Assert.IsInstanceOf<NumberLiteral>(Expr("8'hFF"));
            Assert.IsTrue(((NumberLiteral)Expr("'1")).FillsContext);
            Assert.AreEqual(new[] { "dut", "ctl", "state" }, ((NameExpression)Expr("dut.ctl.state")).Path.ToArray());
            Assert.AreEqual(RangeKind.IndexedUp, ((RangeSelectExpression)Expr("a[i +: 4]")).Kind);
            Assert.AreEqual(RangeKind.IndexedDown, ((RangeSelectExpression)Expr("a[i -: 4]")).Kind);
            Assert.AreEqual(RangeKind.Constant, ((RangeSelectExpression)Expr("w1[3:2]")).Kind);
            Assert.IsInstanceOf<IndexExpression>(Expr("mem[addr][3]"));
            Assert.AreEqual("$clog2", ((CallExpression)Expr("$clog2(N)")).Name);
            Assert.IsFalse(((CallExpression)Expr("$time")).HasParentheses);
            Assert.AreEqual("signed", ((CastExpression)Expr("signed'(a)")).TargetText);
            Assert.AreEqual("8", ((CastExpression)Expr("8'(a + b)")).TargetText);
            Assert.IsInstanceOf<MemberExpression>(Expr("slice[0].sum"));
        }

        [Test]
        public void ConcatenationAndReplication_AsTheWorkshopsWriteThem()
        {
            var nested = (ConcatenationExpression)Expr("{{4{A[3]}}, A[3:0]}");
            var repeated = (ReplicationExpression)nested.Items[0];
            Assert.AreEqual("4", Printer.Print(repeated.Count));
            Assert.AreEqual("A[3]", Printer.Print(repeated.Value.Items.Single()));

            var exam = (ConcatenationExpression)Expr("{a[1:0], {2{1'b1}}}");
            Assert.AreEqual(2, exam.Items.Count);
        }

        [Test]
        public void ABadNumber_IsReported_WithTheLiteralsOwnMessage()
        {
            SyntaxTree tree = Read("module m; assign y = 4'b12; endmodule");
            Assert.AreEqual(DiagnosticCodes.BadNumber, tree.Diagnostics.Single().Code);
        }

        // -----------------------------------------------------------------
        // Statements
        // -----------------------------------------------------------------

        [Test]
        public void BlockingNonBlockingAndCompoundAssignments()
        {
            var s = Statements("a = in;\nb <= a;\ncount += 2;\ni++;\n--j;\n{carry, sum} = a + b;");
            Assert.AreEqual("=", ((AssignmentStatement)s[0]).Operator);
            Assert.IsTrue(((AssignmentStatement)s[1]).IsNonBlocking);
            Assert.AreEqual("+=", ((AssignmentStatement)s[2]).Operator);
            Assert.IsFalse(((IncrementStatement)s[3]).Prefix);
            Assert.IsTrue(((IncrementStatement)s[4]).Prefix);
            Assert.IsInstanceOf<ConcatenationExpression>(((AssignmentStatement)s[5]).Target);
        }

        [Test]
        public void TheWorkshopsTestbench_DelaysEventsAndRepeat()
        {
            var s = Statements("clk = 1'b0;\nrst = 1'b1;\n@(posedge clk);\n#7\nrst = 1'b0;\nrepeat(2) begin\n@(posedge clk);\nend");

            var wait = (TimedStatement)s[2];
            Assert.IsInstanceOf<EventControl>(wait.Control);
            Assert.IsInstanceOf<NullStatement>(wait.Body);

            var delayed = (TimedStatement)s[3];
            Assert.AreEqual("7", Printer.Print(((DelayControl)delayed.Control).Values.Single()));
            Assert.IsInstanceOf<AssignmentStatement>(delayed.Body, "#7 on its own line still delays the next statement");

            Assert.IsInstanceOf<RepeatStatement>(s[4]);
        }

        [Test]
        public void IfElseChains_AndUniqueAndPriority()
        {
            var s = Statements("if (a) x = 1;\nelse if (b) x = 2;\nelse x = 3;\nunique if (c) y = 1;\npriority case (sel) 0: z = 1; default: z = 0; endcase");
            var chain = (IfStatement)s[0];
            Assert.IsInstanceOf<IfStatement>(chain.Else);
            Assert.AreEqual("unique", ((IfStatement)s[1]).Qualifier);
            Assert.AreEqual("priority", ((CaseStatement)s[2]).Qualifier);
        }

        [Test]
        public void CaseCasezCasex_WithLabelsListsAndDefault()
        {
            var s = Statements(
                "case (w)\n2'b00: Y = A & B;\n2'b01, 2'b10: Y = A | B;\ndefault: Y = ~B;\nendcase\n" +
                "casez (w)\n2'b1?: A = B + C;\nendcase\n" +
                "casex (op) 4'b1xx0: r = 1; endcase");

            var plain = (CaseStatement)s[0];
            Assert.AreEqual(3, plain.Items.Count);
            Assert.AreEqual(2, plain.Items[1].Labels.Count);
            Assert.IsTrue(plain.Items[2].IsDefault);
            Assert.AreEqual("casez", ((CaseStatement)s[1]).Keyword);
            Assert.AreEqual("casex", ((CaseStatement)s[2]).Keyword);
        }

        [Test]
        public void Loops_OfEveryKind()
        {
            var s = Statements(
                "for (int i = 0; i < 8; i++) sum = sum + a[i];\n" +
                "for (i = 0, j = 7; i < j; i = i + 1, j--) ;\n" +
                "while (busy) @(posedge clk);\n" +
                "do x = x - 1; while (x > 0);\n" +
                "forever #5 clk = ~clk;\n" +
                "wait (done);");

            var counted = (ForStatement)s[0];
            Assert.AreEqual("int", counted.Initializers[0].Type.Keyword);
            Assert.AreEqual(2, ((ForStatement)s[1]).Initializers.Count);
            Assert.AreEqual(2, ((ForStatement)s[1]).Steps.Count);
            Assert.IsInstanceOf<WhileStatement>(s[2]);
            Assert.IsTrue(((WhileStatement)s[3]).IsDoWhile);
            Assert.IsInstanceOf<ForeverStatement>(s[4]);
            Assert.IsInstanceOf<WaitStatement>(s[5]);
        }

        [Test]
        public void SystemTasksAndTaskCalls()
        {
            var s = Statements("$display(\"a=%d\", a);\n$finish;\n$readmemh(\"prog.hex\", mem);\nsend(data);\nreset_all;");
            Assert.IsTrue(((CallStatement)s[0]).IsSystem);
            Assert.IsFalse(((CallStatement)s[1]).HasParentheses);
            Assert.AreEqual(2, ((CallStatement)s[2]).Arguments.Count);
            Assert.AreEqual("send", ((CallStatement)s[3]).Name);
            Assert.AreEqual("reset_all", ((CallStatement)s[4]).Name);
        }

        [Test]
        public void IntraAssignmentDelays()
        {
            var s = (AssignmentStatement)Statements("a = #5 b;").Single();
            Assert.IsInstanceOf<DelayControl>(s.IntraAssignment);
        }

        [Test]
        public void ABlockMayDeclareLocals_AndBeNamed()
        {
            var process = (ProceduralBlock)Items("initial begin : run\n  integer i;\n  logic [3:0] t;\n  i = 0;\nend : run").Single();
            var block = (BlockStatement)process.Body;
            Assert.AreEqual("run", block.Label);
            Assert.AreEqual(2, block.Declarations.Count);
            Assert.AreEqual(1, block.Statements.Count);
        }

        [Test]
        public void ComparingWhereAssigningWasMeant_IsExplained()
        {
            SyntaxTree tree = Read("module m; initial begin a == 1; end endmodule");
            StringAssert.Contains("'==' compares", tree.Diagnostics.First().Message);
        }

        [Test]
        public void EventListsWithOr_AndBareEvents()
        {
            var s = Statements("@(a or b) y = a;\n@clk;\n@*;");
            Assert.IsTrue(((EventControl)((TimedStatement)s[0]).Control).UsesOr);
            Assert.IsTrue(((EventControl)((TimedStatement)s[1]).Control).Bare);
            Assert.IsTrue(((EventControl)((TimedStatement)s[2]).Control).Implicit);
        }
    }
}
