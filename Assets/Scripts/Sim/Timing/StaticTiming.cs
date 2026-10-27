using System;
using System.Collections.Generic;

namespace BitSorter.Sim
{
    /// <summary>
    /// How fast a design can be clocked (Part 14a): the slowest path from a register to a register sets the minimum period,
    /// its launching register's clock-to-Q, the logic, and its capturing register's setup together. Beside it the slowest
    /// path from an input to a register, from a register to an output, and from an input to an output.
    /// </summary>
    public sealed class TimingReport
    {
        internal TimingReport(StaticPath critical, StaticPath inputToRegister, StaticPath registerToOutput, StaticPath inputToOutput)
        {
            Critical = critical;
            InputToRegister = inputToRegister;
            RegisterToOutput = registerToOutput;
            InputToOutput = inputToOutput;
        }

        /// <summary>The slowest path from a register to a register; null when no register feeds one.</summary>
        public StaticPath Critical { get; }

        /// <summary>The shortest period the design can be clocked at: the critical path's whole; null with no such path.</summary>
        public long? MinimumPeriod => Critical?.Total;

        /// <summary>The slowest path from an input to a register's input, its setup included; null when there is none.</summary>
        public StaticPath InputToRegister { get; }

        /// <summary>The slowest path from a register to an output, its clock-to-Q included; null when there is none.</summary>
        public StaticPath RegisterToOutput { get; }

        /// <summary>The slowest path from an input straight to an output; null when there is none.</summary>
        public StaticPath InputToOutput { get; }
    }

    /// <summary>
    /// One path static timing found: its arcs, the registers at its ends when it has them, and its time in parts -- the
    /// launch (a register's clock-to-Q, or 0 from an input), the logic, and the capture (a register's setup, or 0 at an output).
    /// </summary>
    public sealed class StaticPath
    {
        internal StaticPath(int from, int to, IReadOnlyList<TimingArc> arcs, TimingRegister launch, TimingRegister capture, long logic)
        {
            From = from;
            To = to;
            Arcs = arcs;
            Launch = launch;
            Capture = capture;
            Logic = logic;
        }

        public int From { get; }
        public int To { get; }
        public IReadOnlyList<TimingArc> Arcs { get; }

        /// <summary>The register it starts at; null when it starts at an input.</summary>
        public TimingRegister Launch { get; }

        /// <summary>The register it ends at; null when it ends at an output.</summary>
        public TimingRegister Capture { get; }

        /// <summary>The time the logic takes, launch and setup left out.</summary>
        public long Logic { get; }

        public long Total => (Launch?.Timing.Tcq ?? 0) + Logic + (Capture?.Timing.Tsetup ?? 0);
    }

    /// <summary>
    /// Static timing (Part 14a): the latest each signal can change after the clock's edge, worked out once in topological
    /// order -- a rise and a fall followed apart, each gate's own delay for each -- from every register's launch at its
    /// clock-to-Q, and apart from that from every input at 0.
    /// </summary>
    public static class StaticTiming
    {
        public static TimingReport Analyse(TimingGraph graph)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            int[] order = Topological(graph);
            int count = graph.Design.Signals.Count;

            var fromRegisters = new Arrivals(count);
            var launchedBy = new Dictionary<int, TimingRegister>();
            foreach (TimingRegister register in graph.Registers)
                foreach (int q in register.Launches)
                {
                    fromRegisters.Start(q, register.Timing.Tcq);
                    if (!launchedBy.ContainsKey(q))
                        launchedBy[q] = register;
                }
            fromRegisters.Run(graph, order);

            var fromInputs = new Arrivals(count);
            foreach (int input in graph.Inputs)
                fromInputs.Start(input, 0);
            fromInputs.Run(graph, order);

            StaticPath critical = null, inputToRegister = null;
            foreach (TimingRegister capture in graph.Registers)
                foreach (int d in capture.Captures)
                {
                    critical = Slower(critical, fromRegisters.PathTo(graph, d, launchedBy, capture));
                    inputToRegister = Slower(inputToRegister, fromInputs.PathTo(graph, d, launchedBy: null, capture));
                }

            StaticPath registerToOutput = null, inputToOutput = null;
            foreach (int output in graph.Outputs)
            {
                registerToOutput = Slower(registerToOutput, fromRegisters.PathTo(graph, output, launchedBy, capture: null));
                inputToOutput = Slower(inputToOutput, fromInputs.PathTo(graph, output, launchedBy: null, capture: null));
            }

            return new TimingReport(critical, inputToRegister, registerToOutput, inputToOutput);
        }

        private static StaticPath Slower(StaticPath a, StaticPath b) => b == null ? a : a == null || b.Total > a.Total ? b : a;

