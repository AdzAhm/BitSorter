using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>The run cannot go on: a zero-delay loop never settled, or a process never waited. The messages say where.</summary>
        Stopped,

        /// <summary>The design ended its own run: <c>$finish</c> or <c>$fatal</c>.</summary>
        Finished,

        /// <summary>
        /// The design called <c>$stop</c>: the run is suspended at the end of that time step, and the
        /// next call carries on from the statement after it, as ModelSim's <c>run</c> does.
        /// </summary>
        Suspended,
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

        /// <summary>
        /// The most instructions a process may run without waiting before the run stops: an
        /// <c>always</c> or a loop with no delay or event control in it would otherwise never hand back.
        /// </summary>
        public long MaxProcessSteps { get; set; } = 1_000_000;

        /// <summary>
        /// The most a run may keep in its history -- value changes, transitions and lines printed
        /// together -- before it stops. Each costs about 80 bytes, so the default holds a history near
        /// 320 MB, well inside a browser tab's 2 GB: a testbench with no <c>$finish</c>, run to a
        /// far-off time, must stop with a message rather than take the page and the student's work
        /// down. The corpus processor keeps about 230,000 in its 2,716 cycles.
        /// </summary>
        public long MaxRecorded { get; set; } = 4_000_000;

        /// <summary>Whether every value change is recorded. Off, only the starting values are.</summary>
        public bool RecordChanges { get; set; } = true;

        /// <summary>Whether transitions in flight are recorded, for the look to draw.</summary>
        public bool RecordTransitions { get; set; } = true;

        /// <summary>
        /// The files <c>$readmemh</c> reads and <c>$writememh</c> writes. Null, a design that asks for
        /// one is told there are none, in the transcript.
        /// </summary>
        public ISimFiles Files { get; set; }
    }

    /// <summary>
    /// The engine: runs a <see cref="Design"/> from time 0, event by event, recording what happens in
    /// its <see cref="History"/>.
    /// </summary>
    /// <remarks>
    /// **Time steps and deltas.** Time jumps from one event to the next. Within a time step, changes
    /// with no delay settle over delta cycles, each in three phases:
    /// - Phase 1 applies everything due: gate and assignment outputs whose delay is up, writes a
    ///   stimulus makes, nonblocking writes in their turn, processes whose delay is up. Every signal
    ///   touched is resolved and, if its value changed, recorded; then whatever reads it is woken.
    /// - Phase 2 evaluates every driver that reads a signal that changed, and each one schedules its
    ///   own next output: after its delay, or in the next delta if it has none.
    /// - Phase 3 runs every process that was woken, in a fixed order (<see cref="Design.Processes"/>),
    ///   each until it waits or ends.
    /// A driver only reads values and schedules its own output, and phase 2 starts only once phase 1
    /// has finished, so no driver sees another's half-done work: **the order drivers are visited in
    /// cannot change the result**, and a test builds the same circuits in shuffled orders and compares
    /// their histories. A process's blocking write is seen at once by the process itself and by the
    /// processes after it -- that is what blocking means -- and by drivers in the next delta.
    ///
    /// **Regions** (IEEE 1800, 4.4). Once a delta leaves nothing active, the processes waiting on
    /// <c>#0</c> run (the inactive region); once that is empty too, the nonblocking writes of the time
    /// step are applied together (the NBA region), which may wake more. Only then does time move on.
    ///
    /// **Inertial delay** (IEEE 1800, 28.16, confirmed against Icarus in Docs/oracle-notes.md): a
    /// driver has at most one change on its way. A new evaluation that gives the same value leaves it
    /// alone; one that gives another value cancels it -- the history records it Swallowed -- and then
    /// schedules the new value, unless the output already has it. So a pulse shorter than the delay
    /// never reaches the output.
    ///
    /// **Finishing.** <c>$finish</c> ends the run at the end of the time step it runs in: the rest of
    /// that step -- other processes, nonblocking writes, <c>#0</c>, continuous assignments -- still
    /// happens, as Icarus does it, so the final values are consistent with each other.
    ///
    /// **Suspending.** <c>$stop</c> suspends the run (IEEE 1800, 20.2) where <c>$finish</c> ends it:
    /// the process that called it waits at the statement after it, the rest of the time step happens
    /// as it would for <c>$finish</c>, and <see cref="RunTo"/> returns <see cref="RunStatus.Suspended"/>.
    /// The next call carries on: that process runs on from where it waited, in the same time step --
    /// its deltas numbered on from the suspended step's, so the history's last change of the time is
    /// still the one it settled on -- and time moves on from there. Icarus, run as the oracle runs it
    /// (<c>vvp -n</c>), ends the run at a <c>$stop</c> instead, so the two agree up to it.
    ///
    /// **The transcript.** <c>$display</c> and its kind write lines into the history's messages, at the
    /// time they run; <c>$write</c> leaves its line open for the next. <c>$monitor</c> and <c>$strobe</c>
    /// print once the time step has settled, the monitor first, as Icarus orders them; the monitor only
    /// when one of its values other than the time changed.
    ///
    /// **Time 0.** Every variable starts at x, or at the value it was declared with; every net with a
    /// driver at x, every net without one at z. In the first delta every driver evaluates once, as the
    /// standard has gates and continuous assignments do; once what has no delay has settled, every
    /// process starts. Icarus does the same, so an <c>initial</c> that prints a wire at time 0 sees it
    /// worked out from the starting values rather than x.
    ///
    /// **Allocation.** Events live in a heap array and lists that grow and then stay; once they have
    /// grown, a run with recording off allocates nothing on values of 64 bits or fewer (a test holds
    /// it). Recording grows its buffers by doubling.
    /// </remarks>
    public sealed class Simulator
    {
        /// <summary>What an event is; a <see cref="EventKind.Resume"/> carries its process's generation in <see cref="Event.Token"/>.</summary>
        private enum EventKind : byte { DriverUpdate, Write, Resume, Nba, ForceUpdate }

        private struct Event
        {
            public long Time;
            public long Seq;
            public EventKind Kind;
            public int Index;
            public int Lowest;
            public LogicVec Value;
            public long Token;
        }

        private readonly struct Waiter
        {
            public Waiter(int process, int pc)
            {
                Process = process;
                Pc = pc;
            }

            public readonly int Process;
            public readonly int Pc;
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

        private readonly ProcessInfo[] _processes;
        private readonly int[] _pc;

        /// <summary>The wait a process is suspended on, by the counter of its instruction; -1 when it is not waiting on one.</summary>
        private readonly int[] _waitingAt;
        private readonly bool[] _done;

        /// <summary>Each process's event expressions' values when it began to wait, to tell an edge from them.</summary>
        private readonly LogicVec[][] _last;

        /// <summary>For a fork's branch, the process that forks it; -1 for every other (Part L5).</summary>
        private readonly int[] _parent;

        /// <summary>The branches of every fork in each process: what <c>wait fork</c> and <c>disable fork</c> reach.</summary>
        private readonly int[][] _children;

        /// <summary>The fork a process is joining, by its counter; <see cref="JoiningAll"/> for <c>wait fork</c>; -1 for none.</summary>
        private readonly int[] _joinPc;

        /// <summary>
        /// Moves on each time a process starts again or is ended from outside, so a resumption it had been
        /// given before -- a delay running out -- is known for stale and dropped.
        /// </summary>
        private readonly int[] _generation;

        /// <summary>The instruction each process is held at -- a wait, a delay, a join -- for <c>disable</c> to tell whether it is inside a block.</summary>
        private readonly int[] _blockedAt;

        /// <summary>Where phase 3 has got to in <see cref="_running"/>: a fork's branches go in straight after it.</summary>
        private int _runningAt;

        private const int JoiningAll = -2;

        /// <summary>The values <c>force</c> lays over each signal, and for each signal the forces that read it; null until there is one.</summary>
        private List<ForceRecord>[] _forces, _forceReaders;

        /// <summary>One <c>force</c>: its expression's bits from <see cref="Offset"/>, over the target's bits from <see cref="Lowest"/>.</summary>
        private sealed class ForceRecord
        {
            public int Signal, Lowest, Width, Offset, Total;
            public Expr Value;
            public int[] Reads;
            public LogicVec Current;
        }

        /// <summary>For each signal, the waits that read it.</summary>
        private readonly Waiter[][] _waitersOf;

        private readonly EventHeap _heap = new EventHeap();
        private List<Event> _current = new List<Event>();
        private List<Event> _next = new List<Event>();
        private readonly List<Event> _nba = new List<Event>();
        private List<int> _runnable = new List<int>();
        private List<int> _running = new List<int>();
        private readonly List<int> _inactive = new List<int>();

        private readonly int[] _signalStamp;
        private readonly int[] _driverStamp;
        private int _stamp;
        private int _evalEpoch = 1;
        private readonly List<int> _dirty = new List<int>();
        private readonly List<int> _changed = new List<int>();
        private readonly List<int> _toEvaluate = new List<int>();

        private long _seq;
        private long _tokens;
        private bool _started;
        private bool _processesStarted;
        private bool _finishing;
        private long _now;
        private int _delta;

        /// <summary>The processes a <c>$stop</c> has suspended, which the next call to <see cref="RunTo"/> carries on.</summary>
        private readonly List<int> _suspended = new List<int>();

        /// <summary>Whether a <c>$stop</c> has run in the time step under way.</summary>
        private bool _suspending;

        /// <summary>Why a function was stopped short (<see cref="FunctionRunner.Stopped"/>), each reason said once a run.</summary>
        private readonly HashSet<string> _functionsStopped = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether the next time step carries on the one a <c>$stop</c> suspended, its deltas numbered on from it.</summary>
        private bool _carryingOn;

        /// <summary>The signal <c>$time</c> reads, or -1.</summary>
        private readonly int _time;

        /// <summary>A line <c>$write</c> has begun and nothing has ended yet.</summary>
        private readonly StringBuilder _line = new StringBuilder();
        private PrintSpec _monitor;
        private LogicVec[] _monitorLast;
        private bool _monitorOn = true;
        private readonly List<PrintSpec> _strobes = new List<PrintSpec>();

        public Simulator(Design design, Stimulus stimulus = null, SimOptions options = null)
        {
            _design = design ?? throw new ArgumentNullException(nameof(design));
            _options = options ?? new SimOptions();
            FunctionRunner.TakeStopped();   // a call another run stopped is that run's to say, never this one's

            int signals = design.Signals.Count;
            int drivers = design.Drivers.Count;
            int processes = design.Processes.Count;

            _history = new History(signals);
            _time = design.Time?.Index ?? -1;
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
            var waiters = new List<Waiter>[signals];
            for (int s = 0; s < signals; s++)
            {
                driversOf[s] = new List<int>();
                fanout[s] = new List<int>();
                waiters[s] = new List<Waiter>();
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

            _processes = new ProcessInfo[processes];
            _pc = new int[processes];
            _waitingAt = new int[processes];
            _done = new bool[processes];
            _last = new LogicVec[processes][];
            _parent = new int[processes];
            _joinPc = new int[processes];
            _generation = new int[processes];
            _blockedAt = new int[processes];
            var children = new List<int>[processes];
            var procedural = new bool[signals];

            for (int p = 0; p < processes; p++)
            {
                ProcessInfo process = design.Processes[p];
                _processes[p] = process;
                _waitingAt[p] = -1;
                _last[p] = new LogicVec[process.MaxItems];
                _joinPc[p] = -1;
                _blockedAt[p] = -1;
                children[p] = new List<int>();

                // A branch waits for its fork, as though it had ended.
                _done[p] = process.Dormant;
                _parent[p] = process.Parent != null && process.Parent.Index >= 0 && process.Parent.Index < processes &&
                             design.Processes[process.Parent.Index] == process.Parent ? process.Parent.Index : -1;

                foreach (int s in process.Writes)
                    procedural[s] = true;

                for (int pc = 0; pc < process.Code.Length; pc++)
                {
                    reads.Clear();
                    process.CollectWaitReads(pc, reads);
                    foreach (int s in reads)
                        waiters[s].Add(new Waiter(p, pc));
                }
            }

            for (int p = 0; p < processes; p++)
                if (_parent[p] >= 0)
                    children[_parent[p]].Add(p);
            _children = Array.ConvertAll(children, l => l.ToArray());

            _driversOf = Array.ConvertAll(driversOf, l => l.ToArray());
            _fanout = Array.ConvertAll(fanout, l => l.ToArray());
            _waitersOf = Array.ConvertAll(waiters, l => l.ToArray());

            _sliced = new bool[signals];
            for (int s = 0; s < signals; s++)
                foreach (int d in _driversOf[s])
                    if (_drivers[d].TargetLowest != 0 || _drivers[d].TargetWidth != design.Signals[s].Width)
                        _sliced[s] = true;

            for (int s = 0; s < signals; s++)
            {
                SignalInfo info = design.Signals[s];

                // An input port is a net (IEEE 1800, 23.2.2.3), so one nothing drives reads z. The top's
                // inputs are declared as variables, for a stimulus to write, and read z until it does.
                bool topInput = info.Kind == SignalKind.Variable && info.Port == SystemVerilog.Syntax.PortDirection.Input && !procedural[s];
                bool undrivenNet = (info.Kind == SignalKind.Net || topInput) && _driversOf[s].Length == 0;

                // A signal driven in slices starts from its slices: x where a driver reaches, and on a
                // wire z where none does -- as Icarus shows a wire with assign y[0] = a alone, zzzx.
                LogicVec start = _sliced[s]
                    ? Shape(ResolveSlices(info, _driversOf[s]), info)
                    : info.Initial.HasValue && _driversOf[s].Length == 0
                        ? Shape(info.Initial.Value, info)
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
                    if (procedural[signal.Index])
                        throw new ArgumentException(
                            $"The stimulus writes {info.Name}, which a process writes too; only a variable nothing else writes can be.");

                    _heap.Push(new Event { Time = time, Seq = ++_seq, Kind = EventKind.Write, Index = signal.Index, Value = Shape(value, info) });
                }
            }
        }

        public Design Design => _design;

        public History History => _history;

        /// <summary>How far the run has got.</summary>
        public long Now => _now;

        /// <summary>Whether the run has stopped for good: finished, or unable to go on.</summary>
        public bool Stopped { get; private set; }

        /// <summary>Whether the design ended its own run, with <c>$finish</c> or <c>$fatal</c>.</summary>
        public bool Finished { get; private set; }

        /// <summary>Whether a <c>$stop</c> has suspended the run: the next call to <see cref="RunTo"/> carries on.</summary>
        public bool Suspended { get; private set; }

        /// <summary>
        /// When the next thing is due to happen, or null when nothing is: every process has ended or waits on something
        /// nothing will change, so running on changes nothing (Part 13b: a testbench with no <c>$stop</c> has ended there).
        /// 0 before the run has started.
        /// </summary>
        public long? NextEventTime => !_started ? 0 : _heap.Count > 0 ? _heap.Peek().Time : (long?)null;

        /// <summary>
        /// The round of time 0 the processes started in, once what has no delay had settled from its starting values: the
        /// round a process's first assignments are made in, where a later one answered something (Part 13c). -1 before.
        /// </summary>
        public int ProcessesStartedIn { get; private set; } = -1;

        /// <summary>A signal's value now.</summary>
        public LogicVec Value(SignalId signal) => _values[signal.Index];

        /// <summary>
        /// Runs every event up to and including <paramref name="time"/>, unless
        /// <paramref name="maxEvents"/> runs out first or a <c>$stop</c> suspends the run. A budget only
        /// pauses between time steps, so one time step always finishes; the next call carries on exactly
        /// where this one stopped, a suspended process included.
        /// </summary>
        public RunResult RunTo(long time, long maxEvents = long.MaxValue)
        {
            RunResult result = Run(time, maxEvents);

            // What the design has written into its files is written out whenever a run hands back.
            FlushFiles(-1);
            return result;
        }

        private RunResult Run(long time, long maxEvents)
        {
            if (time < 0)
                throw new ArgumentOutOfRangeException(nameof(time), time, "Time starts at 0.");

            long processed = 0;

            if (Suspended && !Stopped)
                CarryOn();

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

                if (!Stopped && _history.Recorded > _options.MaxRecorded)
                    StopOnFullHistory();

                if (Suspended && !Stopped)
                    return new RunResult(RunStatus.Suspended, _now, processed);
            }

            return new RunResult(Finished ? RunStatus.Finished : RunStatus.Stopped, _now, processed);
        }

        /// <summary>
        /// Takes up a run a <c>$stop</c> suspended: each process it suspended runs again in the same time
        /// step, which the next step carries on rather than begins.
        /// </summary>
        private void CarryOn()
        {
            Suspended = false;
            foreach (int p in _suspended)
                _heap.Push(new Event { Time = _now, Seq = ++_seq, Kind = EventKind.Resume, Index = p, Token = _generation[p] });
            _suspended.Clear();
            _carryingOn = true;
        }

        /// <summary>Runs one time step to the end of its last delta. Returns the events it processed.</summary>
        private long RunTimeStep(long time)
        {
            // A step a $stop suspended goes on where it left off, its deltas numbered after the ones it had.
            int firstDelta = _carryingOn && time == _now ? _delta + 1 : 0;
            _carryingOn = false;

            _now = time;
            long events = 0;

            if (_time >= 0)
                _values[_time] = _written[_time] = LogicVec.FromUInt64((ulong)time, 64);

            while (_heap.Count > 0 && _heap.Peek().Time == time)
            {
                Event e = _heap.Pop();
                if (e.Kind == EventKind.Nba)
                    _nba.Add(e);
                else
                    _current.Add(e);
            }

            bool first = !_started;
            _started = true;

            for (int delta = firstDelta; ; delta++)
            {
                if (!first && _current.Count == 0 && _toEvaluate.Count == 0 && _runnable.Count == 0)
                {
                    // Nothing active is left. At time 0 the processes start now, once the continuous
                    // logic has settled from its starting values, as Icarus starts them; otherwise the
                    // inactive region (#0) runs next, then the NBA region.
                    if (!_processesStarted)
                    {
                        for (int p = 0; p < _processes.Length; p++)
                            if (!_processes[p].Dormant)
                                _runnable.Add(p);
                        _processesStarted = true;
                        ProcessesStartedIn = delta;
                    }
                    else if (_inactive.Count > 0)
                    {
                        _runnable.AddRange(_inactive);
                        _inactive.Clear();
                    }
                    else if (_nba.Count > 0)
                    {
                        _current.AddRange(_nba);
                        _nba.Clear();
                    }
                    else
                    {
                        break;
                    }
                }

                if (delta - firstDelta >= _options.MaxDeltas)
                {
                    StopOnLoop(time);
                    return events;
                }

                _delta = delta;
                _stamp++;
                events += _current.Count;

                // Phase 1: everything due arrives.
                for (int i = 0; i < _current.Count; i++)
                {
                    Event e = _current[i];

                    switch (e.Kind)
                    {
                        case EventKind.Write:
                            _written[e.Index] = e.Value;
                            MarkDirty(e.Index);
                            continue;

                        case EventKind.Nba:
                            _written[e.Index] = Shape(Splice(_written[e.Index], e.Lowest, e.Value), _design.Signals[e.Index]);
                            MarkDirty(e.Index);
                            continue;

                        case EventKind.Resume:
                            // Dropped if the process has since been ended, or disabled out of its delay.
                            if (e.Token == _generation[e.Index])
                                _runnable.Add(e.Index);
                            continue;

                        case EventKind.ForceUpdate:
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
                    if (_options.RecordChanges && !_design.Signals[s].Local)
                        _history.RecordChange(s, time, delta, value);

                    _changed.Add(s);
                }

                _dirty.Clear();

                // Only once every value is in: an edge on an expression of several signals must not be
                // seen half-made.
                for (int i = 0; i < _changed.Count; i++)
                    Changed(_changed[i]);

                _changed.Clear();

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
                _evalEpoch++;

                // Phase 3: every process woken runs, in the design's order, until it waits.
                if (_runnable.Count > 0)
                {
                    List<int> run = _runnable;
                    _runnable = _running;
                    _running = run;
                    _running.Sort();

                    for (int i = 0; i < _running.Count && !Stopped; i++)
                    {
                        _runningAt = i;
                        RunProcess(_running[i]);
                    }

                    _running.Clear();
                }

                SayFunctionsStopped();

                List<Event> swap = _current;
                _current = _next;
                _next = swap;

                if (Stopped)
                    return events;
            }

            Postponed();
            SayFunctionsStopped();   // a $strobe or $monitor calls functions too

            // $finish ends the run once its time step has settled -- every region, as Icarus finishes
            // it -- so the last values agree with each other: a port shows what it is connected to.
            if (_finishing)
            {
                Finished = true;
                Stopped = true;
                EndLine(Severity.Info);
            }
            else if (_suspending)
            {
                _suspending = false;
                Suspended = true;
                _history.Suspend(time);
            }

            return events;
        }

        /// <summary>The postponed region: the monitor, then every strobe, with the values the step settled on.</summary>
        private void Postponed()
        {
            if (_monitor != null && _monitorOn)
            {
                PrintSegment[] segments = _monitor.Segments;
                bool changed = _monitorLast == null;
                if (_monitorLast == null)
                    _monitorLast = new LogicVec[segments.Length];

                for (int i = 0; i < segments.Length; i++)
                {
                    PrintSegment segment = segments[i];
                    if (segment.Argument == null || segment.Untracked)
                        continue;

                    LogicVec now = segment.Argument.Evaluate(_values);
                    if (!changed && !Matching.SameBits(now, _monitorLast[i]))
                        changed = true;
                    _monitorLast[i] = now;
                }

                if (changed)
                    Print(_monitor.Severity, _monitor.Render(_values), endLine: true);
            }

            if (_strobes.Count > 0)
            {
                foreach (PrintSpec strobe in _strobes)
                    Emit(strobe, strobe.Render(_values), endLine: true);
                _strobes.Clear();
            }
        }

        // -----------------------------------------------------------------
        // Files (Part L6)
        // -----------------------------------------------------------------

        /// <summary>A file a design has opened: its name, and everything written to it so far.</summary>
        private sealed class OpenFile
        {
            public string Name;
            public readonly StringBuilder Text = new StringBuilder();
            public bool Dirty;
        }

        /// <summary>The files open, by descriptor: the top bit set over 3 and up for a mode, a single bit above bit 0 for a channel.</summary>
        private readonly Dictionary<int, OpenFile> _openFiles = new Dictionary<int, OpenFile>();

        /// <summary>
        /// <c>$fopen(name, mode)</c> (IEEE 1800, 21.3.1): a descriptor with the top bit set, as Icarus numbers
        /// them; <c>$fopen(name)</c>: a multichannel one, a bit of its own above bit 0, which is the transcript.
        /// "w" starts the file empty and "a" goes on from what is in it. 0 when it cannot be opened: no files
        /// to write into, or a mode that reads, which BitSorter does not do yet.
        /// </summary>
        private long Open(LogicVec name, LogicVec? mode)
        {
            string path = Strings.Text(name);
            string how = mode.HasValue ? Strings.Text(mode.Value) : null;

            if (_options.Files == null || path.Length == 0)
            {
                _history.Message(_now, Severity.Error, $"$fopen(\"{path}\"): there is nowhere to write a file in this run, so it is not opened.");
                return 0;
            }

            bool append = how != null && how.StartsWith("a", StringComparison.Ordinal);
            if (how != null && !how.StartsWith("w", StringComparison.Ordinal) && !append)
            {
                _history.Message(_now, Severity.Error, $"$fopen(\"{path}\", \"{how}\"): BitSorter writes files with \"w\" or \"a\"; reading one is not supported yet.");
                return 0;
            }

            // The lowest one free, as Icarus gives them: a closed file's number is given again.
            int descriptor;
            if (how == null)
            {
                int channel = 1;
                while (channel <= 30 && _openFiles.ContainsKey(1 << channel))
                    channel++;

                if (channel > 30)
                {
                    _history.Message(_now, Severity.Error, $"$fopen(\"{path}\"): every channel of a multichannel descriptor is in use.");
                    return 0;
                }

                descriptor = 1 << channel;
            }
            else
            {
                int n = 3;
                while (_openFiles.ContainsKey(int.MinValue | n))
                    n++;
                descriptor = int.MinValue | n;
            }

            var file = new OpenFile { Name = path, Dirty = true };
            if (append && _options.Files.TryRead(path, out string before))
                file.Text.Append(before);
            _openFiles[descriptor] = file;
            return descriptor;
        }

        /// <summary>A descriptor's value as a number; 0, which names nothing, when it is unknown.</summary>
        private int Descriptor(Expr e)
        {
            LogicVec value = e.Evaluate(_values);
            return value.IsKnown && value.Slice(0, Math.Min(32, value.Width)).TryGetUInt64(out ulong d) ? unchecked((int)(uint)d) : 0;
        }

        /// <summary>Where a print goes: the transcript, or each file a descriptor names -- one file, or a channel's bits.</summary>
        private void Emit(PrintSpec spec, string text, bool endLine)
        {
            if (spec.Destination == null)
            {
                Print(spec.Severity, text, endLine);
                return;
            }

            int descriptor = Descriptor(spec.Destination);
            if (descriptor < 0)
            {
                // A file descriptor: 1 and 2 are standard output and standard error, the transcript here.
                int n = descriptor & 0x7FFFFFFF;
                if (n == 1 || n == 2)
                    Print(n == 2 ? Severity.Error : spec.Severity, text, endLine);
                else if (_openFiles.TryGetValue(descriptor, out OpenFile file))
                    Append(file, text, endLine);
                return;
            }

            if ((descriptor & 1) != 0)
                Print(spec.Severity, text, endLine);
            foreach (KeyValuePair<int, OpenFile> open in _openFiles)
                if (open.Key > 0 && (descriptor & open.Key) != 0)
                    Append(open.Value, text, endLine);
        }

        private static void Append(OpenFile file, string text, bool endLine)
        {
            file.Text.Append(text);
            if (endLine)
                file.Text.Append('\n');
            file.Dirty = true;
        }

        /// <summary><c>$fclose</c>: the file written out, and its descriptor free.</summary>
        private void Close(int descriptor)
        {
            if (descriptor == 0)
                return;

            var closing = new List<int>();
            foreach (int open in _openFiles.Keys)
                if (open == descriptor || descriptor > 0 && open > 0 && (descriptor & open) != 0)
                    closing.Add(open);

            foreach (int open in closing)
            {
                FlushFiles(open);
                _openFiles.Remove(open);
            }
        }

        /// <summary>Writes out the file a descriptor names, or every open one for -1.</summary>
        private void FlushFiles(int descriptor)
        {
            if (_options.Files == null || _openFiles.Count == 0)
                return;

            foreach (KeyValuePair<int, OpenFile> open in _openFiles)
            {
                if (descriptor != -1 && open.Key != descriptor && !(descriptor > 0 && open.Key > 0 && (descriptor & open.Key) != 0))
                    continue;
                if (!open.Value.Dirty)
                    continue;

                _options.Files.Write(open.Value.Name, open.Value.Text.ToString());
                open.Value.Dirty = false;
            }
        }

        /// <summary>Text for the transcript: each line it completes becomes a message.</summary>
        private void Print(Severity severity, string text, bool endLine)
        {
            _line.Append(text);

            int start = 0;
            string all = _line.ToString();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != '\n')
                    continue;
                _history.Message(_now, severity, all.Substring(start, i - start));
                start = i + 1;
            }

            _line.Clear();
            _line.Append(all, start, all.Length - start);

            if (endLine)
                EndLine(severity, always: true);
        }

        /// <summary>Ends the line being written, if there is one, or an empty one if <paramref name="always"/>.</summary>
        private void EndLine(Severity severity, bool always = false)
        {
            if (_line.Length == 0 && !always)
                return;

            _history.Message(_now, severity, _line.ToString());
            _line.Clear();
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

        /// <summary>A signal changed: its readers evaluate next, and any process waiting on it may wake.</summary>
        private void Changed(int s)
        {
            foreach (int reader in _fanout[s])
                MarkToEvaluate(reader);

            // A force follows its expression, as a continuous assignment would, in the next delta.
            if (_forceReaders != null && _forceReaders[s] != null)
            {
                foreach (ForceRecord force in _forceReaders[s])
                {
                    Evaluate(force);
                    _next.Add(new Event { Time = _now, Seq = ++_seq, Kind = EventKind.ForceUpdate, Index = force.Signal });
                }
            }

            foreach (Waiter w in _waitersOf[s])
            {
                if (_waitingAt[w.Process] == w.Pc && Fired(w.Process, w.Pc))
                {
                    _waitingAt[w.Process] = -1;
                    _runnable.Add(w.Process);
                }
            }
        }

        // -----------------------------------------------------------------
        // Processes
        // -----------------------------------------------------------------

        /// <summary>Runs a process from its counter until it waits, ends, or finishes the run.</summary>
        private void RunProcess(int p)
        {
            Instruction[] code = _processes[p].Code;
            int pc = _pc[p];
            long steps = 0;

            while (true)
            {
                if (++steps > _options.MaxProcessSteps)
                {
                    _pc[p] = pc;
                    StopOnRunaway(p);
                    return;
                }

                ref Instruction ins = ref code[pc];

                switch (ins.Op)
                {
                    case OpCode.Assign:
                        Store(ins.Target, ins.TargetWidth, ins.Value.Evaluate(_values));
                        pc++;
                        break;

                    case OpCode.AssignLater:
                        ScheduleLater(ref ins);
                        pc++;
                        break;

                    case OpCode.Jump:
                        pc = ins.Jump;
                        break;

                    case OpCode.JumpUnless:
                        pc = LogicVec.Truth(ins.Value.Evaluate(_values)) == Logic4.One ? pc + 1 : ins.Jump;
                        break;

                    case OpCode.JumpIf:
                        pc = LogicVec.Truth(ins.Value.Evaluate(_values)) == Logic4.One ? ins.Jump : pc + 1;
                        break;

                    case OpCode.JumpIfMatch:
                        pc = Matching.CaseMatches(ins.Value.Evaluate(_values), ins.Other.Evaluate(_values), ins.Case) ? ins.Jump : pc + 1;
                        break;

                    case OpCode.Delay:
                    {
                        long delay = DelayOf(ins.Value.Evaluate(_values));
                        _pc[p] = pc + 1;
                        _blockedAt[p] = pc;
                        if (delay == 0)
                            _inactive.Add(p);
                        else
                            _heap.Push(new Event { Time = _now + delay, Seq = ++_seq, Kind = EventKind.Resume, Index = p, Token = _generation[p] });
                        return;
                    }

                    case OpCode.WaitEvent:
                        _pc[p] = pc + 1;
                        _blockedAt[p] = pc;
                        StartWaiting(p, pc);
                        return;

                    case OpCode.WaitUntil:
                        if (LogicVec.Truth(ins.Value.Evaluate(_values)) == Logic4.One)
                        {
                            pc++;
                            break;
                        }

                        _pc[p] = pc;
                        _blockedAt[p] = pc;
                        StartWaiting(p, pc);
                        return;

                    case OpCode.Fork:
                    {
                        ProcessInfo[] branches = ins.Fork.Branches;
                        foreach (ProcessInfo branch in branches)
                        {
                            if (!_done[branch.Index])
                            {
                                _pc[p] = pc;
                                StopOnForkAgain(p, branch.Index);
                                return;
                            }
                        }

                        // The branches run once this process waits, in the order they are written (IEEE 1800, 9.3.2).
                        int at = _runningAt + 1;
                        foreach (ProcessInfo branch in branches)
                        {
                            int b = branch.Index;
                            _pc[b] = 0;
                            _done[b] = false;
                            _waitingAt[b] = -1;
                            _joinPc[b] = -1;
                            _blockedAt[b] = -1;
                            _generation[b]++;
                            _running.Insert(at++, b);
                        }

                        if (ins.Fork.Join == JoinKind.None || branches.Length == 0)
                        {
                            pc++;
                            break;
                        }

                        _pc[p] = pc + 1;
                        _blockedAt[p] = pc;
                        _joinPc[p] = pc;
                        return;
                    }

                    case OpCode.WaitFork:
                        if (AllDone(_children[p]))
                        {
                            pc++;
                            break;
                        }

                        _pc[p] = pc + 1;
                        _blockedAt[p] = pc;
                        _joinPc[p] = JoiningAll;
                        return;

                    case OpCode.DisableFork:
                        foreach (int child in _children[p])
                            Kill(child);
                        pc++;
                        break;

                    case OpCode.Disable:
                        pc = RunDisable(p, pc, ins.Disable);
                        if (_done[p])
                            return;   // it was inside what it disabled: a branch of a fork in the block
                        break;

                    case OpCode.Force:
                        Force(ref ins);
                        pc++;
                        break;

                    case OpCode.FileOpen:
                        Store(ins.Target, ins.TargetWidth, LogicVec.FromInt64(Open(ins.Value.Evaluate(_values), ins.Other?.Evaluate(_values)), 32));
                        pc++;
                        break;

                    case OpCode.FileClose:
                        Close(Descriptor(ins.Value));
                        pc++;
                        break;

                    case OpCode.FileFlush:
                        FlushFiles(ins.Value == null ? -1 : Descriptor(ins.Value));
                        pc++;
                        break;

                    case OpCode.Release:
                        Release(ins.Target);
                        pc++;
                        break;

                    case OpCode.Print:
                        RunPrint(ins.Print);
                        pc++;
                        break;

                    case OpCode.ReadMemory:
                        ReadMemory(ins.Memory);
                        pc++;
                        break;

                    case OpCode.WriteMemory:
                        WriteMemory(ins.Memory);
                        pc++;
                        break;

                    case OpCode.Finish:
                        _pc[p] = pc;
                        _done[p] = true;
                        _finishing = true;
                        _history.Message(_now, Severity.Info, $"{ins.Text} at t = {_now}: the run ends here.");
                        return;

                    case OpCode.Stop:
                        // Suspended, not ended: it waits at the next statement for the run to carry on.
                        _pc[p] = pc + 1;
                        _blockedAt[p] = pc;
                        _suspended.Add(p);
                        _suspending = true;
                        _history.Message(_now, Severity.Info, $"{ins.Text} at t = {_now}: the run is suspended here, and carries on when run again.");
                        return;

                    default:
                        _pc[p] = pc;
                        _done[p] = true;
                        _blockedAt[p] = -1;
                        Ended(p);
                        return;
                }
            }
        }

        // -----------------------------------------------------------------
        // fork, disable and force (Part L5)
        // -----------------------------------------------------------------

        private bool AllDone(int[] processes)
        {
            foreach (int p in processes)
                if (!_done[p])
                    return false;
            return true;
        }

        /// <summary>A branch has ended: the process that forked it goes on if its join was waiting for that.</summary>
        private void Ended(int child)
        {
            int parent = _parent[child];
            if (parent < 0 || _done[parent])
                return;

            int at = _joinPc[parent];
            if (at == -1)
                return;

            bool goesOn;
            if (at == JoiningAll)
            {
                goesOn = AllDone(_children[parent]);
            }
            else
            {
                ForkSpec fork = _processes[parent].Code[at].Fork;
                if (Array.IndexOf(fork.Branches, _processes[child]) < 0)
                    return;

                goesOn = fork.Join == JoinKind.Any;
                if (!goesOn)
                {
                    goesOn = true;
                    foreach (ProcessInfo branch in fork.Branches)
                        goesOn &= _done[branch.Index];
                }
            }

            if (!goesOn)
                return;

            _joinPc[parent] = -1;
            _blockedAt[parent] = -1;
            _runnable.Add(parent);
        }

        /// <summary>Takes a process out of whatever it was waiting for, and out of the lists that would run it.</summary>
        private void Cancel(int p)
        {
            _waitingAt[p] = -1;
            _joinPc[p] = -1;
            _generation[p]++;
            Remove(_runnable, p, 0);
            Remove(_inactive, p, 0);
            Remove(_running, p, _runningAt + 1);
            _suspended.Remove(p);
        }

        private static void Remove(List<int> list, int p, int from)
        {
            for (int i = list.Count - 1; i >= from; i--)
                if (list[i] == p)
                    list.RemoveAt(i);
        }

        /// <summary>Ends a process from outside -- <c>disable fork</c>, or the block that forked it disabled -- and every one it forked.</summary>
        private void Kill(int p)
        {
            if (!_done[p])
            {
                Cancel(p);
                _done[p] = true;
                _blockedAt[p] = -1;
            }

            foreach (int child in _children[p])
                Kill(child);
        }

        /// <summary>Ends the branches of every fork from <paramref name="range"/> in process <paramref name="p"/>.</summary>
        private void KillForksIn(int p, BlockRange range)
        {
            Instruction[] code = _processes[p].Code;
            for (int i = Math.Max(0, range.Start); i < Math.Min(code.Length, range.End); i++)
                if (code[i].Op == OpCode.Fork)
                    foreach (ProcessInfo branch in code[i].Fork.Branches)
                        Kill(branch.Index);
        }

        /// <summary>
        /// <c>disable</c> a named block or a task (IEEE 1800, 9.6.2): every process inside one of its places
        /// goes on after it -- this one at once, any other in the next delta -- and the processes a fork in it
        /// started end. Returns the counter this process goes on from.
        /// </summary>
        private int RunDisable(int p, int pc, DisableSpec spec)
        {
            int next = pc + 1;
            foreach (BlockRange range in spec.Ranges)
            {
                ProcessInfo owner = range.Process;
                int q = owner?.Index ?? -1;
                if (q < 0 || q >= _processes.Length || _processes[q] != owner)
                    continue;

                if (range.Fork)
                    KillForksIn(q, range);

                if (q == p)
                {
                    if (next == pc + 1 && pc >= range.Start && pc < range.End)
                    {
                        KillForksIn(p, range);
                        next = range.End;
                    }

                    continue;
                }

                if (_done[q] || _blockedAt[q] < range.Start || _blockedAt[q] >= range.End)
                    continue;

                KillForksIn(q, range);
                Cancel(q);
                _pc[q] = range.End;
                _blockedAt[q] = -1;
                _runnable.Add(q);
            }

            return next;
        }

        private void StopOnForkAgain(int p, int branch)
        {
            Stopped = true;
            _history.Message(_now, Severity.Error,
                $"At t = {_now} {_processes[p].Name} forked {_processes[branch].Name} again while it was still running from the fork " +
                "before, and BitSorter runs one of each branch at a time. Wait for it first (join, wait fork), or end it (disable fork).");
        }

        /// <summary><c>force</c> (IEEE 1800, 10.6.2): each piece of the target takes its bits of the value, and keeps following it.</summary>
        private void Force(ref Instruction ins)
        {
            int signals = _values.Length;
            if (_forces == null)
            {
                _forces = new List<ForceRecord>[signals];
                _forceReaders = new List<ForceRecord>[signals];
            }

            var reads = new HashSet<int>();
            ins.Value.CollectReads(reads);
            var readList = new int[reads.Count];
            reads.CopyTo(readList);

            int offset = ins.TargetWidth;
            foreach (LPiece piece in ins.Target)
            {
                offset -= piece.Width;
                Unforce(piece.Signal, piece.Lowest, piece.Width);

                var force = new ForceRecord
                {
                    Signal = piece.Signal,
                    Lowest = piece.Lowest,
                    Width = piece.Width,
                    Offset = offset,
                    Total = ins.TargetWidth,
                    Value = ins.Value,
                    Reads = readList,
                };

                (_forces[piece.Signal] ?? (_forces[piece.Signal] = new List<ForceRecord>())).Add(force);
                foreach (int s in readList)
                    (_forceReaders[s] ?? (_forceReaders[s] = new List<ForceRecord>())).Add(force);

                Evaluate(force);
                Refresh(piece.Signal);
            }
        }

        /// <summary>
        /// <c>release</c>: a wire goes back to what drives it; a variable nothing drives keeps the value it
        /// was forced to until it is next written (IEEE 1800, 10.6.2).
        /// </summary>
        private void Release(LPiece[] target)
        {
            foreach (LPiece piece in target)
            {
                if (!Unforce(piece.Signal, piece.Lowest, piece.Width))
                    continue;

                int s = piece.Signal;
                SignalInfo info = _design.Signals[s];
                if (info.Kind == SignalKind.Variable && _driversOf[s].Length == 0)
                    _written[s] = Shape(Splice(_written[s], piece.Lowest, _values[s].Slice(piece.Lowest, piece.Width)), info);

                Refresh(s);
            }
        }

        /// <summary>Takes away every force on the bits given; whether there was one.</summary>
        private bool Unforce(int s, int lowest, int width)
        {
            List<ForceRecord> forces = _forces?[s];
            if (forces == null)
                return false;

            bool any = false;
            for (int i = forces.Count - 1; i >= 0; i--)
            {
                ForceRecord force = forces[i];
                if (force.Lowest >= lowest + width || force.Lowest + force.Width <= lowest)
                    continue;

                forces.RemoveAt(i);
                foreach (int read in force.Reads)
                    _forceReaders[read]?.Remove(force);
                any = true;
            }

            return any;
        }

        private void Evaluate(ForceRecord force) =>
            force.Current = Fit(force.Value.Evaluate(_values), force.Total).Slice(force.Offset, force.Width);

        /// <summary>A signal's forces laid over its own value.</summary>
        private LogicVec Overlay(int s, LogicVec value)
        {
            List<ForceRecord> forces = _forces?[s];
            if (forces == null || forces.Count == 0)
                return value;

            foreach (ForceRecord force in forces)
                value = Splice(value, force.Lowest, force.Current);
            return Shape(value, _design.Signals[s]);
        }

        /// <summary>A signal's value worked out again now, as a blocking write's is: what a force or a release changes.</summary>
        private void Refresh(int s)
        {
            LogicVec value = Resolve(s);
            if (value.Equals(_values[s]))
                return;

            _values[s] = value;
            if (_options.RecordChanges && !_design.Signals[s].Local)
                _history.RecordChange(s, _now, _delta, value);

            Changed(s);
        }

        private void RunPrint(PrintSpec spec)
        {
            switch (spec.Kind)
            {
                case PrintKind.Display:
                    Emit(spec, spec.Render(_values), endLine: true);
                    return;
                case PrintKind.Write:
                    Emit(spec, spec.Render(_values), endLine: false);
                    return;
                case PrintKind.Strobe:
                    _strobes.Add(spec);
                    return;
                case PrintKind.Monitor:
                    // A new $monitor replaces the one before, and prints at the end of this step.
                    _monitor = spec;
                    _monitorLast = null;
                    return;
                case PrintKind.MonitorOn:
                    _monitorOn = true;
                    _monitorLast = null;
                    return;
                case PrintKind.MonitorOff:
                    _monitorOn = false;
                    return;
            }
        }

        /// <summary>A whole-number task argument, or <paramref name="fallback"/> when there is none or it is unknown.</summary>
        private long Number(Expr e, long fallback)
        {
            if (e == null)
                return fallback;

            LogicVec value = e.Evaluate(_values);
            return value.IsKnown && value.TryGetInt64(out long n) ? n : fallback;
        }

        private void ReadMemory(MemoryTask task)
        {
            MemoryInfo memory = task.Memory;
            string text = null;

            if (_options.Files == null || !_options.Files.TryRead(task.File, out text))
            {
                _history.Message(_now, Severity.Error,
                    $"{task.Task} ({task.Where}): there is no file called {task.File}, so {memory.Name} is left as it was.");
                return;
            }

            var items = new List<MemoryImage.Item>();
            if (!MemoryImage.TryRead(text, task.Hex, memory.Width, items, out string error))
            {
                _history.Message(_now, Severity.Error, $"{task.Task}({task.File}) {error}; {memory.Name} is left as it was.");
                return;
            }

            long start = Number(task.Start, memory.Lowest);
            long end = Number(task.End, memory.Highest);
            long low = Math.Min(start, end), high = Math.Max(start, end);

            if (low < memory.Lowest || high > memory.Highest)
            {
                _history.Message(_now, Severity.Error,
                    $"{task.Task}({task.File}): words {start} to {end} are outside {memory.Name}, which has {memory.Lowest} to {memory.Highest}.");
                return;
            }

            long step = start <= end ? 1 : -1;
            long at = start;
            int loaded = 0;
            bool addressed = false;

            foreach (MemoryImage.Item item in items)
            {
                if (item.IsAddress)
                {
                    addressed = true;
                    at = item.Address;
                    if (at < low || at > high)
                    {
                        _history.Message(_now, Severity.Error,
                            $"{task.Task}({task.File}) line {item.Line}: @{item.Address:x} is outside the words being loaded, {low} to {high}.");
                        return;
                    }

                    continue;
                }

                if (at < low || at > high)
                {
                    _history.Message(_now, Severity.Warning,
                        $"{task.Task}({task.File}) line {item.Line}: more words than {memory.Name} has room for from {start}; the rest are not loaded.");
                    return;
                }

                Write(memory.First.Index + (int)(at - memory.Lowest), 0, item.Word);
                loaded++;
                at += step;
            }

            if (!addressed && loaded < high - low + 1)
                _history.Message(_now, Severity.Warning,
                    $"{task.Task}({task.File}): the file has {loaded} words for the {high - low + 1} of {memory.Name}; the rest are left as they were.");
        }

        private void WriteMemory(MemoryTask task)
        {
            MemoryInfo memory = task.Memory;
            long start = Number(task.Start, memory.Lowest);
            long end = Number(task.End, memory.Highest);

            if (Math.Min(start, end) < memory.Lowest || Math.Max(start, end) > memory.Highest)
            {
                _history.Message(_now, Severity.Error,
                    $"{task.Task}({task.File}): words {start} to {end} are outside {memory.Name}, which has {memory.Lowest} to {memory.Highest}.");
                return;
            }

            if (_options.Files == null)
            {
                _history.Message(_now, Severity.Error, $"{task.Task} ({task.Where}): there is nowhere to write {task.File}.");
                return;
            }

            var words = new List<LogicVec>();
            long step = start <= end ? 1 : -1;
            for (long at = start; ; at += step)
            {
                words.Add(_values[memory.First.Index + (int)(at - memory.Lowest)]);
                if (at == end)
                    break;
            }

            _options.Files.Write(task.File, MemoryImage.Write(words, task.Hex));
        }

        private void StartWaiting(int p, int pc)
        {
            _waitingAt[p] = pc;

            WaitSpec wait = _processes[p].Code[pc].Wait;
            if (wait == null)
                return;

            LogicVec[] last = _last[p];
            for (int i = 0; i < wait.Items.Length; i++)
                last[i] = wait.Items[i].Value.Evaluate(_values);
        }

        /// <summary>Whether a change wakes a process waiting at <paramref name="pc"/>; it notes the new values either way.</summary>
        private bool Fired(int p, int pc)
        {
            ref Instruction ins = ref _processes[p].Code[pc];

            if (ins.Op == OpCode.WaitUntil)
                return LogicVec.Truth(ins.Value.Evaluate(_values)) == Logic4.One;

            EventItem[] items = ins.Wait.Items;
            LogicVec[] last = _last[p];
            bool fired = false;

            for (int i = 0; i < items.Length; i++)
            {
                LogicVec now = items[i].Value.Evaluate(_values);
                if (!fired && Matching.Fires(items[i].Edge, last[i], now))
                    fired = true;
                last[i] = now;
            }

            return fired;
        }

        /// <summary>A delay's value in time units: x or z is 0, as the standard has it, and so is a negative one.</summary>
        private long DelayOf(LogicVec value)
        {
            if (!value.IsKnown)
                return 0;

            if (value.IsSigned)
                return value.TryGetInt64(out long signed) && signed > 0 ? Math.Min(signed, long.MaxValue - _now) : 0;

            return value.TryGetUInt64(out ulong unsigned) ? (long)Math.Min(unsigned, (ulong)(long.MaxValue - _now)) : long.MaxValue - _now;
        }

        /// <summary>A blocking write: each piece of the target takes its bits of the value, now.</summary>
        private void Store(LPiece[] target, int total, LogicVec value)
        {
            if (value.Width != total)
                value = value.Extend(total, false);

            int offset = total;
            foreach (LPiece piece in target)
            {
                offset -= piece.Width;
                LogicVec part = target.Length == 1 ? value : value.Slice(offset, piece.Width);

                int lowest = piece.Lowest;
                if (piece.Offset != null && !TryOffset(piece.Offset, out lowest))
                    continue;

                if (!TryWord(piece, out int signal))
                    continue;

                Write(signal, lowest, part);
            }
        }

        /// <summary>The signal a piece writes: its own, or the word of a memory its index picks; false if that is unknown or outside.</summary>
        private bool TryWord(in LPiece piece, out int signal)
        {
            signal = piece.Signal;
            if (piece.Word == null)
                return true;

            if (!TryOffset(piece.Word, out int word) || word < 0 || word >= piece.Depth)
                return false;

            signal += word;
            return true;
        }

        private void Write(int s, int lowest, LogicVec part)
        {
            SignalInfo info = _design.Signals[s];
            if (!Clip(info.Width, ref lowest, ref part))
                return;

            LogicVec next = Shape(Splice(_written[s], lowest, part), info);
            _written[s] = next;

            // A forced variable takes the write underneath, and shows the force until it is released.
            if (_forces != null)
                next = Overlay(s, next);

            if (next.Equals(_values[s]))
                return;

            _values[s] = next;
            if (_options.RecordChanges && !info.Local)
                _history.RecordChange(s, _now, _delta, next);

            Changed(s);
        }

        /// <summary>A nonblocking write: the value and where it goes are worked out now; it lands in an NBA region.</summary>
        private void ScheduleLater(ref Instruction ins)
        {
            LogicVec value = ins.Value.Evaluate(_values);
            if (value.Width != ins.TargetWidth)
                value = value.Extend(ins.TargetWidth, false);

            long delay = ins.Other == null ? 0 : DelayOf(ins.Other.Evaluate(_values));

            int offset = ins.TargetWidth;
            foreach (LPiece piece in ins.Target)
            {
                offset -= piece.Width;
                LogicVec part = ins.Target.Length == 1 ? value : value.Slice(offset, piece.Width);

                int lowest = piece.Lowest;
                if (piece.Offset != null && !TryOffset(piece.Offset, out lowest))
                    continue;

                if (!TryWord(piece, out int signal))
                    continue;

                if (!Clip(_design.Signals[signal].Width, ref lowest, ref part))
                    continue;

                var e = new Event { Time = _now + delay, Seq = ++_seq, Kind = EventKind.Nba, Index = signal, Lowest = lowest, Value = part };
                if (delay == 0)
                    _nba.Add(e);
                else
                    _heap.Push(e);
            }
        }

        /// <summary>Where a select worked out at run time puts its piece; false if that is unknown.</summary>
        private bool TryOffset(Expr offset, out int lowest) => TryOffset(offset, _values, out lowest);

        /// <summary>A select's place, worked out from <paramref name="values"/>: false when it is unknown or too large.</summary>
        internal static bool TryOffset(Expr offset, LogicVec[] values, out int lowest)
        {
            lowest = 0;
            LogicVec value = offset.Evaluate(values);
            if (!value.IsKnown)
                return false;

            if (value.IsSigned)
            {
                if (!value.TryGetInt64(out long signed) || signed < int.MinValue || signed > int.MaxValue)
                    return false;
                lowest = (int)signed;
                return true;
            }

            if (!value.TryGetUInt64(out ulong unsigned) || unsigned > int.MaxValue)
                return false;

            lowest = (int)unsigned;
            return true;
        }

        /// <summary>The part of a piece that lands inside a signal of <paramref name="width"/> bits; false if none does.</summary>
        internal static bool Clip(int width, ref int lowest, ref LogicVec part)
        {
            long top = (long)lowest + part.Width;
            if (lowest >= width || top <= 0)
                return false;

            if (lowest < 0)
            {
                part = part.Slice(-lowest, (int)top);
                lowest = 0;
            }

            if (lowest + part.Width > width)
                part = part.Slice(0, width - lowest);

            return true;
        }

        // -----------------------------------------------------------------
        // Values
        // -----------------------------------------------------------------

        /// <summary>A signal's value from its drivers or from what was last written, with any force laid over it.</summary>
        private LogicVec Resolve(int s) => _forces == null ? ResolveOwn(s) : Overlay(s, ResolveOwn(s));

        /// <summary>A signal's value from its drivers -- resolved as a wire if it has several -- or from what was last written.</summary>
        private LogicVec ResolveOwn(int s)
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
        internal static LogicVec Splice(LogicVec into, int lowest, LogicVec part)
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

        // -----------------------------------------------------------------
        // Stopping
        // -----------------------------------------------------------------

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

            foreach (int p in _runnable)
            {
                if (shown == 8)
                    break;

                if (names.Length > 0) names.Append(", ");
                names.Append(_processes[p].Name);
                shown++;
            }

            Stopped = true;
            _history.Message(time, Severity.Error,
                $"At t = {time} the circuit was still changing after {_options.MaxDeltas} delta cycles: " +
                $"a loop with no delay never settles. Still changing: {names}. Give a gate in the loop a delay, " +
                "or break the loop.");
        }

        /// <summary>
        /// A function stopped short since the last time this asked -- a loop that never ends, or calls with no way out
        /// (<see cref="FunctionRunner"/>) -- said at the time it happened, each reason once a run.
        /// </summary>
        private void SayFunctionsStopped()
        {
            if (FunctionRunner.Stopped == null)
                return;
            string stopped = FunctionRunner.TakeStopped();
            if (_functionsStopped.Add(stopped))
                _history.Message(_now, Severity.Error, $"At t = {_now} {stopped}");
        }

        private void StopOnRunaway(int p)
        {
            Stopped = true;
            _history.Message(_now, Severity.Error,
                $"At t = {_now} {_processes[p].Name} ran {_options.MaxProcessSteps:N0} steps without waiting: a loop with no " +
                "delay or event control never hands back. Give it a #delay or an @(...).");
        }

        private void StopOnFullHistory()
        {
            Stopped = true;
            _history.Message(_now, Severity.Error,
                $"At t = {_now} the run had kept {_history.Recorded.ToString("N0", CultureInfo.InvariantCulture)} changes, " +
                $"transitions and lines printed, more than the {_options.MaxRecorded.ToString("N0", CultureInfo.InvariantCulture)} " +
                "a run may keep. Run to an earlier time, or end the testbench with $finish.");
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
            if (_driverStamp[d] == _evalEpoch)
                return;

            _driverStamp[d] = _evalEpoch;
            _toEvaluate.Add(d);
        }

        /// <summary>A value at a signal's width and signedness: how it is held.</summary>
        /// <summary>
        /// A value as <paramref name="info"/> holds it: at its width and signedness, and -- for a 2-state
        /// signal -- with every x and z as 0. Every value a signal takes comes through here: its start,
        /// what a stimulus, a process, a nonblocking write or its drivers give it.
        /// </summary>
        internal static LogicVec Shape(LogicVec value, SignalInfo info)
        {
            LogicVec sized = value.Width == info.Width ? value : value.Extend(info.Width, false);
            LogicVec typed = info.Signed ? sized.AsSigned() : sized.AsUnsigned();
            return info.TwoState ? typed.ToTwoState() : typed;
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
