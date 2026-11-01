using System.IO;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The start page's three RV32I processors (Part 15h-6): their text the corpus's to the character, which the oracle
    /// holds to Icarus on every signal; each running the demo program to PASS in the cycles its line on the start page
    /// says; the multicycle one without the oracle's own testbench; and each opening as the drawing of its code.
    /// </summary>
    public class ProcessorSampleTests
    {
        private static string Corpus(string file) =>
            File.ReadAllText(Path.Combine(ExerciseFiles.Root, "Tools", "ci", "Oracle", "corpus", "processor", file)).Replace("\r\n", "\n");

        [Test]
        public void EveryFile_IsTheCorpusFile_ToTheCharacter()
        {
            Assert.AreEqual(7, WorkbenchProcessors.Files.Count);
            foreach ((string file, string text) in WorkbenchProcessors.Files)
                Assert.AreEqual(Corpus(file), text.Replace("\r\n", "\n"),
                    $"{file} differs from the corpus's: copy the corpus file into WorkbenchProcessors.cs");
        }

        [TestCase("Single-cycle RISC-V", "scpu_tb", 408)]
        [TestCase("Multicycle RISC-V", "mcpu_demo_tb", 1568)]
        [TestCase("Pipelined RISC-V", "pcpu_tb", 482)]
        public void AProcessor_RunsTheDemo_ToPass_InTheCyclesItsLineSays(string name, string bench, int cycles)
        {
            (string Name, string Says, System.Func<Project> Build) sample = WorkbenchSamples.All.Single(s => s.Name == name);
            StringAssert.Contains(cycles.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), sample.Says,
                "the line under its name says how many cycles it takes");
            Project project = sample.Build();
            Assert.AreEqual(bench, project.Bench);
            if (bench == "mcpu_demo_tb")
                Assert.IsNull(project.Find("mcpu_tb"), "mcpu.sv's other testbench, the oracle's, is left out");
            Assert.AreSame(project, sample.Build(), "imported once, and shared");

            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult result = sim.RunTo(1_000_000);
            string said = string.Join("\n", sim.History.Messages.Select(m => m.Text));
            Assert.AreEqual(RunStatus.Finished, result.Status, said);
            StringAssert.Contains($"done after {cycles} cycles", said);
            StringAssert.Contains("the sum of 1 to 10: 55", said);
            StringAssert.Contains("ten Fibonacci numbers: 0 1 1 2 3 5 8 13 21 34", said);
            StringAssert.Contains("every instruction once: 41 of 41 checks held", said);
            StringAssert.Contains("PASS: every result as expected", said);
            Assert.AreEqual(result.Time, project.Stimulus.End, "the run is shown to where the testbench stops");
        }

        /// <summary>
        /// Each datapath laid out by hand (the review of the first shots, 2026-11-01): every key of its arrangement a part of
        /// its drawing, each part standing where it was put, against the code it was laid out for.
        /// </summary>
        [TestCase("Single-cycle RISC-V", "scpu_tb.dut.datapath", "scpu_datapath")]
        [TestCase("Multicycle RISC-V", "mcpu_demo_tb.dut.datapath", "mcpu_datapath")]
        public void ADatapath_IsDrawnWhereItWasLaidOut(string name, string scope, string module)
        {
            Project project = WorkbenchSamples.All.Single(s => s.Name == name).Build();
            Module datapath = project.Find(module);
            Assert.AreEqual(WorkbenchProcessors.Arranged[module].Count, datapath.Arrangement.Count);
            Assert.AreEqual(datapath.Source, datapath.ArrangedFor, "laid out against this code");

            CompiledProject compiled = BoardCompiler.Compile(project);
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            RtlDrawing drawing = RtlDrawing.Of(project, RtlExtractor.Of(elaboration.Design, scope), module, arranged: datapath);
            var drawn = drawing.Module.Parts.ToDictionary(p => drawing.KeyOf(p.Id), p => drawing.Module.Layout.PlacementOf(p.Id));
            foreach (System.Collections.Generic.KeyValuePair<string, Placement> laid in datapath.Arrangement)
            {
                Assert.IsTrue(drawn.ContainsKey(laid.Key), $"{laid.Key} names no part of {module}'s drawing");
                Assert.AreEqual(laid.Value, drawn[laid.Key], laid.Key);
            }
            Assert.AreEqual(drawn.Count, datapath.Arrangement.Count, "every part laid out by hand");
        }

        /// <summary>
        /// GATES in a browser opens no more parts than its cap (Part 15h-6, Ahmad's choice, 2026-11-01): the multicycle
        /// controller's thousand gates keep their boxes and say the desktop version opens them, the single-cycle's open, and
        /// the desktop opens every one.
        /// </summary>
        [Test]
        public void GatesInABrowser_KeepTheBoxesPastTheCap_SayingTheDesktopOpensThem()
        {
            int cap = WorkbenchSession.BrowserGatesCap;
            WorkbenchSession multicycle = InsideTheControl("Multicycle RISC-V");
            multicycle.GatesCap = cap;
            int boxes = multicycle.DrawnModule.Parts.Count;
            string refused = multicycle.DrawGates(true);
            Assert.IsNotNull(refused, "past the cap");
            StringAssert.StartsWith("GATES would draw ", refused);
            StringAssert.Contains("desktop version", refused);
            Assert.IsFalse(multicycle.ShowsGatesOfCode, "the boxes kept");
            Assert.AreEqual(boxes, multicycle.DrawnModule.Parts.Count);

            multicycle.GatesCap = null;   // the desktop's
            Assert.IsNull(multicycle.DrawGates(true));
            Assert.IsTrue(multicycle.ShowsGatesOfCode);
            Assert.Greater(multicycle.DrawnModule.Parts.Count, cap, "every gate opened");
            Assert.AreEqual(WorkbenchSession.GatesCapRefusal(multicycle.DrawnModule.Parts.Count, cap), refused, "the refusal counted what it would draw");
            Assert.IsNull(multicycle.DrawGates(false), "BOXES is never refused");
            Assert.IsNull(multicycle.DrawGates(true));

            // On already when the drawing is made again past the cap -- as code made bigger with GATES on, or an import of the
            // same name, would bring it: drawn as its boxes, and why told once (the review of #123).
            multicycle.GatesCap = cap;
            Assert.IsNull(multicycle.TakeGatesRefusal(), "nothing refused yet");
            Assert.IsTrue(multicycle.StepOut());
            Assert.IsTrue(multicycle.StepInto(ControlOf(multicycle)));
            Assert.IsFalse(multicycle.ShowsGatesOfCode, "the boxes");
            Assert.AreEqual(boxes, multicycle.DrawnModule.Parts.Count);
            Assert.AreEqual(refused, multicycle.TakeGatesRefusal());
            Assert.IsNull(multicycle.TakeGatesRefusal(), "told once");

            WorkbenchSession single = InsideTheControl("Single-cycle RISC-V");
            single.GatesCap = cap;
            Assert.IsNull(single.DrawGates(true), "under the cap");
            Assert.IsTrue(single.ShowsGatesOfCode);
            Assert.LessOrEqual(single.DrawnModule.Parts.Count, cap);
        }

        private static WorkbenchSession InsideTheControl(string sample)
        {
            double now = 0;
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == sample).Build(), () => now += 0.01);
            session.Finish();
            Assert.IsTrue(session.StepInto(ControlOf(session)));
            Assert.IsTrue(session.ShowsDrawingOfCode, "the controller drawn");
            return session;
        }

        private static int ControlOf(WorkbenchSession session) =>
            session.DrawnModule.Parts.OfType<BlockPart>().Single(b => b.Module.EndsWith("_control", System.StringComparison.Ordinal)).Id;
    }
}
