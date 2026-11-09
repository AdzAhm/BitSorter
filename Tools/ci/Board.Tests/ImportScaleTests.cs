using System;
using System.Diagnostics;
using System.Text;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// What drawing a large module costs (Part 16d's sweep). A browser frees memory only between frames, and an import is one
    /// frame, so what an import makes is what the page must hold: a module of a few thousand gates is a generated netlist,
    /// well inside an import's 4 MB a file, and its drawing must cost in proportion to it.
    /// </summary>
    /// <remarks>
    /// Measured with <c>GC.GetAllocatedBytesForCurrentThread</c>. .NET 10 keeps on the stack some of what Mono and IL2CPP put
    /// on the heap, so each cap stands well above what dotnet measures, and far below what the shape it guards against made.
    /// </remarks>
    public class ImportScaleTests
    {
        private const long Megabyte = 1024 * 1024;

        /// <summary><paramref name="gates"/> NAND gates side by side, each reading a and b: a flat netlist, one column deep.</summary>
        private static string Wide(int gates)
        {
            var text = new StringBuilder("module wide (input logic a, input logic b, output logic y);\n");
            for (int i = 1; i <= gates; i++)
                text.Append("    logic w").Append(i).Append(";\n    nand g").Append(i).Append(" (w").Append(i).Append(", a, b);\n");
            return text.Append("    assign y = w1;\nendmodule\n").ToString();
        }

        /// <summary>What drawing <paramref name="top"/> of <paramref name="text"/> made, and how long it took; the drawing must succeed.</summary>
        private static (long Made, double Seconds, Module Drawn) Draw(string text, string top)
        {
            SyntaxTree tree = SyntaxReader.ReadText(text, top + ".sv");
            BoardImporter.Import(SyntaxReader.ReadText(Wide(4), "warm.sv"), "wide");   // the first draw of a run pays for its code

            var clock = Stopwatch.StartNew();
            long before = GC.GetAllocatedBytesForCurrentThread();
            ImportResult imported = BoardImporter.Import(tree, top);
            long made = GC.GetAllocatedBytesForCurrentThread() - before;
            double seconds = clock.Elapsed.TotalSeconds;

            Assert.IsTrue(imported.Succeeded, "sanity: it is drawn\n" + string.Join("\n", imported.Problems));
            TestContext.WriteLine($"{top}: {made / (double)Megabyte:F1} MB in {seconds:F2} s");
            return (made, seconds, imported.Project.Find(top));
        }

        /// <summary>
        /// Drawn an edit at a time, each edit checking the whole project again -- a netlist made and every pin of every part
        /// gone over -- a flat module cost the square of its size: a thousand gates made 13.5 GB in 14 s, in one frame.
        /// Drawn whole and checked once, it makes 11 MB.
        /// </summary>
        [Test]
        public void AFlatModuleOfAThousandGates_IsDrawnInMegabytes_NotGigabytes()
        {
            (long made, double seconds, Module drawn) = Draw(Wide(1000), "wide");

            Assert.AreEqual(1003, drawn.Parts.Count, "sanity: every gate and port drawn");
            Assert.Less(made, 64 * Megabyte, "a thousand gates made 13.5 GB, an edit at a time");
            Assert.Less(seconds, 10, "and took 14 s");
        }

        /// <summary><c>assign w1 = a; assign w2 = w1; ...</c>, <paramref name="aliases"/> long: one net of many names.</summary>
        private static string Aliases(int aliases)
        {
            var text = new StringBuilder("module chain (input logic a, output logic y);\n    logic w0;\n    assign w0 = a;\n");
            for (int i = 1; i <= aliases; i++)
                text.Append("    logic w").Append(i).Append(";\n    assign w").Append(i).Append(" = w").Append(i - 1).Append(";\n");
            return text.Append("    assign y = w").Append(aliases).Append(";\nendmodule\n").ToString();
        }

        /// <summary>
        /// The names an assign joins are gathered by a union-find, which had no path compression and put each older root under
        /// the newer name: a chain of aliases became a list, walked from every name, the square of its length -- 16,000 took
        /// 2 s, and 40,000, a file of 1.3 MB, 25 s.
        /// </summary>
        [Test]
        public void AChainOfFortyThousandAliases_IsJoinedInMoments()
        {
            (_, double seconds, Module drawn) = Draw(Aliases(40_000), "chain");

            Assert.AreEqual(1, drawn.Wires.Count, "sanity: one net, a to y, one wire");
            Assert.Less(seconds, 3, "16,000 aliases took 2 s, and the time grew as their square");
        }

        /// <summary><paramref name="gates"/> NAND gates in a ring through one register: a loop, as every state machine has.</summary>
        private static string Ring(int gates)
        {
            var text = new StringBuilder("module ring (input logic clk, input logic b, output logic y);\n    logic w0, q;\n");
            text.Append("    always_ff @(posedge clk) q <= w").Append(gates).Append(";\n    assign w0 = q;\n");
            for (int i = 1; i <= gates; i++)
                text.Append("    logic w").Append(i).Append(";\n    nand g").Append(i).Append(" (w").Append(i).Append(", w").Append(i - 1).Append(", b);\n");
            return text.Append("    assign y = q;\nendmodule\n").ToString();
        }

        /// <summary>
        /// A drawing's columns are found by relaxing every part's depth round after round, and a loop keeps them rising until
        /// as many rounds as there are parts have run; each round looked every pin's part up by a walk of the parts. Two
        /// thousand gates in a ring took 7 s and made 205 MB, once a module was drawn whole.
        /// </summary>
        [Test]
        public void ARingOfTwoThousandGates_IsLaidOutInMoments()
        {
            (long made, double seconds, Module drawn) = Draw(Ring(2000), "ring");

            Assert.AreEqual(2004, drawn.Parts.Count, "sanity: every gate, the register and the ports drawn");
            Assert.Less(made, 64 * Megabyte, "it made 205 MB");
            Assert.Less(seconds, 3, "and took 7 s");
        }

        /// <summary>What importing <paramref name="files"/> made, and how long it took; the import must succeed.</summary>
        private static (long Made, double Seconds, ProjectImport Import) Imported(params SourceFile[] files)
        {
            ProjectImporter.Import(new[] { new SourceFile("warm.sv", Wide(4)) });   // the first import of a run pays for its code

            var clock = Stopwatch.StartNew();
            long before = GC.GetAllocatedBytesForCurrentThread();
            ProjectImport import = ProjectImporter.Import(files);
            long made = GC.GetAllocatedBytesForCurrentThread() - before;
            double seconds = clock.Elapsed.TotalSeconds;

            Assert.IsTrue(import.Succeeded, "sanity: it is imported\n" + string.Join("\n", import.Notes));
            TestContext.WriteLine($"{made / (double)Megabyte:F1} MB in {seconds:F2} s");
            return (made, seconds, import);
        }

        /// <summary><paramref name="size"/> characters of comment lines.</summary>
        private static string Comments(int size)
        {
            const string line = "// a note on the design, written for BitSorter's tests\n";
            var text = new StringBuilder(size + line.Length);
            while (text.Length < size)
                text.Append(line);
            return text.ToString();
        }

        /// <summary>
        /// A module something looks inside by a hierarchical name -- <c>dut.state</c> -- is written, so the importer looks
        /// for each instance's name followed by a dot: it made a regex for every instance and ran it over the whole of its
        /// module's file, the instances times the file. Eight thousand instances in a file of 3.5 MB took 12.8 s, in a generate
        /// branch never made, so that the rest of the import costs next to nothing beside the looking.
        /// </summary>
        [Test]
        public void EightThousandInstances_InALargeFile_AreLookedForInOnePass()
        {
            var text = new StringBuilder("module leaf (input logic a, output logic y);\n    always_comb y = ~a;\nendmodule\n");
            text.Append(Comments(3_500_000));
            text.Append("module top (input logic a, output logic y);\n    always_comb y = a;\n    if (0) begin : never\n");
            for (int i = 0; i < 8000; i++)
                text.Append("        leaf g").Append(i).Append(" (.a(a), .y());\n");
            text.Append("    end\nendmodule\n");

            (_, double seconds, ProjectImport import) = Imported(new SourceFile("top.sv", text.ToString()));

            Assert.AreEqual(2, import.Project.Modules.Count, "sanity: both modules made");
            Assert.Less(seconds, 4, "the instances were looked for one at a time over the whole file");
        }

        /// <summary>
        /// Which of the files kept declare something -- a constant, a type -- or include one that does is found pass by pass
        /// until none is added, and each pass read every file not yet found in full, twice: a chain of includes listed against
        /// its direction adds one a pass. 150 headers of 20 KB, each including the one before and the first declaring a
        /// constant, listed last first, made 2,001 MB in 1.6 s -- in one frame, where a browser frees memory only between them.
        /// </summary>
        [Test]
        public void AChainOfHeadersListedAgainstItsDirection_IsFoundDeclaringInOnePass()
        {
            const int chain = 150;
            string padding = Comments(20_000);
            var files = new System.Collections.Generic.List<SourceFile>
            {
                new SourceFile("m.sv", "// its constants are in \"f149.svh\"\nmodule m (input logic a, output logic y);\n    assign y = a;\nendmodule\n"),
            };
            for (int i = chain - 1; i >= 0; i--)
                files.Add(new SourceFile($"f{i:000}.svh", (i == 0 ? "localparam int K = 1;\n" : $"`include \"f{i - 1:000}.svh\"\n") + padding));

            (long made, double seconds, ProjectImport import) = Imported(files.ToArray());

            Assert.AreEqual(chain, import.Project.Files.Count, "sanity: every header kept");
            Assert.Less(made, 128 * Megabyte, "each pass read every header not yet found, twice");
            Assert.Less(seconds, 1);
        }

        /// <summary>
        /// A written module's text is cut from its file with what the file said between the modules before it -- its defines,
        /// includes and conditionals -- since it saw them there, so a module carries every define above it, and a file of
        /// modules with defines between them carries the square of them, each module's text parsed with its own. A hundred and
        /// twenty modules, each after sixty defines of its own, carried 6.4 MB and made 824 MB in 4.8 s; two hundred after two hundred
        /// each ran for more than ten minutes.
        /// </summary>
        [Test]
        public void AFileOfModulesWithDefinesBetweenThem_CarriesNoMoreThanAFile_AndSaysWhy()
        {
            var text = new StringBuilder();
            for (int m = 0; m < 120; m++)
            {
                for (int d = 0; d < 60; d++)
                    text.Append("`define W").Append(m).Append('_').Append(d).Append(" 1\n");
                text.Append("module m").Append(m).Append(" (input logic clk, output logic q);\n    always_ff @(posedge clk) q <= ~q;\nendmodule\n");
            }

            var clock = Stopwatch.StartNew();
            long before = GC.GetAllocatedBytesForCurrentThread();
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("many.sv", text.ToString()) });
            long made = GC.GetAllocatedBytesForCurrentThread() - before;
            double seconds = clock.Elapsed.TotalSeconds;
            TestContext.WriteLine($"{made / (double)Megabyte:F1} MB in {seconds:F2} s");

            Assert.IsFalse(import.Succeeded, "refused");
            StringAssert.Contains("says so much between its modules", string.Join("\n", import.Notes), "saying why");
            Assert.Less(made, 128 * Megabyte, "what it carried was parsed with each module");
        }

        /// <summary>The pair of the one above: a table of a thousand defines over a hundred modules, each using one, is carried whole.</summary>
        [Test]
        public void ATableOfDefinesOverAHundredModules_IsCarriedIntoEach()
        {
            var text = new StringBuilder();
            for (int d = 0; d < 1000; d++)
                text.Append("`define OP").Append(d).Append(" 7'd").Append(d % 128).Append('\n');
            for (int m = 0; m < 100; m++)
                text.Append("module m").Append(m).Append(" (input logic clk, output logic [6:0] q);\n    always_ff @(posedge clk) q <= `OP")
                    .Append(m).Append(";\nendmodule\n");

            (_, _, ProjectImport import) = Imported(new SourceFile("table.sv", text.ToString()));

            Assert.AreEqual(100, import.Project.Modules.Count);
            StringAssert.Contains("`define OP999 ", import.Project.Find("m99").Source, "every define it saw above it");
        }
    }
}
