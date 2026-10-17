using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// One signal's settled changes -- the last of each time step, repeats dropped, what a waveform
    /// shows -- grown as the run grows, each with its text.
    /// </summary>
    /// <remarks>
    /// <see cref="History.SettledChanges"/> answers the same question for a finished run, but builds a
    /// new list every call. A run made in slices is read again after every slice, so this keeps what it
    /// has worked out and reads only what the history recorded since.
    ///
    /// **Growing is exact, not approximate**, because of where a run can pause: a budget only stops
    /// <see cref="Simulator.RunTo"/> between time steps, so whenever <see cref="Grow"/> runs, the step
    /// of the last change recorded is finished, and that change is the last of its time. The rule is
    /// <see cref="History.SettledChanges"/>' own, applied a piece at a time; a test grows a track after
    /// every slice, at slices of one event and up, and holds it to the one-shot list.
    ///
    /// **The one step that can go on is the one a <c>$stop</c> suspended** (Part 9b-3): running on carries
    /// it on at the same time, so a change can come at the time of the last one read. It then replaces
    /// what the step had settled on, as the one-shot list would have it.
    /// </remarks>
    public sealed class SignalTrack
    {
        private readonly History _history;
        private readonly SignalId _signal;
        private readonly Func<LogicVec, string> _text;
        private readonly List<ValueChange> _settled = new List<ValueChange>(4);
        private readonly List<string> _texts = new List<string>(4);

        /// <summary>How many raw changes have been read.</summary>
        private int _read;

        public SignalTrack(History history, SignalId signal, Func<LogicVec, string> text)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _signal = signal;
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>How many settled changes it has, its starting value included.</summary>
        public int Count => _settled.Count;

        public ValueChange this[int index] => _settled[index];

        /// <summary>A settled change's value as the workbench writes it.</summary>
        public string Text(int index) => _texts[index];

        /// <summary>
        /// Takes in whatever the history recorded since the last call. Only between slices of a run:
        /// see the remarks. True when the settled changes are different.
        /// </summary>
        public bool Grow()
        {
            int count = _history.ChangeCount(_signal);
            if (count == _read)
                return false;

            bool changed = false;

            for (int i = _read; i < count; i++)
            {
                ValueChange change = _history.Change(_signal, i);

                // The last of its time: the next change is at a later time, or there is none yet --
                // and none can come at this time any more, since its step is finished.
                bool lastOfItsTime = i == count - 1 || _history.Change(_signal, i + 1).Time != change.Time;
                if (!lastOfItsTime)
                    continue;

                // A step a $stop suspended, carried on: what it settles on now replaces what it had.
                int last = _settled.Count - 1;
                if (last >= 0 && _settled[last].Time == change.Time)
                {
                    _settled.RemoveAt(last);
                    _texts.RemoveAt(last);
                    changed = true;
                }

                if (_settled.Count > 0 && _settled[_settled.Count - 1].Value.Equals(change.Value))
                    continue;

                _settled.Add(change);
                _texts.Add(_text(change.Value));
                changed = true;
            }

            _read = count;
            return changed;
        }

        /// <summary>
        /// The settled change in force at <paramref name="time"/>: the last at or before its whole time
        /// step, so a moment part-way through a step reads the value the step settled on.
        /// </summary>
        public int IndexAt(double time)
        {
            long step = (long)Math.Floor(time + 1e-9);
            int low = 0, high = _settled.Count - 1, found = 0;

            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (_settled[mid].Time <= step) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }

            return found;
        }

        /// <summary>The last settled change at or before <paramref name="time"/> exactly, or -1: where a gate's flash starts from.</summary>
        public int LastAtOrBefore(double time)
        {
            int low = 0, high = _settled.Count - 1, found = -1;

            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (_settled[mid].Time <= time) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }

            return found;
        }

        /// <summary>The first settled change after <paramref name="time"/>, if there is one yet: the next edge.</summary>
        public long? NextEdgeAfter(long time)
        {
            int i = LastAtOrBefore(time) + 1;
            return i < _settled.Count ? _settled[i].Time : (long?)null;
        }

        /// <summary>The last settled change before <paramref name="time"/>, at a time after 0: the previous edge.</summary>
        public long? LastEdgeBefore(long time)
        {
            int i = LastAtOrBefore(time - 1);
            return i >= 1 ? _settled[i].Time : (long?)null;
        }
    }
}
