using System.Diagnostics;
using System.Linq;
using System.Text;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// What Part 13f's security review tried against an import, end to end: each is read, elaborated and its testbench run,
    /// as an import does, and each must come back -- imported with its run stopped, or refused -- saying why, in moments, and
    /// never take the process with it. The layer each is stopped in has its own test; this holds the whole way through.
    /// </summary>
    public class ImportHostileTests
    {
        private static string Doubling()
        {
            var text = new StringBuilder("`define M0 x\n");
            for (int i = 1; i <= 31; i++)
                text.Append("`define M").Append(i).Append(" `M").Append(i - 1).Append(" `M").Append(i - 1).Append('\n');
            return text.Append("module bomb_tb; logic y = `M31; endmodule\n").ToString();
        }

        private static readonly (string Name, string Text, string Said)[] Hostile =
        {
            ("macros that double", Doubling(), "expand to more than a million tokens"),
            ("a file including itself", "`include \"self.sv\"\nmodule self_tb; endmodule\n", "self.sv"),
            ("nesting a hundred thousand deep", "module deep_tb; logic y = " + new string('(', 100_000) + "1" + new string(')', 100_000) + "; endmodule\n",
                "levels deep"),
            ("a recursion with no way out",
                "module rec_tb;\n    integer y;\n    function automatic integer f(input integer n); return f(n + 1); endfunction\n    initial y = f(0);\nendmodule\n",
                "was called 100 deep"),
            ("a loop that never waits", "module spin_tb;\n    logic x = 0;\n    initial forever x = ~x;\nendmodule\n", "steps without waiting"),
            ("a design bigger than a run can hold", "module big_tb;\n    logic [1048575:0] mem [0:1048575];\nendmodule\n", "million bits"),
            ("a clock that never stops", "module clock_tb;\n    logic clk = 0;\n    always #1 clk = ~clk;\nendmodule\n", null),
            ("a testbench that prints forever", "module chatter_tb;\n    always #1 $display(\"again\");\nendmodule\n", null),
            ("a module instantiating itself", "module loop (input logic a); loop again (.a(a)); endmodule\nmodule loop_tb; logic a; loop l (.a(a)); endmodule\n",
                "instantiates itself"),
        };

        public static string[] Names() => Hostile.Select(h => h.Name).ToArray();

        [TestCaseSource(nameof(Names))]
        public void AHostileProject_ComesBack_InMoments_SayingWhy(string name)
        {
            var hostile = Hostile.Single(h => h.Name == name);
            string file = hostile.Name.Contains("including itself") ? "self.sv" : "hostile.sv";

            var clock = Stopwatch.StartNew();
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile(file, hostile.Text) });
            string said = string.Join("\n", import.Notes);

            // Imported, it is run as the workbench runs it, to where the import found it stops: what the run says goes to the
            // TRANSCRIPT, not the import's notes.
            if (import.Succeeded)
            {
                CompiledProject compiled = BoardCompiler.Compile(import.Project);
                Elaboration elaboration = compiled.Succeeded ? Elaborator.Elaborate(compiled.Tree, compiled.Top) : null;
                if (elaboration != null && !elaboration.HasErrors)
                {
                    var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(import.Project) });
                    sim.RunTo(System.Math.Max(import.Project.Stimulus?.End ?? 0, 1000));
                    said += "\n" + string.Join("\n", sim.History.Messages.Select(m => m.Text).Take(5));
                }
            }

            double seconds = clock.Elapsed.TotalSeconds;
            TestContext.WriteLine($"{name}: {(import.Succeeded ? "imported" : "refused")} in {seconds:0.00} s\n{said}");
            Assert.Less(seconds, 15, "in moments, not after making it all");
            if (hostile.Said != null)
                StringAssert.Contains(hostile.Said, said, "saying why");
        }
    }
}
