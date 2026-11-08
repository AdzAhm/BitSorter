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
    /// module -- whether the name already starts with it, as an elaborated design's do
    /// (<c>alu64.slice[3].s.y</c>), or not, as a circuit built in code may. A process's own working
    /// variables (<see cref="SignalInfo.Local"/>) are left out, and so are a memory's words, as Icarus
    /// leaves them out of <c>$dumpvars</c>. Writes to a <see cref="TextWriter"/> the caller opens: this
    /// layer touches no files.
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
            for (int i = 0, next = 0; i < count; i++)
                ids[i] = design.Signals[i].Local || design.Signals[i].Memory != null ? null : IdCode(next++);

            output.WriteLine("$version BitSorter $end");
            output.WriteLine($"$timescale {timescale} $end");
            WriteScopes(output, design, ids);
            output.WriteLine("$enddefinitions $end");

            // Every settled change after time 0, by time.
            var changes = new List<(long Time, int Signal, LogicVec Value)>();
            for (int s = 0; s < count; s++)
            {
                if (ids[s] == null)
                    continue;

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
                if (ids[s] != null)
                    output.WriteLine(ValueText(history.ValueAt(new SignalId(s), 0), ids[s], design.Signals[s].IsReal));
            output.WriteLine("$end");

            long current = 0;
            foreach ((long time, int signal, LogicVec value) in changes)
            {
                if (time != current)
                {
                    output.WriteLine($"#{time}");
                    current = time;
                }

                output.WriteLine(ValueText(value, ids[signal], design.Signals[signal].IsReal));
            }

            if (endTime > current)
                output.WriteLine($"#{endTime}");
        }

        /// <summary>Signals grouped into nested scopes by the dots in their names, all under the top.</summary>
        private static void WriteScopes(TextWriter output, Design design, string[] ids)
        {
            string prefix = design.Name + ".";
            string FullName(int i) =>
                design.Signals[i].Name.StartsWith(prefix, StringComparison.Ordinal) ? design.Signals[i].Name : prefix + design.Signals[i].Name;

            var order = Enumerable.Range(0, design.Signals.Count)
                .Where(i => ids[i] != null)
                .OrderBy(FullName, StringComparer.Ordinal)
                .ToList();

            var open = new List<string>();

            foreach (int i in order)
            {
                SignalInfo info = design.Signals[i];
                string[] path = FullName(i).Split('.');
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

                if (info.IsReal)
                {
                    // A real is a real in a VCD: r and its value (Part L7).
                    output.WriteLine($"$var real 64 {ids[i]} {path[depth]} $end");
                    continue;
                }

                string kind = info.Kind == SignalKind.Net ? "wire" : "reg";
                string range = info.Width > 1 ? $" [{info.Width - 1}:0]" : string.Empty;
                output.WriteLine($"$var {kind} {info.Width} {ids[i]} {path[depth]}{range} $end");
            }

            for (int k = open.Count; k > 0; k--)
                output.WriteLine("$upscope $end");
        }

        private static string ValueText(LogicVec value, string id, bool real = false) =>
            real ? "r" + Reals.Of(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + id
            : value.Width == 1 ? value[0].ToChar() + id : "b" + value.Format(Radix.Binary) + " " + id;

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
    /// <remarks>
    /// **Its changes are kept as the dump wrote them** (the review of 16c): a vector written short -- <c>b1</c> for 64 bits
    /// -- is kept short, and an id's changes are kept once for all the variables that share it, a port and what drives it.
    /// <see cref="Changes"/> widens each as it is read; <see cref="Kept"/> is what was kept, for a comparison that widens a
    /// value only where two differ (<see cref="VcdReader.Same"/>). Kept at full width, a dump of a few thousand characters
    /// could hold half a gigabyte.
    /// </remarks>
    public sealed class VcdTrace
    {
        private List<(long Time, string Bits)> _kept;
        private bool _shared;

        internal VcdTrace(string name, int width, List<(long Time, string Bits)> kept, bool shared)
        {
            Name = name;
            Width = width;
            _kept = kept;
            _shared = shared;
            Changes = new WidenedChanges(this);
        }

        /// <summary>Its full name, scopes and all, dot-separated: <c>delays.y1</c>.</summary>
        public string Name { get; }

        public int Width { get; }

        /// <summary>
        /// The changes, one per time step at most, each as binary digits at full width -- widened from <see cref="Kept"/> as it
        /// is read. A change set, added or taken away here is this trace's alone: what it shares with another is copied first.
        /// </summary>
        public IList<(long Time, string Bits)> Changes { get; }

        /// <summary>
        /// The changes as kept: each value's digits as the dump wrote them, cut from the left where they are more than
        /// <see cref="Width"/>, and extended where they are read as <see cref="VcdReader.Widen"/> extends them.
        /// </summary>
        public IReadOnlyList<(long Time, string Bits)> Kept => _kept;

        private List<(long Time, string Bits)> Own()
        {
            if (_shared)
            {
                _kept = new List<(long Time, string Bits)>(_kept);
                _shared = false;
            }
            return _kept;
        }

        public override string ToString() => $"{Name}: " + string.Join(", ", Changes.Select(c => $"{c.Time}={c.Bits}"));

        /// <summary>A trace's kept changes, each widened as it is read.</summary>
        private sealed class WidenedChanges : IList<(long Time, string Bits)>
        {
            private readonly VcdTrace _trace;

            public WidenedChanges(VcdTrace trace) => _trace = trace;

            public (long Time, string Bits) this[int index]
            {
                get
                {
                    (long time, string bits) = _trace._kept[index];
                    return (time, VcdReader.Widen(bits, _trace.Width));
                }
                set => _trace.Own()[index] = Cut(value);
            }

            public int Count => _trace._kept.Count;

            public bool IsReadOnly => false;

            private (long Time, string Bits) Cut((long Time, string Bits) change) => (change.Time, VcdReader.Cut(change.Bits, _trace.Width));

            public void Add((long Time, string Bits) item) => _trace.Own().Add(Cut(item));

            public void Insert(int index, (long Time, string Bits) item) => _trace.Own().Insert(index, Cut(item));

            public void RemoveAt(int index) => _trace.Own().RemoveAt(index);

            public void Clear() => _trace.Own().Clear();

            public int IndexOf((long Time, string Bits) item)
            {
                for (int i = 0; i < Count; i++)
                    if (this[i].Equals(item))
                        return i;
                return -1;
            }

            public bool Contains((long Time, string Bits) item) => IndexOf(item) >= 0;

            public bool Remove((long Time, string Bits) item)
            {
                int i = IndexOf(item);
                if (i < 0)
                    return false;
                RemoveAt(i);
                return true;
            }

            public void CopyTo((long Time, string Bits)[] array, int arrayIndex)
            {
                for (int i = 0; i < Count; i++)
                    array[arrayIndex + i] = this[i];
            }

            public IEnumerator<(long Time, string Bits)> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                    yield return this[i];
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    /// <summary>
    /// Reads a VCD -- Icarus's, BitSorter's own, GTKWave's -- into the value each signal settled on at
    /// each time step.
    /// </summary>
    /// <remarks>
    /// Several changes to one signal at one time (a dump writes zero-width glitches as it meets them)
    /// keep only the last, and a "change" to the value it already had is dropped, so two dumps of the
    /// same waveform read back the same. A vector written short is extended as the format says: with x
    /// or z if its leftmost digit is x or z, else with 0 -- where it is read, never as it is kept
    /// (<see cref="VcdTrace.Kept"/>). Real values are skipped.
    ///
    /// **A vector dumped a bit at a time is read back whole** (Part 15i-4): ModelSim writes a module's
    /// vector input as one variable a bit -- <c>a [7]</c> to <c>a [0]</c> -- which read by name alone
    /// was eight signals called <c>a</c>, the last of them, bit 0, standing for the whole. Its bits are
    /// placed in the order they are declared, leftmost first, so <c>[0:3]</c> reads as it is written.
    /// It is kept whole at each time step it changes in, so what such vectors hold between them is
    /// bounded by <see cref="MostSplitDigits"/>.
    ///
    /// **A file made to break it reads as what can be read**: a width past <see cref="MostBits"/> or
    /// under 1, a bit index no number holds, or a second width for an id already declared, leaves that
    /// variable out rather than throwing. **Or it is refused, saying why**: vectors dumped a bit at a time
    /// that would come to more than <see cref="MostSplitDigits"/> digits throw an
    /// <see cref="InvalidDataException"/> (the review of 16c).
    /// </remarks>
    public static class VcdReader
    {
        /// <summary>The widest variable read; a wider one, or one of no width, is left out.</summary>
        public const int MostBits = 1 << 16;

        /// <summary>
        /// The most digits the vectors dumped a bit at a time may come to between them -- each bit read, and each change of
        /// one kept whole -- past which a dump is refused. A course design's ModelSim dump under the 16 MB a file may be comes
        /// nowhere near it; a 65,536-bit one changing a bit a line passes it in a few hundred lines.
        /// </summary>
        public const int MostSplitDigits = 1 << 25;

        public static Dictionary<string, VcdTrace> Read(TextReader input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var traces = new Dictionary<string, VcdTrace>(StringComparer.Ordinal);
            var byId = new Dictionary<string, KeptChanges>(StringComparer.Ordinal);
            var split = new Dictionary<string, SplitVector>(StringComparer.Ordinal);
            var bitsById = new Dictionary<string, List<(SplitVector Vector, int Index)>>(StringComparer.Ordinal);
            var scopes = new List<string>();
            long time = 0;
            long held = 0;

            string[] tokens = input.ReadToEnd().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];

                switch (t)
                {
                    case "$scope":
                        scopes.Add(i + 2 < tokens.Length ? tokens[i + 2] : "");
                        i = SkipToEnd(tokens, i);
                        continue;

                    case "$upscope":
                        if (scopes.Count > 0) scopes.RemoveAt(scopes.Count - 1);
                        i = SkipToEnd(tokens, i);
                        continue;

                    case "$var":
                    {
                        if (i + 4 >= tokens.Length || !int.TryParse(tokens[i + 2], out int width) || width < 1 || width > MostBits)
                        {
                            i = SkipToEnd(tokens, i);
                            continue;
                        }
                        string id = tokens[i + 3];
                        string name = string.Join(".", scopes.Concat(new[] { tokens[i + 4] }));

                        // One bit of a vector dumped a bit at a time: "a [3]", a width of 1 and a single index.
                        string range = i + 5 < tokens.Length ? tokens[i + 5] : "$end";
                        if (width == 1 && range.Length > 2 && range[0] == '[' && range[range.Length - 1] == ']' && range.IndexOf(':') < 0
                            && int.TryParse(range.Substring(1, range.Length - 2), out int index))
                        {
                            if (!split.TryGetValue(name, out SplitVector vector))
                                split[name] = vector = new SplitVector(name);
                            vector.Indices.Add(index);

                            // A vector past the widest read is left out, and its bits cost its id's changes nothing: they are
                            // not listed past it, and the ones listed before go at the definitions' end (the 16d sweep, engine-13).
                            if (vector.Indices.Count <= MostBits)
                            {
                                if (!bitsById.TryGetValue(id, out List<(SplitVector, int)> bits))
                                    bitsById[id] = bits = new List<(SplitVector, int)>();
                                bits.Add((vector, index));
                            }
                            i = SkipToEnd(tokens, i);
                            continue;
                        }

                        // Every variable of an id is one net, and its changes are kept once for all of them; one that gives the
                        // id another width is no dump's, and is left out.
                        if (!byId.TryGetValue(id, out KeptChanges kept))
                            byId[id] = kept = new KeptChanges(width);
                        if (kept.Width == width)
                            traces[name] = new VcdTrace(name, width, kept.Changes, shared: true);
                        i = SkipToEnd(tokens, i);
                        continue;
                    }

                    case "$enddefinitions":
                        foreach (SplitVector vector in split.Values)
                            if (vector.Indices.Count <= MostBits)
                                traces[vector.Name] = vector.Made();
                        foreach (List<(SplitVector Vector, int Index)> bits in bitsById.Values)
                            bits.RemoveAll(bit => bit.Vector.Indices.Count > MostBits);
                        i = SkipToEnd(tokens, i);
                        continue;

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
                    if (!long.TryParse(t.Substring(1), out time))
                        time = 0;
                    continue;
                }

                char first = char.ToLowerInvariant(t[0]);

                if (first == 'b')
                {
                    if (i + 1 >= tokens.Length || t.Length < 2)
                        continue;
                    string bits = t.Substring(1).ToLowerInvariant();
                    Record(byId, tokens[i + 1], time, bits);
                    RecordBit(bitsById, tokens[i + 1], time, bits, ref held);
                    i++;
                }
                else if (first == 'r')
                {
                    i++;
                }
                else if ((first == '0' || first == '1' || first == 'x' || first == 'z') && t.Length > 1)
                {
                    string bit = Scalar(first);
                    Record(byId, t.Substring(1), time, bit);
                    RecordBit(bitsById, t.Substring(1), time, bit, ref held);
                }
            }

            // Each vector dumped a bit at a time keeps the value its last time step ended on.
            foreach (SplitVector vector in split.Values)
                vector.Finish(ref held);

            // A dump with no $enddefinitions: its split vectors, made as their first change came.
            foreach (SplitVector vector in split.Values)
                if (vector.Trace != null && !traces.ContainsKey(vector.Name))
                    traces[vector.Name] = vector.Trace;
            return traces;
        }

        /// <summary>
        /// Digits at <paramref name="width"/>, as a VCD extends a vector written short: with x or z if its leftmost digit is x
        /// or z, else with 0 -- or cut from the left where there are more. No digits at all are x.
        /// </summary>
        public static string Widen(string bits, int width)
        {
            width = Math.Max(1, width);
            if (string.IsNullOrEmpty(bits))
                return new string('x', width);
            if (bits.Length == width)
                return bits;
            if (bits.Length > width)
                return bits.Substring(bits.Length - width);
            return new string(Extension(bits), width - bits.Length) + bits;
        }

        /// <summary>
        /// Whether two values' digits are the same at <paramref name="width"/>, each widened as <see cref="Widen"/> widens it
        /// -- worked out without widening either, so a value a dump wrote short costs no more to compare than it was to read.
        /// </summary>
        public static bool Same(string a, string b, int width)
        {
            width = Math.Max(1, width);
            if (string.IsNullOrEmpty(a))
                a = "x";
            if (string.IsNullOrEmpty(b))
                b = "x";

            int na = Math.Min(a.Length, width), nb = Math.Min(b.Length, width);
            char ea = Extension(a), eb = Extension(b);
            int n = Math.Max(na, nb);
            for (int k = 0; k < n; k++)
            {
                char ca = k < na ? a[a.Length - 1 - k] : ea;
                char cb = k < nb ? b[b.Length - 1 - k] : eb;
                if (ca != cb)
                    return false;
            }

            // Past both, each is its extension, to the width.
            return n == width || ea == eb;
        }

        /// <summary>Digits cut from the left to <paramref name="width"/> where there are more; otherwise as they are.</summary>
        internal static string Cut(string bits, int width) =>
            bits != null && bits.Length > width ? bits.Substring(bits.Length - width) : bits;

        private static char Extension(string bits) => bits[0] == 'x' || bits[0] == 'z' ? bits[0] : '0';

        private static string Scalar(char value) =>
            value == '0' ? "0" : value == '1' ? "1" : value == 'x' ? "x" : value == 'z' ? "z" : value.ToString();

        private static int SkipToEnd(string[] tokens, int i)
        {
            while (i < tokens.Length && tokens[i] != "$end")
                i++;
            return i;
        }

        /// <summary>An id's changes, kept once for every variable sharing it, at the width they were declared.</summary>
        private sealed class KeptChanges
        {
            public KeptChanges(int width) => Width = width;

            public readonly int Width;
            public readonly List<(long Time, string Bits)> Changes = new List<(long Time, string Bits)>();
        }

        private static void Record(Dictionary<string, KeptChanges> byId, string id, long time, string bits)
        {
            if (!byId.TryGetValue(id, out KeptChanges kept))
                return;

            string value = Cut(bits, kept.Width);
            List<(long Time, string Bits)> changes = kept.Changes;

            if (changes.Count > 0 && changes[changes.Count - 1].Time == time)
                changes.RemoveAt(changes.Count - 1);

            if (changes.Count > 0 && Same(changes[changes.Count - 1].Bits, value, kept.Width))
                return;

            changes.Add((time, value));
        }

        /// <summary>
        /// A vector dumped a bit at a time: its bits' indices in the order declared -- leftmost first, as ModelSim writes
        /// both <c>[7:0]</c> and <c>[0:3]</c> -- and its value as they change, kept whole once a time step's changes are all
        /// in. Made once.
        /// </summary>
        private sealed class SplitVector
        {
            public SplitVector(string name) => Name = name;

            public readonly string Name;
            public readonly List<int> Indices = new List<int>();
            public readonly Dictionary<int, int> Place = new Dictionary<int, int>();
            public VcdTrace Trace;

            private List<(long Time, string Bits)> _changes;
            private char[] _value, _kept;
            private bool[] _touched;
            private readonly List<int> _touchedPlaces = new List<int>();
            private bool _pending;
            private long _pendingTime;

            public VcdTrace Made()
            {
                if (Trace != null)
                    return Trace;
                foreach (int index in Indices)
                    if (!Place.ContainsKey(index))
                        Place[index] = Place.Count;
                _value = Enumerable.Repeat('x', Place.Count).ToArray();
                _kept = (char[])_value.Clone();
                _touched = new bool[Place.Count];
                _changes = new List<(long Time, string Bits)>();
                return Trace = new VcdTrace(Name, Place.Count, _changes, shared: false);
            }

            /// <summary>One bit's change; a change at another time first keeps the value the last one ended on.</summary>
            public void Set(int index, char bit, long time, ref long held)
            {
                Made();
                if (!Place.TryGetValue(index, out int place))
                    return;   // declared after the vector was made: no dump's
                if (_pending && time != _pendingTime)
                    Finish(ref held);

                Hold(ref held, 1);
                _value[place] = bit;
                if (!_touched[place])
                {
                    _touched[place] = true;
                    _touchedPlaces.Add(place);
                }
                _pending = true;
                _pendingTime = time;
            }

            /// <summary>The value the time step ended on, kept if it is the first or differs from the last kept.</summary>
            public void Finish(ref long held)
            {
                if (!_pending)
                    return;
                _pending = false;

                bool differs = _changes.Count == 0;
                foreach (int place in _touchedPlaces)
                {
                    if (_value[place] != _kept[place])
                    {
                        differs = true;
                        _kept[place] = _value[place];
                    }
                    _touched[place] = false;
                }
                _touchedPlaces.Clear();

                if (!differs)
                    return;
                Hold(ref held, _value.Length);
                _changes.Add((_pendingTime, new string(_value)));
            }

            private static void Hold(ref long held, int digits)
            {
                held += digits;
                if (held > MostSplitDigits)
                    throw new InvalidDataException(
                        $"its vectors dumped a bit at a time come to more than {MostSplitDigits.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} digits, which is more than BitSorter " +
                        "reads of one dump; compare a shorter run.");
            }
        }

        private static void RecordBit(Dictionary<string, List<(SplitVector Vector, int Index)>> bitsById, string id, long time, string bits, ref long held)
        {
            if (!bitsById.TryGetValue(id, out List<(SplitVector Vector, int Index)> list))
                return;

            foreach ((SplitVector vector, int index) in list)
            {
                if (vector.Indices.Count > MostBits || bits.Length == 0)
                    continue;
                vector.Set(index, bits[bits.Length - 1], time, ref held);
            }
        }
    }
}
