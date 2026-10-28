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

        [Test]
        public void ACheck_ThatIsRightButLate_SaysByHowMuch()
        {
            Exercise exercise = Run.Single(e => e.Id == "out-of-step");
            Grade grade = ExerciseGrader.Grade(exercise, exercise.ToProject(new List<string>()));
            Assert.IsEmpty(grade.Failures, "sanity: its values are right");
            string verdict = ExerciseRun.Verdict(exercise, grade, "ns");
            StringAssert.Contains("every value is right, but it settles in " + grade.Settle + " ns, and must within " + exercise.SettleBy, verdict);
        }
    }
}
