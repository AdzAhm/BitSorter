using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// 5.0's first-time hints and its guided tutorial (Part 15f): each hint fired by what the run shows -- a gate's delay,
    /// a clock, a z, a glitch, an x at a check -- once; the tutorial's steps read off the document, going back when it does;
    /// and the four kinds of teaching kept apart, no four-word run shared between a hint, a tutorial step and any
    /// exercise's goal or hint (CLAUDE.md, "There are four kinds of teaching").
    /// </summary>
    public class SimulatorHintTests
    {
        /// <summary>What <paramref name="project"/> earns, run to the end of its test, with <paramref name="seen"/> already shown.</summary>
        private static string DueAfterARun(Project project, params string[] seen)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Assert.IsTrue(compiled.Succeeded, string.Join("\n", compiled.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(elaboration.HasErrors, string.Join("\n", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(project.Stimulus.StopTime + 1);
            return SimulatorHints.Due(project, elaboration.Design, sim.History, compiled.DesignPath,
                SimulatorHints.Slowest(TimingAnalysis.Of(project)), seen.Contains);
        }

        /// <summary>An exercise's reference solution, with the exercise's test to run.</summary>
        private static Project Solved(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            Project solution = ExerciseFiles.Solution(exercise);
            return solution with { Stimulus = exercise.Test() };
        }

        [Test]
        public void EachHint_IsFiredByWhatTheRunShows_AndOnce()
        {
            Assert.AreEqual(SimulatorHints.GateDelay, DueAfterARun(Solved("half-adder")), "a gate's output moving after the start");
            Assert.IsNull(DueAfterARun(Solved("half-adder"), SimulatorHints.GateDelay), "once: nothing else in a half adder");
            Assert.AreEqual(SimulatorHints.Clock, DueAfterARun(Solved("one-clock-late")), "a clock in the stimulus");
            Assert.AreEqual(SimulatorHints.HighZ, DueAfterARun(Solved("share-the-wire"), SimulatorHints.GateDelay), "a z on the bus");
            Assert.AreEqual(SimulatorHints.Glitch, DueAfterARun(Solved("balance-the-paths"), SimulatorHints.GateDelay),
                "the pulse its goal says the waves show");
            Assert.IsNull(DueAfterARun(Solved("route-the-bit"), SimulatorHints.GateDelay), "one gate on one path earns no glitch");
        }

        /// <summary>The glitch and z hints read a written top's ports from its header (the #90 review: they never came on code).</summary>
        [Test]
        public void OnCode_TheHintsReadItsHeader()
        {
            Project Written(string id)
            {
                Project solved = Solved(id);
                return solved.WithModule(ExerciseFiles.Load(id).Written(solved));
            }

            Assert.AreEqual(SimulatorHints.Glitch, DueAfterARun(Written("balance-the-paths"), SimulatorHints.GateDelay));
            Assert.AreEqual(SimulatorHints.HighZ, DueAfterARun(Written("share-the-wire"), SimulatorHints.GateDelay));
        }

        /// <summary>
        /// An input pulse narrower than a path's delay, followed along one path, is no glitch: three NOTs in a row, their
        /// input up for one nanosecond (the #90 review: it read as one, "two paths into one gate").
        /// </summary>
        [Test]
        public void ANarrowPulseAlongOnePath_IsNoGlitch()
        {
            Exercise chain = ExerciseFiles.Load("route-the-bit");
            Project opened = chain.ToProject(new List<string>());
            Module written = chain.Written(opened);
            string header = written.Source.Substring(0, written.Source.IndexOf("module "));
            Project project = opened.WithModule(written with { Source = header +
                "module " + opened.Top + " (input logic in, output logic binOne, output logic binZero);\n" +
                "    logic n1, n2;\n    not #1 g1 (n1, in);\n    not #1 g2 (n2, n1);\n    not #1 g3 (binOne, n2);\nendmodule\n" });
            var steps = new[]
            {
                new Board.Step(0, SystemVerilog.LogicVec.FromBinary("0")),
                new Board.Step(5, SystemVerilog.LogicVec.FromBinary("1")),
                new Board.Step(6, SystemVerilog.LogicVec.FromBinary("0")),
            };
            var pulse = new Board.Stimulus { Tracks = new[] { new Board.Track { Port = "in", Steps = steps } }, End = 30 };
            Assert.IsNull(DueAfterARun(project with { Stimulus = pulse }, SimulatorHints.GateDelay));
        }

        [Test]
        public void AnXAtACheck_EarnsItsHint_AtCHECK()
        {
            Exercise exercise = ExerciseFiles.Load("half-adder");
            Grade nothing = ExerciseGrader.Grade(exercise, exercise.ToProject(new List<string>()));
            Assert.AreEqual(SimulatorHints.Unknown, SimulatorHints.AfterCheck(nothing, id => false), "outputs driven by nothing read x");
            Assert.IsNull(SimulatorHints.AfterCheck(nothing, id => id == SimulatorHints.Unknown), "once");
            Assert.IsNull(SimulatorHints.AfterCheck(ExerciseGrader.Grade(exercise, ExerciseFiles.Solution(exercise)), id => false));
        }

        [Test]
        public void EveryHint_HasItsWords()
        {
            foreach (string id in SimulatorHints.All)
                Assert.IsFalse(string.IsNullOrWhiteSpace(SimulatorHints.TextFor(id)), id);
            Assert.IsNull(SimulatorHints.TextFor("stalled"), "4.x's are not 5.0's");
            Assert.AreEqual(SimulatorHints.All.Count, SimulatorHints.All.Distinct().Count());
        }

        private static readonly char[] Breaks = { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'', '"' };

        private static HashSet<string> Runs(string text, int length = 4)
        {
            string[] words = text.ToLowerInvariant().Split(Breaks, StringSplitOptions.RemoveEmptyEntries);
            var runs = new HashSet<string>();
            for (int i = 0; i + length <= words.Length; i++)
                runs.Add(string.Join(" ", words, i, length));
            return runs;
        }

        [Test]
        public void TheFourKindsOfTeaching_ShareNoFourWordRun()
        {
            var teaching = new List<(string Where, string Text)>();
            foreach (string id in SimulatorHints.All)
                teaching.Add(("the first-time hint " + id, SimulatorHints.TextFor(id)));
            foreach (GuidedTutorial.Step step in Enum.GetValues(typeof(GuidedTutorial.Step)))
                teaching.Add(("the tutorial's step " + step, GuidedTutorial.TextFor(step)));

            var exercises = new List<(string Where, string Text)>();
            foreach (Exercise exercise in ExerciseFiles.InOrder())
            {
                exercises.Add((exercise.Id + "'s goal", exercise.Goal));
                exercises.Add((exercise.Id + "'s hint", exercise.Hint));
            }

            for (int i = 0; i < teaching.Count; i++)
            {
                HashSet<string> runs = Runs(teaching[i].Text);
                foreach ((string where, string text) in exercises.Concat(teaching.Skip(i + 1)))
                    foreach (string run in Runs(text))
                        Assert.IsFalse(runs.Contains(run), $"{teaching[i].Where} and {where} both say '{run}'");
            }
        }

        [Test]
        public void TheTutorial_ReadsItsStepOffTheDocument_AndGoesBackWithIt()
        {
            Exercise exercise = ExerciseFiles.Load(GuidedTutorial.ExerciseId);
            var editor = new BoardEditor(exercise.ToProject(new List<string>()));
            string top = editor.Project.Top;
            CatalogEntry not = PartsCatalog.Find(editor.Project, top, "not");
            Assert.IsNotNull(not, "sanity: the exercise stocks a NOT");

            Assert.AreEqual(GuidedTutorial.Step.TakeTheNot, GuidedTutorial.At(editor.Project, null, false));
            Assert.AreEqual(GuidedTutorial.Step.PutItDown, GuidedTutorial.At(editor.Project, not, false));

            EditResult placed = PartsCatalog.Place(editor, top, not, new Placement(6, 0));
            Assert.IsTrue(placed.Done, placed.Refusal);
            Assert.AreEqual(GuidedTutorial.Step.WireIn, GuidedTutorial.At(editor.Project, null, false));

            int Port(string name) => editor.Project.TopModule.Ports.Single(p => p.Name == name).Id;
            Assert.IsTrue(editor.Connect(top, Endpoint.At(Port(GuidedTutorial.In), "out"), Endpoint.At(placed.Id, "a")).Done);
            Assert.AreEqual(GuidedTutorial.Step.WireOut, GuidedTutorial.At(editor.Project, null, false));
            Assert.IsTrue(editor.Connect(top, Endpoint.At(placed.Id, "y"), Endpoint.At(Port(GuidedTutorial.Out), "in")).Done);
            Assert.AreEqual(GuidedTutorial.Step.Check, GuidedTutorial.At(editor.Project, null, false));

            Assert.IsTrue(ExerciseGrader.Grade(exercise, editor.Project).Solved, "the steps build the answer");
            Assert.AreEqual(GuidedTutorial.Step.Done, GuidedTutorial.At(editor.Project, null, true));

            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(GuidedTutorial.Step.WireOut, GuidedTutorial.At(editor.Project, null, false), "an undo un-meets a step, by itself");
            Assert.IsTrue(editor.Remove(top, placed.Id).Done);
            Assert.AreEqual(GuidedTutorial.Step.TakeTheNot, GuidedTutorial.At(editor.Project, null, false), "and so does deleting the NOT");
        }
    }
}
