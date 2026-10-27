using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>A check that failed: which output, when, what it read and what was wanted -- as the testbench printed it.</summary>
    public sealed class CheckFailure
    {
        public CheckFailure(string port, long time, string got, string wanted)
        {
            Port = port;
            Time = time;
            Got = got;
            Wanted = wanted;
        }

        public string Port { get; }
        public long Time { get; }

        /// <summary>The value read, and the value wanted, in binary as the testbench says them.</summary>
        public string Got { get; }
        public string Wanted { get; }

        public override string ToString() => $"{Port} at {Time}: {Got}, wanted {Wanted}";
    }

    /// <summary>How a design did at an exercise (Part 15b): what failed, and how much it took.</summary>
    public sealed class Grade
    {
        internal Grade(IReadOnlyList<string> problems, IReadOnlyList<string> faults, IReadOnlyList<CheckFailure> failures, int checks,
            IReadOnlyDictionary<string, int> parts, int gates, long? settle, long? period, bool settleMet, bool periodMet)
        {
            Problems = problems;
            Faults = faults;
            Failures = failures;
            Checks = checks;
            Parts = parts;
            Gates = gates;
            Settle = settle;
            MinimumPeriod = period;
            SettleMet = settleMet;
            PeriodMet = periodMet;
        }

        /// <summary>Why it could not be run at all: it does not compile, or does not elaborate. Empty when it ran.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>The rules it breaks under the exercise's library: a part it does not stock, one too many, a constant.</summary>
        public IReadOnlyList<string> Faults { get; }

        /// <summary>Every check that failed, in the order the testbench made them.</summary>
        public IReadOnlyList<CheckFailure> Failures { get; }

        /// <summary>How many checks the test made.</summary>
        public int Checks { get; }

        /// <summary>What it has of each part (<see cref="PartCounts"/>), a block the exercise stocks counted whole.</summary>
        public IReadOnlyDictionary<string, int> Parts { get; }

        /// <summary>How many gates, of every kind, a bit at a time: the solved card's count.</summary>
        public int Gates { get; }

        /// <summary>The slowest path from an input to an output, by the static timing (Part 14a); null when it could not be reckoned.</summary>
        public long? Settle { get; }

        /// <summary>The slowest register-to-register path, clock to Q and setup included; null with no register.</summary>
        public long? MinimumPeriod { get; }

        /// <summary>Whether it settles in time for a timing exercise, and runs at the period for a pipelining one; true when not asked.</summary>
        public bool SettleMet { get; }
        public bool PeriodMet { get; }

        public bool Solved => Problems.Count == 0 && Faults.Count == 0 && Failures.Count == 0 && SettleMet && PeriodMet;
    }

    /// <summary>
    /// Grades a design at an exercise (Part 15b): runs it under the exercise's own test and library and says what failed --
    /// the self-checking testbench's own lines, so the grade is what the student reads in the TRANSCRIPT and what the
    /// exported testbench says in ModelSim (<c>SelfCheckOracleTests</c>) -- and what it took: its parts, its gates, how fast
    /// it settles and how fast it can be clocked.
    /// </summary>
    public static class ExerciseGrader
    {
        private static readonly Regex Failed = new Regex(@"^FAIL (\S+) at (\d+): (\S+), wanted (\S+)$", RegexOptions.CultureInvariant);

        public static Grade Grade(Exercise exercise, Project project)
        {
            if (exercise == null)
                throw new ArgumentNullException(nameof(exercise));
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            // The exercise's test and library, whatever the document has come to say: the test is the exercise's to set.
            GateLibrary library = exercise.Library();
            Project graded = project with
            {
                Stimulus = exercise.Test(),
                Bench = null,
                Settings = (project.Settings ?? new ProjectSettings()) with { Library = library },
            };

            var faults = new List<string>();
            foreach (BoardDiagnostic diagnostic in BoardRules.Check(graded))
                if (diagnostic.IsError)
                    faults.Add(diagnostic.Message);

            Dictionary<string, int> parts = PartCounts.Of(graded, library.Counted());
            int gates = 0;
            foreach (KeyValuePair<string, int> part in parts)
                if (Enum.TryParse(part.Key, ignoreCase: true, out GateKind kind) && PartCount.Of(kind) == part.Key)
                    gates += part.Value;

            var problems = new List<string>();
            var failures = new List<CheckFailure>();
            CompiledProject compiled = BoardCompiler.Compile(graded);
            if (!compiled.Succeeded)
            {
                foreach (BoardDiagnostic d in compiled.Diagnostics)
                    if (d.IsError)
                        problems.Add(d.Message);
            }
            else
            {
                Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
                if (elaboration.HasErrors)
                {
                    foreach (Diagnostic d in elaboration.Diagnostics)
                        problems.Add(d.ToString());
                }
                else
                {
                    var sim = new Simulator(elaboration.Design);
                    sim.RunTo(graded.Stimulus.StopTime + 1);
                    var asked = new HashSet<(string, long)>();
                    foreach (Check check in graded.Stimulus.Checks)
                        asked.Add((check.Port, check.Time));
                    foreach (SimMessage message in sim.History.Messages)
                    {
                        Match failed = Failed.Match(message.Text ?? "");
                        if (failed.Success && long.TryParse(failed.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long time)
                            && asked.Contains((failed.Groups[1].Value, time)))
                            failures.Add(new CheckFailure(failed.Groups[1].Value, time, failed.Groups[3].Value, failed.Groups[4].Value));
                    }
                }
            }

            long? settle = null, period = null;
            if (problems.Count == 0)
            {
                TimingAnalysis timing = TimingAnalysis.Of(graded);
                if (timing.Succeeded)
                {
                    settle = timing.Paths.Worst?.Worst;
                    period = timing.Timing.MinimumPeriod;
                }
            }

            bool settleMet = exercise.SettleBy < 0 || (settle.HasValue && settle.Value <= exercise.SettleBy);
            bool periodMet = exercise.Period < 0 || (period.HasValue && period.Value <= exercise.Period);
            return new Grade(problems, faults, failures, graded.Stimulus.Checks.Count, parts, gates, settle, period, settleMet, periodMet);
        }
    }
}
