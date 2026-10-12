using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>A signal taking a new value: when, in which delta of that time step, and what.</summary>
    public readonly struct ValueChange
    {
        public readonly long Time;

        /// <summary>
        /// Which round of the time step it happened in: 0 for the first. Zero-delay logic settles over
        /// several rounds at one time, and a signal can change more than once in them.
        /// </summary>
        public readonly int Delta;

        public readonly LogicVec Value;

        internal ValueChange(long time, int delta, LogicVec value)
        {
            Time = time;
            Delta = delta;
            Value = value;
        }

        public override string ToString() => $"{Time}.{Delta} {Value.Format(Radix.Binary)}";
    }

    /// <summary>What happened to a transition in flight.</summary>
    public enum TransitionOutcome
    {
        /// <summary>Still on its way.</summary>
        Pending,

        /// <summary>Arrived: the driver's output took the value.</summary>
        Applied,

        /// <summary>
        /// Cancelled before it arrived, because the driver's inputs changed again: a pulse shorter
        /// than the delay, swallowed (inertial delay). This is the glitch the look draws dying on its wire.
        /// </summary>
        Swallowed,
    }

    /// <summary>
    /// A change a driver scheduled: its output heading for <see cref="Value"/>, decided at
    /// <see cref="ScheduledAt"/> and due at <see cref="DueAt"/>, and how that ended.
    /// </summary>
    /// <remarks>
    /// The engine records scheduling as well as results because the look draws the change travelling
    /// out of the gate that caused it for the gate's delay -- and a value-change log cannot say a
    /// change was ever on its way, least of all one that was swallowed (Docs/architecture-5.md, ADR-6).
    /// </remarks>
    public struct Transition
    {
        public int Driver;
        public SignalId Target;
        public long ScheduledAt;
        public long DueAt;
        public LogicVec Value;
        public TransitionOutcome Outcome;

        /// <summary>When it arrived or was swallowed; meaningless while pending.</summary>
        public long EndedAt;

        public override string ToString() =>
            $"driver {Driver} -> {Value.Format(Radix.Binary)} scheduled {ScheduledAt} due {DueAt}: {Outcome}" +
            (Outcome == TransitionOutcome.Pending ? "" : $" at {EndedAt}");
    }

    public enum Severity { Info, Warning, Error }

    /// <summary>Something the run has to say: a diagnostic now, <c>$info</c> and <c>$error</c> from Part 5.</summary>
    public readonly struct SimMessage
    {
        public readonly long Time;
        public readonly Severity Severity;
        public readonly string Text;

        internal SimMessage(long time, Severity severity, string text)
        {
            Time = time;
            Severity = severity;
            Text = text;
        }

        public override string ToString() => $"{Time}: {Severity}: {Text}";
    }

    /// <summary>
    /// Everything a run did, as the view reads it: every signal's value changes, every transition in
    /// flight, and every message. Rewinding, scrubbing and stepping back are lookups here; nothing
    /// re-runs (ADR-6).
    /// </summary>
    public sealed class History
    {
        private readonly Buffer<ValueChange>[] _changes;
        private readonly Buffer<Transition> _transitions = new Buffer<Transition>(64);
        private readonly List<SimMessage> _messages = new List<SimMessage>();

        internal History(int signalCount)
        {
            _changes = new Buffer<ValueChange>[signalCount];
            for (int i = 0; i < signalCount; i++)
                _changes[i] = new Buffer<ValueChange>(4);
        }

        /// <summary>How many changes a signal has had, its starting value included.</summary>
        public int ChangeCount(SignalId signal) => _changes[signal.Index].Count;

        /// <summary>One of a signal's changes, in order: the first is its value at time 0, before anything ran.</summary>
        public ValueChange Change(SignalId signal, int index) => _changes[signal.Index][index];

        /// <summary>A signal's changes, each the last of its time step: what a waveform and a VCD show.</summary>
        public List<ValueChange> SettledChanges(SignalId signal)
        {
            Buffer<ValueChange> all = _changes[signal.Index];
            var settled = new List<ValueChange>(all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                bool lastOfItsTime = i == all.Count - 1 || all[i + 1].Time != all[i].Time;
                if (!lastOfItsTime)
                    continue;

                if (settled.Count > 0 && settled[settled.Count - 1].Value.Equals(all[i].Value))
                    continue;

                settled.Add(all[i]);
            }

            return settled;
        }

        /// <summary>A signal's value once time step <paramref name="time"/> has settled.</summary>
        public LogicVec ValueAt(SignalId signal, long time)
        {
            Buffer<ValueChange> changes = _changes[signal.Index];

            int low = 0, high = changes.Count - 1, found = 0;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (changes[mid].Time <= time)
                {
                    found = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return changes[found].Value;
        }

        /// <summary>The first time after <paramref name="time"/> at which any signal changed, if one did.</summary>
        public long? NextChangeAfter(long time)
        {
            long? next = null;
            foreach (Buffer<ValueChange> changes in _changes)
            {
                for (int i = 0; i < changes.Count; i++)
                {
                    if (changes[i].Time > time)
                    {
                        if (next == null || changes[i].Time < next) next = changes[i].Time;
                        break;
                    }
                }
            }

            return next;
        }

        /// <summary>The last time before <paramref name="time"/> at which any signal changed, if one did.</summary>
        public long? LastChangeBefore(long time)
        {
            long? last = null;
            foreach (Buffer<ValueChange> changes in _changes)
            {
                for (int i = changes.Count - 1; i >= 0; i--)
                {
                    if (changes[i].Time < time)
                    {
                        if (last == null || changes[i].Time > last) last = changes[i].Time;
                        break;
                    }
                }
            }

            return last;
        }

        /// <summary>How many transitions were recorded.</summary>
        public int TransitionCount => _transitions.Count;

        public Transition TransitionAt(int index) => _transitions[index];

        public IReadOnlyList<SimMessage> Messages => _messages;

        // -----------------------------------------------------------------
        // Recording, for the simulator
        // -----------------------------------------------------------------

        internal void RecordChange(int signal, long time, int delta, LogicVec value) =>
            _changes[signal].Add(new ValueChange(time, delta, value));

        internal int BeginTransition(int driver, SignalId target, long scheduledAt, long dueAt, LogicVec value)
        {
            _transitions.Add(new Transition
            {
                Driver = driver,
                Target = target,
                ScheduledAt = scheduledAt,
                DueAt = dueAt,
                Value = value,
                Outcome = TransitionOutcome.Pending,
            });

            return _transitions.Count - 1;
        }

        internal void EndTransition(int index, TransitionOutcome outcome, long at)
        {
            ref Transition t = ref _transitions.Ref(index);
            t.Outcome = outcome;
            t.EndedAt = at;
        }

        internal void Message(long time, Severity severity, string text) => _messages.Add(new SimMessage(time, severity, text));
    }

    /// <summary>
    /// A list of structs that grows by doubling and hands out references, so a recorded transition can
    /// be updated in place. List&lt;T&gt; cannot: its indexer returns a copy.
    /// </summary>
    internal sealed class Buffer<T>
    {
        private T[] _items;

        public Buffer(int capacity) => _items = new T[Math.Max(1, capacity)];

        public int Count { get; private set; }

        public T this[int index] => _items[index];

        public ref T Ref(int index) => ref _items[index];

        public void Add(T item)
        {
            if (Count == _items.Length)
                Array.Resize(ref _items, _items.Length * 2);

            _items[Count++] = item;
        }
    }
}
