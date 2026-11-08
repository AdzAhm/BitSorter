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

        /// <summary>
        /// How time 0 begins (M5): ModelSim's way wherever BitSorter runs, Icarus's for the Icarus oracle. The standard leaves
        /// it open; see <see cref="Sim.StartOrder"/>.
        /// </summary>
        public StartOrder StartOrder { get; set; } = StartOrder.ModelSim;

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
    /// step are applied (the NBA region), which may wake more. Only then does time move on. They land in
    /// turn (IEEE 1800, 10.4.2): one to bits an earlier one of the region wrote waits for a delta of its
    /// own, so the value between is a change, and its edge wakes what waits on it; what they wake runs
    /// once all have landed (<see cref="TakeNba"/>).
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
    /// time they run; <c>$write</c> leaves its line open for the next, which is shown as it stands once the
    /// run hands back for good or suspends (<see cref="RunTo"/>). <c>$monitor</c> and <c>$strobe</c>
    /// print once the time step has settled, the monitor first, as Icarus orders them; the monitor only
    /// when one of its values other than the time changed.
    ///
    /// **Time 0.** Every variable starts at x, or at the value it was declared with; every net with a
    /// driver at x, every net without one at z. In the first delta every driver evaluates once, as the
    /// standard has gates and continuous assignments do. Then it begins as <see cref="SimOptions.StartOrder"/>
    /// says (M5). ModelSim's way, the default: in that first delta every <c>always</c> that begins with an event
    /// control sets itself waiting, then the other processes run as written, <c>always_comb</c> and
    /// <c>always_latch</c> last -- so an <c>initial</c> that prints a wire at once sees x, and the drivers' first
    /// values arrive a delta later as changes the waiting processes see. Icarus's: once what has no delay has
    /// settled, every process starts in the design's order, so an <c>initial</c> sees the wire worked out.
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

        /// <summary>
        /// The values <c>force</c> and a procedural <c>assign</c> lay over each signal, and for each signal the ones that read
        /// it; null until there is one.
        /// </summary>
        private List<ForceRecord>[] _forces, _forceReaders;

        /// <summary>
        /// One <c>force</c>, or one procedural <c>assign</c> (<see cref="Assigned"/>, M4): its expression's bits from
        /// <see cref="Offset"/>, over the target's bits from <see cref="Lowest"/>.
        /// </summary>
        private sealed class ForceRecord
        {
            public int Signal, Lowest, Width, Offset, Total;
            public Expr Value;
            public int[] Reads;
            public LogicVec Current;

            /// <summary>
            /// A procedural continuous assignment's (IEEE 1800, 10.6.1): laid under every force -- a force wins until it is
            /// released, and then the assign shows again -- and taken away by <c>deassign</c>, never by <c>release</c>.
            /// </summary>
            public bool Assigned;

            /// <summary>A force's value worked out again, which <see cref="Current"/> takes as its update lands, in the next delta.</summary>
            public LogicVec Pending;

            /// <summary>An assign's being worked out again now: what it changes does not set it going again (M4's review).</summary>
            public bool Updating;
        }

        /// <summary>For each signal, the waits that read it.</summary>
        private readonly Waiter[][] _waitersOf;

        private readonly EventHeap _heap = new EventHeap();
        private List<Event> _current = new List<Event>();
        private List<Event> _next = new List<Event>();
        private readonly List<Event> _nba = new List<Event>();

        // The NBA region's updates land in turn (IEEE 1800, 10.4.2; Ahmad's decision, 2026-11-03): as many as land together
        // as touch bits apart, the rest in the deltas after, before anything they wake runs. Where the region has got to in
        // _nba, and for each signal the bits the delta landing now has written, by the chunk's number.
        private int _nbaAt;
        private bool _nbaLanding;
        private int _nbaChunk;
        private readonly int[] _nbaStamp, _nbaLow, _nbaHigh;
        private List<int> _runnable = new List<int>();
        private List<int> _running = new List<int>();
        private readonly List<int> _inactive = new List<int>();

        private readonly int[] _signalStamp;
        private readonly int[] _driverStamp;
        private int _stamp;

        // The race warning (Part 15i-3): within one region of one moment, each change of value carries the process it came
        // from, through drivers of no delay; a process an edge wakes is told of a change it reads, from a process neither it
        // nor the one that made the edge, by a blocking write. Never a nonblocking write: that lands in the NBA region,
        // after every process the edge woke has run.

        private const int NoOrigin = -1, SeveralOrigins = -2;

        /// <summary>The region under way: moved on at every moment, and before its inactive and its NBA regions.</summary>
        private long _region;

        /// <summary>Per signal: the process its change in <see cref="_region"/> came from, and the signal that process wrote.</summary>
        private readonly int[] _origin, _written0;
        private readonly long[] _originRegion;

        /// <summary>Per driver: where what it read this region came from.</summary>
        private readonly int[] _driverOrigin, _driverWritten;
        private readonly long[] _driverOriginRegion;

        /// <summary>Per signal: where a driver's update put on it this region came from, taken if its value changes.</summary>
        private readonly int[] _arriving, _arrivingWritten;
        private readonly long[] _arrivingRegion;

        /// <summary>Per process: whether a change woke it this region, by an edge or not, its signal and where it came from.</summary>
        private readonly long[] _wokenRegion;
        private readonly bool[] _wokenByEdge;
        private readonly int[] _wokenOrigin;
        private readonly int[] _wokenSignal;

        /// <summary>Where the running process's blocking writes come from: itself, or what woke it by a change of value.</summary>
        private int _writer = NoOrigin;

        /// <summary>
        /// The signal its origin wrote, for the warning to name: what the running process writes, or -- woken by a change, as
        /// an always_comb is -- what the process the change came from wrote.
        /// </summary>
        private int _writerWrote = -1;

        /// <summary>Per process woken by a change: the signal the change's origin wrote.</summary>
        private readonly int[] _wokenWrote;

        /// <summary>Each process an edge woke this region: where the edge came from, its signal, and where it took up.</summary>
        private readonly List<(int Process, int Origin, int Clock, int Pc)> _sampled = new List<(int, int, int, int)>();

        /// <summary>
        /// What a process reads from where it takes up to where it next waits -- by its process and that counter -- the
        /// reads one wake makes, never its whole code's (the 15i-3 review: a check a moment after the edge was taken for one
        /// on it). Made when first needed.
        /// </summary>
        private readonly Dictionary<long, HashSet<int>> _readsFrom = new Dictionary<long, HashSet<int>>();

        /// <summary>Per process: what woke it this region -- the change's origin -- kept for the race's chain of causes.</summary>
        private readonly int[] _wakerOf;
        private readonly long[] _wakerRegion;

        /// <summary>The signals given an origin this region, for the race check to walk rather than every read.</summary>
        private readonly List<int> _markedInRegion = new List<int>();

        /// <summary>Each race said, by its pair of processes: once a run.</summary>
        private readonly HashSet<long> _racesSaid = new HashSet<long>();
        private int _evalEpoch = 1;
        private readonly List<int> _dirty = new List<int>();
        private readonly List<int> _changed = new List<int>();
        private readonly List<int> _toEvaluate = new List<int>();

        private long _seq;
        private long _tokens;
        private bool _started;
        private bool _processesStarted;

        /// <summary>The processes in the order they start at time 0, under <see cref="StartOrder.ModelSim"/>; null under Icarus's.</summary>
        private readonly int[] _startOrder;
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
            _nbaStamp = new int[signals];
            _nbaLow = new int[signals];
            _nbaHigh = new int[signals];
            _origin = new int[signals];
            _written0 = new int[signals];
            _originRegion = new long[signals];
            _arriving = new int[signals];
            _arrivingWritten = new int[signals];
            _arrivingRegion = new long[signals];
            _driverOrigin = new int[drivers];
            _driverWritten = new int[drivers];
            _driverOriginRegion = new long[drivers];
            _wokenRegion = new long[processes];
            _wokenByEdge = new bool[processes];
            _wokenOrigin = new int[processes];
            _wokenSignal = new int[processes];
            _wokenWrote = new int[processes];
            _wakerOf = new int[processes];
            _wakerRegion = new long[processes];
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

            _startOrder = _options.StartOrder == StartOrder.ModelSim ? ModelSimStart.Of(design) : null;
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
        /// The round of time 0 the processes started in -- the first, as ModelSim begins time 0, or once what has no delay had
        /// settled from its starting values, as Icarus does (M5): the round a process's first assignments are made in, where a
        /// later one answered something (Part 13c). -1 before.
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

            // A line $write began and nothing ended is shown once the run hands back for good or suspends -- stopped, at a
            // $stop, or with nothing left to run -- as a simulator shows what was written (the 16d sweep, engine-11). Paused,
            // or at a time with more to come, it stays open for the next call to go on with.
            if (_line.Length > 0 && (Stopped || Suspended || _heap.Count == 0))
                EndLine(Severity.Info);

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
            _region++;
            _sampled.Clear();
            _markedInRegion.Clear();

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

            // As ModelSim begins time 0 (M5), a stimulus's writes at time 0 are a testbench's, which an initial would make: they
            // arrive after the processes start, so an always waiting on one sees it -- as the exported testbench's would be.
            if (first && _startOrder != null)
                for (int i = _current.Count - 1; i >= 0; i--)
                    if (_current[i].Kind == EventKind.Write)
                    {
                        _next.Insert(0, _current[i]);
                        _current.RemoveAt(i);
                    }

            // The deltas the NBA region's updates take to land in turn: as many as it has updates at most, never a loop.
            int landed = 0;

            for (int delta = firstDelta; ; delta++)
            {
                if (!first && _current.Count == 0 && _toEvaluate.Count == 0 && _runnable.Count == 0)
                {
                    // Nothing active is left. At time 0, as Icarus begins it (StartOrder.Icarus), the
                    // processes start now, once the continuous logic has settled from its starting values;
                    // otherwise the inactive region (#0) runs next, then the NBA region.
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
                        CheckRaces();
                        _region++;
                        _runnable.AddRange(_inactive);
                        _inactive.Clear();
                    }
                    else if (_nba.Count > 0)
                    {
                        CheckRaces();
                        _region++;
                        _nbaLanding = true;
                        TakeNba(_current);
                    }
                    else
                    {
                        CheckRaces();
                        break;
                    }
                }

                if (delta - firstDelta - landed >= _options.MaxDeltas)
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
                            foreach (ForceRecord force in _forces[e.Index])
                                if (!force.Assigned)
                                    force.Current = force.Pending;
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

                    int driven = _drivers[d].Target.Index;
                    if (_driverOriginRegion[d] == _region)
                        Arrive(driven, _driverOrigin[d], _driverWritten[d]);
                    MarkDirty(driven);
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
                    if (_arrivingRegion[s] == _region)
                        MarkOrigin(s, _arriving[s], _arrivingWritten[s]);

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

                // The rest of the NBA region's updates land first, in the deltas after this one: what they wake -- drivers and
                // processes alike -- waits until every one has, and then reads the value the last one left, as Icarus and
                // ModelSim run it. A process waiting on the edge a value between makes has been woken by it all the same.
                if (_nbaLanding && _nbaAt < _nba.Count)
                {
                    TakeNba(_next);
                    landed++;
                    SwapCurrent();
                    continue;
                }

                _nbaLanding = false;

                // Phase 2: every driver that reads a change decides its next output.
                for (int i = 0; i < _toEvaluate.Count; i++)
                {
                    int d = _toEvaluate[i];
                    LogicVec output = _drivers[d].Evaluate(_values);
                    Schedule(d, Fit(output, _drivers[d].TargetWidth), time);
                }

                _toEvaluate.Clear();
                _evalEpoch++;

                // ModelSim's time 0 (M5): the processes start in the first delta, in its order, before the drivers' first values
                // arrive -- each always that begins with an event control waiting first, so it sees them and every other change.
                bool starting = false;
                if (_startOrder != null && !_processesStarted)
                {
                    _runnable.AddRange(_startOrder);
                    _processesStarted = true;
                    ProcessesStartedIn = delta;
                    starting = true;
                }

                // Phase 3: every process woken runs, in the design's order, until it waits.
                if (_runnable.Count > 0)
                {
                    List<int> run = _runnable;
                    _runnable = _running;
                    _running = run;
                    if (!starting)
                        _running.Sort();

                    for (int i = 0; i < _running.Count && !Stopped; i++)
                    {
                        _runningAt = i;
                        RunProcess(_running[i]);
                    }

                    _running.Clear();
                }

                SayFunctionsStopped();
                SwapCurrent();

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

        /// <summary>What the next delta takes up: what this one put off.</summary>
        private void SwapCurrent()
        {
            List<Event> swap = _current;
            _current = _next;
            _next = swap;
        }

        /// <summary>
        /// The NBA region's next updates, into <paramref name="into"/>: in order, as many as touch bits apart -- an update to
        /// bits within those an earlier one of them wrote waits for a delta of its own, so the value between is a value of
        /// its own, and an edge it makes wakes what waits on it (IEEE 1800, 10.4.2). Bits apart land together, as both
        /// oracles land them: a wait on an expression of them sees no value between.
        /// </summary>
        private void TakeNba(List<Event> into)
        {
            _nbaChunk++;
            int at = _nbaAt;
            for (; at < _nba.Count; at++)
            {
                Event e = _nba[at];
                int s = e.Index, low = e.Lowest, high = e.Lowest + e.Value.Width;
                if (_nbaStamp[s] == _nbaChunk)
                {
                    if (low < _nbaHigh[s] && high > _nbaLow[s])
                        break;
                    _nbaLow[s] = Math.Min(_nbaLow[s], low);
                    _nbaHigh[s] = Math.Max(_nbaHigh[s], high);
                }
                else
                {
                    _nbaStamp[s] = _nbaChunk;
                    _nbaLow[s] = low;
                    _nbaHigh[s] = high;
                }

                into.Add(e);
            }

            _nbaAt = at;
            if (_nbaAt == _nba.Count)
            {
                _nba.Clear();
                _nbaAt = 0;
            }
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
            long token = ++_tokens;

            _pendingToken[d] = token;
            _pendingValue[d] = value;

            // A delay past what a run can count never comes due, as a process's does (DelayOf): the change is on its way,
            // so a later one still takes its place, but no event is made for it and no transition is drawn. Added to the
            // time unchecked it came due at once, at a negative time (the 16d sweep, engine-4).
            if (delay > long.MaxValue - now)
            {
                _pendingTransition[d] = -1;
                return;
            }

            long due = now + delay;
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
            int origin = _originRegion[s] == _region ? _origin[s] : NoOrigin;
            foreach (int reader in _fanout[s])
            {
                MarkToEvaluate(reader);
                if (origin != NoOrigin)
                    DriverReads(reader, origin, _written0[s]);
            }

            // A force follows its expression, as a continuous assignment would, in the next delta: until its update lands it
            // shows what it was, whatever else is written meanwhile. A procedural assign follows at once, as ModelSim has it --
            // a read straight after what it reads changes sees the new value -- and is not set going again by what it changes
            // itself, so assign count = count + 1 adds one, and two that read each other settle (Docs/oracle-notes.md).
            if (_forceReaders != null && _forceReaders[s] != null)
            {
                foreach (ForceRecord force in _forceReaders[s])
                {
                    if (force.Assigned)
                    {
                        if (!force.Updating)
                            Apply(force);
                        continue;
                    }

                    force.Pending = Value(force);
                    _next.Add(new Event { Time = _now, Seq = ++_seq, Kind = EventKind.ForceUpdate, Index = force.Signal });
                }
            }

            foreach (Waiter w in _waitersOf[s])
            {
                if (_waitingAt[w.Process] == w.Pc && Fired(w.Process, w.Pc, out bool edge, out int by))
                {
                    _waitingAt[w.Process] = -1;
                    _runnable.Add(w.Process);

                    // Woken by what fired, which need not be the change under way: a clock falling as a reset rises, the
                    // two in one delta, wakes @(posedge clk, posedge rst) by the reset (the 15i-3 review's exercise).
                    int trigger = by >= 0 ? by : s;
                    int from = _originRegion[trigger] == _region ? _origin[trigger] : NoOrigin;
                    _wokenRegion[w.Process] = _region;
                    _wokenByEdge[w.Process] = edge;
                    _wokenOrigin[w.Process] = from;
                    _wokenSignal[w.Process] = trigger;
                    _wakerOf[w.Process] = from;
                    _wakerRegion[w.Process] = _region;
                    _wokenWrote[w.Process] = from != NoOrigin ? _written0[trigger] : -1;
                }
            }
        }

        // -----------------------------------------------------------------
        // Processes
        // -----------------------------------------------------------------

        /// <summary>Runs a process from its counter until it waits, ends, or finishes the run.</summary>
        private void RunProcess(int p)
        {
            // Its blocking writes come from itself -- woken by time, or by an edge, alongside whatever else the edge woke --
            // or, woken by a change of value as logic is, from where that change came from.
            bool woken = _wokenRegion[p] == _region;
            _writer = !woken || _wokenByEdge[p] ? p : _wokenOrigin[p];
            _writerWrote = !woken || _wokenByEdge[p] ? -1 : _wokenWrote[p];
            if (woken && _wokenByEdge[p])
                _sampled.Add((p, _wokenOrigin[p], _wokenSignal[p], _pc[p]));
            _wokenRegion[p] = 0;
            try
            {
                RunSteps(p);
            }
            finally
            {
                _writer = NoOrigin;
                _writerWrote = -1;
            }
        }

        private void RunSteps(int p)
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
                        Force(ref ins, assigned: false);
                        pc++;
                        break;

                    case OpCode.ProceduralAssign:
                        Force(ref ins, assigned: true);
                        pc++;
                        break;

                    case OpCode.Deassign:
                        Deassign(ins.Target);
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
            _wokenRegion[p] = 0;   // a wake it never ran on is no wake (the 15i-3 review)
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

        /// <summary>
        /// Ends a process from outside -- <c>disable fork</c>, or the block that forked it disabled -- and every one it forked.
        /// Ended, it has ended for whatever waits on it: a parent at <c>wait fork</c> goes on once its last child has (IEEE
        /// 1800, 9.6.1), as it does when one runs out (the review of 16c: it waited for good).
        /// </summary>
        private void Kill(int p)
        {
            if (!_done[p])
            {
                Cancel(p);
                _done[p] = true;
                _blockedAt[p] = -1;
                Ended(p);
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

                // Where it waits, before anything here ends: a branch ended may wake it at the join it ends, and a process
                // inside the block goes on past the block, never from there.
                bool inside = !_done[q] && _blockedAt[q] >= range.Start && _blockedAt[q] < range.End;

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

                if (!inside)
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

        /// <summary>
        /// <c>force</c> (IEEE 1800, 10.6.2), or with <paramref name="assigned"/> a procedural <c>assign</c> (10.6.1): each piece
        /// of the target takes its bits of the value, and keeps following it. Either replaces one of its own kind on those bits.
        /// </summary>
        private void Force(ref Instruction ins, bool assigned)
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
                Unforce(piece.Signal, piece.Lowest, piece.Width, assigned);

                var force = new ForceRecord
                {
                    Signal = piece.Signal,
                    Lowest = piece.Lowest,
                    Width = piece.Width,
                    Offset = offset,
                    Total = ins.TargetWidth,
                    Value = ins.Value,
                    Reads = readList,
                    Assigned = assigned,
                };

                (_forces[piece.Signal] ?? (_forces[piece.Signal] = new List<ForceRecord>())).Add(force);
                foreach (int s in readList)
                    (_forceReaders[s] ?? (_forceReaders[s] = new List<ForceRecord>())).Add(force);

                Apply(force);
            }
        }

        /// <summary>A force or an assign takes its value now, and its signal shows it -- not set going again meanwhile.</summary>
        private void Apply(ForceRecord force)
        {
            force.Updating = true;
            force.Current = force.Pending = Value(force);
            Refresh(force.Signal);
            force.Updating = false;
        }

        /// <summary>
        /// <c>release</c>: a wire goes back to what drives it -- z, with nothing driving it; a variable nothing drives keeps
        /// the value it was forced to until it is next written (IEEE 1800, 10.6.2).
        /// </summary>
        private void Release(LPiece[] target)
        {
            foreach (LPiece piece in target)
            {
                if (!Unforce(piece.Signal, piece.Lowest, piece.Width, assigned: false))
                    continue;

                int s = piece.Signal;
                SignalInfo info = _design.Signals[s];
                if (info.Kind == SignalKind.Variable && _driversOf[s].Length == 0)
                    _written[s] = Shape(Splice(_written[s], piece.Lowest, _values[s].Slice(piece.Lowest, piece.Width)), info);

                Refresh(s);
            }
        }

        /// <summary>
        /// <c>deassign</c> (IEEE 1800, 10.6.1): each variable keeps the value it was assigned until it is next written, and takes
        /// writes again. A force over it stays until it is released.
        /// </summary>
        private void Deassign(LPiece[] target)
        {
            foreach (LPiece piece in target)
            {
                int s = piece.Signal;
                List<ForceRecord> forces = _forces?[s];
                if (forces == null)
                    continue;

                LogicVec kept = _written[s];
                foreach (ForceRecord force in forces)
                    if (force.Assigned && force.Lowest < piece.Lowest + piece.Width && force.Lowest + force.Width > piece.Lowest)
                        kept = Splice(kept, force.Lowest, force.Current);
                if (!Unforce(s, piece.Lowest, piece.Width, assigned: true))
                    continue;

                _written[s] = Shape(kept, _design.Signals[s]);
                Refresh(s);
            }
        }

        /// <summary>
        /// Takes away every force -- or, <paramref name="assigned"/>, every procedural assign -- from the bits given, and from
        /// those alone: one wider than them keeps its bits below and above, as forces of their own (the 16d sweep, engine-10:
        /// it was taken away whole, so release w[1] released four bits). Whether there was one.
        /// </summary>
        private bool Unforce(int s, int lowest, int width, bool assigned)
        {
            List<ForceRecord> forces = _forces?[s];
            if (forces == null)
                return false;

            bool any = false;
            int top = lowest + width;
            for (int i = forces.Count - 1; i >= 0; i--)
            {
                ForceRecord force = forces[i];
                int end = force.Lowest + force.Width;
                if (force.Assigned != assigned || force.Lowest >= top || end <= lowest)
                    continue;

                forces.RemoveAt(i);
                foreach (int read in force.Reads)
                    _forceReaders[read]?.Remove(force);
                any = true;

                if (force.Lowest < lowest)
                    KeepPart(forces, force, force.Lowest, lowest - force.Lowest);
                if (end > top)
                    KeepPart(forces, force, top, end - top);
            }

            return any;
        }

        /// <summary>The bits of <paramref name="force"/> from <paramref name="lowest"/>, <paramref name="width"/> of them, kept as a force of their own.</summary>
        private void KeepPart(List<ForceRecord> forces, ForceRecord force, int lowest, int width)
        {
            int from = lowest - force.Lowest;
            var part = new ForceRecord
            {
                Signal = force.Signal,
                Lowest = lowest,
                Width = width,
                Offset = force.Offset + from,
                Total = force.Total,
                Value = force.Value,
                Reads = force.Reads,
                Assigned = force.Assigned,
                Current = force.Current.Slice(from, width),
                Pending = force.Pending.IsEmpty ? force.Pending : force.Pending.Slice(from, width),
            };

            forces.Add(part);
            foreach (int read in part.Reads)
                (_forceReaders[read] ?? (_forceReaders[read] = new List<ForceRecord>())).Add(part);
        }

        private LogicVec Value(ForceRecord force) => Fit(force.Value.Evaluate(_values), force.Total).Slice(force.Offset, force.Width);

        /// <summary>A signal's procedural assigns laid over its own value, and its forces over those.</summary>
        private LogicVec Overlay(int s, LogicVec value)
        {
            List<ForceRecord> forces = _forces?[s];
            if (forces == null || forces.Count == 0)
                return value;

            foreach (ForceRecord force in forces)
                if (force.Assigned)
                    value = Splice(value, force.Lowest, force.Current);
            foreach (ForceRecord force in forces)
                if (!force.Assigned)
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
        /// <summary>
        /// A memory task's address: signed only if the value is -- 8'd128 is 128, never -128 (the 16d sweep, engine-8) -- and
        /// one past what a long holds as the largest, which is outside any memory. <paramref name="fallback"/> when it is
        /// absent or unknown.
        /// </summary>
        private long Number(Expr e, long fallback)
        {
            if (e == null)
                return fallback;

            LogicVec value = e.Evaluate(_values);
            if (!value.IsKnown)
                return fallback;
            if (value.IsSigned)
                return value.TryGetInt64(out long n) ? n : fallback;
            return value.TryGetUInt64(out ulong u) ? (long)Math.Min(u, long.MaxValue) : long.MaxValue;
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

            if (!MemoryImage.TryCheck(text, task.Hex, out string error))
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

            // Where each word of the range is loaded from -- the last of the image's words to land on it, as an @ may go back
            // -- and only those are made, at the memory's width, once the image is stepped through: a load makes no more than
            // the memory holds, however long its file (the review of 16c).
            int words = (int)(high - low + 1);
            var from = new int[words];
            var length = new int[words];
            for (int i = 0; i < words; i++)
                from[i] = -1;

            void Load()
            {
                for (int i = 0; i < words; i++)
                    if (from[i] >= 0)
                        Write(memory.First.Index + (int)(low + i - memory.Lowest), 0, MemoryImage.Word(text, from[i], length[i], task.Hex, memory.Width));
            }

            long step = start <= end ? 1 : -1;
            long at = start;
            int loaded = 0;
            bool addressed = false;
            var reader = new MemoryImage.Reader(text);

            while (reader.Next(out int token, out int size, out int line, out _))
            {
                if (text[token] == '@')
                {
                    addressed = true;
                    MemoryImage.TryAddress(text, token, size, out at);
                    if (at < low || at > high)
                    {
                        Load();
                        _history.Message(_now, Severity.Error,
                            $"{task.Task}({task.File}) line {line}: @{at:x} is outside the words being loaded, {low} to {high}.");
                        return;
                    }

                    continue;
                }

                if (at < low || at > high)
                {
                    Load();
                    _history.Message(_now, Severity.Warning,
                        $"{task.Task}({task.File}) line {line}: more words than {memory.Name} has room for from {start}; the rest are not loaded.");
                    return;
                }

                from[at - low] = token;
                length[at - low] = size;
                loaded++;
                at += step;
            }

            Load();
            if (!addressed && loaded < words)
                _history.Message(_now, Severity.Warning,
                    $"{task.Task}({task.File}): the file has {loaded} words for the {words} of {memory.Name}; the rest are left as they were.");
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
        /// <param name="edge">
        /// Whether what woke it is an edge from a known value to a known one -- a clock's, as a flip-flop waits on -- for the
        /// race warning. An edge out of x, as a clock's first at time 0, is no clock's edge yet.
        /// </param>
        /// <param name="by">The signal of the item that fired, for the race warning; -1 when it is no one signal.</param>
        private bool Fired(int p, int pc, out bool edge, out int by)
        {
            ref Instruction ins = ref _processes[p].Code[pc];
            edge = false;
            by = -1;

            if (ins.Op == OpCode.WaitUntil)
                return LogicVec.Truth(ins.Value.Evaluate(_values)) == Logic4.One;

            EventItem[] items = ins.Wait.Items;
            LogicVec[] last = _last[p];
            bool fired = false;

            for (int i = 0; i < items.Length; i++)
            {
                LogicVec now = items[i].Value.Evaluate(_values);
                if (!fired && Matching.Fires(items[i].Edge, last[i], now))
                {
                    fired = true;
                    edge = items[i].Edge != EdgeKind.Any && !last[i].IsEmpty && last[i].IsKnown && now.IsKnown;
                    by = items[i].Signal;
                }
                last[i] = now;
            }

            return fired;
        }

        /// <summary>
        /// A delay's value in time units, as the standard reads one (IEEE 1800, 9.4.1): x or z is 0; and a negative one is a
        /// two's-complement unsigned number as wide as a time, 2^64 less its size -- like any delay past what a run can count,
        /// one that never comes due (the review of 16c: it was 0).
        /// </summary>
        private long DelayOf(LogicVec value)
        {
            if (!value.IsKnown)
                return 0;

            long never = long.MaxValue - _now;
            if (value.IsSigned)
                return value.TryGetInt64(out long signed) && signed >= 0 ? Math.Min(signed, never) : never;

            return value.TryGetUInt64(out ulong unsigned) ? (long)Math.Min(unsigned, (ulong)never) : never;
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
            if (_writer != NoOrigin)
                MarkOrigin(s, _writer, _writerWrote >= 0 ? _writerWrote : s);

            Changed(s);
        }

        // -----------------------------------------------------------------
        // The race warning (Part 15i-3)
        // -----------------------------------------------------------------

        private void MarkOrigin(int s, int origin, int written)
        {
            if (_originRegion[s] != _region)
            {
                _markedInRegion.Add(s);
                _originRegion[s] = _region;
                _origin[s] = origin;
                _written0[s] = written;
            }
            else if (_origin[s] != origin)
            {
                _origin[s] = SeveralOrigins;
            }
        }

        private void Arrive(int s, int origin, int written)
        {
            if (_arrivingRegion[s] != _region)
            {
                _arrivingRegion[s] = _region;
                _arriving[s] = origin;
                _arrivingWritten[s] = written;
            }
            else if (_arriving[s] != origin)
            {
                _arriving[s] = SeveralOrigins;
            }
        }

        private void DriverReads(int d, int origin, int written)
        {
            if (_driverOriginRegion[d] != _region)
            {
                _driverOriginRegion[d] = _region;
                _driverOrigin[d] = origin;
                _driverWritten[d] = written;
            }
            else if (_driverOrigin[d] != origin)
            {
                _driverOrigin[d] = SeveralOrigins;
            }
        }

        /// <summary>
        /// The region ends: each process an edge woke in it is told of a signal it reads that changed in it by a blocking
        /// write, from one process that is neither it nor the one the edge came from. The two ran in no set order -- this
        /// engine runs a design's processes in a fixed one, and another simulator may run them in another -- so what it read
        /// is a race. An edge or a change from several processes, or from none, is left alone: it cannot be told which came first.
        /// </summary>
        private void CheckRaces()
        {
            if (_markedInRegion.Count > 0)
            {
                foreach ((int p, int trigger, int clock, int pc) in _sampled)
                {
                    if (trigger < 0)
                        continue;
                    HashSet<int> reads = ReadsFrom(p, pc);
                    foreach (int r in _markedInRegion)
                    {
                        if (!reads.Contains(r))
                            continue;
                        int q = _origin[r];
                        if (q < 0 || q == p)
                            continue;
                        // Ordered after all, by a chain of causes (the 15i-3 review): the writer made the edge, however far
                        // back; the reader, or what woke it, set the writer going; or one forked the other.
                        if (Caused(q, trigger) || Caused(p, q) || Caused(q, p))
                            continue;
                        if (_racesSaid.Add(((long)q << 32) | (uint)p))
                            SayRace(p, q, r, clock);
                    }
                }
            }
            _sampled.Clear();
            _markedInRegion.Clear();
        }

        /// <summary>
        /// Whether <paramref name="cause"/> set <paramref name="effect"/> going this region: is it, or what woke it, or the
        /// process that forked it, and so on back.
        /// </summary>
        private bool Caused(int cause, int effect)
        {
            int at = effect;
            for (int steps = 0; at >= 0 && steps < 64; steps++)
            {
                if (at == cause)
                    return true;
                if (_wakerRegion[at] == _region && _wakerOf[at] >= 0 && _wakerOf[at] != at)
                    at = _wakerOf[at];
                else
                    at = _parent[at];
            }
            return false;
        }

        private void SayRace(int p, int q, int r, int clock)
        {
            // What the writer wrote, and what the reader reads of it where that is another name -- a port, a wire after it.
            int w = _written0[r];
            string wrote = w >= 0 && w < _design.Signals.Count ? _design.Signals[w].Name : _design.Signals[r].Name;
            string read = _design.Signals[r].Name, edge = _design.Signals[clock].Name;
            string reader = _processes[p].Name, writer = _processes[q].Name;
            string reaching = read == wrote ? "" : $" (which {read} follows)";
            _history.Message(_now, Severity.Warning,
                $"At t = {_now} {writer} changes {wrote}{reaching} with a blocking assignment at the edge of {edge} that runs " +
                $"{reader}, which reads it. The two may run in either order, so {reader} may see the value from before the " +
                $"change or after it, and another simulator may not agree with this one. Change {wrote} away from the edge -- " +
                "on the other edge, or a moment after it -- or with <=. (Said once for these two.)");
        }

        /// <summary>
        /// What <paramref name="p"/> reads from <paramref name="pc"/> on, every way its jumps may take, up to where it next
        /// waits -- a delay, an event, a wait, a fork, a stop or its end -- as the wake that took up there runs. An asynchronous
        /// reset its body tests is read like any value.
        /// </summary>
        private HashSet<int> ReadsFrom(int p, int pc)
        {
            long key = ((long)p << 32) | (uint)pc;
            if (_readsFrom.TryGetValue(key, out HashSet<int> known))
                return known;

            Instruction[] code = _processes[p].Code;
            var reads = new HashSet<int>();
            var seen = new HashSet<int>();
            var todo = new Stack<int>();
            todo.Push(pc);
            while (todo.Count > 0)
            {
                int at = todo.Pop();
                if (at < 0 || at >= code.Length || !seen.Add(at))
                    continue;
                ref Instruction ins = ref code[at];
                switch (ins.Op)
                {
                    case OpCode.Delay:
                    case OpCode.WaitEvent:
                    case OpCode.WaitUntil:
                    case OpCode.Fork:
                    case OpCode.Stop:
                    case OpCode.Finish:
                        continue;
                }
                ins.Value?.CollectReads(reads);
                ins.Other?.CollectReads(reads);
                foreach (LPiece piece in ins.Target ?? Array.Empty<LPiece>())
                {
                    piece.Offset?.CollectReads(reads);
                    piece.Word?.CollectReads(reads);
                }
                switch (ins.Op)
                {
                    case OpCode.Jump:
                        todo.Push(ins.Jump);
                        break;
                    case OpCode.JumpIf:
                    case OpCode.JumpUnless:
                    case OpCode.JumpIfMatch:
                        todo.Push(ins.Jump);
                        todo.Push(at + 1);
                        break;
                    default:
                        todo.Push(at + 1);
                        break;
                }
            }
            _readsFrom[key] = reads;
            return reads;
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

        /// <summary>
        /// A signal's value from its drivers -- resolved as a wire if it has several -- or from what was last written: for a net
        /// nothing drives, the value it started with, z, which nothing writes (the review of 16c: it was the value shown, so a
        /// force on one was kept once released).
        /// </summary>
        private LogicVec ResolveOwn(int s)
        {
            SignalInfo info = _design.Signals[s];
            int[] drivers = _driversOf[s];

            if (drivers.Length == 0)
                return _written[s];

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

            bool forced = false;
            foreach (Event e in _current)
            {
                if (e.Kind != EventKind.DriverUpdate && e.Kind != EventKind.ForceUpdate || shown == 8)
                    continue;

                if (names.Length > 0) names.Append(", ");
                names.Append(e.Kind == EventKind.ForceUpdate ? _design.Signals[e.Index].Name : _design[_drivers[e.Index].Target].Name);
                forced |= e.Kind == EventKind.ForceUpdate;
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
                $"a loop with no delay never settles. Still changing: {names}. " +
                (forced ? "A force follows what it reads, its own signal included: force it to a value that does not read it, or "
                        : "Give a gate in the loop a delay, or ") +
                "break the loop.");
        }

        /// <summary>
        /// A function stopped short since the last time this asked -- a loop that never ends, or calls with no way out
        /// (<see cref="FunctionRunner"/>) -- said at the time it happened, each reason once a run. One that never ends stops
        /// the run, as a process that never waits does (the 16d sweep, engine-3): the run went on, and every later call of
        /// it spent the steps again.
        /// </summary>
        private void SayFunctionsStopped()
        {
            if (FunctionRunner.Stopped == null)
                return;
            bool runaway = FunctionRunner.Runaway;
            string stopped = FunctionRunner.TakeStopped();
            if (_functionsStopped.Add(stopped))
                _history.Message(_now, Severity.Error, $"At t = {_now} {stopped}");
            if (runaway)
                Stopped = true;
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
