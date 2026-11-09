using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// The elaborator: SystemVerilog in, a flattened design out, simulated by the engine. Each test
    /// writes a small design, elaborates it, and runs it -- so what is held is what the design does,
    /// not how the netlist happens to be laid out. The oracle (Tools/ci/Oracle) runs the structural
    /// corpus against Icarus as well.
    /// </summary>
    public class ElaboratorTests
    {
        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static Elaboration Elaborate(string text, string top = null, ISourceFiles files = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv", files);
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, top);
        }

        /// <summary>Elaborates, and fails the test on anything said at all.</summary>
        private static Design Clean(string text, string top = null)
        {
            Elaboration e = Elaborate(text, top);
            Assert.IsEmpty(e.Diagnostics, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            Assert.IsNotNull(e.Design);
            return e.Design;
        }

        private static string[] Codes(Elaboration e) => e.Diagnostics.Select(d => d.Code).ToArray();

        private static LogicVec B(string bits) => LogicVec.FromBinary(bits);

        private static LogicVec U(ulong value, int width) => LogicVec.FromUInt64(value, width);

        /// <summary>The design run with these writes at time 0, settled.</summary>
        private static Simulator Settle(Design design, params (string Name, LogicVec Value)[] writes)
        {
            var stimulus = new Stimulus();
            foreach ((string name, LogicVec value) in writes)
                stimulus.Set(0, design.Find(design.Name + "." + name), value);

            var sim = new Simulator(design, stimulus);
            sim.RunTo(1000);
            return sim;
        }

        private static string Bits(Simulator sim, string name) =>
            sim.Value(sim.Design.Find(sim.Design.Name + "." + name)).Format(Radix.Binary);

        private static MemorySourceFiles Corpus()
        {
            string root = RepoRoot.Combine("Tools/ci/Oracle/corpus");
            var files = new MemorySourceFiles();
            foreach (string path in Directory.GetFiles(root, "*.sv*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path));
            return files;
        }

        private static Elaboration ElaborateCorpus(string relative, string top = null)
        {
            SyntaxTree tree = SyntaxReader.Read(relative, Corpus());
            Assert.IsEmpty(tree.Diagnostics, string.Join("\n", tree.Diagnostics));
            return Elaborator.Elaborate(tree, top);
        }

        // -----------------------------------------------------------------
        // Combinational designs, end to end
        // -----------------------------------------------------------------

        [Test]
        public void AHalfAdder_FromAssigns_GivesItsTruthTable()
        {
            Design design = Clean(@"
module half_adder (input logic a, b, output logic s, c);
    assign s = a ^ b;
    assign c = a & b;
endmodule");

            Assert.AreEqual("half_adder", design.Name);
            foreach ((string a, string b, string s, string c) in new[] { ("0", "0", "0", "0"), ("0", "1", "1", "0"), ("1", "0", "1", "0"), ("1", "1", "0", "1") })
            {
                Simulator sim = Settle(design, ("a", B(a)), ("b", B(b)));
                Assert.AreEqual(s + c, Bits(sim, "s") + Bits(sim, "c"), $"a={a} b={b}");
            }
        }

        [Test]
        public void TheTopsPorts_AreListedInOrder_AndItsInputsAreVariablesAStimulusCanWrite()
        {
            Elaboration e = Elaborate(@"
module m (input logic [3:0] a, input logic signed [7:0] b, output logic y, output wire [1:0] z);
    assign y = |a;
    assign z = b[1:0];
endmodule");

            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            CollectionAssert.AreEqual(new[] { "a", "b", "y", "z" }, e.Ports.Select(p => p.Name));
            CollectionAssert.AreEqual(new[] { PortDirection.Input, PortDirection.Input, PortDirection.Output, PortDirection.Output }, e.Ports.Select(p => p.Direction));
            CollectionAssert.AreEqual(new[] { 4, 8, 1, 2 }, e.Ports.Select(p => p.Width));
            Assert.IsTrue(e.Ports[1].Signed);

            Assert.AreEqual(SignalKind.Variable, e.Design[e.Ports[0].Signal].Kind, "the top's inputs are written by the stimulus");
            Assert.AreEqual(SignalKind.Variable, e.Design[e.Ports[2].Signal].Kind, "output logic is a variable");
            Assert.AreEqual(SignalKind.Net, e.Design[e.Ports[3].Signal].Kind, "output wire is a net");
        }

        [Test]
        public void AConcatenationOnTheLeft_KeepsTheCarry_BecauseTheAdditionIsDoneAtItsWidth()
        {
            Design design = Clean(@"
module adder (input logic [3:0] a, b, output logic [3:0] sum, output logic cout);
    assign {cout, sum} = a + b;
endmodule");

            Simulator sim = Settle(design, ("a", U(0xF, 4)), ("b", U(0x1, 4)));
            Assert.AreEqual("0000", Bits(sim, "sum"));
            Assert.AreEqual("1", Bits(sim, "cout"), "the context is five bits wide, so the sum is too");
        }

        [Test]
        public void AComparisonsOperands_AreSizedAgainstEachOther_NotAgainstTheResult()
        {
            Design design = Clean(@"
module cmp (input logic [3:0] a, b, output logic narrow, wide);
    assign narrow = (a + b) > 4'hF;    // four bits: the carry is lost
    assign wide = (a + b) > 5'h0F;     // five bits: it is kept
endmodule");

            Simulator sim = Settle(design, ("a", U(0xF, 4)), ("b", U(0x1, 4)));
            Assert.AreEqual("0", Bits(sim, "narrow"));
            Assert.AreEqual("1", Bits(sim, "wide"));
        }

        [Test]
        public void ASignedValue_IsSignExtended_OnlyWhenTheWholeExpressionIsSigned()
        {
            Design design = Clean(@"
module ext (input logic signed [3:0] a, output logic signed [7:0] same, output logic [7:0] mixed, output logic [7:0] cast);
    assign same = a;                    // signed into a wider target: the sign is copied
    assign mixed = a + 4'b0001;         // an unsigned operand makes it all unsigned: zeros
    assign cast = $unsigned(a);
endmodule");

            Simulator sim = Settle(design, ("a", B("1110")));
            Assert.AreEqual("11111110", Bits(sim, "same"));
            Assert.AreEqual("00001111", Bits(sim, "mixed"));
            Assert.AreEqual("00001110", Bits(sim, "cast"));
        }

        [Test]
        public void ASignedOperand_IsReadUnsigned_InsideAnUnsignedExpression()
        {
            Design design = Clean(@"
module div (input logic signed [3:0] a, b, input logic [3:0] u, output logic both, mixed);
    assign both = (a / b) < 4'sd0;      // all signed: -6 / 2 = -3, below zero
    assign mixed = (a / b) < u;         // u is unsigned, so the division is too: 10 / 2 = 5, not below 3
endmodule");

            Simulator sim = Settle(design, ("a", B("1010")), ("b", B("0010")), ("u", U(3, 4)));
            Assert.AreEqual("1", Bits(sim, "both"));
            Assert.AreEqual("0", Bits(sim, "mixed"));
        }

        [Test]
        public void SelectsCountAgainstTheDeclaredRange_EitherWayRound()
        {
            Design design = Clean(@"
module sel (input logic [7:4] d, input logic [0:3] u, input logic [1:0] i,
            output logic top, low, ubit, output logic [1:0] mid, upart, output logic dyn, output logic [1:0] dpart);
    assign top = d[7];
    assign low = d[4];
    assign mid = d[6:5];
    assign ubit = u[0];          // [0:3]: bit 0 is the most significant
    assign upart = u[1:2];
    assign dyn = d[i + 4];       // a variable index, counted from 4
    assign dpart = d[i + 4 +: 2];
endmodule");

            Simulator sim = Settle(design, ("d", B("1001")), ("u", B("1010")), ("i", B("01")));
            Assert.AreEqual("1", Bits(sim, "top"));
            Assert.AreEqual("1", Bits(sim, "low"));
            Assert.AreEqual("00", Bits(sim, "mid"));
            Assert.AreEqual("1", Bits(sim, "ubit"));
            Assert.AreEqual("01", Bits(sim, "upart"));
            Assert.AreEqual("0", Bits(sim, "dyn"), "d[5]");
            Assert.AreEqual("00", Bits(sim, "dpart"), "d[6:5]");

            sim = Settle(design, ("d", B("1001")), ("u", B("1010")), ("i", B("11")));
            Assert.AreEqual("1", Bits(sim, "dyn"), "d[7]");
            Assert.AreEqual("x1", Bits(sim, "dpart"), "d[8:7]: past the top reads x");
        }

        [Test]
        public void PartOfAVector_CanBeDrivenOnTheLeft_ByEachOfSeveralAssigns()
        {
            Design design = Clean(@"
module parts (input logic [1:0] a, input logic b, output logic [3:0] y);
    assign y[3:2] = a;
    assign y[1] = b;
    assign y[0] = ~b;
endmodule");

            Simulator sim = Settle(design, ("a", B("10")), ("b", B("1")));
            Assert.AreEqual("1010", Bits(sim, "y"));
        }

        [Test]
        public void ReplicationAndConcatenation_Build_TheValuesTheyDescribe()
        {
            Design design = Clean(@"
module rep (input logic [1:0] a, input logic s, output logic [7:0] y, output logic [5:0] z);
    assign y = {4{a}};
    assign z = {{4{s}}, a};
endmodule");

            Simulator sim = Settle(design, ("a", B("10")), ("s", B("1")));
            Assert.AreEqual("10101010", Bits(sim, "y"));
            Assert.AreEqual("111110", Bits(sim, "z"));
        }

        [Test]
        public void TheConditionalOperator_MergesBothChoices_WhenItsConditionIsX()
        {
            Design design = Clean(@"
module mux (input logic [3:0] a, b, input logic s, output logic [3:0] y);
    assign y = s ? a : b;
endmodule");

            Simulator sim = Settle(design, ("a", B("1100")), ("b", B("1010")), ("s", B("x")));
            Assert.AreEqual("1xx0", Bits(sim, "y"));
        }

        // -----------------------------------------------------------------
        // Parameters, typedefs, constants
        // -----------------------------------------------------------------

        [Test]
        public void Parameters_AreWorkedOutInOrder_AndAnInstanceSetsThemByNameOrByPosition()
        {
            Design design = Clean(@"
module widths #(parameter N = 4, parameter M = N * 2) (output logic [M-1:0] y);
    localparam ONES = {M{1'b1}};
    assign y = ONES;
endmodule

module top (output logic [15:0] by_name, output logic [5:0] by_position, output logic [7:0] by_default);
    widths #(.N(8)) a (.y(by_name));
    widths #(1, 6) b (.y(by_position));
    widths c (.y(by_default));
endmodule");

            Simulator sim = Settle(design);
            Assert.AreEqual(new string('1', 16), Bits(sim, "by_name"));
            Assert.AreEqual(new string('1', 6), Bits(sim, "by_position"));
            Assert.AreEqual(new string('1', 8), Bits(sim, "by_default"));
            Assert.AreEqual(16, design[design.Find("top.a.y")].Width);
        }

        [Test]
        public void ABodyParameter_CanBeSet_OnlyWhenTheHeaderHasNoParameterList()
        {
            Design design = Clean(@"
module delay_cell (input logic a, output logic z);
    parameter D = 0;
    buf #(D) g (z, a);
endmodule

module top (input logic a, output logic z);
    delay_cell #(.D(5)) c (.a(a), .z(z));
endmodule");

            var sim = new Simulator(design, new Stimulus().Set(0, design.Find("top.a"), Logic4.One));
            sim.RunTo(10);
            CollectionAssert.AreEqual(new[] { "0 x", "5 1" }, Probes.Settled(sim.History, design.Find("top.z")));

            Elaboration e = Elaborate(@"
module delay_cell #(parameter W = 1) (input logic a, output logic z);
    parameter D = 0;        // a localparam, since the header has a list
    buf #(D) g (z, a);
endmodule

module top (input logic a, output logic z);
    delay_cell #(.D(5)) c (.a(a), .z(z));
endmodule");
            CollectionAssert.Contains(Codes(e), ElaborationCodes.BadParameter);
            Assert.IsNull(e.Design);
        }

        [Test]
        public void ATypedParameter_IsTheContextItsValueIsSizedIn()
        {
            Design design = Clean(@"
module k (output logic [5:0] ones, output logic [7:0] wide);
    localparam [5:0] ONES = '1;           // '1 fills the parameter's six bits
    localparam logic [7:0] SUM = 4'hF + 4'h1;   // the addition is done at eight bits, keeping the carry
    assign ones = ONES;
    assign wide = SUM;
endmodule");

            Simulator sim = Settle(design);
            Assert.AreEqual("111111", Bits(sim, "ones"));
            Assert.AreEqual("00010000", Bits(sim, "wide"));
        }

        [Test]
        public void ConstantFunctions_AndPower_AreWorkedOutBeforeTheRun()
        {
            Design design = Clean(@"
module k (output logic [31:0] bits, output logic [31:0] size, output logic [7:0] pow);
    localparam DEPTH = 1000;
    localparam W = $clog2(DEPTH);
    assign bits = W;
    assign size = 2 ** W;
    assign pow = $bits(pow);
endmodule");

            Simulator sim = Settle(design);
            Assert.AreEqual(U(10, 32).Format(Radix.Binary), Bits(sim, "bits"));
            Assert.AreEqual(U(1024, 32).Format(Radix.Binary), Bits(sim, "size"));
            Assert.AreEqual(U(8, 8).Format(Radix.Binary), Bits(sim, "pow"));
        }

        [Test]
        public void ATypedefEnum_GivesItsNamesTheirValues_AndItsTypeItsWidth()
        {
            Design design = Clean(@"
module colours (output logic [1:0] first, last, output logic wide);
    typedef enum logic [1:0] {RED, GREEN = 2, BLUE} colour_t;
    colour_t c;
    assign c = BLUE;
    assign first = RED;
    assign last = c;
    assign wide = $bits(c) == 2;
endmodule");

            Simulator sim = Settle(design);
            Assert.AreEqual("00", Bits(sim, "first"));
            Assert.AreEqual("11", Bits(sim, "last"), "BLUE comes after GREEN = 2");
            Assert.AreEqual("1", Bits(sim, "wide"));
        }

        // -----------------------------------------------------------------
        // Gates and delays
        // -----------------------------------------------------------------

        [Test]
        public void AGatesOutput_CanBeOneBitOfAVector_AndItsInputsBitsOfOthers()
        {
            Design design = Clean(@"
module g (input logic [1:0] a, output logic [1:0] y);
    and g0 (y[0], a[0], a[1]);
    xor g1 (y[1], a[0], a[1]);
endmodule");

            Simulator sim = Settle(design, ("a", B("11")));
            Assert.AreEqual("01", Bits(sim, "y"));
        }

        [Test]
        public void TheCorpusMux_ChangesItsOutput_AfterTheSlowPathsDelaysAddedUp()
        {
            // a = 1, b = 0, s rises at 10. The slow path is the inverter (3) then two NANDs (1 each):
            // s_n falls at 13, pick_a rises at 14, y falls at 15. From time 0, y waits on s_n (3),
            // pick_a (4) and settles at 5.
            Elaboration e = ElaborateCorpus("structural/mux2.sv");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            Design design = e.Design;

            var stimulus = new Stimulus()
                .Set(0, design.Find("mux2.a"), Logic4.One)
                .Set(0, design.Find("mux2.b"), Logic4.Zero)
                .Set(0, design.Find("mux2.s"), Logic4.Zero)
                .Set(10, design.Find("mux2.s"), Logic4.One);

            var sim = new Simulator(design, stimulus);
            sim.RunTo(40);

            CollectionAssert.AreEqual(new[] { "0 x", "5 1", "15 0" }, Probes.Settled(sim.History, design.Find("mux2.y")));
            CollectionAssert.AreEqual(new[] { "0 x", "3 1", "13 0" }, Probes.Settled(sim.History, design.Find("mux2.g1.Z")));
        }

        [Test]
        public void TheCorpusAlu_IsSixtyFourSlicesWithASixtyFiveBitCarry_AndTheCarryRipplesTwoUnitsASlice()
        {
            // op = 10, add. a is all ones and b is 0, settled; at 200 b becomes 1. Each slice's carry
            // goes through an AND and an OR of delay 1, so carry[i] rises at 200 + 2i and the carry out
            // at 328; sum[i] is carry[i] through an XOR, falling at 201 + 2i, the last at 327.
            Elaboration e = ElaborateCorpus("structural/alu64.sv");
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            Design design = e.Design;

            Assert.AreEqual("alu64", design.Name);
            Assert.AreEqual(65, design[design.Find("alu64.carry")].Width);
            Assert.IsTrue(design.TryFind("alu64.slice[63].s.fa.sum", out _), "named generate blocks are named as Icarus names them");

            var stimulus = new Stimulus()
                .Set(0, design.Find("alu64.a"), LogicVec.Fill(Logic4.One, 64))
                .Set(0, design.Find("alu64.b"), U(0, 64))
                .Set(0, design.Find("alu64.op"), B("10"))
                .Set(200, design.Find("alu64.b"), U(1, 64));

            var sim = new Simulator(design, stimulus);
            sim.RunTo(1000);

            CollectionAssert.AreEqual(new[] { "0 x", "128 0", "328 1" }, Probes.Settled(sim.History, design.Find("alu64.carry_out")));

            SignalId y = design.Find("alu64.y");
            Assert.AreEqual("1" + new string('0', 63), sim.History.ValueAt(y, 326).Format(Radix.Binary), "bit i falls at 201 + 2i");
            Assert.AreEqual(new string('0', 64), sim.History.ValueAt(y, 327).Format(Radix.Binary));
        }

        [Test]
        public void AGenerateLoopWithNoLabel_IsNamedGenblk_AndAnIfGenerateChoosesItsBranch()
        {
            Design design = Clean(@"
module gen #(parameter N = 3, parameter INVERT = 1) (input logic [N-1:0] a, output logic [N-1:0] y);
    genvar i;
    for (i = 0; i < N; i++) begin
        if (INVERT) begin : flip
            assign y[i] = ~a[i];
        end else begin : keep
            assign y[i] = a[i];
        end
    end
endmodule");

            Simulator sim = Settle(design, ("a", B("101")));
            Assert.AreEqual("010", Bits(sim, "y"));

            // No signal is declared inside, so check the names through a wire that is.
            Design named = Clean(@"
module gen2 (input logic [1:0] a, output logic [1:0] y);
    for (genvar i = 0; i < 2; i = i + 1) begin
        wire t = a[i];
        assign y[i] = t;
    end
    if (1) begin
        wire u = 1'b1;
    end
endmodule");
            Assert.IsTrue(named.TryFind("gen2.genblk1[0].t", out _));
            Assert.IsTrue(named.TryFind("gen2.genblk1[1].t", out _));
            Assert.IsTrue(named.TryFind("gen2.genblk2.u", out _), "the second generate construct in the module");
        }

        [Test]
        public void PortsConnect_ByPosition_ByShorthand_AndByWildcard()
        {
            Design design = Clean(@"
module inv (input logic a, output logic y);
    assign y = ~a;
endmodule

module top (input logic a, output logic y1, y2, y3);
    logic y;
    inv p (a, y1);
    inv s (.a, .y(y2));
    inv w (.*);
    assign y3 = y;
endmodule");

            Simulator sim = Settle(design, ("a", B("1")));
            Assert.AreEqual("0", Bits(sim, "y1"));
            Assert.AreEqual("0", Bits(sim, "y2"));
            Assert.AreEqual("0", Bits(sim, "y3"));
        }

        [Test]
        public void NonAnsiPorts_TakeTheirKindFromASecondDeclaration()
        {
            Design design = Clean(@"
module old (a, b, y);
    input [3:0] a;
    input b;
    output [3:0] y;
    reg [3:0] y;
    assign y = b ? a : 4'b0;
endmodule");

            Assert.AreEqual(SignalKind.Variable, design[design.Find("old.y")].Kind);
            Assert.AreEqual(4, design[design.Find("old.y")].Width);
            Simulator sim = Settle(design, ("a", B("0110")), ("b", B("1")));
            Assert.AreEqual("0110", Bits(sim, "y"));
        }

        [Test]
        public void AnUnconnectedInput_ReadsZ_AndSaysSo()
        {
            Elaboration e = Elaborate(@"
module pass (input logic a, output logic y);
    assign y = a;
endmodule

module top (output logic y);
    pass p (.y(y));
endmodule");

            CollectionAssert.AreEqual(new[] { ElaborationCodes.Unconnected }, Codes(e));
            Simulator sim = Settle(e.Design);
            Assert.AreEqual("z", Bits(sim, "y"));
        }

        [Test]
        public void TwoDriversOnAWire_Resolve_AndOnAVariable_AreRefusedWithTheWayOut()
        {
            Elaboration both = Elaborate(@"
module w (input logic a, b, output wire y);
    assign y = a;
    assign y = b;
endmodule");
            CollectionAssert.AreEqual(new[] { ElaborationCodes.SeveralDrivers }, Codes(both), "a warning since Part 14e, never a refusal: it runs");
            Design wire = both.Design;
            Assert.AreEqual("x", Bits(Settle(wire, ("a", B("0")), ("b", B("1"))), "y"));
            Assert.AreEqual("1", Bits(Settle(wire, ("a", B("z")), ("b", B("1"))), "y"));

            Elaboration e = Elaborate(@"
module v (input logic a, b, output logic y);
    assign y = a;
    assign y = b;
endmodule");
            CollectionAssert.AreEqual(new[] { ElaborationCodes.Drivers }, Codes(e));
            StringAssert.Contains("wire", e.Diagnostics[0].Message);
            Assert.AreEqual(4, e.Diagnostics[0].Location.Line, "the second assign is the one refused");
        }

        // -----------------------------------------------------------------
        // What it says, and where
        // -----------------------------------------------------------------

        [TestCase("module m (output logic y); undefined_thing u (.a(y)); endmodule", ElaborationCodes.UnknownModule)]
        [TestCase("module m (output logic y); assign y = nowhere; endmodule", ElaborationCodes.UnknownName)]
        [TestCase("module m (input logic a, output logic [3:0] y); assign y = {a, 3}; endmodule", ElaborationCodes.UnsizedInConcatenation)]
        [TestCase("module m (input logic a, output logic y); function logic slow(input logic x); #1 return ~x; endfunction assign y = slow(a); endmodule", ElaborationCodes.BadCall)]
        [TestCase("module m (input logic a, output logic y); task t; endtask assign y = t(a); endmodule", ElaborationCodes.BadCall)]
        [TestCase("module m (input logic a, output logic y); assign y = missing(a); endmodule", ElaborationCodes.UnknownName)]
        [TestCase("module m (input logic a, output logic y); assign y[2] = a; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic [3:0] a, output logic y); assign y = a[1:2]; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic a, output logic y); and #(1, 2, 3, 4) g (y, a, a); endmodule", ElaborationCodes.BadDelay)]
        [TestCase("module m (input logic a, output logic y); localparam P = a; assign y = P; endmodule", ElaborationCodes.NotConstant)]
        [TestCase("module m (input logic a, output logic y); assign 1'b0 = a; endmodule", ElaborationCodes.BadLValue)]
        [TestCase("module m (input logic a, output logic y); logic y; endmodule", ElaborationCodes.Duplicate)]
        [TestCase("module m (output logic y); struct { logic a; } s; endmodule", ElaborationCodes.NotYet)]
        [TestCase("module c (input logic a); endmodule\nmodule m (output logic y); c i (.b(y)); endmodule", ElaborationCodes.BadPort)]
        [TestCase("module c #(parameter A = 1, localparam B = 2) (input logic a); endmodule\nmodule m (input logic y); c #(.B(3)) i (.a(y)); endmodule", ElaborationCodes.BadParameter)]
        [TestCase("module r (input logic a); r again (.a(a)); endmodule\nmodule m (input logic y); r i (.a(y)); endmodule", ElaborationCodes.Recursion)]
        [TestCase("module m (input logic a, output logic y); for (genvar i = 0; i >= 0; i++) begin end endmodule", ElaborationCodes.TooLarge)]
        // A design bigger than a run can hold (Part 13f): each piece is allowed alone -- a memory under a million words, a
        // signal under a million bits -- but a million words of a million bits would ask for terabytes the moment it ran.
        [TestCase("module m (output logic y); logic [1023:0] mem [0:131071]; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (output logic y); for (genvar i = 0; i < 100; i++) begin : g logic [1048575:0] w; end endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (output logic y); logic [1048575:0] mem [0:1048575]; endmodule", ElaborationCodes.TooLarge)]
        // And by signals: a memory's every word is one, whatever its width, so a million one-bit words -- inside the bits --
        // still asked for most of a gigabyte (Part 13f review).
        [TestCase("module m (output logic y); logic mem [0:1048575]; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (output logic y); logic [7:0] a [0:131071]; logic [7:0] b [0:131071]; endmodule", ElaborationCodes.TooLarge)]
        // And by parts (the Part 16d sweep): each generate loop was held to 65,536 rounds alone, never a loop inside another, and
        // a round that declares no signal and makes no instance was counted by nothing -- two loops of 65,536 made 4.3 thousand
        // million blocks, and a browser's page ran out of memory with no word said.
        [TestCase("module m; for (genvar i = 0; i < 600; i++) begin : a for (genvar j = 0; j < 600; j++) begin : b end end endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (input logic [399:0] a, b, output logic [159999:0] y); for (genvar i = 0; i < 400; i++) begin : r " +
            "for (genvar j = 0; j < 400; j++) begin : c assign y[i*400+j] = a[i] & b[j]; end end endmodule", ElaborationCodes.TooLarge)]
        public void AnError_IsReported_AndThereIsNoDesign(string text, string code)
        {
            Elaboration e = Elaborate(text);
            CollectionAssert.Contains(Codes(e), code, string.Join("\n", e.Diagnostics));
            Assert.IsTrue(e.HasErrors);
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// The 16d sweep: a run of operators counts as levels of nesting, so the reader stops one too deep for the walks that
        /// build it a call a level -- and one just inside that limit is built and run. Its review: the limit was the brackets'
        /// 200, so a parity of 250 bits written out, which ran before, was refused; a tree may be as tall as the deepest
        /// bracket nest makes one, twice that, and the elaborator was measured to build a run that tall in the stack that
        /// nest takes.
        /// </summary>
        [Test]
        public void ARunOfOperatorsJustInsideTheReadersLimit_IsBuiltAndRun()
        {
            int terms = Parser.MaxHeight - 10;
            string chain = string.Join(" ^ ", Enumerable.Range(0, terms).Select(i => $"a[{i}]"));
            Design design = Clean($"module m (input logic [{terms - 1}:0] a, output logic y); assign y = {chain}; endmodule");

            var ones = new string('1', terms - 1);
            Assert.AreEqual((terms - 1) % 2 == 0 ? "0" : "1", Bits(Settle(design, ("a", B("0" + ones))), "y"), "the parity of the ones");
        }

        /// <summary>
        /// What <see cref="Parser.MaxHeight"/> was chosen by, held: the tallest expressions the reader takes -- a run as long as
        /// it allows, and a bracket nest as deep, each bracket holding an operator -- read, printed, elaborated, run and
        /// extracted for the drawing in three quarters of a megabyte of stack, a quarter of the smallest any host gives left
        /// for the caller's frames. Too much stack is an overflow, which nothing catches: the test run itself stops, loudly.
        /// Measured in an optimized build, as CI's is; without optimization every frame is larger, and the same two need
        /// more than a megabyte -- as the 200 brackets the reader always took did.
        /// </summary>
        [Test]
        public void TheTallestExpressionsTheReaderTakes_AreBuiltInThreeQuartersOfAMegabyte()
        {
            var debuggable = (System.Diagnostics.DebuggableAttribute)Attribute.GetCustomAttribute(typeof(Elaborator).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
            if (debuggable != null && debuggable.IsJITOptimizerDisabled)
                Assert.Ignore("measured only in an optimized build, as CI builds it");

            string run = string.Join(" ^ ", Enumerable.Range(0, Parser.MaxHeight - 2).Select(i => $"a[{i % 8}]"));
            string nest = string.Concat(Enumerable.Repeat("(a[0] ^ ", Parser.MaxDepth - 2)) + "a[1]" + new string(')', Parser.MaxDepth - 2);

            foreach (string expression in new[] { run, nest })
            {
                string text = $"module m (input logic [7:0] a, output logic [7:0] y);\n    assign y = {expression};\nendmodule\n";
                string failed = null;
                var thread = new System.Threading.Thread(() =>
                {
                    SyntaxTree tree = SyntaxReader.ReadText(text, "tall.sv");
                    if (tree.Diagnostics.Count > 0)
                    {
                        failed = "the read reported:\n" + string.Join("\n", tree.Diagnostics);
                        return;
                    }
                    Printer.Print(tree);
                    Elaboration e = Elaborator.Elaborate(tree, "m");
                    if (e.HasErrors)
                    {
                        failed = "the elaborator reported:\n" + string.Join("\n", e.Diagnostics);
                        return;
                    }
                    new Simulator(e.Design, null, new SimOptions()).RunTo(10);
                    RtlExtractor.Of(e.Design, "m");
                }, 768 * 1024);
                thread.Start();
                thread.Join();
                Assert.IsNull(failed, failed);
            }
        }

        /// <summary>
        /// The 16d sweep: the messages are capped so nonsense gives a readable number of them, and the cap was one for every
        /// kind -- so six hundred copies of an instance, each with an input left unconnected and warned of, filled it, and the
        /// error after them was dropped: no error, and the design built and run without what failed.
        /// </summary>
        [TestCase("missing u2 (.a(y));", ElaborationCodes.UnknownModule)]
        [TestCase("assign y = nowhere(a);", ElaborationCodes.UnknownName)]
        public void AnErrorAfterTheCapOfWarnings_IsStillReported_AndThereIsNoDesign(string fault, string code)
        {
            Elaboration e = Elaborate(@"
module leaf (input logic a, input logic b, output logic y);
    assign y = a & b;
endmodule

module top (input logic a, output logic y);
    for (genvar i = 0; i < 600; i++) begin : g
        logic out;
        leaf u (.a(a), .y(out));
    end
    " + fault + @"
endmodule", "top");

            Assert.Greater(e.Diagnostics.Count(d => d.Code == ElaborationCodes.Unconnected), DiagnosticBag.MaxItems / 2, "the warnings that fill the cap");
            Assert.IsTrue(Codes(e).Contains(code), "the error after them");
            Assert.IsTrue(e.HasErrors);
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// A replication counted past what a long holds once multiplied by its item is too large, said so -- never let through
        /// a width the multiplication wrapped to nothing, which threw out of Elaborate (the Part 16d sweep).
        /// </summary>
        [TestCase("module m (output logic [3:0] y); assign y = {64'd4611686018427387904{4'b0}}; endmodule")]
        [TestCase("module m (output logic [3:0] y); assign y = {64'h4000000000000000{2'b11}}; endmodule")]
        [TestCase("module m (output logic [3:0] y); assign y = {{64'd2305843009213693952{4'b1010}}, 4'b0}; endmodule")]
        public void AReplicationCountedPastALong_IsTooLarge_NeverThrown(string text)
        {
            Elaboration e = null;
            Assert.DoesNotThrow(() => e = Elaborate(text));
            CollectionAssert.Contains(Codes(e), ElaborationCodes.TooLarge, string.Join("\n", e.Diagnostics));
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// A bound at either end of a long, in a declaration or a select, is too large or outside, said so: worked out as
        /// Math.Abs(msb - lsb) before anything held the bounds, the difference wrapped to long.MinValue and Math.Abs threw out
        /// of Elaborate (the Part 16d sweep).
        /// </summary>
        [TestCase("module m (output logic y); logic [64'sd9223372036854775807:-1] x; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (output logic y); logic [64'sh8000000000000000:0] v; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (output logic y); logic [64'sh8000000000000000:0][1:0] p; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (output logic y); logic [7:0] mem [64'sd9223372036854775807:-1]; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (input logic [7:0] a, output logic y); assign y = a[64'sd9223372036854775807:-1]; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic [7:0] a, output logic y); assign y = a[64'sh8000000000000000:0]; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic a, output logic [7:0] y); assign y[64'sd9223372036854775807:-1] = a; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic a, output logic [7:0] y); assign y[64'sd9223372036854775807 -: 2] = a; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic [3:0][3:0] a, output logic y); assign y = a[0 +: 64'd2305843009213693952]; endmodule", ElaborationCodes.TooLarge)]
        [TestCase("module m (input logic a, output logic [3:0][3:0] y); assign y[0 +: 64'd2305843009213693952] = a; endmodule", ElaborationCodes.TooLarge)]
        public void ABoundAtTheEndOfALong_IsSaid_NeverThrown(string text, string code)
        {
            Elaboration e = null;
            Assert.DoesNotThrow(() => e = Elaborate(text));
            CollectionAssert.Contains(Codes(e), code, string.Join("\n", e.Diagnostics));
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// What cannot be built is said, never only dropped: an indexed part-select of no width, read or written, a $clog2 of
        /// no value or of x, and a cast to no width or past the widest each set the builder's Failed with nothing in the
        /// diagnostics, so the assign or the whole process went and the design ran without a word (the Part 16d sweep).
        /// </summary>
        [TestCase("module m #(parameter W = 0) (input logic [7:0] d, output logic [3:0] y); assign y = d[0 +: W]; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m #(parameter W = -2) (input logic [7:0] d, output logic [3:0] y); assign y = d[7 -: W]; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m #(parameter W = 0) (input logic a, output logic [7:0] y); assign y[0 +: W] = a; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m #(parameter W = 0) (input logic a); logic [7:0] y; initial y[0 +: W] = a; endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m; int x; initial x = $clog2(); endmodule", ElaborationCodes.BadCall)]
        [TestCase("module m; int x; initial x = $clog2(4, 2); endmodule", ElaborationCodes.BadCall)]
        [TestCase("module m; int x; initial x = $clog2(4'b1x01); endmodule", ElaborationCodes.NotConstant)]
        [TestCase("module m (input logic [7:0] v, output logic [7:0] y); assign y = 0'(v); endmodule", ElaborationCodes.BadSelect)]
        [TestCase("module m (input logic [7:0] v, output logic [7:0] y); assign y = 2000000'(v); endmodule", ElaborationCodes.TooLarge)]
        public void WhatCannotBeBuilt_IsSaid_NeverOnlyDropped(string text, string code)
        {
            Elaboration e = Elaborate(text);
            CollectionAssert.Contains(Codes(e), code, string.Join("\n", e.Diagnostics));
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// A port declared inside a generate block -- a misplaced line, a missing end -- is said where it is: the block's items
        /// were declared with no module, and the port's check of the module's port list threw a NullReferenceException out of
        /// Elaborate (the Part 16d sweep).
        /// </summary>
        [TestCase("module m (input logic a, output logic y); if (1) begin : g input b; end assign y = a; endmodule")]
        [TestCase("module m (input logic a, output logic y); for (genvar i = 0; i < 2; i++) begin : g output logic [1:0] z; end assign y = a; endmodule")]
        [TestCase("module m (a, y); input a; output y; if (1) begin : g input a; end assign y = a; endmodule")]
        public void APortDeclaredInAGenerateBlock_IsMisplaced_NeverThrown(string text)
        {
            Elaboration e = null;
            Assert.DoesNotThrow(() => e = Elaborate(text));
            Diagnostic said = e.Diagnostics.Single(d => d.Code == ElaborationCodes.Misplaced);
            StringAssert.Contains("generate block", said.Message);
            Assert.IsNull(e.Design);
        }

        /// <summary>
        /// A constant index at the end of a long is outside, read as x and refused as a target: its element's place, the index
        /// times its element's width, wrapped back to 0, and the select read or wrote element 0 (the Part 16d sweep).
        /// </summary>
        [Test]
        public void AnIndexAtTheEndOfALong_IsOutside_NotElementZero()
        {
            Design read = Clean("module m (input logic [1:0][1:0] p, output logic [1:0] y); assign y = p[64'sh8000000000000000]; endmodule");
            Assert.AreEqual("xx", Bits(Settle(read, ("p", B("0110"))), "y"));

            Elaboration written = Elaborate("module m (input logic [1:0] a, output logic [1:0][1:0] y); assign y[64'sh8000000000000000] = a; endmodule");
            CollectionAssert.Contains(Codes(written), ElaborationCodes.BadSelect, string.Join("\n", written.Diagnostics));
            Assert.IsNull(written.Design);
        }

        /// <summary>
        /// What the parts limit leaves room for: a loop inside a loop of 65,536 rounds between them, an assign each, is built --
        /// the pair of the refusals above, so they cannot pass by refusing everything.
        /// </summary>
        [Test]
        public void LoopsInsideLoops_OfAFullLoopsRounds_AreBuilt()
        {
            Elaboration e = Elaborate("module m (input logic [255:0] a, b, output logic [65535:0] y); for (genvar i = 0; i < 256; i++) begin : r " +
                "for (genvar j = 0; j < 256; j++) begin : c assign y[i*256+j] = a[i] & b[j]; end end endmodule");
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));
            Assert.AreEqual(65536, e.Design.Drivers.Count);
        }

        [Test]
        public void AnImplicitNet_IsAOneBitWire_WithAWarning()
        {
            Elaboration e = Elaborate(@"
module inv (input logic a, output logic y);
    assign y = ~a;
endmodule

module top (input logic a, output logic y);
    inv first (.a(a), .y(middle));
    inv second (.a(middle), .y(y));
endmodule");

            CollectionAssert.AreEqual(new[] { ElaborationCodes.ImplicitNet }, Codes(e));
            Assert.AreEqual(7, e.Diagnostics[0].Location.Line);
            Assert.AreEqual(SignalKind.Net, e.Design[e.Design.Find("top.middle")].Kind);
            Assert.AreEqual("1", Bits(Settle(e.Design, ("a", B("1"))), "y"));
        }

        [Test]
        public void APortOfTheWrongWidth_IsAWarning_AndTheValueIsSizedAsAnAssignmentWouldBe()
        {
            Elaboration e = Elaborate(@"
module wide (input logic [3:0] a, output logic [3:0] y);
    assign y = a;
endmodule

module top (input logic [1:0] a, output logic [7:0] y);
    wide w (.a(a), .y(y));
endmodule");

            CollectionAssert.AreEqual(new[] { ElaborationCodes.WidthMismatch, ElaborationCodes.WidthMismatch }, Codes(e));
            Assert.AreEqual("00000011", Bits(Settle(e.Design, ("a", B("11"))), "y"));
        }

        [Test]
        public void SeveralPossibleTops_AreNamed_AndTheOneThatInstantiatesSomethingIsChosen()
        {
            Elaboration ambiguous = Elaborate("module a; endmodule\nmodule b; endmodule");
            CollectionAssert.AreEqual(new[] { ElaborationCodes.NoTop }, Codes(ambiguous));
            StringAssert.Contains("a, b", ambiguous.Diagnostics[0].Message);

            Elaboration named = Elaborate("module a; endmodule\nmodule b; endmodule", top: "b");
            Assert.IsFalse(named.HasErrors);
            Assert.AreEqual("b", named.Top);

            // A cell library leaves its unused cells uninstantiated; the design using the others is the top.
            Elaboration e = ElaborateCorpus("structural/mux2.sv");
            Assert.AreEqual("mux2", e.Top);
        }

        [Test]
        public void AReadThatFailed_IsNotElaborated()
        {
            SyntaxTree tree = SyntaxReader.ReadText("module m (input a; endmodule", "test.sv");
            Assert.IsTrue(tree.HasErrors);

            Elaboration e = Elaborator.Elaborate(tree);
            Assert.IsNull(e.Design);
            CollectionAssert.AreEqual(tree.Diagnostics, e.Diagnostics, "the read's own diagnostics, and nothing piled on them");
        }

        // -----------------------------------------------------------------
        // Robustness: nothing it is given makes it throw
        // -----------------------------------------------------------------

        [Test]
        public void EveryCorpusFile_Elaborates_OrSaysWhyNot_WithoutThrowing()
        {
            MemorySourceFiles corpus = Corpus();
            string root = RepoRoot.Combine("Tools/ci/Oracle/corpus");

            foreach (string path in Directory.GetFiles(root, "*.sv", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                SyntaxTree tree = SyntaxReader.Read(relative, corpus);
                Elaboration e = null;
                Assert.DoesNotThrow(() => e = Elaborator.Elaborate(tree), relative);
                Assert.IsTrue(e.Design != null || e.HasErrors, relative + ": no design and no reason");
            }
        }

        [Test]
        public void MangledDesigns_NeverMakeItThrow()
        {
            string[] sources = Directory.GetFiles(RepoRoot.Combine("Tools/ci/Oracle/corpus/structural"), "*.sv")
                .Select(File.ReadAllText).ToArray();
            var random = new Random(4252);
            MemorySourceFiles corpus = Corpus();

            for (int round = 0; round < 400; round++)
            {
                string text = sources[random.Next(sources.Length)];
                var chars = text.ToCharArray().ToList();

                for (int edit = 0; edit < 1 + random.Next(4); edit++)
                {
                    int at = random.Next(chars.Count);
                    switch (random.Next(4))
                    {
                        case 0: chars.RemoveRange(at, Math.Min(chars.Count - at, 1 + random.Next(12))); break;
                        case 1: chars.Insert(at, "0123456789[]:{}+-*<>=!~&|^?#()'.,;N"[random.Next(35)]); break;
                        case 2: chars[at] = "ab01xz[]{}():;,#"[random.Next(16)]; break;
                        default: chars.InsertRange(at, " 64'hFFFF_FFFF_FFFF_FFFF ".ToCharArray()); break;
                    }
                }

                string mangled = new string(chars.ToArray());
                SyntaxTree tree = SyntaxReader.ReadText(mangled, "structural/mangled.sv", corpus);
                Assert.DoesNotThrow(() => Elaborator.Elaborate(tree), mangled);
            }
        }
    }
}
