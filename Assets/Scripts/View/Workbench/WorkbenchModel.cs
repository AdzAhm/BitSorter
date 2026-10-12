using System;
using System.Collections.Generic;
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
        private readonly SignalId[] _netSignals;
        private readonly SignalId[] _gateOutputs;
        private readonly SignalId[] _rowSignals;
        private readonly SignalId[] _portSignals;

        /// <summary>Each signal's settled changes, and their text, by signal index.</summary>
        private readonly ValueChange[][] _settled;

        /// <inheritdoc cref="_settled"/>
        private readonly string[][] _settledText;

        /// <summary>Each recorded transition's value as its ring shows it.</summary>
        private readonly string[] _transitionText;

        /// <summary>Which net each signal is drawn as, or -1.</summary>
        private readonly int[] _netOfSignal;

        private readonly List<ChangeMarker> _markers = new List<ChangeMarker>(16);

        public WorkbenchModel(SimSession session, SchematicLayout layout, long end)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            End = end;
            _history = session.History;

            Design design = session.Design;

            _netSignals = new SignalId[layout.Nets.Count];
            _netOfSignal = new int[design.Signals.Count];
            for (int i = 0; i < _netOfSignal.Length; i++) _netOfSignal[i] = -1;

            for (int i = 0; i < layout.Nets.Count; i++)
            {
                _netSignals[i] = design.Find(layout.Nets[i].Signal);
                _netOfSignal[_netSignals[i].Index] = i;
            }

            _gateOutputs = new SignalId[layout.Gates.Count];
            for (int i = 0; i < layout.Gates.Count; i++)
                _gateOutputs[i] = design.Find(layout.Gates[i].Output);

            _rowSignals = new SignalId[layout.Rows.Count];
            for (int i = 0; i < layout.Rows.Count; i++)
                _rowSignals[i] = design.Find(layout.Rows[i].Signal);

            _portSignals = new SignalId[layout.Ports.Count];
            for (int i = 0; i < layout.Ports.Count; i++)
                _portSignals[i] = design.Find(layout.Ports[i].Signal);

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

            SettledAt = FindSettledAt();
            Seek(0);
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
        public LogicVec NetValue(int net) => ValueAt(_netSignals[net], Time);

        /// <summary>The value a port's signal holds now.</summary>
        public LogicVec PortValue(int port) => ValueAt(_portSignals[port], Time);

        /// <summary>
        /// How far into its flash a gate is: 1 the moment its output flips, falling to 0 over
        /// <see cref="SchematicStyle.FlashTime"/>; 0 when it is not flashing. Its new value comes out too.
        /// </summary>
        public float GateFlash(int gate, out LogicVec value)
        {
            ValueChange[] changes = _settled[_gateOutputs[gate].Index];
            float flash = 0f;
            value = default;

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
            int signal = _rowSignals[row].Index;
            int i = IndexAt(_settled[signal], Time);
            text = _settledText[signal][i];
            return _settled[signal][i].Value;
        }

        /// <summary>Whether a row is a bus.</summary>
        public bool RowIsBus(int row) => Session.Design[_rowSignals[row]].Width > 1;

        /// <summary>A row's waveform up to now, oldest first.</summary>
        public void RowSegments(int row, List<WaveSegment> into)
        {
            into.Clear();
            int signal = _rowSignals[row].Index;
            ValueChange[] changes = _settled[signal];

            for (int i = 0; i < changes.Length; i++)
            {
                double from = changes[i].Time;
                if (from > Time) break;

                double to = i + 1 < changes.Length ? Math.Min(changes[i + 1].Time, End) : End;
                into.Add(new WaveSegment { From = from, To = Math.Min(to, Time), Value = changes[i].Value, Text = _settledText[signal][i] });
            }
        }

        /// <summary>
        /// The moments a change on its way to a row's signal was swallowed, up to now: where the
        /// waveform marks a glitch that never arrived.
        /// </summary>
        public void RowSwallowed(int row, List<double> into)
        {
            into.Clear();
            SignalId signal = _rowSignals[row];

            for (int i = 0; i < _history.TransitionCount; i++)
            {
                Transition t = _history.TransitionAt(i);
                if (t.Target.Equals(signal) && t.Outcome == TransitionOutcome.Swallowed && Time >= t.EndedAt)
                    into.Add(t.EndedAt);
            }
        }

        /// <summary>The changes a row's signal ever makes: how many waveform segments it can have.</summary>
        public int RowChangeCount(int row) => _settled[_rowSignals[row].Index].Length;

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

        private LogicVec ValueAt(SignalId signal, double time)
        {
            ValueChange[] changes = _settled[signal.Index];
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
