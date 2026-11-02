using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// A testbench that chooses what it runs by a parameter of an enum type (Part 15h-6c): the processors' PROGRAM -- its
    /// images named by a ?: between file names of one length, which Icarus 12 reads only when they are, a shorter one
    /// padded with a NUL it does not strip.
    /// </summary>
    public class BenchParameterTests
    {
        private const string Probe =
            "typedef enum {SUM, FIBONACCI, TEST} rv_program_t;\n" +
            "module mem #(parameter IMAGE = \"a.hex\") ();\n" +
            "    logic [31:0] m [0:0];\n" +
            "    initial begin\n" +
            "        $readmemh(IMAGE, m);\n" +
            "        $display(\"%0s -> %h\", IMAGE, m[0]);\n" +
            "    end\n" +
            "endmodule\n" +
            "module tb #(parameter rv_program_t PROGRAM = SUM) ();\n" +
            "    localparam IMAGE = PROGRAM == SUM ? \"rv_sum.hex\" : PROGRAM == FIBONACCI ? \"rv_fib.hex\" : \"rv_isa.hex\";\n" +
            "    mem #(.IMAGE(IMAGE)) m ();\n" +
            "    initial $display(\"program %0d\", PROGRAM);\n" +
            "endmodule\n" +
            "module wrap;\n" +
            "    tb #(.PROGRAM(TEST)) t ();\n" +
            "endmodule\n";

        private static MemorySimFiles Images() => new MemorySimFiles()
            .Add("rv_sum.hex", "11111111\n").Add("rv_fib.hex", "22222222\n").Add("rv_isa.hex", "33333333\n");

        private static string[] Printed(string top, Func<SyntaxTree, Elaboration> elaborate = null)
        {
            SyntaxTree tree = SyntaxReader.ReadText(Probe, "probe.sv");
            Assert.IsFalse(tree.HasErrors, string.Join("; ", tree.Diagnostics));
            Elaboration e = elaborate != null ? elaborate(tree) : Elaborator.Elaborate(tree, top);
            Assert.IsFalse(e.HasErrors, string.Join("\n", e.Diagnostics));
            var sim = new Simulator(e.Design, null, new SimOptions { Files = Images() });
            sim.RunTo(10);
            return sim.History.Messages.Where(m => !m.Text.EndsWith(": the run ends here.", StringComparison.Ordinal)
               ).Select(m => m.Text).ToArray();
        }

        [Test]
        public void ATestbench_TakesItsEnumParametersDefault_AndAWrapperGivesItAnother()
        {
            CollectionAssert.AreEquivalent(new[] { "rv_sum.hex -> 11111111", "program 0" }, Printed("tb"));
            CollectionAssert.AreEquivalent(new[] { "rv_isa.hex -> 33333333", "program 2" }, Printed("wrap"));
        }

        private static NameExpression Name(string name)
        {
            var expression = new NameExpression();
            expression.Path.Add(name);
            return expression;
        }

        /// <summary>The top's own parameter given, an enum member named as the top names it, as Icarus's -P sets one.</summary>
        [Test]
        public void TheTopsParameter_IsGivenByName_AnEnumMemberAsTheTopNamesIt()
        {
            var fibonacci = new Dictionary<string, Expression> { ["PROGRAM"] = Name("FIBONACCI") };
            CollectionAssert.AreEquivalent(new[] { "rv_fib.hex -> 22222222", "program 1" },
                Printed(null, tree => Elaborator.Elaborate(tree, "tb", fibonacci)));

            var two = new Dictionary<string, Expression> { ["PROGRAM"] = new NumberLiteral { Text = "2", Value = Literal.Parse("2").Value } };
            CollectionAssert.AreEquivalent(new[] { "rv_isa.hex -> 33333333", "program 2" },
                Printed(null, tree => Elaborator.Elaborate(tree, "tb", two)), "a number as well");
        }

        [Test]
        public void AParameterTheTopHasNot_IsAnError()
        {
            SyntaxTree tree = SyntaxReader.ReadText(Probe, "probe.sv");
            var wrong = new Dictionary<string, Expression> { ["PROGRAMME"] = Name("SUM") };
            Elaboration e = Elaborator.Elaborate(tree, "tb", wrong);
            Assert.IsTrue(e.HasErrors);
            StringAssert.Contains("tb has no parameter called PROGRAMME", string.Join("\n", e.Diagnostics));
        }
    }
}
