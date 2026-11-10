using System;
using BitSorter.SystemVerilog;
using NUnit.Framework;
using BitSorter.Ci;

namespace BitSorter.Sim.Tests
{
    /// <summary>
    /// <see cref="RtlEvaluator"/> evaluating into an array of the caller's (the zero-GC pass, Part 16), as the workbench does
    /// once a step while a drawing of code plays: every net reads what a new array of its own would, whatever the array
    /// held before, and once the netlist has been evaluated once, nothing is allocated for it.
    /// </summary>
    public class RtlEvaluatorReuseTests
    {
        /// <summary>The corpus's pipelined processor, elaborated from its testbench and run part of the way.</summary>
        private static (Design Design, Simulator Sim) Pipelined()
        {
            string root = Ci.RepoRoot.Combine("Tools/ci/Oracle/corpus");
            var sources = new SystemVerilog.Syntax.MemorySourceFiles();
            foreach (string path in System.IO.Directory.GetFiles(root, "*.sv*", System.IO.SearchOption.AllDirectories))
                sources.Add(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), System.IO.File.ReadAllText(path));
            var files = new MemorySimFiles();
            foreach (string path in System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "processor"), "*.hex"))
                files.Add(System.IO.Path.GetFileName(path), System.IO.File.ReadAllText(path));

            Design design = Elaborator.Elaborate(SystemVerilog.Syntax.SyntaxReader.Read("processor/pcpu.sv", sources), "pcpu_tb").Design;
            return (design, new Simulator(design, null, new SimOptions { Files = files }));
        }

        private static LogicVec[] Values(Design design, Simulator sim)
        {
            var values = new LogicVec[design.Signals.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = sim.Value(design.SignalAt(i));
            return values;
        }

        [Test]
        public void EvaluatedIntoAUsedArray_EveryNetReadsAsInANewOne()
        {
            (Design design, Simulator sim) = Pipelined();
            int netlists = 0, nets = 0;
            foreach (ScopeInfo scope in design.Scopes)
            {
                if (scope.Kind != ScopeKind.Instance)
                    continue;
                RtlNetlist netlist = RtlExtractor.Of(design, scope.Path);
                if (netlist == null)
                    continue;
                netlists++;

                // A place for every net and one more, every one of them holding something no net here would.
                var reused = new LogicVec[netlist.Nets.Count + 1];
                for (int i = 0; i < reused.Length; i++)
                    reused[i] = LogicVec.FromUInt64(0x5A5A, 16);

                for (long time = 100; time <= 700; time += 150)
                {
                    sim.RunTo(time);
                    LogicVec[] signals = Values(design, sim);
                    LogicVec[] fresh = RtlEvaluator.Evaluate(netlist, signals);
                    RtlEvaluator.Evaluate(netlist, signals, reused);
                    for (int i = 0; i < fresh.Length; i++)
                    {
                        Assert.AreEqual(fresh[i].IsEmpty, reused[i].IsEmpty, $"{scope.Path}.{netlist.Nets[i].Name} at {time}");
                        if (!fresh[i].IsEmpty)
                            Assert.AreEqual(fresh[i].Format(Radix.Binary), reused[i].Format(Radix.Binary), $"{scope.Path}.{netlist.Nets[i].Name} at {time}");
                    }
                    nets += fresh.Length;
                }
                Assert.AreEqual(LogicVec.FromUInt64(0x5A5A, 16), reused[netlist.Nets.Count], "the place past the nets is left as it was");
            }

            Assert.Greater(netlists, 10, "sanity: every module of the processor");
            Assert.Greater(nets, 1000, "sanity: and their nets, at five moments");
        }

        [Test]
        public void EvaluatedIntoAnArray_OnceEvaluatedBefore_AllocatesNothing()
        {
            (Design design, Simulator sim) = Pipelined();
            sim.RunTo(400);
            LogicVec[] signals = Values(design, sim);
            foreach (ScopeInfo scope in design.Scopes)
            {
                if (scope.Kind != ScopeKind.Instance)
                    continue;
                RtlNetlist netlist = RtlExtractor.Of(design, scope.Path);
                if (netlist == null)
                    continue;
                var values = new LogicVec[netlist.Nets.Count];
                BytesOf(netlist, signals, values, 10);   // the first, which works out its plan, and the count itself warmed
                Assert.AreEqual(0, BytesOf(netlist, signals, values, 100), scope.Path);
            }
        }

        /// <summary>
        /// What <paramref name="times"/> evaluations allocate, counted in a method of their own and never inlined, so the count is
        /// only theirs: inlined, an optimising JIT may move the caller's own allocations into it -- the 0 an assertion boxes.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long BytesOf(RtlNetlist netlist, LogicVec[] signals, LogicVec[] values, int times)
        {
            return Allocations.Least(() =>
            {
                for (int k = 0; k < times; k++)
                    RtlEvaluator.Evaluate(netlist, signals, values);
            });
        }

        /// <summary>
        /// A word a memory is read at by its address, asked of a reader of the caller's rather than read off the signals -- every
        /// word of them x, so a word read there would show -- reads what it does off the signals, and every net with it: what the
        /// workbench gives a drawing of code, which follows no word of a memory (the 16d sweep, waves-14).
        /// </summary>
        [Test]
        public void AWordAskedOfAReader_ReadsAsOffTheSignals()
        {
            (Design design, Simulator sim) = Pipelined();
            sim.RunTo(400);
            LogicVec[] signals = Values(design, sim);
            var noWords = (LogicVec[])signals.Clone();
            foreach (MemoryInfo memory in design.Memories)
                for (int at = 0; at < memory.Depth; at++)
                    noWords[memory.First.Index + at] = LogicVec.AllX(memory.Width);
            int asked = 0;
            LogicVec Word(MemoryInfo memory, int at)
            {
                asked++;
                return signals[memory.First.Index + at];
            }

            int netlists = 0;
            foreach (ScopeInfo scope in design.Scopes)
            {
                if (scope.Kind != ScopeKind.Instance)
                    continue;
                RtlNetlist netlist = RtlExtractor.Of(design, scope.Path);
                if (netlist == null || !System.Linq.Enumerable.Any(netlist.Cells, c => c.Kind == RtlKind.Word))
                    continue;
                netlists++;
                LogicVec[] read = RtlEvaluator.Evaluate(netlist, signals);
                var asking = new LogicVec[netlist.Nets.Count];
                RtlEvaluator.Evaluate(netlist, noWords, asking, Word);
                for (int i = 0; i < read.Length; i++)
                    Assert.AreEqual(read[i].IsEmpty ? "" : read[i].Format(Radix.Binary), asking[i].IsEmpty ? "" : asking[i].Format(Radix.Binary),
                        $"{scope.Path}.{netlist.Nets[i].Name}");
            }

            Assert.Greater(netlists, 0, "sanity: a module of the processor reads a word of a memory by its address");
            Assert.Greater(asked, 0, "sanity: and its words were asked for");
        }

        [Test]
        public void AnArrayWithoutAPlaceForEveryNet_IsRefused()
        {
            (Design design, _) = Pipelined();
            RtlNetlist netlist = RtlExtractor.Of(design, "pcpu_tb.dut");
            Assert.IsNotNull(netlist, "sanity: the processor's own netlist");
            Assert.Throws<ArgumentException>(() => RtlEvaluator.Evaluate(netlist, new LogicVec[design.Signals.Count], new LogicVec[netlist.Nets.Count - 1]));
            Assert.Throws<ArgumentNullException>(() => RtlEvaluator.Evaluate(netlist, new LogicVec[design.Signals.Count], null));
        }
    }
}
