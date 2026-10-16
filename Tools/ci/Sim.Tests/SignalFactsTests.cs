using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// What a waveform needs to list and name any signal, as the elaborator records it (Part 9b-2a):
    /// the scopes of the hierarchy, and each signal's enum, declared range, port direction and scope.
    /// None of it changes what runs -- the run, the history and the VCD are held by the tests that were
    /// already here -- so each test reads the design and, where a value matters, runs it.
    /// </summary>
    public class SignalFactsTests
    {
        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>Elaborates, and fails the test on anything said at all.</summary>
        private static Design Clean(string text, string top = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "facts.sv");
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, top);
            Assert.IsEmpty(e.Diagnostics, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            Assert.IsNotNull(e.Design);
            return e.Design;
        }

        private static SignalInfo Signal(Design design, string name) => design[design.Find(name)];

        private static LogicVec B(string bits) => LogicVec.FromBinary(bits);

        private static string Describe(ScopeInfo s) => $"{s.Path} {s.Kind} {s.Module ?? "-"} {s.Parent}";

        // -----------------------------------------------------------------
        // Port joins
        // -----------------------------------------------------------------

        /// <summary>
        /// A port connected to a net is joined to it by a zero-delay assignment that reads the one whole
        /// signal, inputs into the instance and outputs out of it; an expression does not read as one.
        /// </summary>
        [Test]
        public void APortJoin_ReadsOneWholeSignal_AndAnExpressionDoesNot()
        {
            Design design = Clean(Ripple, "full_adder");

            DriverInfo Into(string target) => design.Drivers.Single(d => design[d.Target].Name == target);
            string Read(DriverInfo driver) =>
                driver is AssignDriver assign && assign.Value.TryRead(out SignalId read) ? design[read].Name : null;

            DriverInfo input = Into("full_adder.h2.a");
            Assert.IsTrue(input.Delay.IsZero, "sanity: a port join takes no time");
            Assert.AreEqual("full_adder.s1", Read(input), "h2's a is joined to the net it is connected to");

            Assert.AreEqual("full_adder.h1.s", Read(Into("full_adder.s1")), "and s1 to the output of h1 that drives it");

            Assert.IsNull(Read(Into("full_adder.h1.s")), "h1's own s is a ^ b, an expression, not a join");
            Assert.IsNull(Read(Into("full_adder.cout")), "nor is c1 | c2");

            Assert.IsFalse(Expr.Apply(UnaryOp.Not, Expr.Read(design.Find("full_adder.a"), 1)).TryRead(out _), "~a reads a, but is not a");
            Assert.IsFalse(Expr.Slice(Expr.Read(design.Find("full_adder.a"), 1), 0, 1).TryRead(out _), "nor is a bit of it");
        }

        // -----------------------------------------------------------------
        // Scopes
        // -----------------------------------------------------------------

        private const string Ripple = @"
module half_adder (input logic a, b, output logic s, c);
    assign s = a ^ b;
    assign c = a & b;
endmodule

module full_adder (input logic a, b, cin, output logic s, cout);
    logic s1, c1, c2;
    half_adder h1 (.a(a), .b(b), .s(s1), .c(c1));
    half_adder h2 (.a(s1), .b(cin), .s(s), .c(c2));
    assign cout = c1 | c2;
endmodule

module ripple #(parameter N = 2) (input logic [N-1:0] a, b, output logic [N-1:0] s, output logic cout);
    logic [N:0] c;
    assign c[0] = 1'b0;
    for (genvar i = 0; i < N; i++) begin : slice
        full_adder fa (.a(a[i]), .b(b[i]), .cin(c[i]), .s(s[i]), .cout(c[i + 1]));
        if (i == 0) begin : first
            wire lsb = 1'b1;
        end
    end
    if (N > 1) begin
        wire wide = 1'b1;
    end
    assign cout = c[N];
endmodule

