using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The exercises as the workbench lists them (Part 15d): numbered in order, the next one to do, the chapters into the
    /// page's columns, and what a CHECK says -- solved, or every reason not, each failed check at its moment.
    /// </summary>
    public class ExerciseRunTests
    {
        private static readonly List<Exercise> Run = ExerciseFiles.InOrder();

        [Test]
        public void TheRun_IsNumberedInOrder_FromOne()
        {
            Assert.AreEqual(1, ExerciseRun.NumberOf(Run, Run[0].Id));
            Assert.AreEqual(Run.Count, ExerciseRun.NumberOf(Run, Run[Run.Count - 1].Id));
            Assert.AreEqual(0, ExerciseRun.NumberOf(Run, "no-such-exercise"));
            Assert.AreSame(Run[4], ExerciseRun.Find(Run, Run[4].Id));
            StringAssert.StartsWith("5  ", ExerciseRun.Heading(5, Run[4]));
            StringAssert.EndsWith(Run[4].Title.ToUpperInvariant(), ExerciseRun.Heading(5, Run[4]));
        }

        [Test]
        public void TheNextExercise_IsTheFirstUnsolved_AfterTheOneOpen_ThenFromTheStart()
        {
            var solved = new HashSet<string> { Run[0].Id, Run[1].Id, Run[3].Id };
            Assert.AreSame(Run[2], ExerciseRun.Next(Run, solved.Contains), "the first unsolved");
            Assert.AreSame(Run[4], ExerciseRun.Next(Run, solved.Contains, after: Run[2].Id), "after the one open");
            Assert.AreSame(Run[2], ExerciseRun.Next(Run, id => id != Run[2].Id, after: Run[5].Id), "round from the start");
            Assert.IsNull(ExerciseRun.Next(Run, id => true), "none when every one is solved");
            Assert.AreEqual(3, ExerciseRun.SolvedCount(Run, solved.Contains));
        }

        [Test]
        public void TheChapters_FillThePagesColumns_InOrder_AndEvenly()
        {
            var chapters = ExerciseRun.Chapters(Run);
            CollectionAssert.AreEqual(Enumerable.Range(1, chapters.Count).ToList(), chapters.Select(c => c.Chapter).ToList(), "every chapter, once, in order");
            Assert.AreEqual(Run.Count, chapters.Sum(c => c.Exercises.Count));

            IReadOnlyList<IReadOnlyList<int>> columns = ExerciseRun.Columns(chapters, 3);
            Assert.AreEqual(3, columns.Count);
            CollectionAssert.AreEqual(Enumerable.Range(0, chapters.Count).ToList(), columns.SelectMany(c => c).ToList(), "every chapter in order, none twice");

            double Height(IReadOnlyList<int> column) => column.Sum(i => chapters[i].Exercises.Count + 1.5);
            double tallest = columns.Max(Height), total = columns.Sum(Height);
            Assert.LessOrEqual(tallest, total / 3 + chapters.Max(c => c.Exercises.Count + 1.5), "no column far taller than a third");
            foreach (int chapter in Enumerable.Range(1, 9))
                StringAssert.DoesNotStartWith("CHAPTER", ExerciseRun.ChapterName(chapter), "each of the course's chapters has its name");
        }

        [Test]
        public void ACheck_ThatFails_SaysWhy_AndLeavesEachCheckToTheTestbench()
        {
            Exercise exercise = Run.Single(e => e.Id == "half-adder");
            var problems = new List<string>();
            Project opened = exercise.ToProject(problems);
            Grade grade = ExerciseGrader.Grade(exercise, opened);
            Assert.IsFalse(grade.Solved, "sanity: nothing built");

            IReadOnlyList<TranscriptLine> lines = ExerciseRun.Lines(exercise, grade, "ns");
            Assert.AreEqual(TranscriptSeverity.Error, lines[0].Severity);
            Assert.AreEqual(ExerciseRun.Verdict(exercise, grade, "ns"), lines[0].Text, "the verdict first");
            Assert.AreEqual(0, lines.Count(l => l.IsMessage), "each failed check is the testbench's own line, at its moment, not a copy");
            StringAssert.Contains(grade.Failures.Count + " of " + grade.Checks + " checks failed", lines[0].Text);

            StringAssert.Contains("a click on it goes to its moment", lines[0].Text);
        }

        [Test]
        public void ACheck_ThatPasses_SaysWhatItTook()
        {
            Exercise exercise = Run.Single(e => e.Id == "half-adder");
            Grade grade = ExerciseGrader.Grade(exercise, ExerciseFiles.Solution(exercise));
            Assert.IsTrue(grade.Solved);
            Assert.AreEqual("CHECK: solved, with " + ExerciseRun.Took(grade.Gates, grade.Settle, "ns"), ExerciseRun.Verdict(exercise, grade, "ns"));
            StringAssert.StartsWith(grade.Gates + " gates, settling in ", ExerciseRun.Took(grade.Gates, grade.Settle, "ns"));
            Assert.AreEqual("1 gate", ExerciseRun.Took(1, null, "ns"));
            Assert.AreEqual(TranscriptSeverity.Info, ExerciseRun.Lines(exercise, grade, "ns").Single().Severity);
        }

        [Test]
        public void TheSolvedCard_SaysTheBlocksItGives_AndAPipelinesPeriod()
        {
            Exercise four = Run.Single(e => e.Id == "four-at-once");
            Grade adders = ExerciseGrader.Grade(four, ExerciseFiles.Solution(four));
            Assert.IsTrue(adders.Solved);
            StringAssert.StartsWith("4 FA, settling in ", ExerciseRun.Took(four, adders, "ns"), "the blocks, not 0 gates");

            Exercise cut = Run.Single(e => e.Id == "cut-it-in-two");
            Grade pipeline = ExerciseGrader.Grade(cut, ExerciseFiles.Solution(cut));
            Assert.IsTrue(pipeline.Solved);
            StringAssert.EndsWith(", at a period of 2 ns", ExerciseRun.Took(cut, pipeline, "ns"));
            Assert.IsNull(pipeline.Settle, "sanity: registers all round, so no settle to say");
        }

        /// <summary>
        /// The solved card's best, beside what this solve took: at an exercise that gives only blocks, each counted whole, its
        /// gates are always 0 and no measure, so the best is how fast it settled (the 16d sweep: "It took 4 FA, settling in
        /// 9 ns. Your best: 0 gates, settling in 9 ns."); at one of gates, their count and the settle, as ever.
        /// </summary>
        [Test]
        public void TheSolvedCardsBest_AtAnExerciseOfBlocks_IsNoCountOfGates()
        {
            Exercise four = Run.Single(e => e.Id == "four-at-once");
            Grade adders = ExerciseGrader.Grade(four, ExerciseFiles.Solution(four));
            Assert.AreEqual(0, adders.Gates, "sanity: a stocked block is counted whole, its gates none");
            string best = ExerciseRun.Best(four, adders.Gates, adders.Settle, "ns");
            StringAssert.DoesNotContain("gate", best, "no count of gates it cannot have");
            Assert.AreEqual("settling in " + adders.Settle + " ns", best);
            Assert.AreEqual("0 gates", ExerciseRun.Best(four, 0, null, "ns"), "with no settle, still something to say");

            Exercise half = Run.Single(e => e.Id == "half-adder");
            Assert.AreEqual("2 gates, settling in 1 ns", ExerciseRun.Best(half, 2, 1, "ns"), "an exercise of gates: their count and the settle");
        }

        [Test]
        public void ACheck_ThatIsRightButLate_SaysByHowMuch()
        {
            Exercise exercise = Run.Single(e => e.Id == "out-of-step");
            Grade grade = ExerciseGrader.Grade(exercise, exercise.ToProject(new List<string>()));
            Assert.IsEmpty(grade.Failures, "sanity: its values are right");
            string verdict = ExerciseRun.Verdict(exercise, grade, "ns");
            StringAssert.Contains("every value is right, but it settles in " + grade.Settle + " ns, and must within " + exercise.SettleBy, verdict);
        }

        private const string HalfAdder =
            "module half_adder (input logic a, input logic b, output logic sum, output logic carry);\n" +
            "    xor #1 g1 (sum, a, b);\n" +
            "    and #1 g2 (carry, a, b);\n" +
            "endmodule\n";

        /// <summary>The half adder answered in code, as WRITE IT leaves it -- its ports and no body -- its code shown, to be typed into.</summary>
        private static (WorkbenchSession Session, Exercise Exercise, string Brief) WrittenHalfAdder()
        {
            Exercise exercise = Run.Single(e => e.Id == "half-adder");
            var editor = new BoardEditor(exercise.ToProject(new List<string>()));
            Module written = exercise.Written(editor.Project);
            EditResult wrote = editor.ReplaceModule(written);
            Assert.IsTrue(wrote.Done, wrote.Refusal);
            double now = 0;
            var session = new WorkbenchSession(editor.Project, () => now += 0.01);
            session.Finish();
            session.ShowCodeOf(session.Project.Top);
            Assert.IsTrue(session.ShowsCode, "sanity: its code on screen");
            return (session, exercise, written.Source.Substring(0, written.Source.IndexOf("module ", System.StringComparison.Ordinal)));
        }

        /// <summary>
        /// CHECK grades what the student typed (the 16d sweep): code typed and still within the editor's pause is applied, then
        /// graded -- solved, where the document held only the ports -- and code with an error is not graded at all, the refusal
        /// saying it is not checked and why, the last code that applied left as it was. It graded the document alone, and under
        /// text with an error could mark the code before it solved.
        /// </summary>
        [Test]
        public void ACheck_GradesTheCodeTyped_AndGradesNothingThatDoesNotApply()
        {
            (WorkbenchSession session, Exercise exercise, string brief) = WrittenHalfAdder();
            Assert.IsFalse(ExerciseGrader.Grade(exercise, session.Project).Solved, "sanity: the ports alone are no answer");

            string answer = brief + HalfAdder;
            session.Code.Type(answer, 0);   // the pause never runs out: no frame is made
            Assert.AreNotEqual(answer, session.Project.TopModule.Source, "sanity: typed, not yet applied");
            Grade solved = ExerciseRun.Check(session, exercise, out string refused);
            Assert.IsNull(refused, refused);
            Assert.IsNotNull(solved);
            Assert.IsTrue(solved.Solved, "the code typed, graded");
            Assert.AreEqual(answer, session.Project.TopModule.Source, "and applied");

            string broken = answer.Replace("and #1 g2 (carry, a, b);", "and #1 g2 (carry, a, b)");
            session.Code.Type(broken, 0);
            Grade none = ExerciseRun.Check(session, exercise, out refused);
            Assert.IsNull(none, "nothing graded: the code on screen is not what would be");
            StringAssert.StartsWith("half_adder's code on screen does not apply, so it is not checked: ", refused);
            Assert.AreEqual(answer, session.Project.TopModule.Source, "the last code that applied as it was");
            Assert.AreEqual(broken, session.Code.Text, "and the code typed still on screen");
        }

        /// <summary>
        /// What EXPORT, the hand-in, the lock and CHECK are told of text that does not apply, kept for a module whose code is
        /// not on screen (Ahmad, 2026-11-03: kept as a draft): whose it is, and how to reach it; and nothing with nothing typed.
        /// </summary>
        [Test]
        public void TypingThatStopsAnExport_IsSaidWithWhoseItIs_AndHowToReachIt()
        {
            (WorkbenchSession session, _, string brief) = WrittenHalfAdder();
            Assert.IsNull(session.WhyTypingStops("exported"), "nothing typed: nothing in the way");

            session.Code.Type(brief + HalfAdder.Replace("endmodule", "endmodul"), 0);
            session.DrawCode(true);
            Assert.IsFalse(session.ShowsCode, "sanity: its drawing up, the code typed kept behind it");
            string said = session.WhyTypingStops("exported");
            StringAssert.StartsWith("half_adder's code, typed and not applied, does not apply, so it is not exported -- open its code to mend it or REVERT it: ",
                said);
        }
    }
}
