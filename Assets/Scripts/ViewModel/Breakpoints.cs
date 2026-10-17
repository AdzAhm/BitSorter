using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>What a breakpoint waits for.</summary>
    public enum BreakKind
    {
        /// <summary>A moment: the playhead reaching <see cref="Breakpoint.Time"/>.</summary>
        Time,

        /// <summary>A signal changing into <see cref="Breakpoint.Value"/>, compared as <c>===</c> compares, x and z included.</summary>
        Value,

        /// <summary>A signal changing to a value with an x bit in it, after time 0, when every variable starts at x.</summary>
        AnyX,
    }

    /// <summary>Where playing the run pauses by itself: at a moment, or when a signal takes a value or goes x.</summary>
    public readonly struct Breakpoint : IEquatable<Breakpoint>
    {
        private Breakpoint(BreakKind kind, long time, int signal, LogicVec value)
        {
            Kind = kind;
            Time = time;
            Signal = signal;
            Value = value;
        }

        /// <summary>A breakpoint at <paramref name="time"/>.</summary>
        public static Breakpoint At(long time)
        {
            if (time < 0)
                throw new ArgumentOutOfRangeException(nameof(time), time, "Time starts at 0.");
            return new Breakpoint(BreakKind.Time, time, -1, default);
        }

        /// <summary>A breakpoint on <paramref name="signal"/> changing into <paramref name="value"/>, as wide as the signal.</summary>
        public static Breakpoint OnValue(int signal, LogicVec value)
        {
            if (signal < 0)
                throw new ArgumentOutOfRangeException(nameof(signal), signal, "A breakpoint on a value is on a signal.");
            if (value.IsEmpty)
                throw new ArgumentException("A breakpoint on a value needs the value.", nameof(value));
            return new Breakpoint(BreakKind.Value, 0, signal, value);
        }

        /// <summary>A breakpoint on <paramref name="signal"/> going x.</summary>
        public static Breakpoint OnX(int signal)
        {
            if (signal < 0)
                throw new ArgumentOutOfRangeException(nameof(signal), signal, "A breakpoint on x is on a signal.");
            return new Breakpoint(BreakKind.AnyX, 0, signal, default);
        }

        public BreakKind Kind { get; }

        /// <summary>The moment, for <see cref="BreakKind.Time"/>.</summary>
        public long Time { get; }

        /// <summary>The signal, by its index in the design; -1 for a moment.</summary>
        public int Signal { get; }

        /// <summary>The value, for <see cref="BreakKind.Value"/>.</summary>
        public LogicVec Value { get; }

        /// <summary>Whether a signal taking <paramref name="value"/> at <paramref name="time"/> fires it.</summary>
        public bool FiresOn(long time, LogicVec value)
        {
            switch (Kind)
            {
                case BreakKind.Value:
                    return value.Width == Value.Width && LogicVec.CaseEqual(value, Value).IsTrue;
                case BreakKind.AnyX:
                    return time > 0 && Breakpoints.HasX(value);
                default:
                    return false;
            }
        }

        public bool Equals(Breakpoint other) =>
            Kind == other.Kind && Time == other.Time && Signal == other.Signal
            && (Kind != BreakKind.Value || LogicVec.CaseEqual(Value, other.Value).IsTrue && Value.Width == other.Value.Width);

        public override bool Equals(object obj) => obj is Breakpoint other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Time.GetHashCode() ^ (Signal * 31);

        public override string ToString() =>
            Kind == BreakKind.Time ? $"at {Time}" : Kind == BreakKind.AnyX ? $"signal {Signal} goes x" : $"signal {Signal} = {Value}";
    }

    /// <summary>Where a breakpoint fires: the moment, and which breakpoint, by its place in the list.</summary>
    public readonly struct BreakHit
    {
        public BreakHit(long time, int index)
        {
            Time = time;
            Index = index;
        }

        public long Time { get; }
        public int Index { get; }

        public override string ToString() => $"{Index} at {Time}";
    }

    /// <summary>
    /// The breakpoints a student has set (Part 9b-3): at a moment, on a signal taking a value, or on a
    /// signal going x -- and where the next one fires.
    /// </summary>
    /// <remarks>
    /// **They are read against the recorded run, never checked inside the engine.** The run is made
    /// ahead of what is shown, so where a breakpoint fires is a question about its history: the first
    /// settled change after the playhead that fires it (<see cref="Next"/>). The engine knows nothing of
    /// them, so setting one never runs anything again.
    ///
    /// A value fires on the change *into* it, as a waveform shows a change: a signal's settled changes
    /// never repeat a value, so every one with the value is a change into it. A breakpoint on x waits
    /// past time 0, where everything starts at x.
    /// </remarks>
    public sealed class Breakpoints
    {
        /// <summary>The finest difference between two moments here: what "after" leaves out.</summary>
        private const double Epsilon = 1e-9;

        private readonly List<Breakpoint> _all = new List<Breakpoint>();

        /// <summary>Every breakpoint, in the order set.</summary>
        public IReadOnlyList<Breakpoint> All => _all;

        public int Count => _all.Count;

        public Breakpoint this[int index] => _all[index];

        /// <summary>Changes whenever a breakpoint is set or taken away: what an answer kept about them is checked against.</summary>
        public int Version { get; private set; }

        /// <summary>Sets a breakpoint. False when it is set already.</summary>
        public bool Add(Breakpoint breakpoint)
        {
            if (_all.Contains(breakpoint))
                return false;
            _all.Add(breakpoint);
            Version++;
            return true;
        }

        /// <summary>Takes a breakpoint away. False when it was not set.</summary>
        public bool Remove(Breakpoint breakpoint)
        {
            if (!_all.Remove(breakpoint))
                return false;
            Version++;
            return true;
        }

        /// <summary>Sets a breakpoint at <paramref name="time"/>, or takes it away if there is one: true when it is now set.</summary>
        public bool ToggleAt(long time)
        {
            Breakpoint at = Breakpoint.At(time);
            if (Remove(at))
                return false;
            Add(at);
            return true;
        }

        /// <summary>Whether there is a breakpoint at <paramref name="time"/>.</summary>
        public bool HasAt(long time) => _all.Contains(Breakpoint.At(time));

        /// <summary>Whether any breakpoint is on <paramref name="signal"/>.</summary>
        public bool HasOn(int signal)
        {
            foreach (Breakpoint b in _all)
                if (b.Signal == signal && b.Kind != BreakKind.Time)
                    return true;
            return false;
        }

        /// <summary>Takes every breakpoint on <paramref name="signal"/> away. Returns how many.</summary>
        public int RemoveOn(int signal) => RemoveWhere(b => b.Kind != BreakKind.Time && b.Signal == signal);

        /// <summary>
        /// Takes away every breakpoint on a signal <paramref name="shown"/> says is not shown: one on a row
        /// taken out would pause the run with nothing on screen saying why.
        /// </summary>
        public int KeepOnly(Func<int, bool> shown) => RemoveWhere(b => b.Kind != BreakKind.Time && !shown(b.Signal));

        /// <summary>Takes every breakpoint away.</summary>
        public void Clear()
        {
            if (_all.Count == 0)
                return;
            _all.Clear();
            Version++;
        }

        private int RemoveWhere(Predicate<Breakpoint> match)
        {
            int removed = _all.RemoveAll(match);
            if (removed > 0)
                Version++;
            return removed;
        }

        /// <summary>
        /// The first breakpoint to fire strictly after <paramref name="after"/>, as far as the run has been
        /// read (<paramref name="trackOf"/> gives a signal's settled changes, or null), or null if none
        /// does. Of two at one moment, the one set first.
        /// </summary>
        public BreakHit? Next(Func<int, SignalTrack> trackOf, double after)
        {
            if (trackOf == null)
                throw new ArgumentNullException(nameof(trackOf));

            BreakHit? best = null;
            for (int i = 0; i < _all.Count; i++)
            {
                long? at = FirstAfter(_all[i], trackOf, after);
                if (at.HasValue && (best == null || at.Value < best.Value.Time))
                    best = new BreakHit(at.Value, i);
            }

            return best;
        }

        private static long? FirstAfter(Breakpoint b, Func<int, SignalTrack> trackOf, double after)
        {
            if (b.Kind == BreakKind.Time)
                return b.Time > after + Epsilon ? b.Time : (long?)null;

            SignalTrack track = trackOf(b.Signal);
            if (track == null)
                return null;

            for (int i = track.LastAtOrBefore(after + Epsilon) + 1; i < track.Count; i++)
            {
                ValueChange change = track[i];
                if (b.FiresOn(change.Time, change.Value))
                    return change.Time;
            }

            return null;
        }

        /// <summary>Whether any bit of <paramref name="value"/> is x.</summary>
        public static bool HasX(LogicVec value)
        {
            for (int i = 0; i < value.Width; i++)
                if (value[i] == Logic4.X)
                    return true;
            return false;
        }

        /// <summary>
        /// Reads a value typed for a breakpoint on a signal <paramref name="width"/> bits wide: a
        /// SystemVerilog literal -- <c>1</c>, <c>4'b1010</c>, <c>'hA</c>, <c>'1</c>, <c>'x</c> -- or a bare
        /// x or z. It is fitted to the width as <c>===</c> would fit it: widened by its own signedness, and
        /// narrowed only if the bits it loses are 0s, or the x, z or sign that pads it. False, with the
        /// reason, when the text is no value or does not fit.
        /// </summary>
        public static bool TryParseValue(string text, int width, out LogicVec value, out string error)
        {
            value = default;
            error = null;
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width), width, "A signal is at least a bit wide.");

            string typed = (text ?? "").Replace(" ", "").Trim();
            if (typed.Length == 1 && "xXzZ".IndexOf(typed[0]) >= 0)
                typed = "'" + typed;

            LiteralResult read = Literal.Parse(typed);
            if (!read.Ok)
            {
                error = read.Error;
                return false;
            }

            if (read.FillsContext)
            {
                value = LogicVec.Fill(read.Value[0], width);
                return true;
            }

            LogicVec literal = read.Value;
            if (literal.Width > width)
            {
                Logic4 pad = literal[width - 1];
                bool padding = pad == Logic4.X || pad == Logic4.Z || (literal.IsSigned && pad == Logic4.One);
                for (int i = width; i < literal.Width; i++)
                {
                    Logic4 bit = literal[i];
                    if (bit != Logic4.Zero && !(padding && bit == pad))
                    {
                        error = $"{text.Trim()} does not fit in {width} bit{(width == 1 ? "" : "s")}.";
                        return false;
                    }
                }
            }

            value = literal.Resize(width).AsUnsigned();
            return true;
        }
    }
}
