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
        public void Inside_TakesValuesAndRanges_BindsAsAComparison_AndPrintsAsWritten()
        {
            var inside = (InsideExpression)Expr("v inside {1, 3, [6:9]}");
            Assert.AreEqual("v", Printer.Print(inside.Value));
            Assert.AreEqual(3, inside.Set.Count);
            var range = (ValueRange)inside.Set[2];
            Assert.AreEqual("6", Printer.Print(range.Low));
            Assert.AreEqual("9", Printer.Print(range.High));

            var both = (BinaryExpression)Expr("a && v inside {2} == 1'b1");
            Assert.AreEqual("&&", both.Operator, "inside binds tighter than && ...");
            var equality = (BinaryExpression)both.Right;
            Assert.AreEqual("==", equality.Operator, "... and tighter than ==");
            Assert.IsInstanceOf<InsideExpression>(equality.Left);

            Assert.AreEqual("v inside {1, 3, [6:9]}", Printer.Print(inside));
        }

        [Test]
        public void AMethod_IsCalledOnANameOrOnWhatCameBefore()
        {
            var name = (MethodCallExpression)Expr("dut.state.name()");
            Assert.AreEqual("name", name.Method);
            Assert.AreEqual(new[] { "dut", "state" }, ((NameExpression)name.Target).Path.ToArray());

            var chained = (MethodCallExpression)Expr("s.next(2).name()");
            Assert.AreEqual("name", chained.Method);
            var next = (MethodCallExpression)chained.Target;
            Assert.AreEqual("next", next.Method);
            Assert.AreEqual("2", Printer.Print(next.Arguments.Single()));

            Assert.AreEqual("s.next(2).name()", Printer.Print(chained));
        }

        [Test]
        public void ACaseInside_TakesRangesAsLabels_AndPrintsAsWritten()
        {
            var c = (CaseStatement)Statements("case (v) inside\n  [0:3]: y = 0;\n  5, [7:9]: y = 1;\n  default: y = 2;\nendcase").Single();
            Assert.IsTrue(c.Inside);
            Assert.IsInstanceOf<ValueRange>(c.Items[0].Labels.Single());
            Assert.AreEqual(2, c.Items[1].Labels.Count);

            string printed = Printer.Print(OnlyModule("module m;\n  initial case (v) inside\n    [0:3]: y = 0;\n    default: y = 2;\n  endcase\nendmodule\n"));
            StringAssert.Contains("case (v) inside", printed);
            StringAssert.Contains("[0:3]: y = 0;", printed);
        }

        [Test]
        public void AStructOrAUnion_IsReadMemberByMember_AndPrintsAsWritten()
        {
            ModuleDeclaration m = OnlyModule(
                "module m;\n  typedef struct packed signed { logic [3:0] op; logic [7:0] a, b; } instr_t;\n" +
                "  union packed { logic [15:0] word; logic [1:0][7:0] bytes; } u;\nendmodule\n");

            var typedef = (TypedefDeclaration)m.Items[0];
            StructType s = typedef.Type.Struct;
            Assert.IsNotNull(s);
            Assert.IsTrue(s.Packed);
            Assert.IsTrue(s.Signed);
            Assert.IsFalse(s.IsUnion);
            CollectionAssert.AreEqual(new[] { "op", "a", "b" }, s.Members.Select(x => x.Name));
            Assert.AreSame(s.Members[1].Type, s.Members[2].Type, "a, b share their declaration's type");

            var union = ((DataDeclaration)m.Items[1]).Type.Struct;
            Assert.IsTrue(union.IsUnion);
            Assert.AreEqual(2, union.Members[1].Type.Packed.Count, "a packed array of two bytes");

            string printed = Printer.Print(Clean(Printer.Print(m)));
            StringAssert.Contains("struct packed signed {logic [3:0] op; logic [7:0] a; logic [7:0] b;} instr_t", printed);
            StringAssert.Contains("union packed {logic [15:0] word; logic [1:0] [7:0] bytes;} u", printed);
        }

        [Test]
        public void AForeach_NamesAVariableForEachDimension_AndMaySkipOne()
        {
            var loop = (ForeachStatement)Statements("foreach (grid[i, , k]) sum += grid[i][0][k];").Single();
            Assert.AreEqual("grid", loop.Array.Name);
            CollectionAssert.AreEqual(new[] { "i", null, "k" }, loop.Variables);
            Assert.IsInstanceOf<AssignmentStatement>(loop.Body);

            StringAssert.Contains("foreach (mem[i])", Printer.Print(OnlyModule("module m;\n  initial foreach (mem[i]) mem[i] = 0;\nendmodule\n")));
        }

        [Test]
        public void APattern_TakesKeysBesideItsValues_AndADefault_AndPrintsAsWritten()
        {
            var keyed = (AssignmentPatternExpression)Expr("'{op: 4'h3, 2: a + 1, default: '0}");
            Assert.AreEqual(2, keyed.Items.Count);
            Assert.AreEqual("op", Printer.Print(keyed.Keys[0]));
            Assert.AreEqual("2", Printer.Print(keyed.Keys[1]));
            Assert.AreEqual("a + 1", Printer.Print(keyed.Items[1]));
            Assert.AreEqual("'0", Printer.Print(keyed.Default));
            Assert.AreEqual("'{op: 4'h3, 2: a + 1, default: '0}", Printer.Print(keyed));

            var positional = (AssignmentPatternExpression)Expr("'{1, '{2, 3}}");
            CollectionAssert.AreEqual(new Expression[] { null, null }, positional.Keys);
            Assert.IsInstanceOf<AssignmentPatternExpression>(positional.Items[1]);
            Assert.AreEqual("'{1, '{2, 3}}", Printer.Print(positional));
        }

        [Test]
        public void ABinaryNandOrNor_IsReadWithAWarning_BindsAsAndOrOr_AndPrintsAsWritten()
        {
            // The course's first workshop writes X ~& Y; the standard has ~& and ~| only as reductions.
            SyntaxTree tree = Read("module m;\n  assign y = a | b ~& c;\n  assign w = a ~| b & c;\nendmodule\n");

            CollectionAssert.AreEqual(new[] { DiagnosticCodes.NonStandardOperator, DiagnosticCodes.NonStandardOperator }, Codes(tree));
            Assert.IsTrue(tree.Diagnostics.All(d => d.Severity == DiagnosticSeverity.Warning));
            StringAssert.Contains("~(a & b)", tree.Diagnostics[0].Message, "the warning says what to write instead");

            var assigns = tree.Modules.Single().Items.OfType<ContinuousAssign>().Select(a => a.Assignments.Single().Value).ToArray();
            Assert.AreEqual("[a | [b ~& c]]", Show(assigns[0]));
            Assert.AreEqual("[a ~| [b & c]]", Show(assigns[1]));
            StringAssert.Contains("assign y = a | b ~& c;", Printer.Print(tree));
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
