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

        /// <summary>
        /// Why it could not be run, or not to its end: it does not compile or elaborate, or its run stopped before the test had
        /// made every check -- a loop that never settles, a <c>$finish</c>. Empty when the test ran through.
        /// </summary>
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

        /// <summary>
        /// The shortest period it can be clocked at: its slowest path between two clock edges -- register to register, input to
        /// register or register to output -- clock to Q and setup included; null with no register.
        /// </summary>
        public long? MinimumPeriod { get; }

        /// <summary>Whether it settles in time for a timing exercise, and runs at the period for a pipelining one; true when not asked.</summary>
        public bool SettleMet { get; }
        public bool PeriodMet { get; }

        /// <summary>Whether it is solved: run through, breaking no rule, every check made and passed, in time and at the period.</summary>
        public bool Solved => Problems.Count == 0 && Faults.Count == 0 && Checks > 0 && Failures.Count == 0 && SettleMet && PeriodMet;
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
        private static readonly Regex Summary = new Regex(@"^(\d+) of (\d+) checks failed$", RegexOptions.CultureInvariant);

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
            // Written code's at its line (Part 15e), as the code editor marks it.
            foreach (BoardDiagnostic diagnostic in BoardRules.Check(graded))
                if (diagnostic.IsError)
                    faults.Add(diagnostic.Line > 0 ? $"{diagnostic.Module}, line {diagnostic.Line}: {diagnostic.Message}" : diagnostic.Message);

            // Written code is a way past the stock, which counts drawn parts: an exercise answered in code is held to its
            // gates by the gate-level rules (Part 15e), and one that is not takes none (the #85 review).
            foreach (Module module in graded.Modules)
                if (module.IsWritten && !exercise.Code)
                    faults.Add($"{module.Name} is written, and this exercise is answered by drawing it.");

            Dictionary<string, int> parts = PartCounts.Of(graded, library.Counted());
            int gates = 0;
            foreach (KeyValuePair<string, int> part in parts)
                if (Enum.TryParse(part.Key, ignoreCase: true, out GateKind kind) && PartCount.Of(kind) == part.Key)
                    gates += part.Value;

            var problems = new List<string>();
            var failures = new List<CheckFailure>();

            // A design that breaks a rule is not run: the rules are the reason, already said, and it would not compile.
            CompiledProject compiled = faults.Count > 0 ? null : BoardCompiler.Compile(graded);
            if (compiled != null && !compiled.Succeeded)
            {
                foreach (BoardDiagnostic d in compiled.Diagnostics)
                    if (d.IsError)
                        problems.Add(d.Message);
            }
            else if (compiled != null)
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

                    // The testbench's own lines: each check that failed, and the line that closes the test -- which says it
                    // ran to its end. A run stopped before it -- a loop that never settles, a $finish -- made only some of its
                    // checks, and is not passed for the ones it never made (the #85 review).
                    bool closed = false;
                    var said = new List<string>();
                    foreach (SimMessage message in sim.History.Messages)
                    {
                        string text = message.Text ?? "";
                        Match failed = Failed.Match(text);
                        Match summary = Summary.Match(text);
                        if (failed.Success && long.TryParse(failed.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long time)
                            && asked.Contains((failed.Groups[1].Value, time)))
                            failures.Add(new CheckFailure(failed.Groups[1].Value, time, failed.Groups[3].Value, failed.Groups[4].Value));
                        else if (summary.Success && summary.Groups[2].Value == asked.Count.ToString(CultureInfo.InvariantCulture)
                            && summary.Groups[1].Value == failures.Count.ToString(CultureInfo.InvariantCulture))
                            closed = true;
                        else if (!text.StartsWith("$stop", StringComparison.Ordinal))
                            said.Add(text);
                    }

                    if (!closed && asked.Count > 0)
                        problems.Add("The test stopped before it had made every check" + (said.Count > 0 ? ": " + said[said.Count - 1] : "."));
                }
            }

            long? settle = null, period = null;
            if (problems.Count == 0 && faults.Count == 0)
            {
                TimingAnalysis timing = TimingAnalysis.Of(graded);
                if (timing.Succeeded)
                {
                    settle = timing.Paths.Worst?.Worst;
                    period = ClockedPeriod(timing.Timing);

                    // Written code (Part 15e) is counted from the design it builds: what the stock counts of drawn parts
                    // never sees. Over the stock, it is a fault as a drawing over it is.
                    if (HasWritten(graded))
                    {
                        parts = WithWritten(graded, library, parts, timing);
                        gates = 0;
                        foreach (KeyValuePair<string, int> part in parts)
                            if (Enum.TryParse(part.Key, ignoreCase: true, out GateKind kind) && PartCount.Of(kind) == part.Key)
                                gates += part.Value;
                        foreach (PartCount count in library.Counts)
                            if (count.Most >= 0 && parts.TryGetValue(count.Part, out int n) && n > count.Most)
                                faults.Add($"The exercise stocks {(count.Most == 0 ? "no " + count.Said : count.Most + " " + count.Said)}, and the design has {n}.");
                    }
                }
                else if (HasWritten(graded))
                {
                    // Code is counted from the design it builds, and this one was not reckoned: it is not passed uncounted
                    // (the #89 review: a signal too wide to time let any number of gates through).
                    faults.Add("Its gates could not be counted against what the exercise gives: " + timing.Problem);
                }
            }

            bool settleMet = exercise.SettleBy < 0 || (settle.HasValue && settle.Value <= exercise.SettleBy);
            bool periodMet = exercise.Period < 0 || (period.HasValue && period.Value <= exercise.Period);
            return new Grade(problems, faults, failures, graded.Stimulus.Checks.Count, parts, gates, settle, period, settleMet, periodMet);
        }

        /// <summary>Whether any module the design is made of is written, but the testbench.</summary>
        private static bool HasWritten(Project project)
        {
            foreach (Module module in project.Modules)
                if (module.IsWritten && module.Name != project.Bench)
                    return true;
            return false;
        }

        /// <summary>
        /// <paramref name="drawn"/> -- the parts the stock counts of the drawing (<see cref="PartCounts"/>) -- with the written
        /// code's added (Part 15e): every gate and register bit the design builds (<paramref name="timing"/>), less the drawn
        /// ones and those inside the blocks the exercise stocks, which count whole, an instance each, wherever they are placed.
        /// Worked out by what is left over, so a gate is never put down to the wrong scope by a port that joins two nets.
        /// </summary>
        private static Dictionary<string, int> WithWritten(Project project, GateLibrary library, Dictionary<string, int> drawn, TimingAnalysis timing)
        {
            HashSet<string> counted = library.Counted();
            var total = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(Dictionary<string, int> into, string part, int n) => into[part] = (into.TryGetValue(part, out int had) ? had : 0) + n;

            foreach (KeyValuePair<string, int> cell in timing.GateCounts)
                if (KindOf(cell.Key) is GateKind kind)
                    Add(total, PartCount.Of(kind), cell.Value);
            // A bit each, however many processes write a vector: a process launches every bit of what it writes, so four
            // writing a bit each of one vector counted sixteen (the #89 review).
            var registerBits = new HashSet<int>();
            foreach (TimingRegister register in timing.Graph.Registers)
                foreach (int bit in register.Launches)
                    registerBits.Add(bit);
            Add(total, PartCount.Register, registerBits.Count);

            // Each stocked block placed, at its outermost: drawn or written, it counts one, and its insides none.
            Design design = timing.Graph.Design;
            var instances = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int s = 0; s < design.Scopes.Count; s++)
            {
                ScopeInfo scope = design.Scopes[s];
                if (scope.Module == null || !counted.Contains(scope.Module) || project.Find(scope.Module) == null)
                    continue;
                bool inside = false;
                for (int up = scope.Parent; up >= 0 && !inside; up = design.Scopes[up].Parent)
                    inside = design.Scopes[up].Module != null && counted.Contains(design.Scopes[up].Module);
                if (!inside)
                    Add(instances, scope.Module, 1);
            }

            var made = new Dictionary<string, int>(drawn, StringComparer.Ordinal);
            var insides = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> block in instances)
            {
                made[block.Key] = block.Value;
                Module module = project.Find(block.Key);
                if (module == null || module.IsWritten)
                    continue;
                foreach (KeyValuePair<string, int> part in PartCounts.Of(project with { Top = block.Key }))
                    Add(insides, part.Key, part.Value * block.Value);
            }

            foreach (KeyValuePair<string, int> part in total)
            {
                int written = part.Value - (drawn.TryGetValue(part.Key, out int d) ? d : 0) - (insides.TryGetValue(part.Key, out int i) ? i : 0);
                if (written > 0)
                    made[part.Key] = (drawn.TryGetValue(part.Key, out int had) ? had : 0) + written;
            }
            return made;
        }

        /// <summary>The gate a timing count's key is of: <c>and2</c> an AND, <c>bufif1</c> a BUFIF1; null for none.</summary>
        private static GateKind? KindOf(string key)
        {
            GateKind? best = null;
            int length = 0;
            foreach (GateKind kind in (GateKind[])Enum.GetValues(typeof(GateKind)))
            {
                string name = PartCount.Of(kind);
                if (name.Length <= length || !key.StartsWith(name, StringComparison.Ordinal))
                    continue;
                bool digits = true;
                for (int c = name.Length; c < key.Length; c++)
                    digits &= char.IsDigit(key[c]);
                if (digits)
                {
                    best = kind;
                    length = name.Length;
                }
            }
            return best;
        }

        /// <summary>
        /// The period <paramref name="report"/>'s design can be clocked at, every path the clock times counted: register to
        /// register, and from an input to a register and from a register to an output, since an exercise's inputs change and
        /// its outputs are read a clock apart. Register to register alone let two registers at the output meet a period with
        /// the whole slow path still in front of them (the #87 review). Null with no register.
        /// </summary>
        private static long? ClockedPeriod(TimingReport report)
        {
            long? period = report.MinimumPeriod;
            foreach (StaticPath path in new[] { report.InputToRegister, report.RegisterToOutput })
                if (path != null && (period == null || path.Total > period.Value))
                    period = path.Total;
            return period;
        }
    }
}
