using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// The 2-state types -- <c>bit</c>, <c>byte</c>, <c>shortint</c>, <c>int</c>, <c>longint</c>, and an
    /// enum with no base type, which is an <c>int</c> -- hold only 0 and 1: they start at 0, and an x or
    /// a z written to one becomes 0 (IEEE 1800, 6.11.2). Icarus agrees, and so does the course's
    /// ModelSim, where a state machine's <c>typedef enum {IDLE, ...}</c> reads IDLE before its reset.
    /// </summary>
    public class TwoStateTests
    {
        private static Simulator Run(string text, long until)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Assert.IsEmpty(tree.Diagnostics, "the read reported:\n" + string.Join("\n", tree.Diagnostics));
            Elaboration e = Elaborator.Elaborate(tree, null);
            Assert.IsFalse(e.HasErrors, "the elaboration reported:\n" + string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, null);
            sim.RunTo(until);
            return sim;
        }

        private static string Now(Simulator sim, string name) =>
            sim.Value(sim.Design.Find(sim.Design.Name + "." + name)).Format(Radix.Binary);

        private static string[] Changes(Simulator sim, string name) =>
            Probes.Settled(sim.History, sim.Design.Find(sim.Design.Name + "." + name));

        [Test]
        public void EveryTwoStateType_StartsAt0_AndLogicStillStartsAtX()
        {
            Simulator sim = Run(@"
module t;
    typedef enum {IDLE, RUN, DONE} state_t;
    bit b;
    bit [3:0] b4;
    byte y;
    shortint h;
    int i;
    longint g;
    state_t s;
    logic l;
    integer n;
endmodule", 1);

            Assert.AreEqual("0", Now(sim, "b"));
            Assert.AreEqual("0000", Now(sim, "b4"));
            Assert.AreEqual(new string('0', 8), Now(sim, "y"));
            Assert.AreEqual(new string('0', 16), Now(sim, "h"));
            Assert.AreEqual(new string('0', 32), Now(sim, "i"));
            Assert.AreEqual(new string('0', 64), Now(sim, "g"));
            Assert.AreEqual(new string('0', 32), Now(sim, "s"), "an enum with no base type is an int, so it starts at its first name");
            Assert.AreEqual("x", Now(sim, "l"));
            Assert.AreEqual(new string('x', 32), Now(sim, "n"), "integer is 4-state");
        }

        [Test]
        public void AnEnumOnALogicBase_StaysFourState()
        {
            Simulator sim = Run(@"
module t;
    typedef enum logic [1:0] {IDLE, RUN} state_t;
    typedef enum bit [1:0] {OFF, ON} mode_t;
    state_t s;
    mode_t m;
endmodule", 1);

            Assert.AreEqual("xx", Now(sim, "s"));
            Assert.AreEqual("00", Now(sim, "m"));
        }

        [Test]
        public void AnXOrZWritten_ByAProcess_Reads0()
        {
            Simulator sim = Run(@"
module t;
    bit [3:0] b4;
    int i;
    initial begin
        b4 = 4'b1x0z;
        i = 'x;
        #5 b4[2:1] = 2'bz1;
    end
endmodule", 10);

            CollectionAssert.AreEqual(new[] { "0 1000", "5 1010" }, Changes(sim, "b4"));
            Assert.AreEqual(new string('0', 32), Now(sim, "i"));
        }

        [Test]
        public void AnXFromAContinuousAssignmentOrANonblockingWrite_Reads0()
        {
            Simulator sim = Run(@"
module t;
    logic a, clk;
    bit y, q;
    assign y = a;
    always_ff @(posedge clk) q <= a;
    initial begin
        clk = 0;
        #5 clk = 1;
        #5 a = 1;
    end
endmodule", 20);

            CollectionAssert.AreEqual(new[] { "0 0", "10 1" }, Changes(sim, "y"));
            CollectionAssert.AreEqual(new[] { "0 0" }, Changes(sim, "q"), "the x it sampled at 5 is held as 0");
        }

        [Test]
        public void AMemoryOfTwoStateWords_StartsAt0()
        {
            Simulator sim = Run(@"
module t;
    bit [3:0] m [0:1];
    logic [3:0] r;
    initial #1 r = m[1];
endmodule", 2);

            Assert.AreEqual("0000", Now(sim, "r"));
        }
    }
}
