using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSorter.Ci;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// CONVERT (Part 13c): a written testbench turned into the stimulus by recording what it drove. The design run under the
    /// stimulus is the design run under the testbench, change for change; a steady toggle becomes a clock; a loop and
    /// $random convert as anything else does, from the run; what the testbench printed is said; and it is one step.
    /// </summary>
    public class BenchConversionTests
    {
        private static Project Imported(params (string Path, string Text)[] files)
        {
            ProjectImport import = ProjectImporter.Import(files.Select(f => new SourceFile(f.Path, f.Text)).ToList());
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return import.Project;
        }

        private static Project ImportedFolder(string folder)
        {
            string root = RepoRoot.Combine("Tools/ci/Oracle/corpus/" + folder);
            return Imported(Directory.GetFiles(root).OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(p => (Path.GetFileName(p), File.ReadAllText(p))).ToArray());
        }

        private static Project Converted(Project project)
        {
            Conversion conversion = BenchConverter.Convert(project);
            Assert.IsTrue(conversion.Succeeded, conversion.Refusal);
            return conversion.Project;
        }

        /// <summary>Every settled change of every signal of the design, by its name inside the design, up to <paramref name="end"/>.</summary>
        private static Dictionary<string, string> DesignHistory(Project project, long end)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            Design design = elaboration.Design;
            var sim = new Simulator(design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            sim.RunTo(end);

            string prefix = compiled.DesignPath + ".";
            var history = new Dictionary<string, string>();
            for (int s = 0; s < design.Signals.Count; s++)
            {
                SignalInfo info = design.Signals[s];
                if (info.Local || info.Memory != null || !info.Name.StartsWith(prefix, System.StringComparison.Ordinal))
                    continue;
                history[info.Name.Substring(prefix.Length)] = string.Join(", ", sim.History.SettledChanges(design.SignalAt(s))
                    .Where(c => c.Time <= end)
                    .Select(c => $"{c.Time} {c.Value.Format(Radix.Binary)}"));
            }

            return history;
        }

        private static void AssertTheSameRun(Project asWritten, Project converted)
        {
            long end = asWritten.Stimulus.End;
            Dictionary<string, string> before = DesignHistory(asWritten, end);
            Dictionary<string, string> after = DesignHistory(converted, end);

            Assert.Greater(before.Count, 0);
            CollectionAssert.AreEquivalent(before.Keys, after.Keys, "the same signals in the design");
            var wrong = before.Where(b => after[b.Key] != b.Value).Select(b => $"{b.Key}\n      testbench {b.Value}\n      stimulus  {after[b.Key]}").ToList();
            Assert.IsEmpty(wrong, $"{wrong.Count} of {before.Count} signals differ:\n  " + string.Join("\n  ", wrong.Take(8)));
        }

        [TestCase("import/alu")]
        [TestCase("import/mux")]
        [TestCase("import/counter")]
        [TestCase("processor")]
        public void UnderTheStimulus_TheDesignRunsAsItDidUnderTheTestbench(string folder)
        {
            Project project = ImportedFolder(folder);
            Project converted = Converted(project);

            Assert.IsNull(converted.Bench);
            Assert.IsNull(converted.Find(project.Bench), "the testbench is taken out");
            Assert.AreEqual(project.Stimulus.End, converted.Stimulus.End);
            AssertTheSameRun(project, converted);
        }

        [Test]
        public void ASteadyToggle_IsAClock_AndAPulseIsNot()
        {
            Stimulus counter = Converted(ImportedFolder("import/counter")).Stimulus;

            ClockTrack clock = counter.Clocks.Single();
            Assert.AreEqual("clk", clock.Port);
            Assert.AreEqual(10, clock.Period);
            Assert.IsFalse(clock.StartsHigh);
            CollectionAssert.AreEquivalent(new[] { "rst_n", "en" }, counter.Tracks.Select(t => t.Port), "a reset pulse and an enable are steps");
        }

        [Test]
        public void ALoopAndRandomNumbers_AreConvertedFromTheRun()
        {
            const string design =
                "module acc (input logic clk, input logic [7:0] d, output logic [7:0] sum);\n" +
                "    always_ff @(posedge clk) sum <= sum + d;\n" +
                "    initial sum = 0;\n" +
                "endmodule\n";
            const string bench =
                "module acc_tb;\n" +
                "    logic clk = 1;\n" +
                "    logic [7:0] d, sum;\n" +
                "    integer seed = 7;\n" +
                "    acc dut (.clk(clk), .d(d), .sum(sum));\n" +
                "    always #4 clk = ~clk;\n" +
                "    task automatic give(input logic [7:0] v); d = v; #8; endtask\n" +
                "    initial begin\n" +
                "        for (int i = 0; i < 6; i++) give($random(seed));\n" +
                "        repeat (3) give(8'd1);\n" +
                "        $display(\"sum %0d\", sum);\n" +
                "        $stop;\n" +
                "    end\n" +
                "endmodule\n";
            Project project = Imported(("acc.sv", design), ("acc_tb.sv", bench));
            Conversion conversion = BenchConverter.Convert(project);

            Assert.IsTrue(conversion.Succeeded, conversion.Refusal);
            Assert.IsTrue(conversion.Project.Stimulus.Clocks.Single().StartsHigh);
            Assert.AreEqual(7, conversion.Project.Stimulus.Tracks.Single(t => t.Port == "d").Steps.Count, "six random, then three ones, which change d once");
            AssertTheSameRun(project, conversion.Project);
            CollectionAssert.AreEqual(new[] { "$display, in one place" }, conversion.Lost);
        }

        private const string Flop =
            "module dff (input logic clk, input logic rst, input logic d, output logic q);\n" +
            "    always_ff @(posedge clk or posedge rst) if (rst) q <= 0; else q <= d;\n" +
            "endmodule\n";

        private const string PlainFlop =
            "module ff (input logic clk, input logic d, output logic q);\n" +
            "    always_ff @(posedge clk) q <= d;\n" +
            "endmodule\n";

        [Test]
        public void ADeclarationsValue_IsNoEventAtTimeZero_AndStaysOne()
        {
            const string bench =
                "module dff_tb;\n" +
                "    logic clk = 0, rst = 1, d = 0, q;\n" +
                "    dff dut (.clk(clk), .rst(rst), .d(d), .q(q));\n" +
                "    always #5 clk = ~clk;\n" +
                "    initial begin #12 rst = 0; #10 d = 1; #30 $stop; end\n" +
                "endmodule\n";
            Project project = Imported(("dff.sv", Flop), ("dff_tb.sv", bench));
            Project converted = Converted(project);

            Assert.IsNotNull(converted.Stimulus.Tracks.Single(t => t.Port == "rst").Start, "rst = 1 from its declaration, not a step");
            Assert.IsFalse(converted.Stimulus.Clocks.Single().SetAtZero);
            AssertTheSameRun(project, converted);
        }

        [Test]
        public void AClockSetByAnInitialBlock_IsSetAtTimeZero_AsAnEvent()
        {
            const string bench =
                "module ff_tb;\n" +
                "    logic clk;\n" +
                "    logic d = 1, q;\n" +
                "    ff dut (.clk(clk), .d(d), .q(q));\n" +
                "    initial clk = 1;\n" +
                "    always #5 clk = ~clk;\n" +
                "    initial #52 $stop;\n" +
                "endmodule\n";
            Project project = Imported(("ff.sv", PlainFlop), ("ff_tb.sv", bench));
            Project converted = Converted(project);

            ClockTrack clock = converted.Stimulus.Clocks.Single();
            Assert.IsTrue(clock.SetAtZero, "x to 1 at time 0 is a rising edge here, which a declaration's value would not be");
            Assert.IsTrue(clock.StartsHigh);
            AssertTheSameRun(project, converted);
        }

        [Test]
        public void AnInputChangedInAnswerToAnEdge_IsChangedAfterTheEdge()
        {
            const string bench =
                "module ff_tb;\n" +
                "    logic clk = 0, d = 0, q;\n" +
                "    ff dut (.clk(clk), .d(d), .q(q));\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) d <= ~d;\n" +
                "    initial #52 $stop;\n" +
                "endmodule\n";
            Project project = Imported(("ff.sv", PlainFlop), ("ff_tb.sv", bench));
            Project converted = Converted(project);

            Assert.IsTrue(converted.Stimulus.Tracks.Single(t => t.Port == "d").Steps.All(s => s.Late), "every change of d answers an edge");
            AssertTheSameRun(project, converted);
        }

        [Test]
        public void APulseNoTimeWide_IsKept()
        {
            const string design =
                "module pulses (input logic g, output logic [3:0] n);\n" +
                "    initial n = 0;\n" +
                "    always @(posedge g) n <= n + 1;\n" +
                "endmodule\n";
            const string bench =
                "module pulses_tb;\n" +
                "    logic g = 0;\n" +
                "    logic [3:0] n;\n" +
                "    pulses dut (.g(g), .n(n));\n" +
                "    initial begin #10 g = 1; #0 g = 0; #10 g = 1; #10 $stop; end\n" +
                "endmodule\n";
            Project project = Imported(("pulses.sv", design), ("pulses_tb.sv", bench));

            AssertTheSameRun(project, Converted(project));
        }

        [Test]
        public void AConvertedStimulus_IsSavedWithItsStartsLateStepsAndClocks_AndReadBack()
        {
            const string bench =
                "module dff_tb;\n" +
                "    logic clk, rst = 1, d = 0, q;\n" +
                "    dff dut (.clk(clk), .rst(rst), .d(d), .q(q));\n" +
                "    initial clk = 0;\n" +
                "    always #5 clk = ~clk;\n" +
                "    always @(posedge clk) d <= ~d;\n" +
                "    initial begin #12 rst = 0; #40 $stop; end\n" +
                "endmodule\n";
            Project converted = Converted(Imported(("dff.sv", Flop), ("dff_tb.sv", bench)));
            Assert.IsTrue(converted.Stimulus.Clocks.Single().SetAtZero);
            Assert.IsTrue(converted.Stimulus.Tracks.Single(t => t.Port == "d").Steps.Any(s => s.Late));
            Assert.IsNotNull(converted.Stimulus.Tracks.Single(t => t.Port == "rst").Start);

            var fields = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            string json = System.Text.Json.JsonSerializer.Serialize(ProjectRecord.From(converted), fields);
            var problems = new List<string>();
            Project again = System.Text.Json.JsonSerializer.Deserialize<ProjectRecord>(json, fields).ToProject(problems);

            Assert.IsEmpty(problems);
            CollectionAssert.AreEqual(BoardPrinter.Print(converted).Select(p => p.Text), BoardPrinter.Print(again).Select(p => p.Text),
                "the testbench the stimulus makes is the same after the save");
        }

        [Test]
        public void ATestbenchThatGivesTheDesignParameters_IsRefused_WithWhy()
        {
            const string design = "module addk #(parameter K = 1) (input logic [3:0] a, output logic [3:0] y);\n    assign y = a + K;\nendmodule\n";
            const string bench =
                "module addk_tb;\n" +
                "    logic [3:0] a, y;\n" +
                "    addk #(.K(5)) dut (.a(a), .y(y));\n" +
                "    initial begin a = 1; #10 a = 2; #10 $stop; end\n" +
                "endmodule\n";

            Conversion conversion = BenchConverter.Convert(Imported(("addk.sv", design), ("addk_tb.sv", bench)));

            Assert.IsFalse(conversion.Succeeded);
            StringAssert.Contains("parameters", conversion.Refusal);
        }

        [Test]
        public void WhatTheTestbenchPrinted_IsSaidToBeLost()
        {
            Conversion conversion = BenchConverter.Convert(ImportedFolder("import/alu"));

            CollectionAssert.AreEqual(new[] { "$display, in 3 places" }, conversion.Lost, "where it is written, not how often it ran");
        }

        [Test]
        public void Converting_IsOneStep_AndTheInputsAreTheStimulusesAgain()
        {
            var editor = new BoardEditor(ImportedFolder("import/mux"));
            Project before = editor.Project;

            Assert.IsFalse(editor.SetInput("a", 0, 5, LogicVec.FromBinary("1")).Done, "sanity: the testbench drives the inputs");
            Assert.IsTrue(editor.ConvertBench().Done);
            Assert.IsNull(editor.Project.Bench);
            Assert.IsTrue(editor.SetInput("a", 0, 5, LogicVec.FromBinary("1")).Done, "the stimulus's to set now");

            Assert.IsTrue(editor.Undo() && editor.Undo());
            Assert.AreSame(before, editor.Project, "two steps back is the project as imported");
        }

        [Test]
        public void WithNoTestbench_ThereIsNothingToConvert()
        {
            var editor = new BoardEditor(SampleBoards.Build("half_adder"));
            EditResult converted = editor.ConvertBench();

            Assert.IsFalse(converted.Done);
            StringAssert.Contains("no testbench", converted.Refusal);
        }
    }
}
