using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>What a line of the TIMING tab is: a fact about the whole design, a path that can be picked, or why there is none.</summary>
    public enum TimingLineKind { Fact, Path, Problem }

    /// <summary>
    /// One line of the TIMING tab (Part 14b): what it is called, what it says, its numbers, and -- for a path -- the signals
    /// it runs through, which is what the canvas lights when it is picked.
    /// </summary>
    public sealed class TimingLine
    {
        internal TimingLine(TimingLineKind kind, string label, string text, string numbers, IReadOnlyList<string> signals = null)
        {
            Kind = kind;
            Label = label ?? "";
            Text = text ?? "";
            Numbers = numbers ?? "";
            Signals = signals ?? Array.Empty<string>();
        }

        public TimingLineKind Kind { get; }

        /// <summary>The left column, in capitals: CRITICAL PATH, MINIMUM PERIOD, GATE COUNT, PATHS; empty for a path of the list.</summary>
        public string Label { get; }

        /// <summary>What it says: a path's ends and the gates along it, a period's sum, the gates by kind.</summary>
        public string Text { get; }

        /// <summary>Its delays, in the project's unit: <c>rise 6 · fall 5 ps</c>.</summary>
        public string Numbers { get; }

        /// <summary>
        /// The signals along it, start first, each named from the design's top -- <c>a</c>, <c>h1.s</c>, <c>cout</c> -- so a
        /// drawing of any instance can find its own; empty for a line that is not a path.
        /// </summary>
        public IReadOnlyList<string> Signals { get; }

        /// <summary>Whether picking it lights something on the canvas.</summary>
        public bool CanPick => Signals.Count > 0;

        public override string ToString() => (Label + " " + Text + " " + Numbers).Trim();
    }

    /// <summary>
    /// The TIMING tab (Part 14b): a document's timing as the course reckons it, read from <see cref="TimingAnalysis"/> into
    /// lines -- the critical path, and for a design with registers the minimum period and the maximum frequency it gives,
    /// the gate count, and every path from an input to an output, the slowest first. Worked out again only once the
    /// document has changed (<see cref="WorkbenchSession.Timing"/>), so asking every frame costs nothing between edits.
    /// </summary>
    /// <remarks>
    /// **The design alone, never its testbench**, and every path is named from the design's top, so the critical path picked
    /// in the top's drawing is the same path inside a block: the drawing of each instance lights what of it is its own
    /// (<see cref="TimingLight"/>).
    /// </remarks>
    public sealed class TimingView
    {
        /// <summary>How many paths the list shows: the slowest, and a line saying how many are left out.</summary>
        public const int MostPathsListed = 200;

        private TimingView(Project project, TimingAnalysis analysis, IReadOnlyList<TimingLine> lines, int critical)
        {
            Project = project;
            Analysis = analysis;
            Lines = lines;
            Critical = critical;
        }

        /// <summary>The timing of nothing: what there is before a document.</summary>
        public static readonly TimingView Empty = new TimingView(null, null, Array.Empty<TimingLine>(), -1);

        /// <summary>The document it was worked out from.</summary>
        public Project Project { get; }

        /// <summary>The analysis read; null for <see cref="Empty"/>.</summary>
        public TimingAnalysis Analysis { get; }

        public IReadOnlyList<TimingLine> Lines { get; }

        /// <summary>The critical path's line, picked until another is: -1 when the design has no path at all.</summary>
        public int Critical { get; }

        /// <summary>The timing of <paramref name="project"/>.</summary>
        public static TimingView Of(Project project)
        {
            if (project == null)
                return Empty;

            TimingAnalysis analysis = TimingAnalysis.Of(project);
            var lines = new List<TimingLine>();
            if (!analysis.Succeeded)
            {
                lines.Add(new TimingLine(TimingLineKind.Problem, "", analysis.Problem, ""));
                return new TimingView(project, analysis, lines, -1);
            }

            string unit = project.Settings?.TimeUnit ?? TimeUnits.Default;
            TimingGraph graph = analysis.Graph;
            TimingReport timing = analysis.Timing;
            PathReport paths = analysis.Paths;
            string top = graph.Design.Scopes.Count > 0 ? graph.Design.Scopes[0].Path : project.Top;
            string Name(int node) => Relative(graph.NameOf(node), top);

            int critical = -1;
            if (graph.Registers.Count > 0)
            {
                // A design with registers is clocked: the critical path is the slowest from one register to the next.
                if (timing.Critical is StaticPath slowest)
                {
                    lines.Add(new TimingLine(TimingLineKind.Fact, "MINIMUM PERIOD", Sum(slowest, unit), Time(slowest.Total, unit)));
                    lines.Add(new TimingLine(TimingLineKind.Fact, "MAXIMUM FREQUENCY", "one over the minimum period", Frequency(slowest.Total, unit)));
                    critical = lines.Count;
                    lines.Add(StaticLine("CRITICAL PATH", slowest, Name, unit));
                }
                else
                {
                    lines.Add(new TimingLine(TimingLineKind.Fact, "MINIMUM PERIOD", "No path runs from one register to another, so nothing here limits the clock.", ""));
                }

                if (timing.InputToRegister is StaticPath inward)
                {
                    if (critical < 0)
                        critical = lines.Count;
                    lines.Add(StaticLine("INPUT TO REGISTER", inward, Name, unit));
                }
                if (timing.RegisterToOutput is StaticPath outward)
                {
                    if (critical < 0)
                        critical = lines.Count;
                    lines.Add(StaticLine("REGISTER TO OUTPUT", outward, Name, unit));
                }
            }
            else if (paths.Worst is TimingPath worst)
            {
                critical = lines.Count;
                lines.Add(PathLine("CRITICAL PATH", worst, Name, unit));
            }
            else
            {
                lines.Add(new TimingLine(TimingLineKind.Fact, "CRITICAL PATH", "No input reaches an output through any gate.", ""));
            }

            lines.Add(new TimingLine(TimingLineKind.Fact, "GATE COUNT", GateCount(graph), ""));

            foreach (IReadOnlyList<string> loop in graph.Loops)
            {
                var names = new List<string>();
                foreach (string signal in loop)
                    names.Add(Relative(signal, top));
                lines.Add(new TimingLine(TimingLineKind.Problem, "LOOP",
                    string.Join(", ", names) + ": a change goes round them for ever, so no path through them is counted.", ""));
            }

            if (paths.Paths.Count > 0)
            {
                string found = paths.Found.ToString("N0", CultureInfo.InvariantCulture) + (paths.Found == 1 ? " path" : " paths")
                    + " from an input to an output, the slowest first"
                    + (paths.Capped ? "; there are more, and only the first " + PathDelays.MaxPaths.ToString("N0", CultureInfo.InvariantCulture) + " were reckoned" : "")
                    + (paths.Paths.Count < paths.Found ? ", the same gates in the same order counted once" : "");
                lines.Add(new TimingLine(TimingLineKind.Fact, "PATHS", found, ""));
                int listed = Math.Min(paths.Paths.Count, MostPathsListed);
                for (int i = 0; i < listed; i++)
                    lines.Add(PathLine("", paths.Paths[i], Name, unit));
                if (listed < paths.Paths.Count)
                    lines.Add(new TimingLine(TimingLineKind.Fact, "",
                        (paths.Paths.Count - listed).ToString("N0", CultureInfo.InvariantCulture) + " faster ones are not listed.", ""));
            }
            else if (graph.Registers.Count > 0)
            {
                lines.Add(new TimingLine(TimingLineKind.Fact, "PATHS", "No path runs from an input to an output without a register in its way.", ""));
            }

            return new TimingView(project, analysis, lines, critical);
        }

        /// <summary>
        /// The line a picked one is in a new view: the same label and text, as an edit that changed nothing on it leaves it --
        /// or the new view's critical path, when it is gone.
        /// </summary>
        public int Find(TimingLine line)
        {
            if (line != null)
                for (int i = 0; i < Lines.Count; i++)
                    if (Lines[i].CanPick && Lines[i].Label == line.Label && Lines[i].Text == line.Text)
                        return i;
            return Critical;
        }

        // -----------------------------------------------------------------
        // The lines
        // -----------------------------------------------------------------

        private static TimingLine PathLine(string label, TimingPath path, Func<int, string> name, string unit)
        {
            var ends = new StringBuilder();
            for (int i = 0; i < path.Alike.Count && i < 3; i++)
            {
                if (i > 0)
                    ends.Append(", ");
                ends.Append(name(path.Alike[i].From)).Append(" → ").Append(name(path.Alike[i].To));
            }
            if (path.Alike.Count > 3)
                ends.Append(" and ").Append((path.Alike.Count - 3).ToString(CultureInfo.InvariantCulture)).Append(" more");

            string numbers = "rise " + Delay(path.Rising) + " · fall " + Delay(path.Falling) + " " + unit;
            return new TimingLine(TimingLineKind.Path, label, ends + "   " + Through(path.Steps), numbers, Signals(path.From, path.Arcs, name));
        }

        private static TimingLine StaticLine(string label, StaticPath path, Func<int, string> name, string unit)
        {
            string ends = name(path.From) + " → " + (path.Capture != null && path.Capture.Launches.Count > 0 ? name(path.Capture.Launches[0]) : name(path.To));
            var steps = new List<TimingArc>();
            foreach (TimingArc arc in path.Arcs)
                if (!arc.IsWire)
                    steps.Add(arc);
            return new TimingLine(TimingLineKind.Path, label, ends + "   " + Through(steps), Time(path.Total, unit), Signals(path.From, path.Arcs, name));
        }

        /// <summary>A minimum period as its sum: <c>clock to Q 2 + logic 1 + setup 3</c>.</summary>
        private static string Sum(StaticPath path, string unit)
        {
            long tcq = path.Launch?.Timing.Tcq ?? 0, tsetup = path.Capture?.Timing.Tsetup ?? 0;
            return "clock to Q " + Number(tcq) + " + logic " + Number(path.Logic) + " + setup " + Number(tsetup) + " " + unit;
        }

        private static string[] Signals(int from, IReadOnlyList<TimingArc> arcs, Func<int, string> name)
        {
            var signals = new string[arcs.Count + 1];
            signals[0] = name(from);
            for (int i = 0; i < arcs.Count; i++)
                signals[i + 1] = name(arcs[i].To);
            return signals;
        }

        /// <summary>What a path passes through, in order: <c>through XOR2, AND2, OR2</c>, or nothing for a bare wire.</summary>
        private static string Through(IEnumerable<TimingArc> steps)
        {
            var through = new StringBuilder();
            foreach (TimingArc arc in steps)
            {
                through.Append(through.Length == 0 ? "through " : ", ");
                through.Append(arc.Gate is GateKind kind ? Primitive(kind, arc.Inputs) : "logic");
            }
            return through.Length == 0 ? "a wire" : through.ToString();
        }

        private static string Primitive(GateKind kind, int inputs)
        {
            string name = kind.ToString().ToUpperInvariant();
            return inputs > 1 && kind != GateKind.Bufif0 && kind != GateKind.Bufif1 && kind != GateKind.Notif0 && kind != GateKind.Notif1
                ? name + inputs.ToString(CultureInfo.InvariantCulture) : name;
        }

        /// <summary>The gates by kind, the most first: <c>5 gates: 2 XOR2, 2 AND2, 1 OR2</c>; a block's gates are its own.</summary>
        private static string GateCount(TimingGraph graph)
        {
            if (graph.Gates == 0)
                return "No gates: what it does is written as logic, not built of gates.";
            var kinds = new List<KeyValuePair<string, int>>(graph.GateCounts);
            kinds.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            var text = new StringBuilder();
            text.Append(graph.Gates.ToString(CultureInfo.InvariantCulture)).Append(graph.Gates == 1 ? " gate: " : " gates: ");
            for (int i = 0; i < kinds.Count; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(kinds[i].Value.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(kinds[i].Key.ToUpperInvariant());
            }
            return text.ToString();
        }

        private static string Delay(long delay) => delay == PathDelays.Never ? "never" : Number(delay);

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Time(long value, string unit) => Number(value) + " " + unit;

        /// <summary><paramref name="name"/> without <paramref name="top"/> in front: <c>full_adder.h1.s</c> is <c>h1.s</c>.</summary>
        public static string Relative(string name, string top) =>
            name.StartsWith(top + ".", StringComparison.Ordinal) ? name.Substring(top.Length + 1) : name;

        /// <summary>
        /// The frequency a clock of <paramref name="period"/> runs at, in the unit that keeps it over 1: a period of 6 ns is
        /// <c>166.7 MHz</c>. Nothing for a period of 0, which no clock has.
        /// </summary>
        public static string Frequency(long period, string unit)
        {
            if (period <= 0)
                return "any: nothing takes time";
            int exponent = -3 * Math.Max(0, IndexOf(unit));   // s 0, ms -3, us -6, ns -9, ps -12, fs -15
            double hertz = 1.0 / (period * Math.Pow(10, exponent));
            string[] names = { "Hz", "kHz", "MHz", "GHz", "THz", "PHz" };
            int step = 0;
            while (step < names.Length - 1 && hertz >= 1000.0)
            {
                hertz /= 1000.0;
                step++;
            }
            string digits = hertz >= 100 ? hertz.ToString("0.#", CultureInfo.InvariantCulture) : hertz.ToString("0.##", CultureInfo.InvariantCulture);
            return digits + " " + names[step];
        }

        private static int IndexOf(string unit)
        {
            for (int i = 0; i < TimeUnits.All.Count; i++)
                if (TimeUnits.All[i] == unit)
                    return i;
            return -1;
        }
    }
}
