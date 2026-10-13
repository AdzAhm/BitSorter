using System;
using System.Collections.Generic;
using System.Text;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>How a call to <see cref="Simulator.RunTo"/> ended.</summary>
    public enum RunStatus
    {
        /// <summary>Everything up to the time asked for has run.</summary>
        ReachedTime,

        /// <summary>The event budget ran out first; call again to go on. How the browser stays responsive.</summary>
        Paused,

        /// <summary>The run cannot go on: a zero-delay loop never settled. The messages say where.</summary>
        Stopped,
    }

    /// <summary>What <see cref="Simulator.RunTo"/> did.</summary>
    public readonly struct RunResult
    {
        public readonly RunStatus Status;

        /// <summary>How far time got.</summary>
        public readonly long Time;

        /// <summary>How many events it processed.</summary>
        public readonly long Events;

        internal RunResult(RunStatus status, long time, long events)
        {
            Status = status;
            Time = time;
            Events = events;
        }

        public override string ToString() => $"{Status} at {Time} after {Events} events";
    }

    /// <summary>What a simulator records and how far it lets a time step go.</summary>
    public sealed class SimOptions
    {
        /// <summary>
        /// The most delta cycles one time step may take before the run stops as a zero-delay loop.
        /// A loop that never settles is a design fault, and must never hang a browser tab.
        /// </summary>
        public int MaxDeltas { get; set; } = 10000;

        /// <summary>Whether every value change is recorded. Off, only the starting values are.</summary>
        public bool RecordChanges { get; set; } = true;

        /// <summary>Whether transitions in flight are recorded, for the look to draw.</summary>
        public bool RecordTransitions { get; set; } = true;
    }

    /// <summary>
    /// The engine: runs a <see cref="Design"/> from time 0, event by event, recording what happens in
    /// its <see cref="History"/>.
    /// </summary>
    /// <remarks>
    /// **Time steps and deltas.** Time jumps from one event to the next. Within a time step, changes
    /// with no delay settle over delta cycles, each in two phases:
    /// - Phase 1 applies everything due: gate and assignment outputs whose delay is up, and writes the
    ///   testbench makes. Every signal touched is resolved and, if its value changed, recorded.
    /// - Phase 2 evaluates every driver that reads a signal that changed, and each one schedules its
    ///   own next output: after its delay, or in the next delta if it has none.
    /// A driver only reads values and schedules its own output, and phase 2 starts only once phase 1
    /// has finished, so no driver sees another's half-done work. **The order drivers are visited in
    /// cannot change the result**: this is 5.0's form of 4.x's order-independence, and a test builds
    /// the same circuits in shuffled orders and compares their histories.
    ///
    /// **Inertial delay** (IEEE 1800, 28.16, confirmed against Icarus in Docs/oracle-notes.md): a
    /// driver has at most one change on its way. A new evaluation that gives the same value leaves it
    /// alone; one that gives another value cancels it -- the history records it Swallowed -- and then
    /// schedules the new value, unless the output already has it. So a pulse shorter than the delay
    /// never reaches the output.
    ///
    /// **Time 0.** Every variable starts at x, every net with a driver at x, every net without one
    /// at z. In the first delta every driver evaluates once, as the standard has gates and continuous
    /// assignments do.
    ///
    /// **Allocation.** Events live in a heap array and two lists that grow and then stay; once they
    /// have grown, a run with recording off allocates nothing (a test holds it). Recording grows its
    /// buffers by doubling.
    /// </remarks>
    public sealed class Simulator
    {
        private enum EventKind : byte { DriverUpdate, Write }

        private struct Event
        {
            public long Time;
            public long Seq;
            public EventKind Kind;
            public int Index;
            public LogicVec Value;
            public long Token;
        }

        private readonly Design _design;
        private readonly SimOptions _options;
        private readonly History _history;

        private readonly LogicVec[] _values;
        private readonly LogicVec[] _written;
        private readonly int[][] _driversOf;

        /// <summary>Whether a signal has a driver that drives only some of its bits.</summary>
        private readonly bool[] _sliced;
        private readonly int[][] _fanout;

        private readonly DriverInfo[] _drivers;
        private readonly LogicVec[] _output;
        private readonly long[] _pendingToken;
        private readonly LogicVec[] _pendingValue;
        private readonly int[] _pendingTransition;

        private readonly EventHeap _heap = new EventHeap();
        private List<Event> _current = new List<Event>();
        private List<Event> _next = new List<Event>();

        private readonly int[] _signalStamp;
        private readonly int[] _driverStamp;
        private int _stamp;
        private readonly List<int> _dirty = new List<int>();
        private readonly List<int> _toEvaluate = new List<int>();

        private long _seq;
        private long _tokens;
        private bool _started;
        private long _now;

        public Simulator(Design design, Stimulus stimulus = null, SimOptions options = null)
        {
            _design = design ?? throw new ArgumentNullException(nameof(design));
            _options = options ?? new SimOptions();

            int signals = design.Signals.Count;
            int drivers = design.Drivers.Count;

            _history = new History(signals);
            _values = new LogicVec[signals];
            _written = new LogicVec[signals];
            _signalStamp = new int[signals];
            _drivers = new DriverInfo[drivers];
            _output = new LogicVec[drivers];
            _pendingToken = new long[drivers];
            _pendingValue = new LogicVec[drivers];
            _pendingTransition = new int[drivers];
            _driverStamp = new int[drivers];

            var driversOf = new List<int>[signals];
            var fanout = new List<int>[signals];
            for (int s = 0; s < signals; s++)
            {
                driversOf[s] = new List<int>();
                fanout[s] = new List<int>();
            }

            var reads = new HashSet<int>();
            for (int d = 0; d < drivers; d++)
            {
                DriverInfo driver = design.Drivers[d];
                _drivers[d] = driver;
                _output[d] = LogicVec.AllX(driver.TargetWidth);
                _pendingTransition[d] = -1;
                driversOf[driver.Target.Index].Add(d);

                reads.Clear();
                driver.CollectReads(reads);
                foreach (int s in reads)
                    fanout[s].Add(d);
            }

            _driversOf = Array.ConvertAll(driversOf, l => l.ToArray());
            _fanout = Array.ConvertAll(fanout, l => l.ToArray());

            _sliced = new bool[signals];
            for (int s = 0; s < signals; s++)
                foreach (int d in _driversOf[s])
                    if (_drivers[d].TargetLowest != 0 || _drivers[d].TargetWidth != design.Signals[s].Width)
                        _sliced[s] = true;

            for (int s = 0; s < signals; s++)
            {
                SignalInfo info = design.Signals[s];
                bool undrivenNet = info.Kind == SignalKind.Net && _driversOf[s].Length == 0;

                // A signal driven in slices starts from its slices: x where a driver reaches, and on a
                // wire z where none does -- as Icarus shows a wire with assign y[0] = a alone, zzzx.
                LogicVec start = _sliced[s]
                    ? Shape(ResolveSlices(info, _driversOf[s]), info)
                    : Shape(LogicVec.Fill(undrivenNet ? Logic4.Z : Logic4.X, info.Width), info);

                _values[s] = start;
                _written[s] = start;
                _history.RecordChange(s, 0, -1, start);
            }

            if (stimulus != null)
            {
                foreach ((long time, SignalId signal, LogicVec value) in stimulus.Writes)
                {
                    SignalInfo info = design[signal];
                    if (info.Kind != SignalKind.Variable || _driversOf[signal.Index].Length > 0)
                        throw new ArgumentException(
                            $"The stimulus writes {info.Name}, which is driven continuously; only an undriven variable can be written.");

                    _heap.Push(new Event { Time = time, Seq = ++_seq, Kind = EventKind.Write, Index = signal.Index, Value = Shape(value, info) });
                }
            }
        }

        public Design Design => _design;

        public History History => _history;

        /// <summary>How far the run has got.</summary>
        public long Now => _now;

        /// <summary>Whether the run has stopped for good.</summary>
        public bool Stopped { get; private set; }

        /// <summary>A signal's value now.</summary>
        public LogicVec Value(SignalId signal) => _values[signal.Index];

        /// <summary>
        /// Runs every event up to and including <paramref name="time"/>, unless
        /// <paramref name="maxEvents"/> runs out first. A budget only pauses between time steps, so one
        /// time step always finishes; the next call carries on exactly where this one stopped.
        /// </summary>
        public RunResult RunTo(long time, long maxEvents = long.MaxValue)
        {
            if (time < 0)
                throw new ArgumentOutOfRangeException(nameof(time), time, "Time starts at 0.");

            long processed = 0;

            while (!Stopped)
            {
                long next = !_started ? 0 : _heap.Count > 0 ? _heap.Peek().Time : long.MaxValue;

                if (next > time)
                {
                    _now = Math.Max(_now, time);
                    return new RunResult(RunStatus.ReachedTime, _now, processed);
                }

                if (processed >= maxEvents)
                    return new RunResult(RunStatus.Paused, _now, processed);

                processed += RunTimeStep(next);
            }

            return new RunResult(RunStatus.Stopped, _now, processed);
        }

        /// <summary>Runs one time step to the end of its last delta. Returns the events it processed.</summary>
        private long RunTimeStep(long time)
        {
            _now = time;
            long events = 0;

            while (_heap.Count > 0 && _heap.Peek().Time == time)
                _current.Add(_heap.Pop());

            bool first = !_started;
            _started = true;

            for (int delta = 0; _current.Count > 0 || first; delta++)
            {
                if (delta >= _options.MaxDeltas)
                {
                    StopOnLoop(time);
                    return events;
                }

                _stamp++;
                events += _current.Count;

                // Phase 1: everything due arrives.
                for (int i = 0; i < _current.Count; i++)
                {
                    Event e = _current[i];

                    if (e.Kind == EventKind.Write)
                    {
                        _written[e.Index] = e.Value;
                        MarkDirty(e.Index);
                        continue;
                    }

                    int d = e.Index;
                    if (_pendingToken[d] != e.Token)
                        continue;   // swallowed after it was scheduled

                    _output[d] = e.Value;
                    _pendingToken[d] = 0;
                    if (_pendingTransition[d] >= 0)
                        _history.EndTransition(_pendingTransition[d], TransitionOutcome.Applied, time);
                    _pendingTransition[d] = -1;

                    MarkDirty(_drivers[d].Target.Index);
                }

                _current.Clear();

                for (int i = 0; i < _dirty.Count; i++)
                {
                    int s = _dirty[i];
                    LogicVec value = Resolve(s);
                    if (value.Equals(_values[s]))
                        continue;

                    _values[s] = value;
                    if (_options.RecordChanges)
                        _history.RecordChange(s, time, delta, value);

                    foreach (int reader in _fanout[s])
                        MarkToEvaluate(reader);
                }

                _dirty.Clear();

                if (first)
                {
                    for (int d = 0; d < _drivers.Length; d++)
                        MarkToEvaluate(d);
                    first = false;
                }

                // Phase 2: every driver that reads a change decides its next output.
                for (int i = 0; i < _toEvaluate.Count; i++)
                {
                    int d = _toEvaluate[i];
                    LogicVec output = _drivers[d].Evaluate(_values);
                    Schedule(d, Fit(output, _drivers[d].TargetWidth), time);
                }

                _toEvaluate.Clear();

                List<Event> swap = _current;
                _current = _next;
                _next = swap;
            }

            return events;
        }

        /// <summary>Inertial scheduling: see the remarks on <see cref="Simulator"/>.</summary>
        private void Schedule(int d, LogicVec value, long now)
        {
            if (_pendingToken[d] != 0)
            {
                if (value.Equals(_pendingValue[d]))
                    return;

                if (_pendingTransition[d] >= 0)
                    _history.EndTransition(_pendingTransition[d], TransitionOutcome.Swallowed, now);

                _pendingToken[d] = 0;
                _pendingTransition[d] = -1;
            }

            if (value.Equals(_output[d]))
                return;

            DriverInfo driver = _drivers[d];
            long delay = driver.Delay.For(_output[d], value);
            long due = now + delay;
            long token = ++_tokens;

            _pendingToken[d] = token;
            _pendingValue[d] = value;
            _pendingTransition[d] = _options.RecordTransitions
                ? _history.BeginTransition(d, driver.Target, now, due, value)
                : -1;

            var e = new Event { Time = due, Seq = ++_seq, Kind = EventKind.DriverUpdate, Index = d, Value = value, Token = token };

            if (delay == 0)
                _next.Add(e);
            else
                _heap.Push(e);
        }

        /// <summary>A signal's value from its drivers -- resolved as a wire if it has several -- or from what was last written.</summary>
        private LogicVec Resolve(int s)
        {
            SignalInfo info = _design.Signals[s];
            int[] drivers = _driversOf[s];

            if (drivers.Length == 0)
                return info.Kind == SignalKind.Variable ? _written[s] : _values[s];

            if (_sliced[s])
                return Shape(ResolveSlices(info, drivers), info);

            LogicVec value = _output[drivers[0]];
            for (int i = 1; i < drivers.Length; i++)
                value = LogicVec.ResolveWire(value, _output[drivers[i]]);

            return Shape(value, info);
        }

        /// <summary>
        /// A signal some of whose drivers drive only some of its bits. On a wire every driver
        /// contributes z outside its slice and the contributions resolve as drivers do; a variable's
        /// slices never overlap (the builder refuses that), so each is put in place, and a bit no
        /// driver reaches is x.
        /// </summary>
        private LogicVec ResolveSlices(SignalInfo info, int[] drivers)
        {
            if (info.Kind == SignalKind.Variable)
            {
                LogicVec composed = LogicVec.AllX(info.Width);
                foreach (int d in drivers)
                    composed = Splice(composed, _drivers[d].TargetLowest, _output[d]);
                return composed;
            }

            LogicVec undriven = LogicVec.Fill(Logic4.Z, info.Width);
            LogicVec value = Splice(undriven, _drivers[drivers[0]].TargetLowest, _output[drivers[0]]);
            for (int i = 1; i < drivers.Length; i++)
                value = LogicVec.ResolveWire(value, Splice(undriven, _drivers[drivers[i]].TargetLowest, _output[drivers[i]]));

            return value;
        }

        /// <summary><paramref name="into"/> with <paramref name="part"/> in place of its bits from <paramref name="lowest"/>.</summary>
        private static LogicVec Splice(LogicVec into, int lowest, LogicVec part)
        {
            int width = into.Width;
            int top = lowest + part.Width;
            LogicVec result = part;

            if (top < width)
                result = LogicVec.Concat(into.Slice(top, width - top), result);
            if (lowest > 0)
                result = LogicVec.Concat(result, into.Slice(0, lowest));

            return result;
        }

        private void StopOnLoop(long time)
        {
            var names = new StringBuilder();
            int shown = 0;

            foreach (Event e in _current)
            {
                if (e.Kind != EventKind.DriverUpdate || shown == 8)
                    continue;

                if (names.Length > 0) names.Append(", ");
                names.Append(_design[_drivers[e.Index].Target].Name);
                shown++;
            }

            Stopped = true;
            _history.Message(time, Severity.Error,
                $"At t = {time} the circuit was still changing after {_options.MaxDeltas} delta cycles: " +
                $"a loop with no delay never settles. Still changing: {names}. Give a gate in the loop a delay, " +
                "or break the loop.");
        }

        private void MarkDirty(int s)
        {
            if (_signalStamp[s] == _stamp)
                return;

            _signalStamp[s] = _stamp;
            _dirty.Add(s);
        }

        private void MarkToEvaluate(int d)
        {
            if (_driverStamp[d] == _stamp)
                return;

            _driverStamp[d] = _stamp;
            _toEvaluate.Add(d);
        }

        /// <summary>A value at a signal's width and signedness: how it is held.</summary>
        private static LogicVec Shape(LogicVec value, SignalInfo info)
        {
            LogicVec sized = value.Width == info.Width ? value : value.Extend(info.Width, false);
            return info.Signed ? sized.AsSigned() : sized.AsUnsigned();
        }

        /// <summary>A driver's output at its target's width, as plain bits: signedness belongs to the signal.</summary>
        private static LogicVec Fit(LogicVec value, int width) =>
            (value.Width == width ? value : value.Extend(width, false)).AsUnsigned();

        /// <summary>A binary min-heap of events by time, then by when they were scheduled. Grows, never shrinks, allocates nothing once grown.</summary>
        private sealed class EventHeap
        {
            private Event[] _items = new Event[64];

            public int Count { get; private set; }

            public Event Peek() => _items[0];

            public void Push(Event e)
            {
                if (Count == _items.Length)
                    Array.Resize(ref _items, _items.Length * 2);

                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (!Before(e, _items[parent]))
                        break;
                    _items[i] = _items[parent];
                    i = parent;
                }

                _items[i] = e;
            }

            public Event Pop()
            {
                Event top = _items[0];
                Event last = _items[--Count];
                _items[Count] = default;

                if (Count > 0)
                {
                    int i = 0;
                    while (true)
                    {
                        int child = 2 * i + 1;
                        if (child >= Count)
                            break;
                        if (child + 1 < Count && Before(_items[child + 1], _items[child]))
                            child++;
                        if (!Before(_items[child], last))
                            break;
                        _items[i] = _items[child];
                        i = child;
                    }

                    _items[i] = last;
                }

                return top;
            }

            private static bool Before(in Event a, in Event b) => a.Time < b.Time || (a.Time == b.Time && a.Seq < b.Seq);
        }
    }
}
