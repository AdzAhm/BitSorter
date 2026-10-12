using System.Linq;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;
using static BitSorter.SystemVerilog.Tests.Syntax.SyntaxKit;

namespace BitSorter.SystemVerilog.Tests.Syntax
{
    /// <summary>
    /// Modules and what they hold: every form the course's workshops write, the common general ones
    /// beside them, and plain refusals for what BitSorter does not read.
    /// </summary>
    public class ParserTests
    {
        // -----------------------------------------------------------------
        // Headers
        // -----------------------------------------------------------------

        [Test]
        public void AnAnsiHeader_GivesEachPortItsDirectionTypeAndComment()
        {
            ModuleDeclaration m = OnlyModule(
                "module mux4to1(\n" +
                "    output logic O,      // the chosen input\n" +
                "    input logic D0, D1,\n" +
                "    input logic [1:0] S  // the select\n" +
                ");\nendmodule");

            Assert.AreEqual("mux4to1", m.Name);
            Assert.AreEqual(new[] { "O", "D0", "D1", "S" }, m.Ports.Select(p => p.Name).ToArray());
            Assert.AreEqual(PortDirection.Output, m.Ports[0].Direction);
            Assert.AreEqual(PortDirection.Input, m.Ports[2].Direction, "D1 takes the direction before it");
            Assert.AreEqual("logic", m.Ports[2].Type.Keyword, "and its type");
            Assert.AreEqual(1, m.Ports[3].Type.Packed.Count);
            Assert.AreEqual("the chosen input", m.Ports[0].Comment);
            Assert.AreEqual("the select", m.Ports[3].Comment);
        }

        [Test]
        public void AHeaderParameterList_IsRead()
        {
            ModuleDeclaration m = OnlyModule("module adder #(parameter N = 8, parameter int M = 2, localparam K = N + 1) (input logic [N-1:0] a);\nendmodule");
            Assert.AreEqual(new[] { "N", "M", "K" }, m.HeaderParameters.Select(p => p.Name).ToArray());
            Assert.AreEqual("int", m.HeaderParameters[1].Type.Keyword);
            Assert.IsTrue(m.HeaderParameters[2].IsLocal);
            Assert.AreEqual("N + 1", Printer.Print(m.HeaderParameters[2].Value));
        }

        [Test]
        public void ATestbench_HasNoPortsAtAll()
        {
            ModuleDeclaration m = OnlyModule("module HA_tb;\nendmodule");
            Assert.IsFalse(m.HasPortList);
            Assert.IsEmpty(m.Ports);
        }

        [Test]
        public void ANonAnsiHeader_ListsNames_AndTheBodyDeclaresThem()
        {
            ModuleDeclaration m = OnlyModule("module old(a, b, y);\n  input a, b;\n  output reg y;\nendmodule");
            Assert.AreEqual(new[] { "a", "b", "y" }, m.PortNames.ToArray());
            var outputs = (PortItemDeclaration)m.Items[1];
            Assert.AreEqual(PortDirection.Output, outputs.Direction);
            Assert.AreEqual("reg", outputs.Type.Keyword);
        }

        [Test]
        public void AWirePort_KeepsItsNetType()
        {
            ModuleDeclaration m = OnlyModule("module m(input wire logic [3:0] a, output wire y);\nendmodule");
            Assert.AreEqual("wire", m.Ports[0].NetType);
            Assert.AreEqual("logic", m.Ports[0].Type.Keyword);
            Assert.AreEqual("wire", m.Ports[1].NetType);
            Assert.IsTrue(m.Ports[1].Type.IsImplicit);
        }

        [Test]
        public void SeveralModules_AreReadInOrder()
        {
            SyntaxTree tree = Clean("module a; endmodule\nmodule b; endmodule : b\n");
            Assert.AreEqual(new[] { "a", "b" }, tree.Modules.Select(m => m.Name).ToArray());
        }

        // -----------------------------------------------------------------
        // Items the course writes
        // -----------------------------------------------------------------

