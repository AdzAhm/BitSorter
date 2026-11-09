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
    }
}
