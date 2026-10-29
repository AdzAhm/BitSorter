using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>A change on its way down one branch of a wire, as the schematic draws it at the current moment.</summary>
    public struct ChangeMarker
    {
        /// <summary>The net it is travelling on, by its index in the layout.</summary>
        public int Net;

        /// <summary>The branch it is on.</summary>
        public StageRoute Branch;

        /// <summary>How far along the branch it has got.</summary>
        public float Distance;

        /// <summary>Where that is.</summary>
        public Vec2 Point;

        /// <summary>The value it carries, and that value as its ring shows it.</summary>
        public LogicVec Value;

        /// <inheritdoc cref="Value"/>
        public string Text;

        /// <summary>1 while it travels; falling to 0 as a swallowed change fades.</summary>
        public float Alpha;

        /// <summary>Whether it was cancelled before it arrived, and is fading where it stopped.</summary>
        public bool Swallowed;
    }

    /// <summary>One stretch of a signal's waveform: a value held from one moment to the next.</summary>
    public struct WaveSegment
    {
        public double From, To;
        public LogicVec Value;

        /// <summary>The value as the waveform writes it.</summary>
        public string Text;
    }

    /// <summary>
    /// The workbench's view-model: one run of a circuit, read at a moment. What each wire holds, which
    /// changes are on their way and how far they have got, which gate has just flipped, and the
    /// waveform up to now -- every answer read from the run's history, never re-run (ADR-6).
    /// </summary>
    /// <remarks>
    /// The run is made ahead of what is shown -- all at once by <see cref="Of"/>, or a slice a frame by
    /// <see cref="RunSession"/> -- and rewinding, scrubbing and replaying are this class answering for
    /// a different moment. <see cref="Seek"/> moves the moment and works out the markers once, and
    /// everything that draws reads them, so the rings, their digits and the glow cannot disagree
    /// about where a change is.
    ///
    /// **It follows a run that is still being made** (Part 9b). <see cref="Grow"/> reads what the
    /// history recorded since it last looked: each signal's settled changes (<see cref="SignalTrack"/>),
    /// the new transitions' text, and the swallowed changes -- including a change that was still on its
    /// way last time and has been cancelled since. Until the run is <see cref="Complete"/>, a moment
    /// cannot be shown past where it has got (<see cref="Reached"/>). A model grown slice by slice
    /// answers every moment exactly as one made in one go; a test holds the two together at slices of
    /// one event and up.
    ///
    /// **What it reads per frame does not grow with the run.** Transitions are recorded in the order
    /// they are scheduled, so the ones that can be on their way at a moment are a window found by
    /// binary search, not a walk of the whole run; a gate's flash and a row's count of swallowed
    /// changes are binary searches too.
    ///
    /// Time here is a double, in the run's time units (nanoseconds as shown): the engine's time is
    /// an integer, and the moments between are where a change is part-way down its wire.
    ///
    /// **Nothing here allocates once the run is made**, the text included: every value a ring, a
    /// readout or a bus segment shows is formatted once, as the change is read, so a frame of
    /// playback makes no garbage.
    /// </remarks>
    public sealed class WorkbenchModel
    {
        private readonly History _history;

        /// <summary>Signal indices, -1 where the layout names a signal the design does not have.</summary>
        private readonly int[] _netSignals;

        /// <inheritdoc cref="_netSignals"/>
        private readonly int[] _partOutputs;

        /// <summary>The signal each row of the waveform shows, -1 for a row with none (a group's header); and in what radix.</summary>
        private int[] _rowSignals;

        /// <inheritdoc cref="_rowSignals"/>
        private WaveRadix[] _rowRadix;

        /// <summary>The radix a change on each net is written in on its ring: the radix of the first row showing its node, or the default.</summary>
        private WaveRadix[] _netRadix;

        /// <summary>The node each net's signal is in, -1 where the design does not have it.</summary>
        private readonly int[] _netNode;

        /// <summary>Texts in a radix other than the default: a row's changes keyed by signal, a ring's transitions by -1 - net.</summary>
        private readonly TextCache _texts = new TextCache();

        /// <inheritdoc cref="_netSignals"/>
        private readonly int[] _portSignals;

        /// <summary>The settled changes of each signal the layout reads, by signal index; null for the rest.</summary>
        private readonly SignalTrack[] _tracks;

        /// <summary>Each recorded transition's value as its ring shows it.</summary>
        private readonly List<string> _transitionText = new List<string>(64);

        /// <summary>Which net each signal is drawn as, or -1.</summary>
        private readonly int[] _netOfSignal;

        private readonly List<ChangeMarker> _markers = new List<ChangeMarker>(16);
        private readonly List<int> _markerNets = new List<int>(16);

        /// <summary>
        /// Each row's swallowed changes, in the order they were recorded: which transition, and where it
        /// died. A change counts for a row when it was on its way to any name of the row's net, not only
        /// the port's: a gate with delay prints as a library cell, and its changes go to the cell's own
        /// output, which is one of the net's aliases.
        /// </summary>
        private List<(int Transition, double Ended)>[] _swallowedOf;

        /// <summary>The same moments, in time order: what a row's count so far is a binary search of.</summary>
        private List<double>[] _swallowedTimes;

        /// <summary>Transitions still on their way when last read, by index: any may be swallowed since.</summary>
        private readonly List<int> _pending = new List<int>();

        /// <summary>How many transitions have been read.</summary>
        private int _transitionsRead;

        /// <summary>The longest any transition takes, scheduled to due: how far back a moment's window reaches.</summary>
        private double _longestSpan;

        /// <summary>Whether every transition so far was scheduled no earlier than the one before: what the window relies on.</summary>
        private bool _inOrder = true;

        private long _settledAt = long.MaxValue;

        public WorkbenchModel(SimSession session, SchematicLayout layout, long end, bool complete = true)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            End = end;
            _history = session.History;

            Design design = session.Design;
            int Index(string name) => name != null && design.TryFind(name, out SignalId id) ? id.Index : -1;

            _netSignals = new int[layout.Nets.Count];
            _netOfSignal = new int[design.Signals.Count];
            for (int i = 0; i < _netOfSignal.Length; i++) _netOfSignal[i] = -1;

            for (int i = 0; i < layout.Nets.Count; i++)
            {
                _netSignals[i] = Index(layout.Nets[i].Signal);
                foreach (string alias in layout.Nets[i].Aliases)
                {
                    int signal = Index(alias);
                    if (signal >= 0)
                        _netOfSignal[signal] = i;
                }
            }

            _partOutputs = new int[layout.Parts.Count];
            for (int i = 0; i < layout.Parts.Count; i++)
                _partOutputs[i] = Index(layout.Parts[i].Output);

            Aliases = SignalAliases.Of(design);
            Signals = layout.DesignPath != null ? SignalIndex.Of(design, layout.DesignPath, layout.Cells) : null;
            _netNode = new int[layout.Nets.Count];
            for (int i = 0; i < layout.Nets.Count; i++)
                _netNode[i] = Aliases.NodeOf(_netSignals[i]);

            var rows = new int[layout.Rows.Count];
            for (int i = 0; i < layout.Rows.Count; i++)
                rows[i] = Index(layout.Rows[i].Signal);

            _portSignals = new int[layout.Ports.Count];
            for (int i = 0; i < layout.Ports.Count; i++)
                _portSignals[i] = Index(layout.Ports[i].Signal);

            // A track for every signal something here reads; the others are never asked about.
            _tracks = new SignalTrack[design.Signals.Count];
            void Track(int signal)
            {
                if (signal >= 0 && _tracks[signal] == null)
                    _tracks[signal] = new SignalTrack(_history, design.SignalAt(signal), TextOf);
            }

            foreach (int signal in _netSignals) Track(signal);
            foreach (int signal in _partOutputs) Track(signal);
            foreach (int signal in _portSignals) Track(signal);
            if (layout.Drawn != null)
            {
                // The drawing of code's reads, found in this run by name (Part 15h-4).
                _drawnSignals = new int[layout.Drawn.ReadNames.Count];
                for (int i = 0; i < _drawnSignals.Length; i++)
                {
                    _drawnSignals[i] = Index(layout.Drawn.ReadNames[i]);
                    Track(_drawnSignals[i]);
                }
            }

            SetRows(rows);
            Grow(complete);
            Seek(0);
        }

        /// <summary>
        /// Which node every signal is in (<see cref="SignalAliases"/>): what decides which rows a
        /// swallowed change is marked on.
        /// </summary>
        public SignalAliases Aliases { get; }

        /// <summary>
        /// Every signal a row can show, as the design is built and as a list to search
        /// (<see cref="SignalIndex"/>); null for a layout that does not say where the design is.
        /// </summary>
        public SignalIndex Signals { get; }

        /// <summary>
        /// Sets what the waveform's rows show: a signal each, by its index in the design (-1 for a row
        /// with none, as a group's header has), each in its <paramref name="radixes"/>' radix or its
        /// own default (<see cref="DefaultRadix"/>). A signal not read before gets its track, read
        /// as far as the run has got, and every row its glitch marks so far; the moment stays.
        /// </summary>
        /// <remarks>
        /// The default rows are the layout's, one per port (<see cref="SchematicLayout.Rows"/>). Rows
        /// are chosen by the list the view keeps (<see cref="WaveList"/>); the model only answers for
        /// whichever signals they are. A net's rings are written in the radix of the first row showing
        /// any name of its node, so a bus read as unsigned in the waveform reads unsigned on its wire.
        /// </remarks>
        public void SetRows(IReadOnlyList<int> signals, IReadOnlyList<WaveRadix> radixes = null)
        {
            if (signals == null)
                throw new ArgumentNullException(nameof(signals));
            if (radixes != null && radixes.Count != signals.Count)
                throw new ArgumentException("A radix for every row, or none.", nameof(radixes));

            Design design = Session.Design;
            int count = signals.Count;
            _rowSignals = new int[count];
            _rowRadix = new WaveRadix[count];
            _swallowedOf = new List<(int, double)>[count];
            _swallowedTimes = new List<double>[count];

            for (int row = 0; row < count; row++)
            {
                int signal = signals[row];
                if (signal < -1 || signal >= design.Signals.Count)
                    throw new ArgumentOutOfRangeException(nameof(signals), signal, "A row shows a signal of the design, or -1 for none.");

                _rowSignals[row] = signal;
                _rowRadix[row] = radixes != null ? radixes[row] : DefaultRadix(signal);
                _swallowedOf[row] = new List<(int, double)>();
                _swallowedTimes[row] = new List<double>();

                if (signal >= 0 && _tracks[signal] == null)
                {
                    _tracks[signal] = new SignalTrack(_history, design.SignalAt(signal), TextOf);
                    _tracks[signal].Grow();
                }
            }

            // Every change swallowed so far, marked on the rows that show its node.
            for (int i = 0; i < _transitionsRead; i++)
            {
                Transition t = _history.TransitionAt(i);
                if (t.Outcome == TransitionOutcome.Swallowed)
                    Swallowed(i, t);
            }

            _netRadix = new WaveRadix[_netSignals.Length];
            for (int net = 0; net < _netRadix.Length; net++)
            {
                _netRadix[net] = DefaultRadix(_netSignals[net]);
                for (int row = 0; row < count; row++)
                {
                    if (_netNode[net] >= 0 && Aliases.NodeOf(_rowSignals[row]) == _netNode[net])
                    {
                        _netRadix[net] = _rowRadix[row];
                        break;
                    }
                }
            }

            RowsVersion++;
            PlaceMarkers();
        }

        /// <summary>Counts every <see cref="SetRows"/>: what tells the view its rows have changed.</summary>
        public int RowsVersion { get; private set; }

        /// <summary>
        /// The radix a signal is written in until a row says otherwise: an enum's names, a bus in hex,
        /// a bit as 0, 1, x or z.
        /// </summary>
        public WaveRadix DefaultRadix(int signal)
        {
            if (signal < 0)
                return WaveRadix.Binary;
            SignalInfo info = Session.Design.Signals[signal];
            if (info.Enum != null)
                return WaveRadix.Symbolic;
            return info.Width > 1 ? WaveRadix.Hex : WaveRadix.Binary;
        }

        /// <summary>
        /// A document compiled, elaborated, run to where its testbench stops and laid out: what the
        /// workbench shows of it, made in one go. Throws when the document does not compile; the
        /// workbench only opens one that does.
        /// </summary>
        public static WorkbenchModel Of(Project project)
        {
            (Design design, SchematicLayout layout, long horizon) = Prepare(project);
            var session = new SimSession(design, null, new SimOptions { Files = ProjectFiles.ForRun(project) });
            RunResult result = session.RunTo(horizon);
            return new WorkbenchModel(session, layout, horizon, result.Status != RunStatus.Suspended || result.Time >= horizon);
        }

        /// <summary>
        /// A document compiled and elaborated, its layout, and the time its run is made to: where the
        /// testbench stops (<see cref="Stimulus.StopTime"/>), or 100 without one. The layout is the top's, or
        /// what <paramref name="layoutOf"/> lays out of the compiled document -- a module stepped into (Part 12a).
        /// </summary>
        public static (Design Design, SchematicLayout Layout, long Horizon) Prepare(Project project,
            Func<Project, CompiledProject, SchematicLayout> layoutOf = null)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                throw new InvalidOperationException("The document does not compile: " + string.Join("; ", compiled.Diagnostics));

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
                throw new InvalidOperationException("The document does not elaborate: " + string.Join("; ", elaboration.Diagnostics));

            long horizon = project.Stimulus?.StopTime ?? 100;
            return (elaboration.Design, layoutOf != null ? layoutOf(project, compiled) : DocumentSchematic.Build(project, compiled), horizon);
        }

        public SimSession Session { get; }
        public SchematicLayout Layout { get; }

        /// <summary>The unit the document counts its time in: what every time the workbench writes is in.</summary>
        public string Unit => Layout.Unit;

        /// <summary>The end of the timeline: where the run is made to. It grows when the run is run on past it (<see cref="Extend"/>).</summary>
        public long End { get; private set; }

        /// <summary>
        /// Changes each time the model reads more of the run, or its timeline grows: what an answer worked
        /// out from the run, and kept, is checked against.
        /// </summary>
        public int RunVersion { get; private set; }

        /// <summary>
        /// How fast the run is played, in time units a second. A gate's flash and a swallowed change's fade
        /// last as many seconds at any speed: <see cref="SchematicStyle.FlashTime"/> and
        /// <see cref="SchematicStyle.FadeTime"/> are how long they last at the reference's
        /// <see cref="SchematicStyle.NanosecondsPerSecond"/>, and at any other they last that many times
        /// longer or shorter in time units. At the reference the ratio is exactly 1, so nothing moves.
        /// </summary>
        public double UnitsPerSecond
        {
            get => _unitsPerSecond;
            set
            {
                if (!(value > 0.0) || double.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value), value, "A speed is some finite time a second.");
                if (value == _unitsPerSecond)
                    return;

                _unitsPerSecond = value;
                double scale = value / SchematicStyle.NanosecondsPerSecond;
                _flashTime = SchematicStyle.FlashTime * scale;
                _fadeTime = SchematicStyle.FadeTime * scale;
                PlaceMarkers();
            }
        }

        private double _unitsPerSecond = SchematicStyle.NanosecondsPerSecond;
        private double _flashTime = SchematicStyle.FlashTime;
        private double _fadeTime = SchematicStyle.FadeTime;

        /// <summary>
        /// The timeline grows to <paramref name="end"/>, the run being run on to it: it is no longer
        /// made, and nothing past where the run has got can be shown until it is.
        /// </summary>
        public void Extend(long end)
        {
            if (end <= End)
                return;

            End = end;
            Complete = false;
            _settledAt = long.MaxValue;
            RunVersion++;
        }

        /// <summary>Whether the run is made: no moment of the timeline is still to come.</summary>
        public bool Complete { get; private set; }

        /// <summary>How far the run has got: a moment past it cannot be shown until it is made.</summary>
        public long Reached => Session.Reached;

        /// <summary>The moment being shown.</summary>
        public double Time { get; private set; }

        /// <summary>
        /// The last moment anything happens: after it, nothing changes and nothing is on its way. Known
        /// once the run is <see cref="Complete"/>; until then, <c>long.MaxValue</c>.
        /// </summary>
        public long SettledAt => _settledAt;

        /// <summary>The changes on their way at <see cref="Time"/>, one per branch they travel down.</summary>
        public IReadOnlyList<ChangeMarker> Markers => _markers;

        /// <summary>
        /// The net each of <see cref="Markers"/> is on, in the same order: what a chunk of the drawing
        /// asks of every marker to find its own, without copying each marker out to ask it.
        /// </summary>
        public IReadOnlyList<int> MarkerNets => _markerNets;

        /// <summary>Shows another moment: clamped to the timeline, and to what has run, and every marker worked out for it.</summary>
        public void Seek(double time)
        {
            double last = Complete ? End : Math.Min(End, Reached);
            Time = Math.Max(0.0, Math.Min(last, time));
            PlaceMarkers();
        }

        /// <summary>
        /// Reads what the run recorded since the last call -- only between slices of it, never before
        /// its first -- and, once <paramref name="complete"/>, works out when it settled. True when
        /// anything new was read. The moment shown stays where it was.
        /// </summary>
        public bool Grow(bool complete)
        {
            bool grew = false;

            foreach (SignalTrack track in _tracks)
                if (track != null && track.Grow())
                    grew = true;

            // Transitions on their way last time, which may have arrived or been cancelled since.
            for (int p = _pending.Count - 1; p >= 0; p--)
            {
                int i = _pending[p];
                Transition t = _history.TransitionAt(i);
                if (t.Outcome == TransitionOutcome.Pending)
                    continue;

                _pending.RemoveAt(p);
                if (t.Outcome == TransitionOutcome.Swallowed)
                    Swallowed(i, t);
                grew = true;
            }

            int count = _history.TransitionCount;
            for (int i = _transitionsRead; i < count; i++)
            {
                Transition t = _history.TransitionAt(i);
                _transitionText.Add(TextOf(t.Value));

                if (i > 0 && t.ScheduledAt < _history.TransitionAt(i - 1).ScheduledAt)
                    _inOrder = false;
                if (t.DueAt > t.ScheduledAt)
                    _longestSpan = Math.Max(_longestSpan, t.DueAt - t.ScheduledAt);

                if (t.Outcome == TransitionOutcome.Pending)
                    _pending.Add(i);
                else if (t.Outcome == TransitionOutcome.Swallowed)
                    Swallowed(i, t);

                grew = true;
            }

            _transitionsRead = count;

            if (complete && !Complete)
            {
                Complete = true;
                _settledAt = FindSettledAt();
                grew = true;
            }

            if (grew)
            {
                RunVersion++;
                PlaceMarkers();
            }

            return grew;
        }

        // -----------------------------------------------------------------
        // What the schematic shows
        // -----------------------------------------------------------------

        /// <summary>The value a net holds now: its signal's, or for a wire of a drawing of code no signal holds, worked out (Part 15h-4).</summary>
        public LogicVec NetValue(int net)
        {
            int signal = _netSignals[net];
            if (signal < 0 && Layout.Drawn != null && Layout.Drawn.Has(net))
                return DrawnValue(net);
            return ValueAt(signal, Layout.Nets[net].Width, Time);
        }

        private int[] _drawnSignals;
        private LogicVec[] _drawnInputs, _drawnValues, _drawnInner;
        private double _drawnAt = double.NaN;

        /// <summary>A wire of a drawing of code no signal holds: the netlist worked out from this run's signals now, once a moment.</summary>
        private LogicVec DrawnValue(int net)
        {
            DrawnValues drawn = Layout.Drawn;
            if (!(Time == _drawnAt))
            {
                if (_drawnInputs == null)
                {
                    _drawnInputs = new LogicVec[drawn.SignalCount];
                    for (int i = 0; i < _drawnInputs.Length; i++)
                        _drawnInputs[i] = LogicVec.AllX(1);
                }
                for (int i = 0; i < _drawnSignals.Length; i++)
                {
                    int signal = _drawnSignals[i];
                    _drawnInputs[drawn.Reads[i]] = signal >= 0 ? ValueAt(signal, 1, Time) : LogicVec.AllX(drawn.ReadWidths[i]);
                }
                _drawnValues = RtlEvaluator.Evaluate(drawn.Netlist, _drawnInputs);
                _drawnInner = drawn.Drawing != null && drawn.Drawing.InnerCount > 0 ? drawn.Drawing.Inner(_drawnValues) : null;
                _drawnAt = Time;
            }
            int at = drawn.RtlNetOf(net);
            return at >= 0 ? _drawnValues[at] : _drawnInner[-2 - at];   // a wire inside an opening into gates (GATES)
        }

        /// <summary>The value a part's signal holds now: a lamp's, what it shows.</summary>
        public LogicVec PartValue(int part) => ValueAt(_partOutputs[part], 1, Time);

        /// <summary>The value a port's signal holds now.</summary>
        public LogicVec PortValue(int port) => ValueAt(_portSignals[port], 1, Time);

        /// <summary>
        /// How far into its flash a gate is: 1 the moment its output flips, falling to 0 over
        /// <see cref="SchematicStyle.FlashTime"/> at the reference's speed (<see cref="UnitsPerSecond"/>); 0
        /// when it is not flashing. Its new value comes out too.
        /// </summary>
        /// <remarks>
        /// Only the gate's last flip at or before now can be flashing: changes come in time order, so if
        /// that one's flash is over, every earlier one's is too.
        /// </remarks>
        public float GateFlash(int gate, out LogicVec value)
        {
            value = default;
            int signal = _partOutputs[gate];
            if (signal < 0)
                return 0f;

            SignalTrack track = _tracks[signal];
            int i = track.LastAtOrBefore(Time);
            if (i < 1)
                return 0f;

            double at = track[i].Time;
            if (at <= 0 || Time >= at + _flashTime)
                return 0f;

            value = track[i].Value;
            return (float)(1.0 - (Time - at) / _flashTime);
        }

        /// <summary>How many changes are on their way: one per transition, however many branches it takes.</summary>
        public int InFlight
        {
            get
            {
                Window(out int first, out int last);
                int count = 0;
                for (int i = first; i <= last; i++)
                {
                    Transition t = _history.TransitionAt(i);
                    if (t.DueAt > t.ScheduledAt && Time >= t.ScheduledAt && Time < Ended(t))
                        count++;
                }

                return count;
            }
        }

        // -----------------------------------------------------------------
        // What the waveform shows
        // -----------------------------------------------------------------

        /// <summary>A row's value now, and its text.</summary>
        public LogicVec RowValue(int row, out string text) => RowValueAt(row, Time, out text);

        /// <summary>
        /// A row's value at <paramref name="time"/>, and its text: what the values column reads at the cursor (Part 15h).
        /// Held to what has run, as a moment shown is: past it the run has said nothing yet.
        /// </summary>
        public LogicVec RowValueAt(int row, double time, out string text)
        {
            int signal = _rowSignals[row];
            if (signal < 0)
            {
                text = "x";
                return LogicVec.AllX(1);
            }

            double last = Complete ? End : Math.Min(End, Reached);
            SignalTrack track = _tracks[signal];
            int i = track.IndexAt(Math.Max(0.0, Math.Min(last, time)));
            text = RowText(row, i);
            return track[i].Value;
        }

        /// <summary>How many rows the waveform has.</summary>
        public int RowCount => _rowSignals.Length;

        /// <summary>The signal a row shows, by its index in the design; -1 for none.</summary>
        public int RowSignal(int row) => _rowSignals[row];

        /// <summary>The radix a row is written in.</summary>
        public WaveRadix RowRadix(int row) => _rowRadix[row];

        /// <summary>
        /// A row's change <paramref name="change"/> as the row writes it: formatted once, in the row's
        /// radix, so a frame of playback formats nothing.
        /// </summary>
        public string RowText(int row, int change)
        {
            int signal = _rowSignals[row];
            SignalTrack track = _tracks[signal];
            WaveRadix radix = _rowRadix[row];
            if (radix == DefaultRadix(signal) && Session.Design.Signals[signal].Enum == null)
                return track.Text(change);
            return _texts.Get(signal, change, track[change].Value, radix, Session.Design.Signals[signal].Enum);
        }

        /// <summary>The drawn net showing a row's signal -- any name of its node -- or -1: what selecting a row lights.</summary>
        public int NetOfRow(int row)
        {
            int node = Aliases.NodeOf(_rowSignals[row]);
            if (node < 0)
                return -1;
            for (int net = 0; net < _netNode.Length; net++)
                if (_netNode[net] == node)
                    return net;
            return -1;
        }

        /// <summary>Whether a row shows a net's signal, by any name of its node: what pointing at a wire lights.</summary>
        public bool RowShowsNet(int row, int net) =>
            net >= 0 && net < _netNode.Length && _netNode[net] >= 0 && Aliases.NodeOf(_rowSignals[row]) == _netNode[net];

        /// <summary>Whether a row is a bus.</summary>
        public bool RowIsBus(int row) => _rowSignals[row] >= 0 && Session.Design.Signals[_rowSignals[row]].Width > 1;

        /// <summary>A row's waveform up to now, oldest first.</summary>
        public void RowSegments(int row, List<WaveSegment> into) => RowSegments(row, into, Time);

        /// <summary>
        /// A row's segments up to <paramref name="until"/>: the whole run's, for a waveform drawn once
        /// and revealed. While the run is being made, the last value known runs on to the end; nothing
        /// past where the run has got is ever revealed.
        /// </summary>
        public void RowSegments(int row, List<WaveSegment> into, double until)
        {
            into.Clear();
            int signal = _rowSignals[row];
            if (signal < 0)
                return;
            SignalTrack track = _tracks[signal];

            for (int i = 0; i < track.Count; i++)
            {
                double from = track[i].Time;
                if (from > until) break;

                double to = i + 1 < track.Count ? Math.Min(track[i + 1].Time, End) : End;
                into.Add(new WaveSegment { From = from, To = Math.Min(to, until), Value = track[i].Value, Text = RowText(row, i) });
            }
        }

        /// <summary>
        /// The moments a change on its way to a row's signal was swallowed, up to now: where the
        /// waveform marks a glitch that never arrived.
        /// </summary>
        public void RowSwallowed(int row, List<double> into) => RowSwallowed(row, into, Time);

        /// <summary>A row's swallowed changes up to <paramref name="until"/>.</summary>
        public void RowSwallowed(int row, List<double> into, double until)
        {
            into.Clear();
            foreach ((int _, double ended) in _swallowedOf[row])
                if (until >= ended)
                    into.Add(ended);
        }

        /// <summary>How many changes on their way to a row's signal are swallowed so far in the run: what its glitch marks can cost.</summary>
        public int RowSwallowedCount(int row) => _swallowedOf[row].Count;

        /// <summary>How many of them have been swallowed by now: how many marks the row's glitches show.</summary>
        public int RowSwallowedSoFar(int row)
        {
            List<double> times = _swallowedTimes[row];
            int low = 0, high = times.Count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (times[mid] <= Time) low = mid + 1;
                else high = mid;
            }

            return low;
        }

        /// <summary>
        /// A row's signal's settled changes so far, each with its text: what the waveform draws a window
        /// of (<see cref="WaveView.Spans"/>). Null for a row whose signal the design does not have.
        /// </summary>
        public SignalTrack RowTrack(int row) => _rowSignals[row] < 0 ? null : _tracks[_rowSignals[row]];

        /// <summary>The changes a row's signal makes so far: how many waveform segments it can have.</summary>
        public int RowChangeCount(int row) => _rowSignals[row] < 0 ? 0 : _tracks[_rowSignals[row]].Count;

        /// <summary>A signal's settled changes so far, by its index in the design; null for one nothing here reads.</summary>
        public SignalTrack TrackOf(int signal) => signal >= 0 && signal < _tracks.Length ? _tracks[signal] : null;

        /// <summary>
        /// The first edge after <paramref name="time"/> on any of <paramref name="rows"/>, as far as the run
        /// has got, or null: where STEP goes (Part 9b-3).
        /// </summary>
        public long? NextEdgeAfter(IReadOnlyList<int> rows, double time)
        {
            long whole = (long)Math.Floor(time + 1e-9);
            long? next = null;
            foreach (int row in rows)
            {
                SignalTrack track = RowTrack(row);
                long? edge = track?.NextEdgeAfter(whole);
                if (edge.HasValue && (next == null || edge.Value < next.Value))
                    next = edge;
            }

            return next;
        }

        /// <summary>The last edge before <paramref name="time"/> on any of <paramref name="rows"/>, after time 0, or null: where STEP BACK goes.</summary>
        public long? LastEdgeBefore(IReadOnlyList<int> rows, double time)
        {
            long whole = (long)Math.Ceiling(time - 1e-9);
            long? last = null;
            foreach (int row in rows)
            {
                SignalTrack track = RowTrack(row);
                long? edge = track?.LastEdgeBefore(whole);
                if (edge.HasValue && (last == null || edge.Value > last.Value))
                    last = edge;
            }

            return last;
        }

        // -----------------------------------------------------------------
        // Text
        // -----------------------------------------------------------------

        /// <summary>
        /// A value as the workbench writes it by default: 0, 1, x or z for one bit, a bus in hex
        /// (<see cref="ValueText"/>). A hex digit is <c>x</c> or <c>z</c> when all its bits are, and
        /// <c>X</c> or <c>Z</c> when only some are, as <c>$display</c> writes it.
        /// </summary>
        /// <remarks>
        /// A bus with any unknown bit used to read plain <c>x</c> whatever else it held (Part 9b-2a
        /// changed that): <c>4'b1x01</c> is now <c>X</c>, and <c>8'b0000_1x01</c> is <c>0X</c>, so the
        /// digits that are known still say so.
        /// </remarks>
        public static string TextOf(LogicVec value) =>
            ValueText.Of(value, value.Width > 1 ? WaveRadix.Hex : WaveRadix.Binary);

        // -----------------------------------------------------------------
        // Reading the history
        // -----------------------------------------------------------------

        private LogicVec ValueAt(int signal, int width, double time)
        {
            if (signal < 0)
                return LogicVec.AllX(Math.Max(1, width));
            SignalTrack track = _tracks[signal];
            return track[track.IndexAt(time)].Value;
        }

        /// <summary>A transition's value as a ring on <paramref name="net"/> writes it: in the radix of the row showing the net, if one does.</summary>
        private string RingText(int net, int transition, LogicVec value)
        {
            WaveRadix radix = _netRadix[net];
            int signal = _netSignals[net];
            if (radix == DefaultRadix(signal) && (signal < 0 || Session.Design.Signals[signal].Enum == null))
                return _transitionText[transition];
            return _texts.Get(-1 - net, transition, value, radix, signal >= 0 ? Session.Design.Signals[signal].Enum : null);
        }

        /// <summary>When a transition stopped travelling: on arrival, or where it was swallowed.</summary>
        private static double Ended(Transition t) =>
            t.Outcome == TransitionOutcome.Swallowed ? t.EndedAt : t.DueAt;

        /// <summary>
        /// A swallowed transition, marked on every row showing any name of the node it was on its way
        /// to (<see cref="SignalAliases"/>): a gate with delay prints as a library cell, so its changes
        /// go to the cell's own output, which the net it drives is joined to -- and, inside a block,
        /// to the block's port and the parent's net beyond it.
        /// </summary>
        private void Swallowed(int index, Transition t)
        {
            int target = t.Target.Index;

            for (int row = 0; row < _swallowedOf.Length; row++)
            {
                if (!Aliases.SameNode(target, _rowSignals[row]))
                    continue;

                // In the order the transitions were recorded, as a run made in one go lists them; a
                // change cancelled late can come after one recorded later.
                List<(int Transition, double Ended)> marks = _swallowedOf[row];
                int at = marks.Count;
                while (at > 0 && marks[at - 1].Transition > index)
                    at--;
                marks.Insert(at, (index, t.EndedAt));

                List<double> times = _swallowedTimes[row];
                int when = times.Count;
                while (when > 0 && times[when - 1] > t.EndedAt)
                    when--;
                times.Insert(when, t.EndedAt);
            }
        }

        /// <summary>
        /// The transitions that can be on their way, or fading, at the moment shown: scheduled at or
        /// before it, and no longer before it than the longest a transition takes and a fade lasts. A
        /// superset, which the exact test then narrows -- in the order they were recorded, so the
        /// markers come out as a walk of the whole run would give them.
        /// </summary>
        private void Window(out int first, out int last)
        {
            int count = _transitionsRead;
            if (!_inOrder)
            {
                first = 0;
                last = count - 1;
                return;
            }

            double earliest = Time - _longestSpan - _fadeTime - 1.0;

            int low = 0, high = count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (_history.TransitionAt(mid).ScheduledAt < earliest) low = mid + 1;
                else high = mid;
            }

            first = low;

            high = count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (_history.TransitionAt(mid).ScheduledAt <= Time) low = mid + 1;
                else high = mid;
            }

            last = low - 1;
        }

        private void PlaceMarkers()
        {
            _markers.Clear();
            _markerNets.Clear();

            Window(out int first, out int last);
            for (int i = first; i <= last; i++)
            {
                Transition t = _history.TransitionAt(i);
                if (t.DueAt <= t.ScheduledAt)
                    continue;

                int net = _netOfSignal[t.Target.Index];
                if (net < 0)
                    continue;

                bool swallowed = t.Outcome == TransitionOutcome.Swallowed;
                double ended = Ended(t);
                double span = t.DueAt - t.ScheduledAt;
                double along;
                float alpha = 1f;

                if (Time >= t.ScheduledAt && Time < ended)
                {
                    along = (Time - t.ScheduledAt) / span;
                }
                else if (swallowed && Time >= ended && Time < ended + _fadeTime)
                {
                    along = (ended - t.ScheduledAt) / span;
                    alpha = (float)(1.0 - (Time - ended) / _fadeTime);
                }
                else
                {
                    continue;
                }

                IReadOnlyList<StageRoute> branches = Layout.Nets[net].Branches;
                for (int b = 0; b < branches.Count; b++)
                {
                    float distance = (float)along * branches[b].Length;
                    _markerNets.Add(net);
                    _markers.Add(new ChangeMarker
                    {
                        Net = net,
                        Branch = branches[b],
                        Distance = distance,
                        Point = branches[b].PointAt(distance),
                        Value = t.Value,
                        Text = RingText(net, i, t.Value),
                        Alpha = alpha,
                        Swallowed = swallowed && Time >= ended,
                    });
                }
            }
        }

        /// <summary>When everything in the finished run is over: the last settled change of any signal, and the last transition to end.</summary>
        private long FindSettledAt()
        {
            long last = 0;
            Design design = Session.Design;

            for (int i = 0; i < design.Signals.Count; i++)
            {
                List<ValueChange> changes = _history.SettledChanges(design.SignalAt(i));
                if (changes.Count > 0)
                    last = Math.Max(last, changes[changes.Count - 1].Time);
            }

            for (int i = 0; i < _history.TransitionCount; i++)
            {
                Transition t = _history.TransitionAt(i);
                if (t.Outcome != TransitionOutcome.Pending)
                    last = Math.Max(last, t.EndedAt);
            }

            return last;
        }
    }
}
