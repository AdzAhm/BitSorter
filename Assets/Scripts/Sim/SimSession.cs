using System;
using BitSorter.SystemVerilog;

namespace BitSorter.Sim
{
    /// <summary>
    /// A run as the view drives it: run ahead in slices, and read any moment back from the history --
    /// rewind, scrub and step back are lookups, and an edit is a replay from 0 (ADR-6).
    /// </summary>
    /// <remarks>
    /// Thin on purpose. The design is immutable and the engine deterministic, so "go back and change
    /// an input" needs no undo inside the engine: a new session on the same design with the new
    /// stimulus, run to where the old one had got, gives exactly the history the change would have.
    /// </remarks>
    public sealed class SimSession
    {
        private readonly SimOptions _options;

        public SimSession(Design design, Stimulus stimulus, SimOptions options = null)
        {
            Design = design ?? throw new ArgumentNullException(nameof(design));
            Stimulus = stimulus ?? new Stimulus();
            _options = options ?? new SimOptions();
            Simulator = new Simulator(design, Stimulus, _options);
        }

        public Design Design { get; }

        public Stimulus Stimulus { get; }

        public Simulator Simulator { get; }

        public History History => Simulator.History;

        /// <summary>How far the run has got: nothing at or before it changes again.</summary>
        public long Reached => Simulator.Now;

        /// <summary>Runs ahead to <paramref name="time"/>, at most <paramref name="maxEvents"/> events at a time.</summary>
        public RunResult RunTo(long time, long maxEvents = long.MaxValue) => Simulator.RunTo(time, maxEvents);

        /// <summary>A signal's value at a moment the run has reached.</summary>
        public LogicVec ValueAt(SignalId signal, long time) => History.ValueAt(signal, Math.Min(time, Reached));

        /// <summary>The next moment after <paramref name="time"/> that anything changed, within what has run.</summary>
        public long? StepForward(long time)
        {
            long? next = History.NextChangeAfter(time);
            return next.HasValue && next.Value <= Reached ? next : null;
        }

        /// <summary>The last moment before <paramref name="time"/> that anything changed.</summary>
        public long? StepBack(long time) => History.LastChangeBefore(time);

        /// <summary>
        /// The same design run again with <paramref name="stimulus"/>, as far as this session had got.
        /// What an edited input or a restart is.
        /// </summary>
        public SimSession Replay(Stimulus stimulus)
        {
            var replay = new SimSession(Design, stimulus, _options);
            replay.RunTo(Reached);
            return replay;
        }
    }
}
