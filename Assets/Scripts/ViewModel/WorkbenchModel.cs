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

        /// <inheritdoc cref="_netSignals"/>
        private readonly int[] _rowSignals;

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
        private readonly List<(int Transition, double Ended)>[] _swallowedOf;

        /// <summary>The same moments, in time order: what a row's count so far is a binary search of.</summary>
        private readonly List<double>[] _swallowedTimes;

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

            _rowSignals = new int[layout.Rows.Count];
            for (int i = 0; i < layout.Rows.Count; i++)
                _rowSignals[i] = Index(layout.Rows[i].Signal);

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
            foreach (int signal in _rowSignals) Track(signal);
            foreach (int signal in _portSignals) Track(signal);

            _swallowedOf = new List<(int, double)>[layout.Rows.Count];
            _swallowedTimes = new List<double>[layout.Rows.Count];
            for (int row = 0; row < _swallowedOf.Length; row++)
            {
                _swallowedOf[row] = new List<(int, double)>();
                _swallowedTimes[row] = new List<double>();
            }

            Grow(complete);
            Seek(0);
        }

        /// <summary>
        /// A document compiled, elaborated, run to where its testbench stops and laid out: what the
        /// workbench shows of it, made in one go. Throws when the document does not compile; the
        /// workbench only opens one that does.
        /// </summary>
        public static WorkbenchModel Of(Project project)
        {
            (Design design, SchematicLayout layout, long horizon) = Prepare(project);
            var session = new SimSession(design, null);
            session.RunTo(horizon);
            return new WorkbenchModel(session, layout, horizon);
        }

        /// <summary>
        /// A document compiled and elaborated, its layout, and the time its run is made to: where the
        /// testbench stops (<see cref="Stimulus.StopTime"/>), or 100 without one.
        /// </summary>
        public static (Design Design, SchematicLayout Layout, long Horizon) Prepare(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                throw new InvalidOperationException("The document does not compile: " + string.Join("; ", compiled.Diagnostics));

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
                throw new InvalidOperationException("The document does not elaborate: " + string.Join("; ", elaboration.Diagnostics));

            long horizon = project.Stimulus?.StopTime ?? 100;
            return (elaboration.Design, DocumentSchematic.Build(project, compiled), horizon);
        }

        public SimSession Session { get; }
        public SchematicLayout Layout { get; }

        /// <summary>The end of the timeline: where the run is made to.</summary>
        public long End { get; }

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
                PlaceMarkers();
            return grew;
        }

        // -----------------------------------------------------------------
        // What the schematic shows
        // -----------------------------------------------------------------

        /// <summary>The value a net holds now.</summary>
        public LogicVec NetValue(int net) => ValueAt(_netSignals[net], Layout.Nets[net].Width, Time);

        /// <summary>The value a part's signal holds now: a lamp's, what it shows.</summary>
        public LogicVec PartValue(int part) => ValueAt(_partOutputs[part], 1, Time);

        /// <summary>The value a port's signal holds now.</summary>
        public LogicVec PortValue(int port) => ValueAt(_portSignals[port], 1, Time);

        /// <summary>
        /// How far into its flash a gate is: 1 the moment its output flips, falling to 0 over
        /// <see cref="SchematicStyle.FlashTime"/>; 0 when it is not flashing. Its new value comes out too.
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
            if (at <= 0 || Time >= at + SchematicStyle.FlashTime)
                return 0f;

            value = track[i].Value;
            return (float)(1.0 - (Time - at) / SchematicStyle.FlashTime);
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
        public LogicVec RowValue(int row, out string text)
        {
            int signal = _rowSignals[row];
            if (signal < 0)
            {
                text = "x";
                return LogicVec.AllX(1);
            }

            SignalTrack track = _tracks[signal];
            int i = track.IndexAt(Time);
            text = track.Text(i);
            return track[i].Value;
        }

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
                into.Add(new WaveSegment { From = from, To = Math.Min(to, until), Value = track[i].Value, Text = track.Text(i) });
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

        /// <summary>The changes a row's signal makes so far: how many waveform segments it can have.</summary>
        public int RowChangeCount(int row) => _rowSignals[row] < 0 ? 0 : _tracks[_rowSignals[row]].Count;

        // -----------------------------------------------------------------
        // Text
        // -----------------------------------------------------------------

        /// <summary>
        /// A value as the workbench writes it: 0, 1, x or z for one bit; a bus in hex, x if any bit
        /// is unknown and z if all are undriven.
        /// </summary>
        public static string TextOf(LogicVec value)
        {
            if (value.Width == 1)
            {
                switch (value[0])
                {
                    case Logic4.Zero: return "0";
                    case Logic4.One: return "1";
                    case Logic4.Z: return "z";
                    default: return "x";
                }
            }

            if (value.IsAllZ) return "z";
            return value.IsKnown ? value.Format(Radix.Hex).ToUpperInvariant() : "x";
        }

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

        /// <summary>When a transition stopped travelling: on arrival, or where it was swallowed.</summary>
        private static double Ended(Transition t) =>
            t.Outcome == TransitionOutcome.Swallowed ? t.EndedAt : t.DueAt;

        /// <summary>A swallowed transition, marked on every row whose net it was on its way to.</summary>
        private void Swallowed(int index, Transition t)
        {
            int target = t.Target.Index;

            for (int row = 0; row < _swallowedOf.Length; row++)
            {
                int signal = _rowSignals[row];
                if (signal < 0)
                    continue;

                int net = _netOfSignal[signal];
                bool toThisRow = target == signal || (net >= 0 && target >= 0 && target < _netOfSignal.Length && _netOfSignal[target] == net);
                if (!toThisRow)
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

            double earliest = Time - _longestSpan - SchematicStyle.FadeTime - 1.0;

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
                else if (swallowed && Time >= ended && Time < ended + SchematicStyle.FadeTime)
                {
                    along = (ended - t.ScheduledAt) / span;
                    alpha = (float)(1.0 - (Time - ended) / SchematicStyle.FadeTime);
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
                        Text = _transitionText[i],
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
