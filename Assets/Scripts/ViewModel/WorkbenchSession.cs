using System;
using BitSorter.Board;
using BitSorter.SystemVerilog;

namespace BitSorter.ViewModel
{
    /// <summary>What a frame of a <see cref="WorkbenchSession"/> did.</summary>
    public enum SessionFrame
    {
        /// <summary>Nothing new to show.</summary>
        Nothing,

        /// <summary>The run shown has more to read: the same run, further on.</summary>
        Grew,

        /// <summary>
        /// A replay took over: <see cref="WorkbenchSession.Run"/> is a new run, of the document as edited,
        /// and what shows the run is built again round it.
        /// </summary>
        Switched,
    }

    /// <summary>
    /// The document the workbench shows, kept for editing and undo, its run, and the run of it as edited
    /// while that is being made (Part 9b-3).
    /// </summary>
    /// <remarks>
    /// **An edit is replayed beside the run it replaces.** The run shown is the document's before the
    /// edit; the edited document is compiled and run a slice a frame (<see cref="RunSession"/>), as the
    /// first run was, and takes over only once it has reached the moment shown -- run on as far as the
    /// shown run had been, past a <c>$stop</c> -- so the waveform never goes blank and the playhead
    /// never jumps. A replay is the whole run again rather than a run picked up from the edit, since the
    /// testbench is one <c>initial</c> block that drives every input (a replay means compiling again);
    /// an input changes nothing before it changes, so what comes before the edit is the same history,
    /// and <c>WorkbenchSessionTests</c> holds every replay to that.
    ///
    /// **The latest edit wins**: one made while a replay is still being made drops that replay and
    /// starts its own, of the document with both edits in. Undo and redo are edits like any other. A
    /// change of unit is one too: it rescales nothing, but every time on screen and every delay drawn is
    /// written in it, so the run that writes them is made again. **RESTART takes a replay at once**
    /// (<see cref="TakeReplay"/>): the moment it goes to, 0, is made already.
    /// </remarks>
    public sealed class WorkbenchSession
    {
        private const double Epsilon = 1e-9;

        private readonly Func<double> _now;
        private readonly int _chunkEvents;
        private readonly double _budgetMilliseconds;

        /// <summary>Whether the replay has been asked to run on as far as the shown run's timeline.</summary>
        private bool _replayRanOn;

        /// <summary>
        /// <paramref name="project"/>, compiled and its first slice run, each slice timed by
        /// <paramref name="nowMilliseconds"/>. Throws when the document does not compile.
        /// </summary>
        public WorkbenchSession(Project project, Func<double> nowMilliseconds,
            int chunkEvents = RunSlicer.ChunkEvents, double budgetMilliseconds = RunSlicer.BudgetMilliseconds)
        {
            _now = nowMilliseconds ?? throw new ArgumentNullException(nameof(nowMilliseconds));
            _chunkEvents = chunkEvents;
            _budgetMilliseconds = budgetMilliseconds;
            Editor = new BoardEditor(project);
            Run = RunSession.Open(project, _now, _chunkEvents, _budgetMilliseconds);
        }

        /// <summary>The document, and its undo; every change goes through it, and through this session's own edits for a replay.</summary>
        public BoardEditor Editor { get; }

        public Project Project => Editor.Project;

        /// <summary>The unit the document counts its time in.</summary>
        public string Unit => Project.Settings.TimeUnit;

        /// <summary>The run shown: the document's, or, while <see cref="Replay"/> is being made, the document's before its latest edit.</summary>
        public RunSession Run { get; private set; }

        /// <summary>The run of the document as edited, being made, which takes over <see cref="Run"/> once it has reached the moment shown; null when there is none.</summary>
        public RunSession Replay { get; private set; }

        /// <summary>Whether a replay is being made.</summary>
        public bool Replaying => Replay != null;

        /// <summary>The moment the latest edit changes the run from: before it, the replay's history is the shown run's.</summary>
        public long ChangedFrom { get; private set; }

        /// <summary>How many replays have taken over: what is built round a run checks this to know when to build again.</summary>
        public int Version { get; private set; }

