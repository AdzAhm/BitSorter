using System;
using BitSorter.Board;
using BitSorter.Sim;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A document compiled and run a slice a frame, with the model that shows it following the run as
    /// it is made.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkbenchModel.Of"/> makes the whole run before anything is shown, which is right for
    /// a test and wrong for the course's processor, whose run takes seconds. This makes it a slice at a
    /// time instead (<see cref="RunSlicer"/>), and the model shows each moment as soon as it has run.
    ///
    /// **The first slice runs inside <see cref="Open"/>**, so a small document is made before the
    /// first frame is drawn -- the reference circuit is, and opens exactly as it did when it was made
    /// in one go -- and the model never reads a history whose first step has not run.
    ///
    /// **A <c>$stop</c> suspends it** (Part 9b-3), and <see cref="RunOn"/> carries it on, the timeline
    /// growing with it when it runs past its end. The timeline is made only once the run has reached its
    /// end: a run suspended short of it is not, and a moment past where it was suspended waits for it to
    /// be run on.
    /// </remarks>
    public sealed class RunSession
    {
        private RunSession(WorkbenchModel model, RunSlicer slicer)
        {
            Model = model;
            Slicer = slicer;
        }

        /// <summary>
        /// Compiles <paramref name="project"/> and makes the first slice of its run, timed by
        /// <paramref name="nowMilliseconds"/>. Throws when the document does not compile.
        /// </summary>
        public static RunSession Open(Project project, Func<double> nowMilliseconds,
            int chunkEvents = RunSlicer.ChunkEvents, double budgetMilliseconds = RunSlicer.BudgetMilliseconds)
        {
            (Design design, SchematicLayout layout, long horizon) = WorkbenchModel.Prepare(project);
            var session = new SimSession(design, null);
            var slicer = new RunSlicer(session, horizon, nowMilliseconds, chunkEvents, budgetMilliseconds);

            slicer.Slice();
            return new RunSession(new WorkbenchModel(session, layout, horizon, Made(slicer, horizon)), slicer);
        }

        /// <summary>Whether the slicer has made the whole of a timeline to <paramref name="end"/>: done, and not suspended short of it.</summary>
        private static bool Made(RunSlicer slicer, long end) =>
            slicer.Done && (slicer.Ended != RunStatus.Suspended || slicer.Reached >= end);

        /// <summary>What shows the run: made again round a new drawing by <see cref="Relayout"/>, the run going on underneath.</summary>
        public WorkbenchModel Model { get; private set; }

        /// <summary>
        /// The run shown round another drawing (Part 10a): an edit that moved a part, or one whose replay is still
        /// being made, is drawn at once over the run there is. The simulation is the same one, read again from
        /// the start of its history, and the moment and the speed stay. A net the drawing has and the run does not
        /// -- a gate just placed -- shows no value until the replay takes over.
        /// </summary>
        public void Relayout(SchematicLayout layout)
        {
            WorkbenchModel old = Model;
            var model = new WorkbenchModel(old.Session, layout, old.End, Made(Slicer, old.End)) { UnitsPerSecond = old.UnitsPerSecond };
            model.Seek(old.Time);
            Model = model;
        }

        public RunSlicer Slicer { get; }

        /// <summary>Whether the run is made and the model has read all of it.</summary>
        public bool Done => Model.Complete;

        /// <summary>Whether a <c>$stop</c> has suspended the run where it has got to: <see cref="RunOn"/> carries it on.</summary>
        public bool Suspended => Slicer.Done && Slicer.Ended == RunStatus.Suspended;

        /// <summary>How the run ended, once it has -- for now, if a <c>$stop</c> suspended it; null while it is being made.</summary>
        public RunStatus? Ended => Slicer.Ended;

        /// <summary>
        /// Carries the run on to <paramref name="horizon"/> or its own end, whichever is later, the timeline
        /// growing to it: after a <c>$stop</c>, or once the run reached its end. False when it cannot go on.
        /// </summary>
        public bool RunOn(long horizon)
        {
            if (!Slicer.RunOn(horizon))
                return false;

            if (Slicer.Horizon > Model.End)
                Model.Extend(Slicer.Horizon);
            return true;
        }

        /// <summary>
        /// One frame's share of making the run: a slice, then the model reads what it recorded. True
        /// when the model has anything new to show; false once the run is made.
        /// </summary>
        public bool Frame()
        {
            if (Model.Complete)
                return false;

            Slicer.Slice();
            return Model.Grow(Made(Slicer, Model.End));
        }

        /// <summary>Makes the rest of the run now: what a capture or a test that needs all of it does.</summary>
        public void Finish()
        {
            if (Model.Complete)
                return;

            Slicer.Finish();
            Model.Grow(Made(Slicer, Model.End));
        }
    }
}
