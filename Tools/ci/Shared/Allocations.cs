using System;

namespace BitSorter.Ci
{
    /// <summary>What a piece of work allocates on the calling thread, measured so the runtime's own work does not count.</summary>
    /// <remarks>
    /// A test that holds a frame, a lookup or a run to allocating nothing measures it with
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/>. That counts everything the thread allocates between two reads,
    /// the runtime's own one-time work included -- the JIT compiling a path the first time it is taken, or a loop switched
    /// to its optimised code part way round -- and under load that work can land inside the one measure: 1,632 bytes in the
    /// waves' span cutter and 4,360 in the drawing's evaluator, on 2026-11-09, each in CI or a loaded local run and never
    /// alone. So the work is done once to warm it, then measured three times, and the least is what it allocates: the
    /// runtime's one-time work lands in one measure at most, and work that allocated would show in every one.
    ///
    /// Shared by the CI test projects as a linked file, as <see cref="RepoRoot"/> is.
    /// </remarks>
    public static class Allocations
    {
        /// <summary>How many times <see cref="Least"/> measures.</summary>
        public const int Measures = 3;

        /// <summary>
        /// The least <paramref name="work"/> allocates in one of <see cref="Measures"/> measures, after one call to warm it.
        /// The delegate is made by the caller before any measure, so it is not counted.
        /// </summary>
        public static long Least(Action work)
        {
            if (work == null)
                throw new ArgumentNullException(nameof(work));

            work();
            long least = long.MaxValue;
            for (int measure = 0; measure < Measures; measure++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                work();
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return least;
        }
    }
}