        // -----------------------------------------------------------------
        // Edits, each replayed
        // -----------------------------------------------------------------

        /// <summary>An input given <paramref name="value"/> from <paramref name="from"/> until <paramref name="until"/> (<see cref="BoardEditor.SetInput"/>), and replayed.</summary>
        public EditResult SetInput(string port, long from, long until, LogicVec value) =>
            Replayed(Editor.SetInput(port, from, until, value), from);

        /// <summary>A one-bit input flipped over the stretch <paramref name="at"/> is in (<see cref="BoardEditor.InvertInput(string, long, out long)"/>), and replayed.</summary>
        public EditResult InvertInput(string port, long at)
        {
            EditResult result = Editor.InvertInput(port, at, out long from);
            return Replayed(result, from);
        }

        /// <summary>The document counted in <paramref name="unit"/> (<see cref="BoardEditor.SetSettings"/>), and replayed: nothing is rescaled, everything is relabelled.</summary>
        public EditResult SetUnit(string unit) =>
            unit == Unit ? EditResult.Ok() : Replayed(Editor.SetSettings(Project.Settings with { TimeUnit = unit }), 0);

        /// <summary>The last edit taken back, and replayed. False when there is none.</summary>
        public bool Undo()
        {
            if (!Editor.Undo())
                return false;
            StartReplay(0);
            return true;
        }

        /// <summary>The last edit taken back, made again, and replayed. False when there is none.</summary>
        public bool Redo()
        {
            if (!Editor.Redo())
                return false;
            StartReplay(0);
            return true;
        }

        private EditResult Replayed(EditResult result, long from)
        {
            if (result.Done)
                StartReplay(from);
            return result;
        }

        private void StartReplay(long from)
        {
            ChangedFrom = Math.Max(0, from);
            Replay = RunSession.Open(Project, _now, _chunkEvents, _budgetMilliseconds);
            _replayRanOn = false;
        }

        // -----------------------------------------------------------------
        // Frames
        // -----------------------------------------------------------------

        /// <summary>
        /// One frame's share of the work with the moment <paramref name="shown"/> on screen: a slice of
        /// the replay, which takes over once it has reached that moment, or, with none, a slice of the
        /// run shown.
        /// </summary>
        public SessionFrame Frame(double shown)
        {
            if (Replay == null)
                return Run.Frame() ? SessionFrame.Grew : SessionFrame.Nothing;

            Replay.Frame();

            // The shown run may have been run on past its end at a $stop; the replay goes as far, once.
            if (!_replayRanOn && Replay.Done && Replay.Model.End < Run.Model.End)
            {
                _replayRanOn = true;
                Replay.RunOn(Run.Model.End);
            }

            double target = Math.Min(Math.Max(0.0, shown), Run.Model.End);
            bool caughtUp = Made(Replay) >= target - Epsilon && (Replay.Model.End >= Run.Model.End || Replay.Done);
            bool cannotGoOn = Replay.Done && (_replayRanOn || Replay.Model.End >= Run.Model.End);

            if (caughtUp || cannotGoOn)
            {
                TakeOver();
                return SessionFrame.Switched;
            }

            return SessionFrame.Nothing;
        }

        /// <summary>The replay takes over now, made as far as it is: RESTART's, since 0 is made already. False when there is none.</summary>
        public bool TakeReplay()
        {
            if (Replay == null)
                return false;
            TakeOver();
            return true;
        }

        /// <summary>Makes the whole of what is being made, and lets a replay take over: what a capture or a test that needs all of it does.</summary>
        public void Finish()
        {
            if (Replay != null)
            {
                Replay.Finish();
                if (Replay.Model.End < Run.Model.End && Replay.RunOn(Run.Model.End))
                    Replay.Finish();
                TakeOver();
            }

            Run.Finish();
        }

        private void TakeOver()
        {
            Run = Replay;
            Replay = null;
            Version++;
        }

        /// <summary>How far a run is made: what it can show.</summary>
        private static double Made(RunSession run) =>
            run.Model.Complete ? run.Model.End : Math.Min(run.Model.End, run.Model.Reached);
    }
}
