using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The exercises as a course (Part 15c), held under dotnet as the 4.x levels are by the Edit Mode <c>CurriculumTests</c>:
    /// every file opens and its reference solution solves it, every starting circuit fails as it opens, the thirty levels are
    /// all here under their own ids, the run is in order chapter by chapter, every exercise has a goal and a hint that gives
    /// nothing away, and a thing is taught before it is asked for. <c>ExerciseOracleTests</c> holds every solution's
    /// self-checking testbench to passing under Icarus too.
    /// </summary>
    public class ExerciseCurriculumTests
    {
        private static IEnumerable<string> Ids() => ExerciseFiles.Ids();

        [TestCaseSource(nameof(Ids))]
        public void EveryExercise_Opens_AndItsSolutionSolvesIt(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            Assert.AreEqual(id, exercise.Id, "an exercise is known by its file's name");

            var problems = new List<string>();
            Assert.IsNotNull(exercise.ToProject(problems), string.Join("\n", problems));

            Grade grade = ExerciseGrader.Grade(exercise, ExerciseFiles.Solution(exercise));
            Assert.IsTrue(grade.Solved, $"{id}'s solution does not solve it:\n  " +
                string.Join("\n  ", grade.Problems.Concat(grade.Faults).Concat(grade.Failures.Select(f => f.ToString()))) +
                (grade.SettleMet ? "" : $"\n  it settles in {grade.Settle}, past {exercise.SettleBy}") +
                (grade.PeriodMet ? "" : $"\n  its period is {grade.MinimumPeriod}, past {exercise.Period}"));
        }

        [TestCaseSource(nameof(Ids))]
        public void EveryStartingCircuit_FailsAsItOpens(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            if (string.IsNullOrWhiteSpace(exercise.Start))
                Assert.Pass("no starting circuit");

            var problems = new List<string>();
            Project opened = exercise.ToProject(problems);
            Assert.IsNotNull(opened, string.Join("\n", problems));
            Assert.IsFalse(ExerciseGrader.Grade(exercise, opened).Solved, $"{id} opens on a circuit that already solves it");
        }

        [Test]
        public void TheThirtyLevels_AreAllExercises_UnderTheirOwnIds()
        {
            string levels = Path.Combine(ExerciseFiles.Root, "Assets", "Resources", "Levels");
            var ids = new HashSet<string>(Ids());
            foreach (string level in Directory.EnumerateFiles(levels, "*.json").Select(Path.GetFileNameWithoutExtension))
                Assert.IsTrue(ids.Contains(level), $"the level {level} is no exercise: its id is what its progress and its board carry over under");
        }

        [Test]
        public void TheRun_IsInOrder_ChapterByChapter()
        {
            List<Exercise> run = ExerciseFiles.InOrder();
            var orders = new HashSet<int>();
            int chapter = 0;
            foreach (Exercise exercise in run)
            {
                Assert.IsTrue(orders.Add(exercise.Order), $"two exercises take the place {exercise.Order}");
                Assert.GreaterOrEqual(exercise.Chapter, chapter, $"{exercise.Id} goes back to chapter {exercise.Chapter} after chapter {chapter}");
                Assert.Greater(exercise.Chapter, 0, $"{exercise.Id} has no chapter");
                chapter = exercise.Chapter;
            }
        }

        [TestCaseSource(nameof(Ids))]
        public void EveryExercise_HasAGoal_AndAHint_ThatAreNotOneSentence(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            Assert.IsNotEmpty(exercise.Goal.Trim(), "a goal says what to build");
            Assert.IsNotEmpty(exercise.Hint.Trim(), "a hint nudges");
            Assert.AreNotEqual(exercise.Goal.Trim(), exercise.Hint.Trim());
        }

        private static readonly char[] Breaks = { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'', '"' };

        private static HashSet<string> Words(string text) =>
            new HashSet<string>(text.ToLowerInvariant().Split(Breaks, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>
        /// The rule that catches a hint stating its whole answer, as the levels' was (CLAUDE.md: the goal may name gates, the
        /// hint may not). Naming one part can be the premise -- "NAND alone can build every gate there is" -- but naming two of
        /// those it stocks is a wiring diagram in prose. A word is a word: "and" and "or" count.
        /// </summary>
        [TestCaseSource(nameof(Ids))]
        public void NoHint_NamesMoreThanOneOfItsOwnParts(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            HashSet<string> words = Words(exercise.Hint);
            string[] named = exercise.Stock.Select(s => s.Part.ToLowerInvariant()).Where(words.Contains).ToArray();
            Assert.LessOrEqual(named.Length, 1, $"{id}'s hint names {string.Join(", ", named)}: that is its answer. Hint: {exercise.Hint}");
        }

        [TestCaseSource(nameof(Ids))]
        public void NoHint_SaysWhichPartMakesWhichOutput(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            string hint = exercise.Hint.ToLowerInvariant();
            foreach (ExerciseSignal output in exercise.Outputs)
                foreach (PartCount part in exercise.Stock)
                    Assert.IsFalse(hint.Contains(output.Name.ToLowerInvariant() + " is " + part.Part.ToLowerInvariant()),
                        $"{id}'s hint says outright what makes {output.Name}. Hint: {exercise.Hint}");
        }

        /// <summary>
        /// A thing is taught before it is asked for (CLAUDE.md, "A mechanic is taught before it is required"): the first exercise
        /// to ask each one says so in its goal -- a time to settle by, a clock, a circuit to open on, a block, a period.
        /// </summary>
        [Test]
        public void TheFirstToAskEachThing_SaysSoInItsGoal()
        {
            List<Exercise> run = ExerciseFiles.InOrder();

            void First(string what, Func<Exercise, bool> asks, params string[] says)
            {
                Exercise first = run.FirstOrDefault(asks);
                if (first == null)
                    return;
                foreach (string word in says)
                    StringAssert.Contains(word, first.Goal, $"{first.Id} is the first to ask for {what}, and its goal has to say so");
            }

            First("a time to settle by", e => e.SettleBy > 0, "settle", "TIMING");
            First("a clock", e => e.Clocked, "clock", "starts at");
            First("a register", e => e.Stock.Any(s => s.Part == PartCount.Register), "register");
            First("a circuit to open on", e => !string.IsNullOrWhiteSpace(e.Start), "opens already built");
            First("a block", e => e.Stock.Any(s => !IsGateOrRegister(s.Part)), "block");
            First("a period", e => e.Period > 0, "period");
        }

        /// <summary>
        /// An answer left free where others are asked -- an x among an output's values -- comes after the exercise that teaches
        /// it (CLAUDE.md, "A don't-care is written one of two ways"). An output free on every row is no such lesson: it is left
        /// alone, as route the bit's second output is.
        /// </summary>
        [Test]
        public void AnAnswerLeftFree_ComesAfterTheDontCareExercise()
        {
            List<Exercise> run = ExerciseFiles.InOrder();
            int dontCare = run.FindIndex(e => e.Id == "dont-care");
            Assert.GreaterOrEqual(dontCare, 0, "sanity: the don't-care exercise is here");
            Assert.IsTrue(LeavesAnAnswerFree(run[dontCare]), "sanity: the don't-care exercise leaves answers free itself");
            for (int i = 0; i < dontCare; i++)
                Assert.IsFalse(LeavesAnAnswerFree(run[i]), $"{run[i].Id} leaves an answer free, before the don't-care exercise teaches it");
        }

        /// <summary>
        /// Whether <paramref name="exercise"/> leaves an answer free, either of the two ways: an x among an output's values,
        /// or a combination left out of a combinational test of two to four input bits, which could have listed every one.
        /// A clocked test is a sequence, not a table, and leaves combinations out as a matter of course.
        /// </summary>
        private static bool LeavesAnAnswerFree(Exercise exercise)
        {
            foreach (ExerciseSignal output in exercise.Outputs)
            {
                int free = output.Values.Count(v => !v.IsKnown);
                if (free > 0 && free < output.Values.Count)
                    return true;
            }

            int bits = exercise.Inputs.Sum(i => i.Width);
            if (exercise.Clocked || bits < 2 || bits > 4)
                return false;
            var combinations = new HashSet<string>();
            for (int v = 0; v < exercise.Inputs[0].Values.Count; v++)
                combinations.Add(string.Join(",", exercise.Inputs.Select(i => i.Values[v].Format(SystemVerilog.Radix.Binary))));
            return combinations.Count < 1 << bits;
        }

        /// <summary>
        /// The chapters about circuits that forget come before those about circuits that remember, and do not interleave:
        /// nothing before chapter <see cref="FirstClockedChapter"/> has a clock or a register, and everything in it and the
        /// chapter after does. The last chapter, timing's own, may have either.
        /// </summary>
        [Test]
        public void TheCombinationalChapters_ComeBeforeTheClockedOnes()
        {
            foreach (Exercise exercise in ExerciseFiles.InOrder())
            {
                bool remembers = exercise.Clocked || exercise.Stock.Any(s => s.Part == PartCount.Register && s.Most != 0);
                if (exercise.Chapter < FirstClockedChapter)
                    Assert.IsFalse(remembers, $"{exercise.Id}, in chapter {exercise.Chapter}, has a clock or a register before chapter {FirstClockedChapter}");
                else if (exercise.Chapter <= FirstClockedChapter + 1)
                    Assert.IsTrue(exercise.Clocked, $"{exercise.Id}, in chapter {exercise.Chapter}, has no clock");
            }
        }

        /// <summary>Where the circuits that remember begin: flip-flops and registers.</summary>
        private const int FirstClockedChapter = 7;

        /// <summary>
        /// A circuit an exercise opens on fails for the reason its goal gives: where the goal says it is right, every value is
        /// right and it is too slow -- settling too late, or needing too long a period; otherwise it is wrong in its values.
        /// </summary>
        [TestCaseSource(nameof(Ids))]
        public void EveryStartingCircuit_FailsForTheReasonItsGoalGives(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            if (string.IsNullOrWhiteSpace(exercise.Start))
                Assert.Pass("no starting circuit");

            Grade grade = ExerciseGrader.Grade(exercise, exercise.ToProject(new List<string>()));
            bool right = grade.Failures.Count == 0;
            bool saysRight = exercise.Goal.Contains("and right") || exercise.Goal.Contains("the right thing");
            Assert.AreEqual(saysRight, right, right
                ? $"{id}'s circuit reads every value right, and its goal has to say so: only its timing is wrong"
                : $"{id}'s goal says its circuit is right, and it reads {grade.Failures.Count} values wrong");
            if (right)
                Assert.IsFalse(grade.SettleMet && grade.PeriodMet, $"{id}'s circuit is right and in time");
        }

        /// <summary>
        /// The words of 4.x's token game are not 5.0's: a goal or a hint never says tick, token, source, sink or bin. A wire
        /// carries a signal, an input drives it, and an output is read.
        /// </summary>
        [TestCaseSource(nameof(Ids))]
        public void NoGoalOrHint_SpeaksOfTheTokenGame(string id)
        {
            Exercise exercise = ExerciseFiles.Load(id);
            var old = new System.Text.RegularExpressions.Regex(@"\b(ticks?|tokens?|sources?|sinks?|bins?)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (string text in new[] { exercise.Goal, exercise.Hint })
                Assert.IsFalse(old.IsMatch(text), $"{id} says '{old.Match(text).Value}': {text}");
        }

        private static bool IsGateOrRegister(string part) =>
            part == PartCount.Register || Enum.TryParse(part, ignoreCase: true, out Sim.GateKind kind) && PartCount.Of(kind) == part;
    }
}