        [Test]
        public void BodyParameters_AreReadOneEach()
        {
            var items = Items("parameter Tpdhl = 1;\nparameter Tpdlh = 1;\nlocalparam N = 2'b11, NUM_OF_CYCLES = 10;");
            var parameters = items.Cast<ParameterDeclaration>().ToArray();
            Assert.AreEqual(new[] { "Tpdhl", "Tpdlh", "N", "NUM_OF_CYCLES" }, parameters.Select(p => p.Name).ToArray());
            Assert.IsFalse(parameters[0].IsLocal);
            Assert.IsTrue(parameters[3].IsLocal);
        }

        [Test]
        public void Declarations_WithVectorsArraysAndValues()
        {
            var items = Items("logic a, b, c;\nlogic [7:0] w1; // w1[7] is the MSB\nlogic [31:0] mem [0:1023];\nwire w = a & b;");

            var first = (DataDeclaration)items[0];
            Assert.AreEqual(new[] { "a", "b", "c" }, first.Declarators.Select(d => d.Name).ToArray());

            var vector = (DataDeclaration)items[1];
            Assert.AreEqual("w1[7] is the MSB", vector.Comment);

            var memory = (DataDeclaration)items[2];
            Assert.AreEqual(1, memory.Declarators[0].Unpacked.Count);

            var net = (DataDeclaration)items[3];
            Assert.AreEqual("wire", net.NetType);
            Assert.IsNotNull(net.Declarators[0].Initializer);
        }

        [Test]
        public void Primitives_NamedAndUnnamed_WithAndWithoutDelays()
        {
            var items = Items("not(O1, A);\nnor(O2, A, B);\nand a1 (out1, in[0], sel);\nnand #(Tpdlh, Tpdhl) g (Z, A, B);\nbuf #3 b1 (y, a), b2 (z, a);");
            var gates = items.Cast<PrimitiveInstantiation>().ToArray();

            Assert.AreEqual("not", gates[0].Primitive);
            Assert.IsNull(gates[0].Instances[0].Name);
            Assert.AreEqual(3, gates[1].Instances[0].Terminals.Count);
            Assert.AreEqual("a1", gates[2].Instances[0].Name);
            Assert.AreEqual(2, gates[3].Delay.Values.Count);
            Assert.IsTrue(gates[3].Delay.Parenthesized);
            Assert.AreEqual(2, gates[4].Instances.Count);
        }

        [Test]
        public void Instances_ByNameAndByPosition_WithParameters()
        {
            var items = Items(
                "mymodule inst1 (net4, net1, net2);\n" +
                "mymodule inst2 (.C(net5), .A(net2), .B(net3));\n" +
                "AND2 #(.Tpdhl(2), .Tpdlh(3)) and_ist (.Z(and_out), .A(a), .B(b));\n" +
                "mymodule #(.N(2)) uut2 (clk, rst, w2);\n" +
                "shifter #(4) s (.a(a), .out());");

            var positional = (ModuleInstantiation)items[0];
            Assert.IsTrue(positional.Instances[0].Connections.All(c => c.Name == null));

            var named = (ModuleInstantiation)items[1];
            Assert.AreEqual(new[] { "C", "A", "B" }, named.Instances[0].Connections.Select(c => c.Name).ToArray());

            var withDelays = (ModuleInstantiation)items[2];
            Assert.AreEqual(new[] { "Tpdhl", "Tpdlh" }, withDelays.Parameters.Select(p => p.Name).ToArray());
            Assert.AreEqual("and_ist", withDelays.Instances[0].Name);

            var both = (ModuleInstantiation)items[3];
            Assert.AreEqual("N", both.Parameters.Single().Name);
            Assert.AreEqual(3, both.Instances[0].Connections.Count);

            var open = (ModuleInstantiation)items[4];
            Assert.IsNull(open.Parameters.Single().Name, "#(4) gives a parameter by position");
            Assert.IsNull(open.Instances[0].Connections[1].Value, ".out() is left open");
        }

        [Test]
        public void ADeclarationOfATypedefsType_IsNotTakenForAnInstance()
        {
            var items = Items("typedef enum { idle_st, start_st } sm_type;\nsm_type current_state;\nsm_type next_state;");
            var typedef = (TypedefDeclaration)items[0];
            Assert.AreEqual(new[] { "idle_st", "start_st" }, typedef.Type.Enum.Members.Select(m => m.Name).ToArray());
            Assert.IsInstanceOf<DataDeclaration>(items[1]);
            Assert.AreEqual("sm_type", ((DataDeclaration)items[1]).Type.Name);
        }