        /// <summary>Every signal in an order where an arc's start comes before its end: the loops' arcs are not in the graph.</summary>
        private static int[] Topological(TimingGraph graph)
        {
            int count = graph.Design.Signals.Count;
            var incoming = new int[count];
            for (int s = 0; s < count; s++)
                foreach (int a in graph.Leaving(s))
                    incoming[graph.Arcs[a].To]++;

            var ready = new Queue<int>();
            for (int s = 0; s < count; s++)
                if (incoming[s] == 0)
                    ready.Enqueue(s);

            var order = new List<int>(count);
            while (ready.Count > 0)
            {
                int s = ready.Dequeue();
                order.Add(s);
                foreach (int a in graph.Leaving(s))
                    if (--incoming[graph.Arcs[a].To] == 0)
                        ready.Enqueue(graph.Arcs[a].To);
            }

            return order.ToArray();
        }

        /// <summary>The latest each signal can rise and fall after some starts, and the arc each got it by.</summary>
        private sealed class Arrivals
        {
            private readonly long[] _rise, _fall;
            private readonly int[] _riseBy, _fallBy;
            private readonly bool[] _riseFromFall, _fallFromFall;
            private readonly long[] _start;

            public Arrivals(int count)
            {
                _rise = new long[count];
                _fall = new long[count];
                _riseBy = new int[count];
                _fallBy = new int[count];
                _riseFromFall = new bool[count];
                _fallFromFall = new bool[count];
                _start = new long[count];
                for (int s = 0; s < count; s++)
                {
                    _rise[s] = _fall[s] = _start[s] = PathDelays.Never;
                    _riseBy[s] = _fallBy[s] = -1;
                }
            }

            public void Start(int signal, long at)
            {
                _rise[signal] = Math.Max(_rise[signal], at);
                _fall[signal] = Math.Max(_fall[signal], at);
                _start[signal] = Math.Max(_start[signal], at);
            }

            public void Run(TimingGraph graph, int[] order)
            {
                foreach (int s in order)
                {
                    if (_rise[s] == PathDelays.Never && _fall[s] == PathDelays.Never)
                        continue;
                    foreach (int a in graph.Leaving(s))
                    {
                        TimingArc arc = graph.Arcs[a];
                        int to = arc.To;
                        Reach(arc.Sense, s, out long riseIn, out bool riseFromFall, out long fallIn, out bool fallFromFall);
                        if (riseIn != PathDelays.Never && riseIn + arc.Rise > _rise[to])
                        {
                            _rise[to] = riseIn + arc.Rise;
                            _riseBy[to] = a;
                            _riseFromFall[to] = riseFromFall;
                        }

                        if (fallIn != PathDelays.Never && fallIn + arc.Fall > _fall[to])
                        {
                            _fall[to] = fallIn + arc.Fall;
                            _fallBy[to] = a;
                            _fallFromFall[to] = fallFromFall;
                        }
                    }
                }
            }

            /// <summary>When a change at <paramref name="s"/> makes an arc's output rise, and fall, and from which of its own.</summary>
            private void Reach(Sense sense, int s, out long rise, out bool riseFromFall, out long fall, out bool fallFromFall)
            {
                switch (sense)
                {
                    case Sense.Positive:
                        rise = _rise[s];
                        riseFromFall = false;
                        fall = _fall[s];
                        fallFromFall = true;
                        break;
                    case Sense.Negative:
                        rise = _fall[s];
                        riseFromFall = true;
                        fall = _rise[s];
                        fallFromFall = false;
                        break;
                    default:
                        bool fromFall = _fall[s] > _rise[s];
                        rise = fall = Math.Max(_rise[s], _fall[s]);
                        riseFromFall = fallFromFall = fromFall;
                        break;
                }
            }

            /// <summary>
            /// The latest path to <paramref name="end"/>, walked back along the arcs that set it to where it started; null when
            /// nothing reaches it. Its launch is the register that writes its start, when <paramref name="launchedBy"/> says one does.
            /// </summary>
            public StaticPath PathTo(TimingGraph graph, int end, IReadOnlyDictionary<int, TimingRegister> launchedBy, TimingRegister capture)
            {
                bool rising = _rise[end] >= _fall[end];
                long arrival = rising ? _rise[end] : _fall[end];
                if (arrival == PathDelays.Never)
                    return null;

                var arcs = new List<TimingArc>();
                int at = end;
                for (int guard = 0; guard <= graph.Arcs.Count; guard++)
                {
                    int by = rising ? _riseBy[at] : _fallBy[at];
                    if (by < 0)
                        break;
                    bool fromFall = rising ? _riseFromFall[at] : _fallFromFall[at];
                    TimingArc arc = graph.Arcs[by];
                    arcs.Add(arc);
                    at = arc.From;
                    rising = !fromFall;
                }

                arcs.Reverse();
                TimingRegister launch = null;
                if (launchedBy != null)
                    launchedBy.TryGetValue(at, out launch);
                long start = _start[at] == PathDelays.Never ? 0 : _start[at];
                return new StaticPath(at, end, arcs, launch, capture, arrival - start);
            }
        }
    }
}