module ripple_tb;
    logic [1:0] a, b;
    logic [1:0] s;
    logic cout;
    ripple dut (.a(a), .b(b), .s(s), .cout(cout));
    initial begin : drive
        logic [1:0] k;
        k = 2'b01;
        a = k;
        b = 2'b01;
        #10 $finish;
    end
endmodule";

        [Test]
        public void EveryScopeTheElaborationMade_IsListed_TopFirst_AndEachAfterItsParent()
        {
            Design design = Clean(Ripple);

            // Two half adders in each full adder, a full adder in each pass of the loop: the same module
            // instantiated four times is four scopes. The loop's passes are named slice[0] and slice[1],
            // the if inside the first pass by its label, the unnamed if after the loop genblk2 -- the
            // module's second generate construct -- and the testbench's named block is compiled once the
            // testbench's instance is in, so it comes last.
            CollectionAssert.AreEqual(new[]
            {
                "ripple_tb Top ripple_tb -1",
                "ripple_tb.dut Instance ripple 0",
                "ripple_tb.dut.slice[0] Generate - 1",
                "ripple_tb.dut.slice[0].fa Instance full_adder 2",
                "ripple_tb.dut.slice[0].fa.h1 Instance half_adder 3",
                "ripple_tb.dut.slice[0].fa.h2 Instance half_adder 3",
                "ripple_tb.dut.slice[0].first Generate - 2",
                "ripple_tb.dut.slice[1] Generate - 1",
                "ripple_tb.dut.slice[1].fa Instance full_adder 7",
                "ripple_tb.dut.slice[1].fa.h1 Instance half_adder 8",
                "ripple_tb.dut.slice[1].fa.h2 Instance half_adder 8",
                "ripple_tb.dut.genblk2 Generate - 1",
                "ripple_tb.drive Block - 0",
            }, design.Scopes.Select(Describe).ToArray());

            for (int i = 0; i < design.Scopes.Count; i++)
            {
                ScopeInfo scope = design.Scopes[i];
                if (i == 0)
                {
                    Assert.AreEqual(scope.Path, scope.Name, "the top is named by its whole path");
                    continue;
                }

                Assert.Less(scope.Parent, i, $"{scope.Path} comes after its parent");
                Assert.AreEqual(design.Scopes[scope.Parent].Path + "." + scope.Name, scope.Path, "a scope's name is its own part of its path");
            }

            Assert.AreEqual("slice[1]", design.Scopes[7].Name, "a pass of a loop is one level, brackets and all");
            Assert.AreEqual("h2", design.Scopes[10].Name);
        }

        [Test]
        public void EverySignal_KnowsTheScopeItWasDeclaredIn_WhosePathIsExactlyItsPrefix()
        {
            Design design = Clean(Ripple);

            Assert.AreEqual(0, Signal(design, "ripple_tb.a").Scope);
            Assert.AreEqual(1, Signal(design, "ripple_tb.dut.c").Scope);
            Assert.AreEqual(1, Signal(design, "ripple_tb.dut.cout").Scope, "a port is declared in its module's own scope");
            Assert.AreEqual(6, Signal(design, "ripple_tb.dut.slice[0].first.lsb").Scope);
            Assert.AreEqual(10, Signal(design, "ripple_tb.dut.slice[1].fa.h2.s").Scope);
            Assert.AreEqual(11, Signal(design, "ripple_tb.dut.genblk2.wide").Scope);
            Assert.AreEqual(12, Signal(design, "ripple_tb.drive.k").Scope, "what a named block declares is recorded under it");

            // The general rule: the scope a signal names is the longest scope its name begins with, so
            // nothing between the two can claim it.
            foreach (SignalInfo signal in design.Signals)
            {
                Assert.GreaterOrEqual(signal.Scope, 0, $"{signal.Name} has a scope");
                string prefix = design.Scopes[signal.Scope].Path + ".";
                StringAssert.StartsWith(prefix, signal.Name);

                foreach (ScopeInfo other in design.Scopes)
                    if (other.Path.Length > prefix.Length - 1)
                        Assert.IsFalse(signal.Name.StartsWith(other.Path + ".", System.StringComparison.Ordinal),
                            $"{signal.Name} is inside {other.Path}, deeper than the scope it names");
            }
        }

        [Test]
        public void TheEnginesOwnSignals_HaveNoScope_AndAForLoopsVariable_HasItsProcesses()
        {
            Design design = Clean(@"
module t;
    logic [1:0] n;
    initial begin
        for (int i = 0; i < 2; i++)
            n = i;
        repeat (2) #1;
        $display(""%t"", $time);
    end
endmodule");

            Assert.IsTrue(design.Time.HasValue);
            Assert.AreEqual(-1, design[design.Time.Value].Scope, "$time belongs to no scope of the source");

            SignalInfo repeat = design.Signals.Single(s => s.Name.Contains("$repeat"));
            Assert.AreEqual(-1, repeat.Scope, "nor does a repeat's count, which the engine makes for itself");

            SignalInfo i = design.Signals.Single(s => s.Name.StartsWith("t.i#", System.StringComparison.Ordinal));
            Assert.IsTrue(i.Local);
            Assert.AreEqual(0, i.Scope, "a for loop's variable is the source's, declared where the loop is");
            Assert.AreEqual(31, i.Msb);
        }

        [Test]
        public void ACircuitBuiltInCode_HasNoScopes_AndItsSignalsOnlyTheirWidths()
        {
            var b = new NetlistBuilder("top");
            SignalId a = b.Variable("a", 4), y = b.Net("y", 4);
            MemoryInfo m = b.Memory("m", 8, false, 0, 3);
            b.Assign(y, b.Read(a), Delay.Zero);
            Design design = b.Build();

            Assert.IsEmpty(design.Scopes);
            foreach (SignalId s in new[] { a, y, m.Word(2) })
            {
                SignalInfo info = design[s];
                Assert.AreEqual(info.Width - 1, info.Msb, info.Name);
                Assert.AreEqual(0, info.Lsb, info.Name);
                Assert.IsNull(info.Enum, info.Name);
                Assert.IsNull(info.Port, info.Name);
                Assert.AreEqual(-1, info.Scope, info.Name);
            }
        }

        // -----------------------------------------------------------------
        // Enums
        // -----------------------------------------------------------------

        private const string Machine = @"
typedef enum logic [1:0] {IDLE, RUN, DONE = 2'b11} state_t;

module machine (input logic go, output state_t shown);
    state_t state, odd;
    state_t trail [0:1];
    enum {A, B, C} letter;
    enum logic [2:0] {LOW = 3'd1, HIGH = 3'd6} level;
    enum {X, Y} p, q;
    typedef enum bit {OFF, ON} switch_t;
    switch_t power;
    logic [1:0] plain;

    assign state = RUN;
    assign odd = state_t'(2'b10);
    assign shown = state;
    assign letter = C;
    assign level = HIGH;
    assign power = ON;
    assign plain = 2'b01;
endmodule";

        [Test]
        public void AnEnumThroughATypedef_IsCarriedByEverySignalOfIt_PortsAndMemoriesIncluded()
        {
            Design design = Clean(Machine);
            EnumInfo state = Signal(design, "machine.state").Enum;

            Assert.IsNotNull(state);
            Assert.AreEqual("state_t", state.Name);
            Assert.AreEqual(2, state.Width);
            CollectionAssert.AreEqual(new[] { "IDLE", "RUN", "DONE" }, state.Members.Select(m => m.Name).ToArray(), "in the order declared");
            CollectionAssert.AreEqual(new[] { "00", "01", "11" }, state.Members.Select(m => m.Value.Format(Radix.Binary)).ToArray(),
                "each the one before plus one, unless given a value");

            Assert.AreSame(state, Signal(design, "machine.odd").Enum, "one typedef, one enum");
            Assert.AreSame(state, Signal(design, "machine.shown").Enum, "a port of the type carries it too");
            Assert.AreSame(state, Signal(design, "machine.trail[1]").Enum, "and every word of a memory of it");
        }

        [Test]
        public void AnEnumDeclaredInPlace_HasNoName_AndIsAnIntUnlessItNamesABase()
        {
            Design design = Clean(Machine);

            SignalInfo letter = Signal(design, "machine.letter");
            Assert.IsNull(letter.Enum.Name);
            Assert.AreEqual(32, letter.Enum.Width);
            Assert.AreEqual(32, letter.Width);
            CollectionAssert.AreEqual(new[] { 0L, 1L, 2L }, letter.Enum.Members.Select(m => m.Value.TryGetInt64(out long v) ? v : -1).ToArray());
            Assert.IsTrue(letter.Enum.Members.All(m => m.Value.Width == 32 && m.Value.IsSigned), "an int's members are signed 32-bit values");

            Assert.AreSame(Signal(design, "machine.p").Enum, Signal(design, "machine.q").Enum, "one declaration of two signals, one enum");
            Assert.AreNotSame(letter.Enum, Signal(design, "machine.p").Enum, "two enums written alike are still two");

            EnumInfo power = Signal(design, "machine.power").Enum;
            Assert.AreEqual("switch_t", power.Name, "a typedef inside the module names its enum as one outside does");
            Assert.AreEqual(1, power.Width);
        }

        [Test]
        public void EnumMembersGivenValues_KeepThem_AndAValueNoMemberHas_HasNoName()
        {
            Design design = Clean(Machine);
            EnumInfo level = Signal(design, "machine.level").Enum;

            CollectionAssert.AreEqual(new[] { "001", "110" }, level.Members.Select(m => m.Value.Format(Radix.Binary)).ToArray());
            Assert.IsTrue(level.TryName(B("110"), out string high));
            Assert.AreEqual("HIGH", high);
            Assert.IsFalse(level.TryName(B("010"), out _), "a value no member has");
            Assert.IsFalse(level.TryName(B("1x0"), out _), "an unknown bit is never a member");

            // As the engine runs it: the value a signal settles on names its member, or none.
            var sim = new Simulator(design);
            sim.RunTo(10);
            Assert.IsTrue(Signal(design, "machine.state").Enum.TryName(sim.Value(design.Find("machine.state")), out string run));
            Assert.AreEqual("RUN", run);
            Assert.IsTrue(Signal(design, "machine.letter").Enum.TryName(sim.Value(design.Find("machine.letter")), out string c));
            Assert.AreEqual("C", c);
            Assert.IsFalse(Signal(design, "machine.odd").Enum.TryName(sim.Value(design.Find("machine.odd")), out _),
                "2'b10 is a state this machine never names");
        }

        [Test]
        public void ASignalOfNoEnum_HasNone()
        {
            Design design = Clean(Machine);

            Assert.IsNull(Signal(design, "machine.plain").Enum);
            Assert.IsNull(Signal(design, "machine.go").Enum);
        }

        [Test]
        public void AnEnumMetTwice_ThroughANonAnsiPortGivenAgain_IsOneEnum_DeclaredOnce()
        {
            // output st; then its type: the declaration is read for the port and again for the body, and
            // its names must not be declared twice.
            Design design = Clean(@"
module old (st);
    output st;
    enum logic [1:0] {P, Q} st;
    assign st = Q;
endmodule");

            SignalInfo st = Signal(design, "old.st");
            Assert.AreEqual(PortDirection.Output, st.Port);
            Assert.IsNotNull(st.Enum);
            CollectionAssert.AreEqual(new[] { "P", "Q" }, st.Enum.Members.Select(m => m.Name).ToArray());
        }

        [Test]
        public void EachInstance_HasItsOwnEnum_ForItsParametersMayMakeItDiffer()
        {
            Design design = Clean(@"
module counter #(parameter START = 0) (output logic [1:0] y);
    typedef enum logic [1:0] {FIRST = START, SECOND} step_t;
    step_t s;
    assign s = SECOND;
    assign y = s;
endmodule

module two (output logic [1:0] y0, y1);
    counter #(.START(0)) c0 (.y(y0));
    counter #(.START(2)) c1 (.y(y1));
endmodule");

            EnumInfo first = Signal(design, "two.c0.s").Enum, second = Signal(design, "two.c1.s").Enum;
            Assert.AreNotSame(first, second);
            Assert.AreEqual("01", first.Members[1].Value.Format(Radix.Binary));
            Assert.AreEqual("11", second.Members[1].Value.Format(Radix.Binary));
        }

        // -----------------------------------------------------------------
        // Ranges
        // -----------------------------------------------------------------

        [Test]
        public void TheDeclaredRange_IsKept_EitherWayRound()
        {
            Design design = Clean(@"
module ranges (input logic [7:0] down, input logic [0:7] up, input logic one, output int count, output logic [3:-2] odd);
    logic [15:8] mid;
    integer n;
    byte b8;
    logic [3:0] mem [0:1];
    typedef logic [0:2] triple_t;
    triple_t t;
    wire bare;
    assign count = 0;
    assign odd = 6'b0;
endmodule");

            void Range(string name, int msb, int lsb, int width)
            {
                SignalInfo s = Signal(design, "ranges." + name);
                Assert.AreEqual((msb, lsb, width), (s.Msb, s.Lsb, s.Width), name);
            }

            Range("down", 7, 0, 8);
            Range("up", 0, 7, 8);
            Range("one", 0, 0, 1);
            Range("bare", 0, 0, 1);
            Range("count", 31, 0, 32);
            Range("odd", 3, -2, 6);
            Range("mid", 15, 8, 8);
            Range("n", 31, 0, 32);
            Range("b8", 7, 0, 8);
            Range("mem[0]", 3, 0, 4);
            Range("t", 0, 2, 3);
        }

        // -----------------------------------------------------------------
        // Ports
        // -----------------------------------------------------------------

        [Test]
        public void APortSaysWhichWayItGoes_AtTheTopAndInside_AndNothingElseIsOne()
        {
            Elaboration e = Elaborator.Elaborate(SyntaxReader.ReadText(@"
module inner (input logic a, output logic y);
    logic t;
    assign t = ~a;
    assign y = t;
endmodule

module classic (a, y);
    input a;
    output y;
    reg y;
    always @(a) y = a;
endmodule

module outer (input logic x, output logic z, w2);
    logic w;
    inner u (.a(x), .y(w));
    classic k (.a(w), .y(w2));
    assign z = w;
endmodule", "ports.sv"));
            Assert.IsEmpty(e.Diagnostics, string.Join("\n", e.Diagnostics));
            Design design = e.Design;

            Assert.AreEqual(PortDirection.Input, Signal(design, "outer.x").Port);
            Assert.AreEqual(PortDirection.Output, Signal(design, "outer.z").Port);
            Assert.IsNull(Signal(design, "outer.w").Port, "what an instance's port is connected to is not a port");

            Assert.AreEqual(PortDirection.Input, Signal(design, "outer.u.a").Port);
            Assert.AreEqual(PortDirection.Output, Signal(design, "outer.u.y").Port);
            Assert.IsNull(Signal(design, "outer.u.t").Port);

            Assert.AreEqual(PortDirection.Input, Signal(design, "outer.k.a").Port, "a port declared the old way");
            Assert.AreEqual(PortDirection.Output, Signal(design, "outer.k.y").Port, "given its type again, still the port");

            // The top's ports as the elaboration lists them say the same.
            foreach (ElaboratedPort port in e.Ports)
                Assert.AreEqual(port.Direction, design[port.Signal].Port, port.Name);
        }
    }
}