        [Test]
        public void AnEnumWithABaseTypeAndValues()
        {
            var typedef = (TypedefDeclaration)Items("typedef enum logic [1:0] {RED = 2'b00, GREEN = 2'b11} light_t;").Single();
            Assert.AreEqual("logic", typedef.Type.Enum.BaseType.Keyword);
            Assert.AreEqual("2'b11", Printer.Print(typedef.Type.Enum.Members[1].Value));
        }

        [Test]
        public void TheWorkshopsGenerate_GenvarOutsideAndIPlusPlus()
        {
            var items = Items(
                "genvar i;\n" +
                "generate\n" +
                "for (i = 0; i < 32; i++)\n" +
                "begin\n" +
                "mux2to1 mux_inst(.O(O[i]), .S(S),\n.A(A[i]), .B(B[i]));\n" +
                "end\n" +
                "endgenerate");

            Assert.AreEqual("i", ((GenvarDeclaration)items[0]).Names.Single());
            var region = (GenerateRegion)items[1];
            var loop = (LoopGenerate)region.Items.Single();
            Assert.IsFalse(loop.DeclaresGenvar);
            Assert.AreEqual("++", loop.StepOperator);
            Assert.IsInstanceOf<ModuleInstantiation>(loop.Body.Items.Single());
        }

        [Test]
        public void AGenerateFor_WithItsOwnGenvarAndANamedBlock()
        {
            var loop = (LoopGenerate)Items("for (genvar k = 0; k < N; k = k + 1) begin : stage\n assign y[k] = a[k];\nend").Single();
            Assert.IsTrue(loop.DeclaresGenvar);
            Assert.AreEqual("stage", loop.Body.Label);
            Assert.AreEqual("=", loop.StepOperator);
        }

        [Test]
        public void IfGenerate_WithElseIf()
        {
            var item = (IfGenerate)Items("if (N == 1) begin\n assign y = a;\nend else if (N == 2) begin\n assign y = b;\nend else\n assign y = c;").Single();
            Assert.IsInstanceOf<IfGenerate>(item.Else.Items.Single());
        }

        [Test]
        public void ContinuousAssigns_ToBitsAndPartsAndConcatenations()
        {
            var items = Items("assign z[3] = a[1];\nassign z[1:0] = 2'b01;\nassign {carry, sum} = vec[1:0];\nassign #2 y = a, w = b;");
            Assert.IsInstanceOf<IndexExpression>(((ContinuousAssign)items[0]).Assignments[0].Target);
            Assert.IsInstanceOf<RangeSelectExpression>(((ContinuousAssign)items[1]).Assignments[0].Target);
            Assert.IsInstanceOf<ConcatenationExpression>(((ContinuousAssign)items[2]).Assignments[0].Target);
            var delayed = (ContinuousAssign)items[3];
            Assert.IsNotNull(delayed.Delay);
            Assert.AreEqual(2, delayed.Assignments.Count);
        }

        [Test]
        public void Processes_OfEveryKind()
        {
            var items = Items(
                "always_comb begin Z = A & B; end\n" +
                "always_ff @(posedge clk, posedge rst) a <= in;\n" +
                "always @(*) y = a;\n" +
                "always begin #10 clk = ~clk; end\n" +
                "initial begin a = 0; end\n" +
                "always_latch if (en) q = d;");

            Assert.AreEqual(new[] { ProcessKind.AlwaysComb, ProcessKind.AlwaysFf, ProcessKind.Always, ProcessKind.Always, ProcessKind.Initial, ProcessKind.AlwaysLatch },
                items.Cast<ProceduralBlock>().Select(p => p.Kind).ToArray());

            var ff = (ProceduralBlock)items[1];
            var timed = (TimedStatement)ff.Body;
            var events = (EventControl)timed.Control;
            Assert.AreEqual(new[] { "posedge", "posedge" }, events.Events.Select(e => e.Edge).ToArray());
            Assert.IsTrue(((EventControl)((TimedStatement)((ProceduralBlock)items[2]).Body).Control).Implicit);
        }

