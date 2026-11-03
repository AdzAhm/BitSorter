using System;
using System.Collections.Generic;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// What an import of a processor makes (Part 16b). A browser frees memory only between frames, and a sample is imported in
    /// the frame that opens it: the pipelined processor's import made 484 MB, 460 of it trying to draw each of its modules --
    /// every one of which comes in written -- with every module written so far in the project, all checked again on every
    /// edit of each attempt.
    /// </summary>
    public class ImportCostTests
    {
        /// <summary>The cap on one import of a processor: some four times what it makes, and an eighth of what it made.</summary>
        private const long Cap = 64L * 1024 * 1024;

        private static IReadOnlyList<SourceFile> SourcesOf(string processor)
        {
            var sources = new List<SourceFile>();
            foreach ((string name, string text) in WorkbenchProcessors.Files)
                if (name == processor || name == "rv32i.svh" || name.EndsWith(".hex", StringComparison.Ordinal))
                    sources.Add(new SourceFile(name, text));
            return sources;
        }

        [TestCase("scpu.sv", "scpu_tb")]
        [TestCase("mcpu.sv", "mcpu_demo_tb")]
        [TestCase("pcpu.sv", "pcpu_tb")]
        public void ImportingAProcessor_MakesUnderTheCap(string processor, string bench)
        {
            IReadOnlyList<SourceFile> sources = SourcesOf(processor);
            ProjectImporter.Import(sources, bench);   // once first, as a session's other imports would warm what is shared

            long before = GC.GetAllocatedBytesForCurrentThread();
            ProjectImport import = ProjectImporter.Import(SourcesOf(processor), bench);
            long made = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(import.Succeeded, "sanity: it imports");
            Assert.Less(made, Cap, $"{processor} made {made / (1024.0 * 1024.0):F1} MB importing");
        }
    }
}
