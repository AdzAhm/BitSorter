using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// Writes a run as a VCD (value change dump, IEEE 1800 21.7): the plain-text waveform format every
    /// simulator writes and GTKWave opens. Export, and the oracle's way of comparing with Icarus.
    /// </summary>
    /// <remarks>
    /// Each signal's settled value per time step is written, never the deltas within one: a waveform
    /// shows the value a time step ends on, and so does Icarus's dump once read back
    /// (<see cref="VcdReader"/>). Hierarchical names become nested scopes under the design's top
    /// module. Writes to a <see cref="TextWriter"/> the caller opens: this layer touches no files.
    /// </remarks>
    public static class VcdWriter
    {
        public static void Write(TextWriter output, Design design, History history, long endTime, string timescale = "1ns")
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (design == null) throw new ArgumentNullException(nameof(design));
            if (history == null) throw new ArgumentNullException(nameof(history));

            int count = design.Signals.Count;
            var ids = new string[count];
            for (int i = 0; i < count; i++)
                ids[i] = IdCode(i);

            output.WriteLine("$version BitSorter $end");
            output.WriteLine($"$timescale {timescale} $end");
            output.WriteLine($"$scope module {design.Name} $end");
            WriteScopes(output, design, ids);
            output.WriteLine("$upscope $end");
            output.WriteLine("$enddefinitions $end");

            // Every settled change after time 0, by time.
            var changes = new List<(long Time, int Signal, LogicVec Value)>();
            for (int s = 0; s < count; s++)
            {
                foreach (ValueChange change in history.SettledChanges(new SignalId(s)))
                {
                    if (change.Time > 0 && change.Time <= endTime)
                        changes.Add((change.Time, s, change.Value));
                }
            }

            changes.Sort((x, y) => x.Time != y.Time ? x.Time.CompareTo(y.Time) : x.Signal.CompareTo(y.Signal));

            output.WriteLine("#0");
            output.WriteLine("$dumpvars");
            for (int s = 0; s < count; s++)
                output.WriteLine(ValueText(history.ValueAt(new SignalId(s), 0), ids[s]));
            output.WriteLine("$end");

            long current = 0;
            foreach ((long time, int signal, LogicVec value) in changes)
            {
                if (time != current)
                {
                    output.WriteLine($"#{time}");
                    current = time;
                }

                output.WriteLine(ValueText(value, ids[signal]));
            }

            if (endTime > current)
                output.WriteLine($"#{endTime}");
        }

        /// <summary>Signals grouped into nested scopes by the dots in their names.</summary>
        private static void WriteScopes(TextWriter output, Design design, string[] ids)
        {
            var order = Enumerable.Range(0, design.Signals.Count)
                .OrderBy(i => design.Signals[i].Name, StringComparer.Ordinal)
                .ToList();

            var open = new List<string>();

            foreach (int i in order)
            {
                SignalInfo info = design.Signals[i];
                string[] path = info.Name.Split('.');
                int depth = path.Length - 1;

                int keep = 0;
                while (keep < open.Count && keep < depth && open[keep] == path[keep])
                    keep++;

                for (int k = open.Count; k > keep; k--)
                    output.WriteLine("$upscope $end");
                open.RemoveRange(keep, open.Count - keep);

                for (int k = keep; k < depth; k++)
                {
                    output.WriteLine($"$scope module {path[k]} $end");
                    open.Add(path[k]);
                }

                string kind = info.Kind == SignalKind.Net ? "wire" : "reg";
                string range = info.Width > 1 ? $" [{info.Width - 1}:0]" : string.Empty;
                output.WriteLine($"$var {kind} {info.Width} {ids[i]} {path[depth]}{range} $end");
            }

            for (int k = open.Count; k > 0; k--)
                output.WriteLine("$upscope $end");
        }

        private static string ValueText(LogicVec value, string id) =>
            value.Width == 1 ? value[0].ToChar() + id : "b" + value.Format(Radix.Binary) + " " + id;

        /// <summary>A short identifier from the printable characters '!' to '~', as VCD uses.</summary>
        private static string IdCode(int index)
        {
            var code = new StringBuilder();
            do
            {
                code.Append((char)('!' + index % 94));
                index /= 94;
            }
            while (index > 0);

            return code.ToString();
        }
    }

    /// <summary>One signal of a VCD: its full name, width and the value each time step settled on.</summary>
    public sealed class VcdTrace
    {
        internal VcdTrace(string name, int width)
        {
            Name = name;
            Width = width;
        }

        /// <summary>Its full name, scopes and all, dot-separated: <c>delays.y1</c>.</summary>
        public string Name { get; }

        public int Width { get; }

        /// <summary>The changes, one per time step at most, each as binary digits at full width.</summary>
        public List<(long Time, string Bits)> Changes { get; } = new List<(long, string)>();

        public override string ToString() => $"{Name}: " + string.Join(", ", Changes.Select(c => $"{c.Time}={c.Bits}"));
    }

    /// <summary>
    /// Reads a VCD -- Icarus's, BitSorter's own, GTKWave's -- into the value each signal settled on at
    /// each time step.
    /// </summary>
    /// <remarks>
    /// Several changes to one signal at one time (a dump writes zero-width glitches as it meets them)
    /// keep only the last, and a "change" to the value it already had is dropped, so two dumps of the
    /// same waveform read back the same. A vector written short is extended as the format says: with x
    /// or z if its leftmost digit is x or z, else with 0. Real values are skipped.
    /// </remarks>
    public static class VcdReader
    {
        public static Dictionary<string, VcdTrace> Read(TextReader input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var traces = new Dictionary<string, VcdTrace>(StringComparer.Ordinal);
            var byId = new Dictionary<string, List<VcdTrace>>(StringComparer.Ordinal);
            var scopes = new List<string>();
            long time = 0;

            string[] tokens = input.ReadToEnd().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];

                switch (t)
                {
                    case "$scope":
                        scopes.Add(tokens[i + 2]);
                        i = SkipToEnd(tokens, i);
                        continue;

                    case "$upscope":
                        if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1);
                        i = SkipToEnd(tokens, i);
                        continue;

                    case "$var":
                    {
                        int width = int.Parse(tokens[i + 2]);
                        string id = tokens[i + 3];
                        string name = string.Join(".", scopes.Concat(new[] { tokens[i + 4] }));
                        var trace = new VcdTrace(name, width);
                        traces[name] = trace;
                        if (!byId.TryGetValue(id, out List<VcdTrace> list))
                            byId[id] = list = new List<VcdTrace>();
                        list.Add(trace);
                        i = SkipToEnd(tokens, i);
                        continue;
                    }

                    case "$dumpvars": case "$dumpall": case "$dumpon": case "$dumpoff": case "$end":
                        continue;
                }

                if (t.StartsWith("$", StringComparison.Ordinal))
                {
                    i = SkipToEnd(tokens, i);
                    continue;
                }

                if (t[0] == '#')
                {
                    time = long.Parse(t.Substring(1));
                    continue;
                }

                char first = char.ToLowerInvariant(t[0]);

                if (first == 'b')
                {
                    Record(byId, tokens[i + 1], time, t.Substring(1).ToLowerInvariant());
                    i++;
                }
                else if (first == 'r')
                {
                    i++;
                }
                else if (first == '0' || first == '1' || first == 'x' || first == 'z')
                {
                    Record(byId, t.Substring(1), time, first.ToString());
                }
            }

            return traces;
        }

        private static int SkipToEnd(string[] tokens, int i)
        {
            while (i < tokens.Length && tokens[i] != "$end")
                i++;
            return i;
        }

        private static void Record(Dictionary<string, List<VcdTrace>> byId, string id, long time, string bits)
        {
            if (!byId.TryGetValue(id, out List<VcdTrace> traces))
                return;

            foreach (VcdTrace trace in traces)
            {
                string full = Extend(bits, trace.Width);
                List<(long Time, string Bits)> changes = trace.Changes;

                if (changes.Count > 0 && changes[changes.Count - 1].Time == time)
                    changes.RemoveAt(changes.Count - 1);

                if (changes.Count > 0 && changes[changes.Count - 1].Bits == full)
                    continue;

                changes.Add((time, full));
            }
        }

        private static string Extend(string bits, int width)
        {
            if (bits.Length >= width)
                return bits.Substring(bits.Length - width);

            char pad = bits[0] == 'x' || bits[0] == 'z' ? bits[0] : '0';
            return new string(pad, width - bits.Length) + bits;
        }
    }
}
