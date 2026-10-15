using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>A change on its way down one branch of a wire, as the schematic draws it at the current moment.</summary>
    public struct ChangeMarker
    {
        /// <summary>The net it is travelling on, by its index in the layout.</summary>
        public int Net;

        /// <summary>The branch it is on.</summary>
        public Route Branch;

        /// <summary>How far along the branch it has got.</summary>
        public float Distance;

        /// <summary>Where that is.</summary>
        public Vector2 Point;

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
    /// The run is made once, to its end, when the model is built; rewinding, scrubbing and replaying
    /// are this class answering for a different moment. <see cref="Seek"/> moves the moment and
    /// works out the markers once, and everything that draws reads them, so the rings, their digits
    /// and the glow cannot disagree about where a change is.
    ///
    /// Time here is a double, in the run's time units (nanoseconds as shown): the engine's time is
    /// an integer, and the moments between are where a change is part-way down its wire.
    ///
    /// **Nothing here allocates after it is built**, the text included: every value a ring, a
    /// readout or a bus segment shows is formatted once, here, so a frame of playback makes no
    /// garbage.
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

        /// <summary>Each signal's settled changes, and their text, by signal index.</summary>
        private readonly ValueChange[][] _settled;

        /// <inheritdoc cref="_settled"/>
        private readonly string[][] _settledText;

        /// <summary>Each recorded transition's value as its ring shows it.</summary>
        private readonly string[] _transitionText;

        /// <summary>Which net each signal is drawn as, or -1.</summary>
        private readonly int[] _netOfSignal;

        private readonly List<ChangeMarker> _markers = new List<ChangeMarker>(16);
        private readonly List<int> _markerNets = new List<int>(16);
        private readonly double[][] _swallowedOf;

        public WorkbenchModel(SimSession session, SchematicLayout layout, long end)
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

            _settled = new ValueChange[design.Signals.Count][];
            _settledText = new string[design.Signals.Count][];
            for (int i = 0; i < design.Signals.Count; i++)
            {
                _settled[i] = _history.SettledChanges(design.SignalAt(i)).ToArray();
                _settledText[i] = Array.ConvertAll(_settled[i], change => TextOf(change.Value));
            }

            _transitionText = new string[_history.TransitionCount];
            for (int i = 0; i < _transitionText.Length; i++)
                _transitionText[i] = TextOf(_history.TransitionAt(i).Value);

            // Where each signal's swallowed changes died, in the order they happened: the waveform asks
            // for every row's on every frame, and walking the whole run's transitions each time was
            // the run's length times the rows.
            var swallowed = new List<double>[design.Signals.Count];
            for (int i = 0; i < _history.TransitionCount; i++)
            {
                Transition t = _history.TransitionAt(i);
                if (t.Outcome == TransitionOutcome.Swallowed && t.Target.Index >= 0 && t.Target.Index < swallowed.Length)
                    (swallowed[t.Target.Index] ??= new List<double>()).Add(t.EndedAt);
            }

            _swallowedOf = new double[design.Signals.Count][];
            for (int i = 0; i < _swallowedOf.Length; i++)
                _swallowedOf[i] = swallowed[i]?.ToArray() ?? Array.Empty<double>();

            SettledAt = FindSettledAt();
            Seek(0);
        }

        /// <summary>
        /// A document compiled, elaborated, run to the end of its stimulus and laid out: what the
        /// workbench shows of it. Throws when the document does not compile; the workbench only opens
        /// one that does.
        /// </summary>
        public static WorkbenchModel Of(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            if (!compiled.Succeeded)
                throw new InvalidOperationException("The document does not compile: " + string.Join("; ", compiled.Diagnostics));

            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            if (elaboration.HasErrors)
                throw new InvalidOperationException("The document does not elaborate: " + string.Join("; ", elaboration.Diagnostics));

            long end = project.Stimulus?.End ?? 100;
            var session = new SimSession(elaboration.Design, null);
            session.RunTo(end);
            return new WorkbenchModel(session, DocumentSchematic.Build(project, compiled), end);
        }

        public SimSession Session { get; }
        public SchematicLayout Layout { get; }

        /// <summary>The end of the timeline: how far the run went.</summary>
        public long End { get; }

        /// <summary>The moment being shown.</summary>
        public double Time { get; private set; }

        /// <summary>The last moment anything happens: after it, nothing changes and nothing is on its way.</summary>
        public long SettledAt { get; }

        /// <summary>The changes on their way at <see cref="Time"/>, one per branch they travel down.</summary>
        public IReadOnlyList<ChangeMarker> Markers => _markers;

        /// <summary>
        /// The net each of <see cref="Markers"/> is on, in the same order: what a chunk of the drawing
        /// asks of every marker to find its own, without copying each marker out to ask it.
        /// </summary>
        public IReadOnlyList<int> MarkerNets => _markerNets;

        /// <summary>Shows another moment: clamped to the timeline, and every marker worked out for it.</summary>
        public void Seek(double time)
        {
            Time = Math.Max(0.0, Math.Min(End, time));
            PlaceMarkers();
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
        public float GateFlash(int gate, out LogicVec value)
        {
            float flash = 0f;
            value = default;
            if (_partOutputs[gate] < 0)
                return 0f;

            ValueChange[] changes = _settled[_partOutputs[gate]];

            for (int i = 1; i < changes.Length; i++)
            {
                double at = changes[i].Time;
                if (at > 0 && Time >= at && Time < at + SchematicStyle.FlashTime)
                {
                    flash = (float)(1.0 - (Time - at) / SchematicStyle.FlashTime);
                    value = changes[i].Value;
                }
            }

            return flash;
        }

        /// <summary>How many changes are on their way: one per transition, however many branches it takes.</summary>
        public int InFlight
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _history.TransitionCount; i++)
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

            int i = IndexAt(_settled[signal], Time);
            text = _settledText[signal][i];
            return _settled[signal][i].Value;
        }

        /// <summary>Whether a row is a bus.</summary>
        public bool RowIsBus(int row) => _rowSignals[row] >= 0 && Session.Design.Signals[_rowSignals[row]].Width > 1;

        /// <summary>A row's waveform up to now, oldest first.</summary>
        public void RowSegments(int row, List<WaveSegment> into) => RowSegments(row, into, Time);

        /// <summary>A row's segments up to <paramref name="until"/>: the whole run's, for a waveform drawn once and revealed.</summary>
        public void RowSegments(int row, List<WaveSegment> into, double until)
        {
            into.Clear();
            int signal = _rowSignals[row];
            if (signal < 0)
                return;
            ValueChange[] changes = _settled[signal];

            for (int i = 0; i < changes.Length; i++)
            {
                double from = changes[i].Time;
                if (from > until) break;

                double to = i + 1 < changes.Length ? Math.Min(changes[i + 1].Time, End) : End;
                into.Add(new WaveSegment { From = from, To = Math.Min(to, until), Value = changes[i].Value, Text = _settledText[signal][i] });
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
            int signal = _rowSignals[row];
            if (signal < 0)
                return;

            foreach (double ended in _swallowedOf[signal])
                if (until >= ended)
                    into.Add(ended);
        }

        /// <summary>How many changes on their way to a row's signal are swallowed in the whole run: what its glitch marks can cost.</summary>
        public int RowSwallowedCount(int row) => _rowSignals[row] < 0 ? 0 : _swallowedOf[_rowSignals[row]].Length;

        /// <summary>How many of them have been swallowed by now: how many marks the row's glitches show.</summary>
        public int RowSwallowedSoFar(int row)
        {
            if (_rowSignals[row] < 0)
                return 0;

            int count = 0;
            foreach (double ended in _swallowedOf[_rowSignals[row]])
                if (Time >= ended)
                    count++;
            return count;
        }

        /// <summary>The changes a row's signal ever makes: how many waveform segments it can have.</summary>
        public int RowChangeCount(int row) => _rowSignals[row] < 0 ? 0 : _settled[_rowSignals[row]].Length;

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
            ValueChange[] changes = _settled[signal];
            return changes[IndexAt(changes, time)].Value;
        }

        /// <summary>The settled change in force at <paramref name="time"/>: the last at or before its whole time step.</summary>
        private static int IndexAt(ValueChange[] changes, double time)
        {
            long step = (long)Math.Floor(time + 1e-9);
            int low = 0, high = changes.Length - 1, found = 0;

            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (changes[mid].Time <= step) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }

            return found;
        }

        /// <summary>When a transition stopped travelling: on arrival, or where it was swallowed.</summary>
        private static double Ended(Transition t) =>
            t.Outcome == TransitionOutcome.Swallowed ? t.EndedAt : t.DueAt;

        private void PlaceMarkers()
        {
            _markers.Clear();
            _markerNets.Clear();

            for (int i = 0; i < _history.TransitionCount; i++)
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

                IReadOnlyList<Route> branches = Layout.Nets[net].Branches;
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

        private long FindSettledAt()
        {
            long last = 0;

            foreach (ValueChange[] changes in _settled)
                if (changes.Length > 0)
                    last = Math.Max(last, changes[changes.Length - 1].Time);

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
