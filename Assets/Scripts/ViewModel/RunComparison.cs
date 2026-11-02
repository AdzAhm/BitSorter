using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// The run held to another simulator's of the same project (Part 15i-5, Ahmad's choice, 2026-11-02): COMPARE WITH MODELSIM
    /// reads the VCD an exported run.do has ModelSim write, matches its signals to the run's by their full names -- the
    /// testbench's, as both elaborate it -- and finds the first moment, and the signal, where the two differ; or says they
    /// agree on everything compared. Every signal of the run but a process's own variables, a memory's words, a string and
    /// a real, as the oracle compares them; up to the moment before the run's end, since a <c>$stop</c>'s last step is
    /// kept differently by the two (Docs/oracle-notes.md).
    ///
    /// **A start told apart from what follows.** ModelSim starts an <c>always</c> before an <c>initial</c> at time 0, where
    /// BitSorter and Icarus start the <c>initial</c> first -- the standard leaves it open -- so a reset set at time 0 may be
    /// seen at once in one and at the first clock in the other. A signal that differs from time 0 is said apart
    /// (<see cref="DifferAtStart"/>) and followed from the moment the two first agree on it: the first difference after
    /// that is <see cref="Time"/>.
    /// </summary>
    public sealed class RunComparison
    {
        private RunComparison(int compared, IReadOnlyList<string> missing, long until, long? time, string signal, string ours, string theirs,
            int row, IReadOnlyList<string> atStart)
        {
            DifferAtStart = atStart;
            Compared = compared;
            Missing = missing;
            Until = until;
            Time = time;
            Signal = signal;
            Ours = ours;
            Theirs = theirs;
            Index = row;
        }

        /// <summary>The signals that differ from time 0, as the two simulators start their processes in different orders.</summary>
        public IReadOnlyList<string> DifferAtStart { get; }

        /// <summary>How many signals were held to the other run.</summary>
        public int Compared { get; }

        /// <summary>The run's signals the other's dump has not got, by full name.</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>The last moment compared.</summary>
        public long Until { get; }

        /// <summary>The first moment the two differ at; null when they agree.</summary>
        public long? Time { get; }

        /// <summary>The signal they first differ on, by full name; the first by name of those differing at <see cref="Time"/>.</summary>
        public string Signal { get; }

        /// <summary>Its value here, and in the other run, as binary digits.</summary>
        public string Ours { get; }

        /// <inheritdoc cref="Ours"/>
        public string Theirs { get; }

        /// <summary>The design's index of <see cref="Signal"/>; -1 when they agree.</summary>
        public int Index { get; }

        public bool Agree => Time == null;

        /// <summary>
        /// The run made of <paramref name="design"/> -- its <paramref name="history"/>, to <paramref name="end"/> -- held to
        /// <paramref name="theirs"/>, a dump read by <see cref="VcdReader"/>.
        /// </summary>
        public static RunComparison Of(Design design, History history, long end, IReadOnlyDictionary<string, VcdTrace> theirs)
        {
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (history == null) throw new ArgumentNullException(nameof(history));
            if (theirs == null) throw new ArgumentNullException(nameof(theirs));

            long until = end - 1;
            var missing = new List<string>();
            var atStart = new List<string>();
            int compared = 0;
            long? first = null;
            string signal = null, ours = null, other = null;
            int row = -1;

            for (int s = 0; s < design.Signals.Count; s++)
            {
                SignalInfo info = design.Signals[s];
                if (info.Local || info.Memory != null || info.IsString || info.IsReal)
                    continue;
                if (!theirs.TryGetValue(info.Name, out VcdTrace trace))
                {
                    missing.Add(info.Name);
                    continue;
                }
                compared++;

                var mine = new List<(long Time, string Bits)>();
                foreach (ValueChange change in history.SettledChanges(design.SignalAt(s)))
                    if (change.Time <= until)
                        mine.Add((change.Time, change.Value.Format(Radix.Binary)));

                if (FirstDifference(mine, trace.Changes, info.Width, until, out bool fromStart) is (long at, string here, string there)
                    && (first == null || at < first || (at == first && string.CompareOrdinal(info.Name, signal) < 0)))
                {
                    first = at;
                    signal = info.Name;
                    ours = here;
                    other = there;
                    row = s;
                }
                if (fromStart)
                    atStart.Add(info.Name);
            }

            return new RunComparison(compared, missing, until, first, signal, ours, other, row, atStart);
        }

        /// <summary>
        /// The first moment two signals' settled values differ, up to <paramref name="until"/>; null when they never do. A
        /// difference from time 0 is <paramref name="fromStart"/>, and passed over until the two first agree.
        /// </summary>
        private static (long, string, string)? FirstDifference(List<(long Time, string Bits)> mine, List<(long Time, string Bits)> theirs,
            int width, long until, out bool fromStart)
        {
            string unknown = new string('x', Math.Max(1, width));
            string here = unknown, there = unknown;
            fromStart = false;
            bool starting = true;
            int i = 0, j = 0;
            while (i < mine.Count || j < theirs.Count)
            {
                long next = Math.Min(i < mine.Count ? mine[i].Time : long.MaxValue, j < theirs.Count ? theirs[j].Time : long.MaxValue);
                if (next > until)
                    break;
                while (i < mine.Count && mine[i].Time == next)
                    here = Fit(mine[i++].Bits, width);
                while (j < theirs.Count && theirs[j].Time == next)
                    there = Fit(theirs[j++].Bits, width);
                if (here == there)
                {
                    starting = false;
                    continue;
                }
                if (next == 0)
                {
                    fromStart = true;
                    continue;
                }
                if (fromStart && starting)
                    continue;
                return (next, here, there);
            }
            return null;
        }

        /// <summary>Digits at <paramref name="width"/>: cut from the left, or extended as a VCD extends a short vector.</summary>
        private static string Fit(string bits, int width)
        {
            if (string.IsNullOrEmpty(bits))
                return new string('x', Math.Max(1, width));
            if (bits.Length >= width)
                return bits.Substring(bits.Length - width);
            char pad = bits[0] == 'x' || bits[0] == 'z' ? bits[0] : '0';
            return new string(pad, width - bits.Length) + bits;
        }

        /// <summary>The verdict, for the chip: in a few words.</summary>
        public string Verdict(string unit) => Agree
            ? $"ModelSim agrees: {Count(Compared, "signal")} the same up to {Moment(Until, unit)}" +
              (DifferAtStart.Count > 0 ? $", but {Count(DifferAtStart.Count, "signal")} as they start" : "")
            : $"ModelSim differs first at {Moment(Time.Value, unit)}, on {Short(Signal)}";

        /// <summary>What the TRANSCRIPT says of it: the difference at its moment, with each value, or the agreement; then what was left out.</summary>
        public IReadOnlyList<TranscriptLine> Lines(string unit)
        {
            var lines = new List<TranscriptLine>();
            if (Agree)
                lines.Add(new TranscriptLine(TranscriptSeverity.Info,
                    $"COMPARE: ModelSim's run agrees with this one on all {Count(Compared, "signal")}, up to {Moment(Until, unit)}.", -1));
            else
                lines.Add(new TranscriptLine(TranscriptSeverity.Warning,
                    $"COMPARE: at {Moment(Time.Value, unit)} {Signal} is {Ours} here and {Theirs} in ModelSim -- the first moment the two " +
                    "runs differ. A race at that moment is the usual reason: see the TRANSCRIPT's warnings.", Time.Value));
            if (DifferAtStart.Count > 0)
                lines.Add(new TranscriptLine(TranscriptSeverity.Info,
                    $"COMPARE: {string.Join(", ", DifferAtStart.Count > 8 ? Take(DifferAtStart, 8) : DifferAtStart)}" +
                    (DifferAtStart.Count > 8 ? $" and {DifferAtStart.Count - 8} more" : "") +
                    " differ from t = 0 until the two first agree on them: ModelSim starts an always before an initial at time 0, " +
                    "BitSorter the initial first, and the standard leaves it open -- a reset set at time 0 is seen at once in one, " +
                    "at the first clock in the other.", 0));
            if (Missing.Count > 0)
                lines.Add(new TranscriptLine(TranscriptSeverity.Info,
                    $"COMPARE: not in ModelSim's dump, so not compared: {string.Join(", ", Missing.Count > 8 ? Take(Missing, 8) : Missing)}" +
                    (Missing.Count > 8 ? $" and {Missing.Count - 8} more" : "") + ".", -1));
            return lines;
        }

        private static IEnumerable<string> Take(IReadOnlyList<string> list, int count)
        {
            for (int i = 0; i < count && i < list.Count; i++)
                yield return list[i];
        }

        private static string Short(string name)
        {
            int dot = name.IndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private static string Count(int n, string what) => n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? "" : "s");

        private static string Moment(long time, string unit) => "t = " + time.ToString(CultureInfo.InvariantCulture) + " " + unit;
    }
}
