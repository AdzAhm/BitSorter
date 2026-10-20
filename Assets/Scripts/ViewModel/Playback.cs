using System;
using System.Collections.Generic;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>Why playing the run stopped by itself; <see cref="None"/> while it plays, or once it is paused by hand.</summary>
    public enum PauseReason
    {
        None,

        /// <summary>A <c>$stop</c>: the run was suspended there, and PLAY runs it on.</summary>
        Suspended,

        /// <summary>A breakpoint fired (<see cref="Playback.ReasonBreak"/> says which).</summary>
        Breakpoint,

        /// <summary>The playhead reached the RUN TO moment.</summary>
        RunTo,

        /// <summary><c>$finish</c> ended the run.</summary>
        Finished,

        /// <summary>The run could not go on; its messages say why.</summary>
        Stopped,

        /// <summary>The end of the timeline, with nothing to say it ended otherwise.</summary>
        End,
    }

    /// <summary>
    /// Playing a run (Part 9b-3): the playhead moving through it at a speed, pausing by itself at a
    /// <c>$stop</c>, a breakpoint, the RUN TO moment and the end, stepping from edge to edge, and running
    /// the run on past a <c>$stop</c> as ModelSim's RUN does.
    /// </summary>
    /// <remarks>
    /// **The run is made ahead of the playhead** (<see cref="RunSession"/>), so playing is reading:
    /// where it pauses next is a question about what is recorded -- the history's suspensions, the
    /// breakpoints' first firing (<see cref="Breakpoints.Next"/>) -- never something checked as the
    /// engine runs. Each is looked for strictly after the playhead, so PLAY from where one paused goes
    /// on past it.
    ///
    /// **PLAY at a <c>$stop</c> runs the run on** (Ahmad's choice, 2026-10-16). Suspended short of the
    /// timeline's end it runs on to the end; at the end -- where a board's own testbench calls
    /// <c>$stop</c> -- it runs on as long again as the first run, or to the RUN TO moment if that is
    /// later, and the timeline grows with it. PLAY at an end the design gave with <c>$finish</c>, or
    /// one the run had to stop at, plays it again from the start, as PLAY at the end did before.
    ///
    /// **Speed is a ladder that doubles**, from a tenth of a unit a second to over a thousand, and then
    /// INSTANT, which goes straight to wherever it would pause. The model is told the speed, so a gate's
    /// flash and a fade last the same seconds at any of them (<see cref="WorkbenchModel.UnitsPerSecond"/>);
    /// INSTANT, which shows no motion at all, flashes as the reference's speed does.
    ///
    /// Nothing here allocates as it plays: the next breakpoint is kept until the playhead passes it or
    /// the breakpoints or the run change, and the suspensions are a binary search.
    /// </remarks>
    public sealed class Playback
    {
        /// <summary>Every speed but INSTANT, in time units a second.</summary>
        public static readonly IReadOnlyList<double> Speeds = Array.AsReadOnly(new[]
        {
            0.1, 0.2, 0.4, 0.8, 1.6, 3.2, 6.4, 12.8, 25.6, 51.2, 102.4, 204.8, 409.6, 819.2, 1638.4,
        });

        /// <summary>The reference's speed, <see cref="SchematicStyle.NanosecondsPerSecond"/>.</summary>
        public const int DefaultSpeed = 4;

        /// <summary>INSTANT's place on the ladder: one past the fastest speed.</summary>
        public static int InstantSpeed => Speeds.Count;

        private const double Epsilon = 1e-9;

        private RunSession _run;
        private Func<int, SignalTrack> _trackOf;

        /// <summary>How long the first run was: how far PLAY at the end runs it on.</summary>
        private readonly long _runLength;

        private BreakHit? _break;
        private double _breakFrom = double.NaN;
        private int _breakVersion = -1, _breakRunVersion = -1;

        /// <summary>Playing <paramref name="run"/>, paused at its start, at the speed <paramref name="speed"/> on the ladder.</summary>
        public Playback(RunSession run, int speed = DefaultSpeed)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _trackOf = run.Model.TrackOf;
            _runLength = Math.Max(1, run.Model.End);
            SetSpeed(speed);
        }

        public WorkbenchModel Model => _run.Model;

        public RunSession Run => _run;

        public Breakpoints Breakpoints { get; } = new Breakpoints();

        public bool Playing { get; private set; }

        /// <summary>The speed's place on the ladder: <see cref="Speeds"/>, then <see cref="InstantSpeed"/>.</summary>
        public int Speed { get; private set; }

        public bool Instant => Speed == InstantSpeed;

        /// <summary>Time units a second; infinite at INSTANT.</summary>
        public double UnitsPerSecond => Instant ? double.PositiveInfinity : Speeds[Speed];

        /// <summary>
        /// Where playing pauses by itself on the way, besides a <c>$stop</c>, a breakpoint and the end;
        /// null for nowhere. PLAY from it goes on past it.
        /// </summary>
        public long? RunTo
        {
            get => _runTo;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Time starts at 0.");
                _runTo = value;
            }
        }

        private long? _runTo;

        /// <summary>Why playing last stopped by itself; <see cref="PauseReason.None"/> while it plays or once paused by hand.</summary>
        public PauseReason Reason { get; private set; }

        /// <summary>Where it stopped, for <see cref="Reason"/>.</summary>
        public long ReasonTime { get; private set; }

        /// <summary>Which breakpoint fired, by its place in <see cref="Breakpoints"/>, for <see cref="PauseReason.Breakpoint"/>; else -1.</summary>
        public int ReasonBreak { get; private set; } = -1;

        /// <summary>How far the run is made: the end of the timeline, or where the run has got.</summary>
        public double Made => Model.Complete ? Model.End : Math.Min(Model.End, Model.Reached);

        /// <summary>One step faster on the ladder, up to INSTANT. False at INSTANT.</summary>
        public bool Faster()
        {
            if (Speed >= InstantSpeed)
                return false;
            SetSpeed(Speed + 1);
            return true;
        }

        /// <summary>One step slower. False at the slowest.</summary>
        public bool Slower()
        {
            if (Speed <= 0)
                return false;
            SetSpeed(Speed - 1);
            return true;
        }

        /// <summary>A speed on the ladder, by its place.</summary>
        public void SetSpeed(int speed)
        {
            if (speed < 0 || speed > InstantSpeed)
                throw new ArgumentOutOfRangeException(nameof(speed), speed, "A speed is a place on the ladder, INSTANT the last.");
            Speed = speed;
            Model.UnitsPerSecond = Instant ? SchematicStyle.NanosecondsPerSecond : Speeds[speed];
        }

        /// <summary>
        /// Plays from where the playhead is: the run run on first, at a <c>$stop</c> or an end it reached;
        /// from the start, at an end the design gave or the run had to stop at.
        /// </summary>
        public void Play()
        {
            ClearReason();

            if (Model.Time >= Made - Epsilon)
            {
                if (_run.Suspended || (Model.Complete && _run.Ended == RunStatus.ReachedTime))
                    _run.RunOn(NextHorizon());
                else if (Model.Complete)
                    Model.Seek(0.0);
            }

            Playing = true;
        }

        /// <summary>Stops playing where it is.</summary>
        public void Pause()
        {
            Playing = false;
            ClearReason();
        }

        /// <summary>Plays, or pauses.</summary>
        public void Toggle()
        {
            if (Playing)
                Pause();
            else
                Play();
        }

        /// <summary>
        /// Plays <paramref name="run"/> from here on: the same document, edited and run again
        /// (<see cref="WorkbenchSession"/>). The speed, the breakpoints, RUN TO and whether it is playing
        /// carry over, and the playhead stays where it was, as far as the new run is made. Why it last
        /// paused does not: it paused in a run that is gone. How far PLAY at the end runs it on stays the
        /// first run's length, which an edit to an input does not change.
        /// </summary>
        public void Switch(RunSession run)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (run == _run)
                return;

            double at = Model.Time;
            _run = run;
            _trackOf = run.Model.TrackOf;
            _break = null;
            _breakFrom = double.NaN;
            _breakVersion = -1;
            _breakRunVersion = -1;

            SetSpeed(Speed);
            ClearReason();
            Model.Seek(Math.Min(at, Made));
        }

        /// <summary>
        /// The same run, shown round another drawing (<see cref="RunSession.Relayout"/>, Part 10a): what was read
        /// off the model it had is read off the new one, and nothing else changes -- not the moment, the speed,
        /// whether it is playing or why it last paused.
        /// </summary>
        public void Refresh() => _trackOf = Model.TrackOf;

        /// <summary>Back to the start, playing.</summary>
        public void Restart()
        {
            ClearReason();
            Model.Seek(0.0);
            Playing = true;
        }

        /// <summary>
        /// A frame of <paramref name="seconds"/> while playing: the playhead moved on at the speed, and
        /// paused where it must. True when it moved or stopped.
        /// </summary>
        public bool Frame(double seconds)
        {
            if (!Playing)
                return false;

            double from = Model.Time;
            double made = Made;
            double target = Instant ? double.PositiveInfinity : from + Math.Max(0.0, seconds) * UnitsPerSecond;

            // Where it pauses by itself next, strictly after where it is: a $stop first, then a
            // breakpoint, then the RUN TO moment, when two fall on one moment.
            double stop = double.PositiveInfinity;
            PauseReason why = PauseReason.None;
            int which = -1;

            long? suspension = NextSuspension(from);
            if (suspension.HasValue)
            {
                stop = suspension.Value;
                why = PauseReason.Suspended;
            }

            BreakHit? hit = NextBreak(from);
            if (hit.HasValue && hit.Value.Time < stop)
            {
                stop = hit.Value.Time;
                why = PauseReason.Breakpoint;
                which = hit.Value.Index;
            }

            if (_runTo.HasValue && _runTo.Value > from + Epsilon && _runTo.Value < stop)
            {
                stop = _runTo.Value;
                why = PauseReason.RunTo;
                which = -1;
            }

            if (stop <= made + Epsilon && target >= stop - Epsilon)
            {
                Model.Seek(stop);
                StopFor(why, (long)Math.Round(stop), which);
                return true;
            }

            if (target >= made)
            {
                Model.Seek(made);
                if (Model.Complete && made >= Model.End)
                    StopFor(EndReason(), Model.End, -1);
                return made != from || !Playing;
            }

            Model.Seek(target);
            return true;
        }

        /// <summary>To the next edge after the playhead on any of <paramref name="rows"/>, paused. False when there is none yet.</summary>
        public bool Step(IReadOnlyList<int> rows)
        {
            Pause();
            long? edge = Model.NextEdgeAfter(rows, Model.Time);
            if (!edge.HasValue || edge.Value > Made + Epsilon)
                return false;
            Model.Seek(edge.Value);
            return true;
        }

        /// <summary>To the last edge before the playhead on any of <paramref name="rows"/>, paused. False when there is none.</summary>
        public bool StepBack(IReadOnlyList<int> rows)
        {
            Pause();
            long? edge = Model.LastEdgeBefore(rows, Model.Time);
            if (!edge.HasValue)
                return false;
            Model.Seek(edge.Value);
            return true;
        }

        /// <summary>Where a run on goes: the RUN TO moment if it is further, else the timeline's end, or as long again as the first run past it.</summary>
        private long NextHorizon()
        {
            long reached = Model.Reached;
            long horizon = reached < Model.End ? Model.End : reached + _runLength;
            if (_runTo.HasValue && _runTo.Value > horizon)
                horizon = _runTo.Value;
            return horizon;
        }

        private PauseReason EndReason()
        {
            switch (_run.Ended)
            {
                case RunStatus.Finished: return PauseReason.Finished;
                case RunStatus.Stopped: return PauseReason.Stopped;
                case RunStatus.Suspended: return PauseReason.Suspended;
                default: return PauseReason.End;
            }
        }

        private void StopFor(PauseReason why, long at, int which)
        {
            Playing = false;
            Reason = why;
            ReasonTime = at;
            ReasonBreak = which;
        }

        private void ClearReason()
        {
            Reason = PauseReason.None;
            ReasonTime = 0;
            ReasonBreak = -1;
        }

        /// <summary>The first suspension strictly after <paramref name="from"/>, or null.</summary>
        private long? NextSuspension(double from)
        {
            IReadOnlyList<long> suspensions = Model.Session.History.Suspensions;
            int low = 0, high = suspensions.Count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (suspensions[mid] > from + Epsilon) high = mid;
                else low = mid + 1;
            }

            return low < suspensions.Count ? suspensions[low] : (long?)null;
        }

        /// <summary>The next breakpoint to fire strictly after <paramref name="from"/>, kept until it is passed or anything it was read from changes.</summary>
        private BreakHit? NextBreak(double from)
        {
            if (Breakpoints.Count == 0)
                return null;

            bool kept = _breakVersion == Breakpoints.Version && _breakRunVersion == Model.RunVersion
                && from >= _breakFrom && (_break == null || from < _break.Value.Time - Epsilon);
            if (!kept)
            {
                _break = Breakpoints.Next(_trackOf, from);
                _breakFrom = from;
                _breakVersion = Breakpoints.Version;
                _breakRunVersion = Model.RunVersion;
            }

            return _break;
        }
    }
}
