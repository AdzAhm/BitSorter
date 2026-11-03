using System;
using System.Runtime.CompilerServices;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// A run that records everything, as the workbench runs one, allocates what its history keeps and not a byte besides (the
    /// zero-GC pass, Part 16): each delta's work is the engine's own, which <see cref="GuardrailTests"/> holds to nothing once
    /// warm, and what recording adds is the history's buffers growing to hold the changes and the transitions.
    /// </summary>
    /// <remarks>
    /// The account is exact. A buffer doubles when it is full (<c>History</c>'s <c>Buffer</c>: a signal's changes from 4
    /// places, the transitions from 64), so its growth over a stretch of the run is the arrays it made, each a header and
    /// twice the places it had, at the struct's own size. A change to how the buffers grow changes these sums too. Measured
    /// over stretches where nothing is printed: a message is text the run keeps as well, and is left out of the account.
    /// </remarks>
    public class RecordingAllocationTests
    {
        private const int FirstChangePlaces = 4, FirstTransitionPlaces = 64;

        /// <summary>A 64-bit array's header: its object header, method table and length.</summary>
        private const int ArrayHeader = 24;

        [Test]
        public void ARecordingRun_AllocatesItsHistoryAndNothingElse()
        {
            string root = Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus");
            var sources = new SystemVerilog.Syntax.MemorySourceFiles();
            foreach (string path in System.IO.Directory.GetFiles(root, "*.sv*", System.IO.SearchOption.AllDirectories))
                sources.Add(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), System.IO.File.ReadAllText(path));
            var files = new MemorySimFiles();
            foreach (string path in System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "processor"), "*.hex"))
                files.Add(System.IO.Path.GetFileName(path), System.IO.File.ReadAllText(path));

            // The multicycle processor's demonstration, every change and transition recorded: it prints only at its end.
            Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.Read("processor/mcpu.sv", sources), "mcpu_demo_tb").Design;
            var sim = new Simulator(design, null, new SimOptions { Files = files, RecordChanges = true, RecordTransitions = true });
            sim.RunTo(100);

            long recorded = 0;
            for (long from = 100; from < 1300; from += 300)
            {
                long[] changes = Changes(design, sim);
                int transitions = sim.History.TransitionCount, messages = sim.History.Messages.Count;
                long bytes = Allocated(sim, from + 300);
                Assert.AreEqual(from + 300, sim.Now, "sanity: the run goes on past the stretch");
                Assert.AreEqual(messages, sim.History.Messages.Count, "sanity: a stretch the run prints nothing in");

                long grown = Grown(transitions, sim.History.TransitionCount, FirstTransitionPlaces, Unsafe.SizeOf<Transition>());
                long[] now = Changes(design, sim);
                for (int i = 0; i < now.Length; i++)
                {
                    grown += Grown(changes[i], now[i], FirstChangePlaces, Unsafe.SizeOf<ValueChange>());
                    recorded += now[i] - changes[i];
                }
                Assert.AreEqual(grown, bytes, $"{from} to {from + 300} ns: what the run allocated beyond its history's growth");
            }

            Assert.Greater(recorded, 5000, "sanity: the stretches recorded thousands of changes");
        }

        /// <summary>
        /// What running on to <paramref name="time"/> allocates, counted in a method of its own, never inlined, and asking
        /// nothing else: with the run's result kept and asserted on in here, 24 bytes more were counted each time.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Allocated(Simulator sim, long time)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            sim.RunTo(time);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static long[] Changes(Design design, Simulator sim)
        {
            var counts = new long[design.Signals.Count];
            for (int i = 0; i < counts.Length; i++)
                counts[i] = sim.History.ChangeCount(design.SignalAt(i));
            return counts;
        }

        /// <summary>
        /// What a buffer of <paramref name="first"/> places, doubling when full, allocates growing from <paramref name="from"/>
        /// items to <paramref name="to"/>: an array of twice the places each time it was full, rounded to whole words.
        /// </summary>
        private static long Grown(long from, long to, int first, int size)
        {
            long bytes = 0;
            for (long places = first; places < to; places *= 2)
                if (places >= from)
                    bytes += ArrayHeader + (2 * places * size + 7) / 8 * 8;
            return bytes;
        }
    }
}
