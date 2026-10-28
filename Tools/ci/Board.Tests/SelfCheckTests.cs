using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The self-checking testbench (Part 15b, the brief's D5): a stimulus's checks are printed into its testbench, which says
    /// each one that fails and how many did; an x in a value wanted leaves the bit free, a 0 or a 1 read as x fails, and a z
    /// is compared exactly; and the checks are saved. <c>SelfCheckOracleTests</c> holds Icarus to the same lines.
    /// </summary>
    public class SelfCheckTests
    {
        private static Project Checked(string sums, string carries) => SampleBoards.CheckedHalfAdder(sums, carries);

        internal static List<string> Printed(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(project.Stimulus.StopTime + 10);
            // What the testbench printed: the engine's own note of the $stop it ends on is not one of its lines.
            return sim.History.Messages.Select(m => m.Text).Where(t => !t.StartsWith("$stop", System.StringComparison.Ordinal)).ToList();
        }

        [Test]
        public void ARightDesign_FailsNoCheck_AndSaysSo()
        {
            CollectionAssert.AreEqual(new[] { "0 of 8 checks failed" }, Printed(Checked("0110", "0001")));
        }

        [Test]
        public void AWrongExpectation_IsSaid_WithItsMoment_WhatWasRead_AndWhatWasWanted()
        {
            CollectionAssert.AreEqual(new[]
            {
                "FAIL s at 19: 1, wanted 0",
                "FAIL c at 39: 1, wanted 0",
                "2 of 8 checks failed",
            }, Printed(Checked("0010", "0000")));
        }

        [Test]
        public void AnXWanted_LeavesTheBitFree()
        {
            CollectionAssert.AreEqual(new[] { "0 of 8 checks failed" }, Printed(Checked("xxxx", "0x0x")));
        }

        /// <summary>A 0 or a 1 wanted where the output reads x fails: <c>if</c> takes an x as false, so the check asks for a definite 1.</summary>
        [Test]
        public void AnOutputReadAsX_FailsACheckThatWantsAValue()
        {
            Project project = Checked("0110", "0001");
            Track a = project.Stimulus.Tracks.Single(t => t.Port == "a");
            var unknown = a with { Steps = a.Steps.Select(s => s.Time == 30 ? new Step(30, LogicVec.FromBinary("x")) : s).ToList() };
            Project withX = project with { Stimulus = project.Stimulus with { Tracks = project.Stimulus.Tracks.Select(t => t.Port == "a" ? unknown : t).ToList() } };

            List<string> printed = Printed(withX);
            CollectionAssert.Contains(printed, "FAIL s at 39: x, wanted 0");
            CollectionAssert.Contains(printed, "FAIL c at 39: x, wanted 1");
        }

        [Test]
        public void AValueWithAZ_IsComparedExactly()
        {
            var z = new Check { Port = "s", Time = 9, Expected = LogicVec.FromBinary("z") };
            Assert.IsTrue(z.Exact);
            Assert.IsTrue(z.Passes(LogicVec.FromBinary("z")));
            Assert.IsFalse(z.Passes(LogicVec.FromBinary("0")), "a z wanted is a z, not anything");

            var free = new Check { Port = "s", Time = 9, Expected = LogicVec.FromBinary("1x") };
            Assert.IsFalse(free.Exact);
            Assert.IsTrue(free.Passes(LogicVec.FromBinary("10")) && free.Passes(LogicVec.FromBinary("11")));
            Assert.IsFalse(free.Passes(LogicVec.FromBinary("01")));
            Assert.IsFalse(free.Passes(LogicVec.FromBinary("x0")), "an x where a 1 is wanted");
        }

        [Test]
        public void TheTestbench_IsPrintedInTheCoursesStyle()
        {
            CompiledProject compiled = BoardCompiler.Compile(Checked("0110", "0001"));
            string bench = BoardPrinter.Print(compiled).Single(f => f.Path.Contains("_tb")).Text;
            StringAssert.Contains("int errors = 0;", bench);
            StringAssert.Contains("if ((s ==? 1'b0) !== 1'b1)", bench);
            StringAssert.Contains("$display(\"FAIL s at %0t: %b, wanted 0\", $time, s);", bench);
            StringAssert.Contains("errors = errors + 1;", bench);
            StringAssert.Contains("$display(\"%0d of 8 checks failed\", errors);", bench);

            string unchecked_ = BoardPrinter.Print(BoardCompiler.Compile(SampleBoards.Build("half_adder"))).Single(f => f.Path.Contains("_tb")).Text;
            StringAssert.DoesNotContain("errors", unchecked_, "a stimulus with no checks prints the testbench it always did");
        }

        /// <summary>A check follows its port: renamed with it, and gone with it (the #85 review); and a check on no output is refused.</summary>
        [Test]
        public void ACheck_FollowsItsPort_AndOneOnNoOutputIsRefused()
        {
            var editor = new BoardEditor(Checked("0110", "0001"));
            Module top = editor.Project.TopModule;
            PortPart s = top.Ports.Single(p => p.Name == "s");
            Assert.IsTrue(editor.Change(top.Name, s with { Name = "sum" }).Done);
            Assert.That(editor.Project.Stimulus.Checks.Where(c => c.Port != "c").Select(c => c.Port), Is.All.EqualTo("sum"));

            Assert.IsTrue(editor.Remove(top.Name, s.Id).Done);
            Assert.That(editor.Project.Stimulus.Checks.Select(c => c.Port), Is.All.EqualTo("c"), "the sum's checks went with it");

            Project onAnInput = editor.Project with
            {
                Stimulus = editor.Project.Stimulus with { Checks = new[] { new Check { Port = "a", Time = 9, Expected = LogicVec.FromBinary("1") } } },
            };
            StringAssert.Contains("The test checks a, which is not an output of half_adder.",
                string.Join("\n", BoardRules.Check(onAnInput).Where(d => d.IsError).Select(d => d.Message)));
            Project tooWide = editor.Project with
            {
                Stimulus = editor.Project.Stimulus with { Checks = new[] { new Check { Port = "c", Time = 9, Expected = LogicVec.FromBinary("01") } } },
            };
            StringAssert.Contains("c is 1 bit, and the test wants 2 bits of it at 9.",
                string.Join("\n", BoardRules.Check(tooWide).Where(d => d.IsError).Select(d => d.Message)));
        }

        [Test]
        public void TheChecks_AreSaved_AndAnOlderFileHasNone()
        {
            Project project = Checked("0110", "0x01");
            var problems = new List<string>();
            Stimulus back = StimulusRecord.From(project.Stimulus).ToStimulus(problems);
            Assert.IsEmpty(problems);
            CollectionAssert.AreEqual(project.Stimulus.Checks.Select(c => (c.Port, c.Time, c.Expected.Format(Radix.Binary))),
                back.Checks.Select(c => (c.Port, c.Time, c.Expected.Format(Radix.Binary))));

            Stimulus older = new StimulusRecord { checks = null }.ToStimulus(problems);
            Assert.IsEmpty(older.Checks);
        }
    }
}
