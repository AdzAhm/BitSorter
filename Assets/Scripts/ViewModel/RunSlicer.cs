using System;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Makes a run a slice at a time, so a long one never freezes a frame: chunks of about a thousand
    /// events while the frame has time left, as Part 6b measured the browser (Docs/architecture-5.md,
    /// section 5).
    /// </summary>
    /// <remarks>
    /// **The clock is handed in**, as milliseconds from any origin, so a test drives it: the view
    /// passes Unity's real time, and nothing here reads one of its own.
    ///
    /// **The horizon is always a finite time.** <see cref="Simulator.RunTo"/> asked for
    /// <c>long.MaxValue</c> never returns on a design whose events run out without a <c>$finish</c>:
    /// with nothing queued the next event is at <c>long.MaxValue</c>, which is never past the time asked
    /// for, and each step processes nothing, so no budget is ever spent. A finite horizon ends it.
    ///
    /// A slice always runs at least one chunk, however little time is left, so a run moves on every
    /// frame and cannot be starved by a frame that was already slow.
    /// </remarks>
    public sealed class RunSlicer
    {
        /// <summary>Events in one chunk: small enough to check the clock often, large enough that checking costs nothing.</summary>
        public const int ChunkEvents = 1000;

        /// <summary>Milliseconds of a frame a slice may take: half a frame at 60 Hz.</summary>
        public const double BudgetMilliseconds = 8.0;

        private readonly SimSession _session;
        private readonly Func<double> _now;
        private readonly int _chunkEvents;
        private readonly double _budget;

        public RunSlicer(SimSession session, long horizon, Func<double> nowMilliseconds,
            int chunkEvents = ChunkEvents, double budgetMilliseconds = BudgetMilliseconds)
        {
            if (horizon < 0 || horizon == long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(horizon), horizon, "A run needs a finite end: see the remarks.");
            if (chunkEvents < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkEvents), chunkEvents, "A chunk is at least one event.");

            _session = session ?? throw new ArgumentNullException(nameof(session));
            _now = nowMilliseconds ?? throw new ArgumentNullException(nameof(nowMilliseconds));
            Horizon = horizon;
            _chunkEvents = chunkEvents;
            _budget = budgetMilliseconds;
        }

        /// <summary>The time the run is made to.</summary>
        public long Horizon { get; }

        /// <summary>Whether the run is made: it reached the horizon, or the design ended it, or it had to stop.</summary>
        public bool Done { get; private set; }

        /// <summary>How it ended, once it has; null while it is still being made.</summary>
        public RunStatus? Ended { get; private set; }

        /// <summary>How far the run has got: nothing at or before it changes again.</summary>
        public long Reached => _session.Reached;

        /// <summary>How many chunks have run, all slices together.</summary>
        public int Chunks { get; private set; }

        /// <summary>
        /// Runs chunks until the budget is spent or the run is made, and at least one. Returns how many
        /// ran: none once the run is done.
        /// </summary>
        public int Slice()
        {
            if (Done)
                return 0;

            double start = _now();
            int ran = 0;

            do
            {
                RunResult result = _session.RunTo(Horizon, _chunkEvents);
                ran++;

                if (result.Status != RunStatus.Paused)
                {
                    Done = true;
                    Ended = result.Status;
                    break;
                }
            }
            while (_now() - start < _budget);

            Chunks += ran;
            return ran;
        }

        /// <summary>Makes the whole run now, without slicing: what a test or a capture that needs it finished does.</summary>
        public void Finish()
        {
            while (!Done)
            {
                RunResult result = _session.RunTo(Horizon, _chunkEvents);
                Chunks++;
                if (result.Status != RunStatus.Paused)
                {
                    Done = true;
                    Ended = result.Status;
                }
            }
        }
    }
}
