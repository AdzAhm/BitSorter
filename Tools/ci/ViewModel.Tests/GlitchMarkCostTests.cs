using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// What a frame reads of a row's glitch marks -- the "glitch swallowed" labels and the crosses on the waves -- is what the
    /// view shows of the row, not every mark since the run began (the 16d sweep, waves-13). The marks are kept in time order
    /// for that, and the model says what it reads a frame does not grow with the run; but the waves took every mark up to the
    /// playhead, from the start, at every redraw, and tested each against the view. This times the model's read; that the
    /// waves use it, dotnet cannot run, and <c>WaveFrameRulesTests</c> reads their source for it.
    /// </summary>
    public class GlitchMarkCostTests
    {
        /// <summary>A buffer slower than its input's pulses, every one of which it swallows: <paramref name="pulses"/> of them.</summary>
        private static Project Glitchy(int pulses)
        {
            ProjectImport import = ProjectImporter.Import(new[]
            {
                new SourceFile("slow.sv", "module slow (input logic a, output logic y);\n" +
                                          "    buf #3 g (y, a);\n" +
                                          "endmodule\n"),
                new SourceFile("slow_tb.sv", "module slow_tb;\n" +
                                             "    logic a, y;\n" +
                                             "    slow dut (.a(a), .y(y));\n" +
                                             "    initial begin\n" +
                                             "        a = 0;\n" +
                                             $"        repeat ({pulses}) begin #4 a = 1; #1 a = 0; end\n" +
                                             "        #10 $finish;\n" +
                                             "    end\n" +
                                             "endmodule\n"),
            });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return import.Project;
        }

        /// <summary>What the waves read of a row's glitch marks for one frame: those the view shows, up to the playhead.</summary>
        private static void Frame(WorkbenchModel model, int row, WaveView view, List<double> shown) =>
            model.RowSwallowedIn(row, view, shown);

        [Test]
        public void ARowsGlitchMarks_AreReadAsFarAsTheViewShows_NotFromTheRunsStart()
        {
            const int pulses = 20000;
            WorkbenchModel model = WorkbenchModel.Of(Glitchy(pulses));
            int row = Enumerable.Range(0, model.RowCount).Single(r => model.Layout.Rows[r].Label == "y");
            Assert.GreaterOrEqual(model.RowSwallowedCount(row), pulses, "sanity: every pulse swallowed on its way to y");
            model.Seek(model.End);

            var view = new WaveView(1000f);
            view.Fit((long)model.End);
            Assert.IsTrue(view.ShowRange(model.End - 30, model.End - 10), "sanity: twenty units of the run shown");
            var shown = new List<double>();

            Frame(model, row, view, shown);   // warmed
            var marks = new List<double>();
            model.RowSwallowed(row, marks, model.End);
            CollectionAssert.AreEqual(marks.Where(view.Shows).OrderBy(t => t), shown, "the marks the view shows, and those alone, in time order");
            Assert.That(shown.Count, Is.InRange(1, 8), "sanity: a handful in view");

            const int frames = 200;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++)
                Frame(model, row, view, shown);
            double each = watch.Elapsed.TotalMilliseconds * 1000 / frames;
            TestContext.WriteLine($"{model.RowSwallowedCount(row)} marks on the row, {shown.Count} in view: {each:F1} microseconds a frame");
            Assert.Less(each, 10, "a frame's read of the row's marks, in microseconds: the marks in view, not the run's");
        }
    }
}
