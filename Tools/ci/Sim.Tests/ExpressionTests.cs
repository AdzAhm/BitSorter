using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// Part L3's expressions. <c>corpus/behavioural/expressions.sv</c> holds what Icarus can run as well
    /// against it -- <c>==?</c>, the bit counters, casts, an enum's methods, <c>$random</c>; these hold what
    /// Icarus 12 cannot read (<c>inside</c>, <c>case inside</c>, a method of a method, <c>signed'</c>), by the
    /// standard, and what is refused.
    /// </summary>
    public class ExpressionTests
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

        [Test]
        public void Inside_MatchesAValueOrARange_AndIsXOnlyWhenNoMemberMatchesButOneMight()
        {
            CollectionAssert.AreEqual(new[] { "1 0 1 1", "x 1 0", "1 0" }, Printed(@"
module t;
    logic [3:0] v = 4'd5, w = 4'b01x1, z = 4'b1x00;
    initial begin
        $display(""%b %b %b %b"", v inside {1, 5, 7}, v inside {[6:9]}, v inside {[0:3], [4:5]}, v inside {4'b01x1});
        $display(""%b %b %b"", w inside {5, 7}, w inside {1, 4'b01xx}, z inside {1, 2});
        $display(""%b %b"", v inside {[8:15], 5} && 1'b1, !(v inside {5}));
    end
endmodule"));
        }

        [Test]
        public void ACaseInside_TakesTheFirstLabelThatMatches_RangesAndWildcardsIncluded()
        {
            CollectionAssert.AreEqual(new[] { "2 low", "5 five or high", "8 five or high", "12 twelve-ish", "14 twelve-ish", "15 other" }, Printed(@"
module t;
    logic [3:0] v;
    task automatic sort(input logic [3:0] value);
        v = value;
        case (v) inside
            [0:3]: $display(""%0d low"", v);
            5, [7:9]: $display(""%0d five or high"", v);
            4'b11x0, 4'b1101: $display(""%0d twelve-ish"", v);
            default: $display(""%0d other"", v);
        endcase
    endtask
    initial begin
        sort(2); sort(5); sort(8); sort(12); sort(14); sort(15);
    end
endmodule"));
        }

        [Test]
        public void AMethodOfAMethod_AndNextOfANonMember_GivesTheTypesDefault()
        {
            CollectionAssert.AreEqual(new[] { "[DONE] [IDLE] [RUN]", "0 [RED]" }, Printed(@"
module t;
    typedef enum logic [1:0] {IDLE, RUN, DONE} state_t;
    typedef enum {RED, GREEN, BLUE} colour_t;
    state_t s = RUN;
    colour_t c;
    initial begin
        $display(""[%s] [%s] [%s]"", s.next().name(), s.next(2).name(), s.prev().next().name());
        c = colour_t'(7);
        $display(""%0d [%s]"", c.next(), c.next().name());
    end
endmodule"));
        }

        [Test]
        public void SignedAndUnsignedCasts_ReadTheSameBitsAnotherWay()
        {
            CollectionAssert.AreEqual(new[] { "-4 15 12" }, Printed(@"
module t;
    initial $display(""%0d %0d %0d"", signed'(4'b1100), unsigned'(-4'sd1), unsigned'(4'b1100));
endmodule"));
        }

        [Test]
        public void Random_IsTheStandardsGenerator_NumberForNumberAsIcarus()
        {
            // Icarus 12's numbers, 2026-10-19: seed 0 starts the generator at its own constant, -5 wraps.
            CollectionAssert.AreEqual(new[]
            {
                "303379748 -1844104698", "-1064739199 1082744015", "-2071669239 75814084",
                "2147139071 -345344", "-230244124 1917239041",
            }, Printed(@"
module t;
    integer seed, r;
    initial begin
        seed = 0;
        repeat (3) begin r = $random(seed); $display(""%0d %0d"", r, seed); end
        seed = -5;
        repeat (2) begin r = $random(seed); $display(""%0d %0d"", r, seed); end
    end
endmodule"));
        }

        [Test]
        public void URandomRange_StaysInItsRange_AndUnseededCallsShareNoSeedWithRandom()
        {
            string[] lines = Printed(@"
module t;
    int seed = 5, a, b;
    initial begin
        for (int i = 0; i < 200; i++) begin
            a = $urandom_range(20, 10);
            b = $urandom_range(3);
            if (a < 10 || a > 20 || b < 0 || b > 3) $display(""out of range %0d %0d"", a, b);
        end
        $display(""%0d"", $random);
        a = $urandom(seed);
        $display(""moved %0d"", seed != 5);
    end
endmodule");

            CollectionAssert.AreEqual(new[] { "303379748", "moved 1" }, lines,
                "the first unseeded $random is still the standard's first, whatever $urandom_range drew before it");
        }

        [TestCase("module t; logic [31:0] y; assign y = $random; endmodule", "process statement only")]
        [TestCase("module t; logic [3:0] v; initial $display(\"%0d\", v.name()); endmodule", "on an enum only")]
        [TestCase("module t; typedef enum {A, B} e_t; e_t s; initial $display(\"%0d\", s.size()); endmodule", "not a method of an enum")]
        [TestCase("module t; typedef enum {A, B} e_t; e_t s; initial $display(\"%0d\", s.next(1, 2)); endmodule", "at most one argument")]
        [TestCase("module t; int y; initial y = $urandom_range(); endmodule", "takes the largest value")]
        public void WhatCannotRun_IsRefused_SayingWhy(string text, string said)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, "test.sv");
            Elaboration e = Elaborator.Elaborate(tree, null);
            Assert.IsTrue(e.HasErrors || tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "it should have been refused");
            StringAssert.Contains(said, string.Join("\n", e.Diagnostics.Concat(tree.Diagnostics).Select(d => d.Message)));
        }
    }
}
