using System;
using System.Collections.Generic;
using System.Globalization;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>One bit of one of the top module's inputs: <c>a</c>, or <c>a[2]</c> of a bus.</summary>
    public readonly struct InputBit
    {
        public InputBit(string port, int width, int bit)
        {
            Port = port;
            Width = width;
            Bit = bit;
        }

        public string Port { get; }

        /// <summary>How wide its port is.</summary>
        public int Width { get; }

        /// <summary>Which bit of its port, from 0.</summary>
        public int Bit { get; }

        /// <summary>As SystemVerilog names it: <c>a</c> for a one-bit port, <c>a[2]</c> for a bit of a bus.</summary>
        public string Name => Width == 1 ? Port : Port + "[" + Bit.ToString(CultureInfo.InvariantCulture) + "]";

        public override string ToString() => Name;
    }

    /// <summary>
    /// One step of a step test (Part 14c): at <see cref="At"/>, input bit <see cref="Input"/> rises or falls, every input bit
    /// standing as <see cref="Word"/> says before it -- bit k of the word is <see cref="StepPlan.Inputs"/>[k] -- and nothing
    /// else changes until <see cref="StepPlan.Settle"/> later.
    /// </summary>
    public readonly struct StepWindow
    {
        public StepWindow(long at, int input, bool rising, ulong word)
        {
            At = at;
            Input = input;
            Rising = rising;
            Word = word;
        }

        public long At { get; }
        public int Input { get; }
        public bool Rising { get; }

        /// <summary>Every input bit just before the step: bit k is input k's value.</summary>
        public ulong Word { get; }
    }

    /// <summary>A step test: the stimulus that plays it, the input bits it steps, and each step it takes.</summary>
    public sealed class StepPlan
    {
        internal StepPlan(Stimulus stimulus, IReadOnlyList<InputBit> inputs, IReadOnlyList<StepWindow> windows, long settle)
        {
            Stimulus = stimulus;
            Inputs = inputs;
            Windows = windows;
            Settle = settle;
        }

        public Stimulus Stimulus { get; }
        public IReadOnlyList<InputBit> Inputs { get; }
        public IReadOnlyList<StepWindow> Windows { get; }

        /// <summary>How long is left after each step before the next: longer than any path, so every change it causes is in it.</summary>
        public long Settle { get; }
    }

    /// <summary>
    /// Step tests (Part 14c): stimuli that step the top module's inputs one bit at a time and leave each step long enough to
    /// settle, so the delay from an input to an output can be read off the waves -- or, stepped from every value of the
    /// other inputs, found at its worst.
    /// </summary>
    /// <remarks>
    /// **The settle time is the slowest path the timing found, and half again** (<see cref="SettleFor"/>): static timing's
    /// path is the latest any change can arrive, so every change a step makes falls inside the time left after it, and the
    /// next step's changes cannot be taken for its.
    ///
    /// **Exhaustive is for at most <see cref="MostInputBits"/> input bits**: each bit stepped up and down from each of the
    /// 2^(n-1) values of the rest is n 2^(n-1) pairs of steps, 5,120 at ten bits. The values of the rest are walked in Gray
    /// order, one bit changing between them, as a counter in the course's style would walk them.
    /// </remarks>
    public static class StepTests
    {
        /// <summary>The most input bits a design is stepped exhaustively for.</summary>
        public const int MostInputBits = 10;

        /// <summary>The top module's inputs as bits, a port's from bit 0 up, the ports in their order.</summary>
        public static IReadOnlyList<InputBit> InputsOf(Project project)
        {
            var bits = new List<InputBit>();
            Module top = project?.TopModule;
            if (top == null)
                return bits;

            if (top.IsWritten)
            {
                foreach (ModulePort port in WrittenModules.PortsOf(project, top))
                    if (port.Direction == PortDirection.Input)
                        for (int b = 0; b < port.Width; b++)
                            bits.Add(new InputBit(port.Name, port.Width, b));
                return bits;
            }

            foreach (PortPart port in top.Ports)
                if (port.Direction == PortDirection.Input)
                    for (int b = 0; b < port.Width; b++)
                        bits.Add(new InputBit(port.Name, port.Width, b));
            return bits;
        }

        /// <summary>How long to leave between steps when the slowest path is <paramref name="worst"/>: that, and half again, and at least 2.</summary>
        public static long SettleFor(long worst) => Math.Max(2, worst + Math.Max(1, (worst + 1) / 2));

        /// <summary>The most input bits STEP TEST steps, one after another; any more are held at 0.</summary>
        public const int MostStepped = 64;

        /// <summary>
        /// STEP TEST: every input at 0, then each input bit in turn rises, is left to settle, falls, and is left to settle --
        /// a delay read off the waves with the cursors, from the step to the output's change. An input the stimulus drives
        /// as a clock keeps its clock and is not stepped; past <see cref="MostStepped"/> bits, the rest are held at 0.
        /// </summary>
        public static StepPlan Readable(Project project, long settle)
        {
            if (settle < 1)
                throw new ArgumentOutOfRangeException(nameof(settle), settle, "A step is left at least 1 to settle.");

            var clocks = new HashSet<string>(StringComparer.Ordinal);
            foreach (ClockTrack clock in project?.Stimulus?.Clocks ?? Array.Empty<ClockTrack>())
                clocks.Add(clock.Port);
            var held = new List<InputBit>();
            var inputs = new List<InputBit>();
            foreach (InputBit bit in InputsOf(project))
                if (!clocks.Contains(bit.Port))
                    (inputs.Count < MostStepped && bit.Bit < 64 ? inputs : held).Add(bit);

            var words = new List<(long At, ulong Word)> { (0, 0) };
            var windows = new List<StepWindow>();
            long t = settle;
            for (int i = 0; i < inputs.Count; i++)
            {
                ulong high = 1UL << i;
                windows.Add(new StepWindow(t, i, rising: true, word: 0));
                words.Add((t, high));
                windows.Add(new StepWindow(t + settle, i, rising: false, word: high));
                words.Add((t + settle, 0));
                t += 2 * settle;
            }

            Stimulus stimulus = Play(inputs, words, t, held) with { Clocks = project?.Stimulus?.Clocks ?? Array.Empty<ClockTrack>() };
            return new StepPlan(stimulus, inputs, windows, settle);
        }

        /// <summary>
        /// The measuring run: each input bit stepped up and back down from every value of the others, each step left to
        /// settle. Refused for more than <see cref="MostInputBits"/> input bits.
        /// </summary>
        public static StepPlan Exhaustive(Project project, long settle)
        {
            if (settle < 1)
                throw new ArgumentOutOfRangeException(nameof(settle), settle, "A step is left at least 1 to settle.");
            IReadOnlyList<InputBit> inputs = InputsOf(project);
            if (inputs.Count > MostInputBits)
                throw new ArgumentException("More than " + MostInputBits + " input bits are too many to step from every value of the rest.", nameof(project));

            int n = inputs.Count;
            var words = new List<(long At, ulong Word)> { (0, 0) };
            var windows = new List<StepWindow>();
            long t = settle;
            ulong word = 0;
            for (int i = 0; i < n; i++)
            {
                ulong self = 1UL << i;
                for (ulong g = 0; g < 1UL << (n - 1); g++)
                {
                    // The rest in Gray order, bit i left out: one of them changes between one pair of steps and the next.
                    ulong rest = Spread(g ^ (g >> 1), i);
                    if (rest != word)
                    {
                        words.Add((t, rest));
                        word = rest;
                        t += settle;
                    }

                    windows.Add(new StepWindow(t, i, rising: true, word: rest));
                    words.Add((t, rest | self));
                    windows.Add(new StepWindow(t + settle, i, rising: false, word: rest | self));
                    words.Add((t + settle, rest));
                    t += 2 * settle;
                }
            }

            return new StepPlan(Play(inputs, words, t, Array.Empty<InputBit>()), inputs, windows, settle);
        }

        /// <summary><paramref name="g"/>'s bits spread round bit <paramref name="skip"/>, which is left 0.</summary>
        private static ulong Spread(ulong g, int skip)
        {
            ulong low = g & ((1UL << skip) - 1);
            ulong high = (g >> skip) << (skip + 1);
            return low | high;
        }

        /// <summary>
        /// The stimulus that holds the inputs at each word from its time: a step on a port wherever its value changes, and
        /// every bit in <paramref name="held"/> at 0 throughout.
        /// </summary>
        private static Stimulus Play(IReadOnlyList<InputBit> inputs, List<(long At, ulong Word)> words, long end, IReadOnlyList<InputBit> held)
        {
            var ports = new List<(string Port, int Width)>();
            var at = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (InputBit bit in inputs)
                if (!at.ContainsKey(bit.Port))
                {
                    at[bit.Port] = ports.Count;
                    ports.Add((bit.Port, bit.Width));
                }
            foreach (InputBit bit in held)
                if (!at.ContainsKey(bit.Port))
                {
                    at[bit.Port] = ports.Count;
                    ports.Add((bit.Port, bit.Width));
                }

            var tracks = new List<Track>();
            for (int p = 0; p < ports.Count; p++)
            {
                var steps = new List<Step>();
                ulong? last = null;
                foreach ((long time, ulong word) in words)
                {
                    ulong value = 0;
                    for (int k = 0; k < inputs.Count; k++)
                        if (inputs[k].Port == ports[p].Port && (word >> k & 1) != 0)
                            value |= 1UL << inputs[k].Bit;
                    if (last == value)
                        continue;
                    last = value;
                    steps.Add(new Step(time, LogicVec.FromUInt64(value, ports[p].Width)));
                }

                tracks.Add(new Track { Port = ports[p].Port, Steps = steps });
            }

            return new Stimulus { Tracks = tracks, End = end };
        }
    }
}
