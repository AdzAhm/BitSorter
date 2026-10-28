using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The exercises as the workbench shows them (Part 15d), Unity-free: numbered in the order they are played, grouped by
    /// chapter into the EXERCISES page's columns, the next one to do -- and what a CHECK says, in the chip and in the
    /// TRANSCRIPT. Every word is made from the exercise and its grade, never restated.
    /// </summary>
    public static class ExerciseRun
    {
        /// <summary>What a chapter is called, as its heading on the EXERCISES page says it: the course's order (Part 15c).</summary>
        public static string ChapterName(int chapter)
        {
            switch (chapter)
            {
                case 1: return "GATES";
                case 2: return "K-MAPS";
                case 3: return "NAND AND NOR";
                case 4: return "DELAY AND BUSES";
                case 5: return "BUILDING BLOCKS";
                case 6: return "ADDERS";
                case 7: return "FLIP-FLOPS AND REGISTERS";
                case 8: return "COUNTERS AND STATE MACHINES";
                case 9: return "CRITICAL PATH AND PIPELINING";
                default: return "CHAPTER " + Number(chapter);
            }
        }

        /// <summary>Where <paramref name="id"/> is in <paramref name="run"/>, counted from 1; 0 when it is not there.</summary>
        public static int NumberOf(IReadOnlyList<Exercise> run, string id)
        {
            for (int i = 0; i < (run?.Count ?? 0); i++)
                if (run[i].Id == id)
                    return i + 1;
            return 0;
        }

        /// <summary>The exercise <paramref name="id"/> in <paramref name="run"/>, or null.</summary>
        public static Exercise Find(IReadOnlyList<Exercise> run, string id)
        {
            int number = NumberOf(run, id);
            return number > 0 ? run[number - 1] : null;
        }

        /// <summary>How an exercise is headed: its number and its title, <c>12  HALF ADDER</c>.</summary>
        public static string Heading(int number, Exercise exercise) =>
            Number(number) + "  " + (exercise?.Title ?? "").ToUpperInvariant();

        /// <summary>How many of <paramref name="run"/> are solved.</summary>
        public static int SolvedCount(IReadOnlyList<Exercise> run, Func<string, bool> solved)
        {
            int count = 0;
            foreach (Exercise exercise in run ?? Array.Empty<Exercise>())
                if (solved(exercise.Id))
                    count++;
            return count;
        }

        /// <summary>
        /// The exercise to do next: the first unsolved one after <paramref name="after"/>, or, with none after it or no
        /// <paramref name="after"/>, the first unsolved one of all. Null when every one is solved.
        /// </summary>
        public static Exercise Next(IReadOnlyList<Exercise> run, Func<string, bool> solved, string after = null)
        {
            int from = NumberOf(run, after);
            for (int i = from; i < (run?.Count ?? 0); i++)
                if (!solved(run[i].Id))
                    return run[i];
            for (int i = 0; i < Math.Min(from, run?.Count ?? 0); i++)
                if (!solved(run[i].Id))
                    return run[i];
            return null;
        }

        /// <summary>The chapters of <paramref name="run"/>, in order, each with its exercises in order.</summary>
        public static IReadOnlyList<(int Chapter, IReadOnlyList<Exercise> Exercises)> Chapters(IReadOnlyList<Exercise> run)
        {
            var chapters = new List<(int, IReadOnlyList<Exercise>)>();
            List<Exercise> current = null;
            int chapter = int.MinValue;
            foreach (Exercise exercise in run ?? Array.Empty<Exercise>())
            {
                if (current == null || exercise.Chapter != chapter)
                {
                    chapter = exercise.Chapter;
                    current = new List<Exercise>();
                    chapters.Add((chapter, current));
                }
                current.Add(exercise);
            }
            return chapters;
        }

        /// <summary>
        /// The chapters split into <paramref name="columns"/> runs, in order, as evenly as the page's columns can hold them:
        /// the tallest column as short as it can be, a heading counted as <paramref name="headingRows"/> of an exercise's rows.
        /// Each column is the chapters' places in <see cref="Chapters"/>.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<int>> Columns(IReadOnlyList<(int Chapter, IReadOnlyList<Exercise> Exercises)> chapters, int columns,
            double headingRows = 1.5)
        {
            int n = chapters?.Count ?? 0;
            columns = Math.Max(1, Math.Min(columns, Math.Max(1, n)));
            var height = new double[n + 1];
            for (int i = 0; i < n; i++)
                height[i + 1] = height[i] + chapters[i].Exercises.Count + headingRows;

            // best[k, i]: the tallest column, the first i chapters in k columns; cut[k, i] where the last of them starts.
            var best = new double[columns + 1, n + 1];
            var cut = new int[columns + 1, n + 1];
            for (int i = 0; i <= n; i++)
                best[1, i] = height[i];
            for (int k = 2; k <= columns; k++)
                for (int i = 0; i <= n; i++)
                {
                    best[k, i] = double.MaxValue;
                    for (int j = 0; j <= i; j++)
                    {
                        double tallest = Math.Max(best[k - 1, j], height[i] - height[j]);
                        if (tallest < best[k, i])
                        {
                            best[k, i] = tallest;
                            cut[k, i] = j;
                        }
                    }
                }

            var made = new List<IReadOnlyList<int>>(columns);
            int end = n;
            for (int k = columns; k >= 1; k--)
            {
                int start = k == 1 ? 0 : cut[k, end];
                var column = new List<int>();
                for (int i = start; i < end; i++)
                    column.Add(i);
                made.Insert(0, column);
                end = start;
            }
            return made;
        }

        // -----------------------------------------------------------------
        // What CHECK says
        // -----------------------------------------------------------------

        /// <summary>What a design took, in the solved card's words: <c>2 gates, settling in 1 ns</c>.</summary>
        public static string Took(int gates, long? settle, string unit) =>
            Count(gates, "gate") + (settle.HasValue ? ", settling in " + Number(settle.Value) + " " + unit : "");

        /// <summary>
        /// What <paramref name="grade"/>'s design took at <paramref name="exercise"/>: the blocks it gives, each counted whole,
        /// and its own gates -- <c>4 FA</c>, not <c>0 gates</c> -- how fast it settles, and for a pipelining exercise the
        /// period it can be clocked at (the #88 review).
        /// </summary>
        public static string Took(Exercise exercise, Grade grade, string unit)
        {
            var said = new List<string>();
            foreach (PartCount count in exercise.Stock)
                if (count.Part != PartCount.Register && !Enum.TryParse(count.Part, ignoreCase: true, out Sim.GateKind _)
                    && grade.Parts.TryGetValue(count.Part, out int blocks) && blocks > 0)
                    said.Add(Number(blocks) + " " + count.Part);
            if (grade.Gates > 0 || said.Count == 0)
                said.Add(Count(grade.Gates, "gate"));
            string took = string.Join(" and ", said);
            if (grade.Settle.HasValue)
                took += ", settling in " + Number(grade.Settle.Value) + " " + unit;
            if (exercise.Period > 0 && grade.MinimumPeriod.HasValue)
                took += ", at a period of " + Number(grade.MinimumPeriod.Value) + " " + unit;
            return took;
        }

        /// <summary>The one line the chip says after a CHECK: solved, or the first reason it is not.</summary>
        public static string Verdict(Exercise exercise, Grade grade, string unit)
        {
            if (grade.Problems.Count > 0)
                return "CHECK could not run it through: " + grade.Problems[0];
            if (grade.Faults.Count > 0)
                return "CHECK: it is not made of what the exercise gives -- " + grade.Faults[0];
            if (grade.Checks == 0)
                return "CHECK made no checks: the test did not reach them";
            if (grade.Failures.Count > 0)
                return "CHECK: " + Number(grade.Failures.Count) + " of " + Count(grade.Checks, "check") + " failed. " +
                       "The TRANSCRIPT has each one: a click on it goes to its moment";
            if (!grade.SettleMet)
                return "CHECK: every value is right, but it settles in " + Number(grade.Settle ?? 0) + " " + unit +
                       ", and must within " + Number(exercise.SettleBy) + ". The TIMING tab shows its slowest path";
            if (!grade.PeriodMet)
                return "CHECK: every value is right, but it needs a period of " + Number(grade.MinimumPeriod ?? 0) + " " + unit +
                       ", and must run at " + Number(exercise.Period) + ". The TIMING tab shows its slowest path";
            return "CHECK: solved, with " + Took(exercise, grade, unit);
        }

        /// <summary>
        /// What a CHECK puts at the head of the TRANSCRIPT: the verdict, then what stopped the run and every rule broken. Each
        /// failed check is already there, at its moment: the exercise's own self-checking testbench prints it as the run
        /// reaches it (<c>FAIL sum at 14: 1, wanted 0</c>), and a copy here put every one in the TRANSCRIPT twice (the 15d shots).
        /// </summary>
        public static IReadOnlyList<TranscriptLine> Lines(Exercise exercise, Grade grade, string unit)
        {
            var lines = new List<TranscriptLine>();
            lines.Add(new TranscriptLine(grade.Solved ? TranscriptSeverity.Info : TranscriptSeverity.Error, Verdict(exercise, grade, unit), -1));
            foreach (string problem in grade.Problems)
                lines.Add(new TranscriptLine(TranscriptSeverity.Error, problem, -1));
            foreach (string fault in grade.Faults)
                lines.Add(new TranscriptLine(TranscriptSeverity.Error, fault, -1));
            return lines;
        }

        private static string Count(int count, string what) => Number(count) + " " + what + (count == 1 ? "" : "s");

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
