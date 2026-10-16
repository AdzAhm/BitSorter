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
            return new RunSession(new WorkbenchModel(session, layout, horizon, slicer.Done), slicer);
        }

        public WorkbenchModel Model { get; }

        public RunSlicer Slicer { get; }

        /// <summary>Whether the run is made and the model has read all of it.</summary>
        public bool Done => Model.Complete;

        /// <summary>
        /// One frame's share of making the run: a slice, then the model reads what it recorded. True
        /// when the model has anything new to show; false once the run is made.
        /// </summary>
        public bool Frame()
        {
            if (Model.Complete)
                return false;

            Slicer.Slice();
            return Model.Grow(Slicer.Done);
        }

        /// <summary>Makes the rest of the run now: what a capture or a test that needs all of it does.</summary>
        public void Finish()
        {
            if (Model.Complete)
                return;

            Slicer.Finish();
            Model.Grow(true);
        }
    }
}
