using System;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// An imported project in the workbench (Part 13b): run as its own testbench runs it, to where that stops, with the
    /// project's files there for <c>$readmemh</c>; its design's code shown in the circuit's window when the design is
    /// written; and its inputs not the stimulus's to set while the testbench drives them.
    /// </summary>
    public class ImportedProjectTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private const string Gate =
            "module gate (input logic a, input logic b, output logic y);\n" +
            "    always_comb y = a ^ b;\n" +
            "endmodule\n";

        private const string Bench =
            "module gate_tb;\n" +
            "    logic a, b, y;\n" +
            "    logic [7:0] words [0:1];\n" +
            "    gate dut (.a(a), .b(b), .y(y));\n" +
            "    initial begin\n" +
            "        $readmemh(\"pair.hex\", words);\n" +
            "        a = words[0][0]; b = words[1][0];\n" +
            "        #10 b = ~b;\n" +
            "        #10 $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private static WorkbenchSession Imported()
        {
            ProjectImport import = ProjectImporter.Import(new[]
            {
                new SourceFile("gate.sv", Gate), new SourceFile("gate_tb.sv", Bench), new SourceFile("pair.hex", "01\n00\n"),
            });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            var session = new WorkbenchSession(import.Project, Ticking(0.01));
            session.Finish();
            return session;
        }

        private static string ValueOf(WorkbenchSession session, string port, long time)
        {
            RunSession run = session.Run;
            Design design = run.Model.Session.Design;
            return run.Model.Session.History.ValueAt(design.Find(run.Model.Layout.DesignPath + "." + port), time).Format(Radix.Binary);
        }

        [Test]
        public void TheTestbenchRunsTheDesign_ToWhereItStops_WithTheProjectsFiles()
        {
            WorkbenchSession session = Imported();

            Assert.AreEqual("gate_tb.dut", session.Run.Model.Layout.DesignPath, "the design's signals are under the testbench's instance of it");
            Assert.AreEqual(20, session.Run.Model.End);
            Assert.AreEqual("1", ValueOf(session, "a", 5), "the memory image was read");
            Assert.AreEqual("1", ValueOf(session, "y", 5));
            Assert.AreEqual("0", ValueOf(session, "y", 15));
        }

        [Test]
        public void AWrittenDesign_ShowsItsCode_AtTheTop()
        {
            WorkbenchSession session = Imported();

            Assert.AreEqual("gate", session.Module);
            Assert.IsTrue(session.ShowsCode);
            StringAssert.Contains("always_comb y = a ^ b;", session.Code.Text);
        }

        [Test]
        public void WhileTheTestbenchDrivesTheInputs_SettingOneIsRefused_AndNothingIsReplayed()
        {
            WorkbenchSession session = Imported();
            Project before = session.Project;

            EditResult set = session.SetInput("b", 0, 5, LogicVec.FromBinary("1"));
            EditResult flipped = session.InvertInput("a", 3);

            Assert.IsFalse(set.Done);
            Assert.IsFalse(flipped.Done);
            StringAssert.Contains("gate_tb", set.Refusal);
            Assert.AreSame(before, session.Project);
        }

        [Test]
        public void ConvertingTheTestbench_RunsTheSame_AndGivesTheInputsToTheWaves()
        {
            WorkbenchSession session = Imported();
            string[] before = { ValueOf(session, "a", 5), ValueOf(session, "y", 5), ValueOf(session, "y", 15) };

            Assert.IsTrue(session.ConvertBench().Done);
            session.Finish();

            Assert.IsNull(session.Project.Bench);
            Assert.IsNotNull(session.Project.Stimulus.Tracks.SingleOrDefault(t => t.Port == "b"), "b's change at 10 is a step now");
            CollectionAssert.AreEqual(before, new[] { ValueOf(session, "a", 5), ValueOf(session, "y", 5), ValueOf(session, "y", 15) },
                "the design sees what it saw under the testbench");
            Assert.IsTrue(session.SetInput("b", 0, 5, LogicVec.FromBinary("1")).Done, "and its inputs are set on the waves");
        }
    }
}
