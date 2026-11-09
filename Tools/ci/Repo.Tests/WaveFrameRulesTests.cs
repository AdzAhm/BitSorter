using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// A frame of the waves reads a row's glitch marks as far as the view shows them (<c>WorkbenchModel.RowSwallowedIn</c>, a
    /// binary search), never from the run's start. <c>GlitchMarkCostTests</c> times that read under dotnet; this holds the
    /// view to it, which dotnet cannot run. The waves' labels (<c>WaveformView.Place</c>) and their crosses
    /// (<c>WorkbenchDrawing.Glitches</c>) each took every mark since the run began and tested each against the view, so a
    /// frame's cost grew with the run (the 16d sweep) -- and the cost test alone passed whatever the view called (the
    /// sweep's review).
    /// </summary>
    public class WaveFrameRulesTests
    {
        /// <summary>A call of the model's whole-run read of a row's swallowed changes: <c>RowSwallowed(</c>, not <c>RowSwallowedIn(</c>.</summary>
        private static readonly Regex WholeRun = new Regex(@"\.RowSwallowed\s*\(", RegexOptions.Compiled);

        [Test]
        public void TheWaves_ReadARowsGlitchMarks_AsFarAsTheViewShowsThem()
        {
            var fromTheStart = new List<string>();
            var inView = new HashSet<string>();
            foreach (string file in Directory.EnumerateFiles(RepoRoot.Combine("Assets/Scripts/View"), "*.cs", SearchOption.AllDirectories))
            {
                string code = PureLayerRulesTests.CommentsAndLiterals.Replace(File.ReadAllText(file), " ");
                if (WholeRun.IsMatch(code))
                    fromTheStart.Add(Path.GetFileName(file));
                if (code.Contains(".RowSwallowedIn(", StringComparison.Ordinal))
                    inView.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(fromTheStart, "these read a row's glitch marks from the run's start at every frame, where RowSwallowedIn reads the marks in view: "
                + string.Join(", ", fromTheStart));

            // Paired, so the rule cannot pass by nothing reading the marks: the labels and the crosses still read them, in view.
            foreach (string reader in new[] { "WaveformView.cs", "WorkbenchDrawing.cs" })
                Assert.IsTrue(inView.Contains(reader), $"{reader} reads a row's glitch marks in view (RowSwallowedIn)");
        }

        [Test]
        public void TheRule_SeesTheWholeRunsRead_AndNotTheViewsNorAComment()
        {
            Assert.IsTrue(WholeRun.IsMatch("_model.RowSwallowed(row, _swallowed);"), "the waves' read before the 16d sweep");
            Assert.IsTrue(WholeRun.IsMatch("model.RowSwallowed (row, Swallowed);"));
            Assert.IsFalse(WholeRun.IsMatch("_model.RowSwallowedIn(row, View, _swallowed);"));
            Assert.IsFalse(WholeRun.IsMatch("int n = _model.RowSwallowedCount(row);"));
            Assert.IsFalse(WholeRun.IsMatch(PureLayerRulesTests.CommentsAndLiterals.Replace("// once _model.RowSwallowed(row, all)", " ")));
        }
    }
}