        [Test]
        public void Functions_AndTasks()
        {
            var items = Items(
                "function automatic logic [7:0] max8(input logic [7:0] a, b);\n  return a > b ? a : b;\nendfunction\n" +
                "function integer parity;\n  input [7:0] v;\n  parity = ^v;\nendfunction\n" +
                "task pulse(output logic s);\n  s = 1; #1 s = 0;\nendtask");

            var max = (SubroutineDeclaration)items[0];
            Assert.IsTrue(max.IsAutomatic);
            Assert.AreEqual(new[] { "a", "b" }, max.Ports.Select(p => p.Name).ToArray());
            Assert.IsInstanceOf<ReturnStatement>(max.Statements.Single());

            var parity = (SubroutineDeclaration)items[1];
            Assert.IsInstanceOf<PortItemDeclaration>(parity.Declarations.Single());

            var pulse = (SubroutineDeclaration)items[2];
            Assert.IsTrue(pulse.IsTask);
            Assert.AreEqual(2, pulse.Statements.Count);
        }

        // -----------------------------------------------------------------
        // Refusals and recovery
        // -----------------------------------------------------------------

        [Test]
        public void WhatBitSorterDoesNotRead_IsRefusedWhereItStands_AndTheRestIsStillRead()
        {
            SyntaxTree tree = Read(
                "class packet; int x; endclass\n" +
                "module m;\n" +
                "  defparam u.N = 4;\n" +
                "  assert property (a);\n" +
                "  assign y = a;\n" +
                "endmodule\n");

            Assert.AreEqual(new[] { DiagnosticCodes.Unsupported, DiagnosticCodes.Unsupported, DiagnosticCodes.Unsupported }, Codes(tree));
            StringAssert.Contains("defparam", tree.Diagnostics[1].Message);
            Assert.AreEqual(3, tree.Diagnostics[1].Location.Line);
            Assert.IsInstanceOf<ContinuousAssign>(tree.Modules.Single().Items.Last(), "the assign after them was still read");
        }

        [Test]
        public void AMissingSemicolon_IsReportedOnTheLineItIsMissingFrom()
        {
            SyntaxTree tree = Read("module m;\n  logic a\n  assign a = 1;\nendmodule\n");
            Diagnostic d = tree.Diagnostics.First();
            Assert.AreEqual(DiagnosticCodes.Expected, d.Code);
            Assert.AreEqual(2, d.Location.Line, d.ToString());
            StringAssert.Contains("Missing ';'", d.Message);
        }

        [Test]
        public void AnAssignmentWithoutAssign_IsExplained()
        {
            SyntaxTree tree = Read("module m;\n  y = a & b;\nendmodule\n");
            Assert.AreEqual(DiagnosticCodes.AssignmentOutsideProcess, tree.Diagnostics.Single().Code);
            StringAssert.Contains("assign y", tree.Diagnostics.Single().Message);
        }

        [Test]
        public void AReservedWordAsAName_SaysSo()
        {
            SyntaxTree tree = Read("module m;\n  logic output;\nendmodule\n");
            StringAssert.Contains("reserved word", tree.Diagnostics.First().Message);
        }

        [Test]
        public void CodeOutsideAModule_IsReported()
        {
            SyntaxTree tree = Read("assign y = 1;\nmodule m; endmodule");
            Assert.AreEqual(DiagnosticCodes.NotInModule, tree.Diagnostics.First().Code);
            Assert.AreEqual("m", tree.Modules.Single().Name);
        }

        [Test]
        public void AModuleWithNoEnd_IsReported()
        {
            SyntaxTree tree = Read("module m;\n  assign y = 1;\n");
            StringAssert.Contains("endmodule", tree.Diagnostics.Last().Message);
            Assert.AreEqual(1, tree.Modules.Single().Items.Count);
        }

        [Test]
        public void TheSlidesTypos_AreErrorsButDoNotDerailTheRead()
        {
            // '20#' and ';(' appear in the workshop slides; a student copying them should get errors, not a crash.
            SyntaxTree tree = Read("module HA_tb;\nlogic a;\ninitial begin\na = 0;\n20#\na = 1;\nend\nendmodule\nmodule next; endmodule\n");
            Assert.IsTrue(tree.HasErrors);
            Assert.AreEqual(new[] { "HA_tb", "next" }, tree.Modules.Select(m => m.Name).ToArray());
        }
    }
}
