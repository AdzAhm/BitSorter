using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The TIMING tab's rows (Part 15h): PATHS folds under an arrow, shut to begin with past eight lines, and a line too long
    /// for its row opens onto the rows under it. Ahmad's imported ALU listed two hundred paths, each cut short (2026-10-28).
    /// Text is measured here at 7 a character, as a stand-in for the font.
    /// </summary>
    public class TimingFoldTests
    {
        private static readonly Func<string, float> Width = s => s.Length * 7f;
        private const float Wide = 100000f;

        /// <summary>An <paramref name="bits"/>-bit ripple adder, drawn gate by gate: paths of every length from every input.</summary>
        private static Project Adder(int bits)
        {
            var s = new StringBuilder("module add(input logic cin");
            for (int i = 0; i < bits; i++)
                s.Append(", input logic a" + i + ", input logic b" + i);
            for (int i = 0; i < bits; i++)
                s.Append(", output logic s" + i);
            s.Append(", output logic cout);\n");
            for (int i = 0; i <= bits; i++)
                s.Append("    wire c" + i + ";\n");
            for (int i = 0; i < bits; i++)
                s.Append("    wire t" + i + ", u" + i + ", v" + i + ";\n");
            s.Append("    buf g0(c0, cin);\n");
            for (int i = 0; i < bits; i++)
                s.Append($"    xor x{i}(t{i}, a{i}, b{i});\n    xor y{i}(s{i}, t{i}, c{i});\n    and p{i}(u{i}, a{i}, b{i});\n    and q{i}(v{i}, t{i}, c{i});\n    or r{i}(c{i + 1}, u{i}, v{i});\n");
            s.Append("    buf g1(cout, c" + bits + ");\nendmodule\n");
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("add.sv", s.ToString()) });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            return import.Project;
        }

        private static int PathsLine(TimingView view) => view.Lines.ToList().FindIndex(l => l.Label == "PATHS");

        /// <summary>
        /// The 4-bit adder's PATHS starts shut: a row with its arrow, and none of its paths under it. Opened, every path is a
        /// row, in order; shut again, they go.
        /// </summary>
        [Test]
        public void ManyPaths_StartShut_AndOpenAndShutUnderTheArrow()
        {
            TimingView view = TimingView.Of(Adder(4));
            int paths = PathsLine(view);
            Assert.Greater(TimingFold.Under(view.Lines, paths), TimingFold.ShutPast, "sanity: more paths than a section opens with");
            Assert.AreEqual(view.Lines.Count, paths + 1 + TimingFold.Under(view.Lines, paths), "sanity: PATHS is the last section");

            var fold = new TimingFold();
            IReadOnlyList<TimingRow> rows = fold.Of(view.Lines, Wide, Width);
            Assert.AreEqual(paths + 1, rows.Count, "every line to PATHS, and none of its paths");
            TimingRow header = rows[rows.Count - 1];
            Assert.AreEqual(paths, header.Line);
            Assert.AreEqual(TimingArrow.Shut, header.Arrow);
            Assert.IsTrue(rows.Take(paths).All(r => r.Arrow == TimingArrow.None), "nothing else folds, and nothing is too long");
            Assert.AreEqual(-1, fold.RowOf(paths + 1), "a path folded away is on no row");

            Assert.IsTrue(fold.Toggle(rows.Count - 1), "the arrow opens it");
            rows = fold.Of(view.Lines, Wide, Width);
            Assert.AreEqual(view.Lines.Count, rows.Count, "every path a row");
            Assert.AreEqual(TimingArrow.Open, rows[paths].Arrow);
            CollectionAssert.AreEqual(Enumerable.Range(0, view.Lines.Count), rows.Select(r => r.Line), "in the lines' order");
            Assert.AreEqual(paths + 1, fold.RowOf(paths + 1));

            Assert.IsTrue(fold.Toggle(paths), "and shuts it");
            Assert.AreEqual(paths + 1, fold.Of(view.Lines, Wide, Width).Count);
            Assert.IsFalse(fold.Toggle(0), "a row with no arrow does nothing");
        }

        /// <summary>The full adder's few paths start open, under an arrow that shuts them.</summary>
        [Test]
        public void AFewPaths_StartOpen()
        {
            TimingView view = TimingView.Of(WorkbenchSamples.FullAdder());
            int paths = PathsLine(view);
            Assert.That(TimingFold.Under(view.Lines, paths), Is.InRange(1, TimingFold.ShutPast), "sanity");

            var fold = new TimingFold();
            IReadOnlyList<TimingRow> rows = fold.Of(view.Lines, Wide, Width);
            Assert.AreEqual(view.Lines.Count, rows.Count, "every line a row");
            Assert.AreEqual(TimingArrow.Open, rows[paths].Arrow);
        }

        /// <summary>
        /// A choice is kept by what it is about: the view made again after an edit is new lines, and PATHS shut stays shut, as
        /// a path opened stays open while it reads the same.
        /// </summary>
        [Test]
        public void AChoice_OutlivesTheViewMadeAgain()
        {
            Project project = WorkbenchSamples.FullAdder();
            TimingView view = TimingView.Of(project);
            int paths = PathsLine(view);
            var fold = new TimingFold();
            fold.Of(view.Lines, Wide, Width);
            Assert.IsTrue(fold.Toggle(paths));

            TimingView again = TimingView.Of(project);
            Assert.AreNotSame(view.Lines, again.Lines, "sanity: made again");
            IReadOnlyList<TimingRow> rows = fold.Of(again.Lines, Wide, Width);
            Assert.AreEqual(paths + 1, rows.Count, "PATHS still shut");
            Assert.AreEqual(TimingArrow.Shut, rows[paths].Arrow);
        }

        /// <summary>
        /// A line too long for its row starts shut, cut short; opened, it runs on over the rows under it, each no wider than the
        /// room, cut only after a comma or at the gap before the gates, and together the whole of it.
        /// </summary>
        [Test]
        public void ALongLine_OpensOntoTheRowsUnderIt()
        {
            TimingView view = TimingView.Of(Adder(4));
            int critical = view.Critical;
            string text = view.Lines[critical].Text;
            const float room = 30 * 7f;
            Assert.Greater(Width(text), room, "sanity: the critical path is longer than thirty characters");

            var fold = new TimingFold();
            IReadOnlyList<TimingRow> rows = fold.Of(view.Lines, room, Width);
            int at = fold.RowOf(critical);
            Assert.AreEqual(TimingArrow.Shut, rows[at].Arrow);
            Assert.AreEqual(text, rows[at].Text, "whole, to be cut short as it is drawn");
            Assert.AreEqual(-1, rows[at].Part);

            Assert.IsTrue(fold.Toggle(at));
            rows = fold.Of(view.Lines, room, Width);
            TimingRow[] mine = rows.Where(r => r.Line == critical).ToArray();
            Assert.Greater(mine.Length, 1, "over more than one row");
            Assert.AreEqual(TimingArrow.Open, mine[0].Arrow);
            CollectionAssert.AreEqual(Enumerable.Range(-1, mine.Length), mine.Select(r => r.Part), "the line's row, then its parts in order");
            Assert.IsTrue(mine.Skip(1).All(r => r.Arrow == TimingArrow.None));
            foreach (TimingRow row in mine)
                Assert.LessOrEqual(Width(row.Text), room, $"\"{row.Text}\" fits its row");
            Assert.AreEqual(Squeezed(text), Squeezed(string.Join(" ", mine.Select(r => r.Text))), "the whole of it, nothing lost");
            Assert.IsTrue(mine.Take(mine.Length - 1).All(r => r.Text.EndsWith(",") || text.Contains(r.Text + "   ")),
                "each row ends after a comma or before the gates: " + string.Join(" | ", mine.Select(r => r.Text)));
        }

        /// <summary>No room yet -- a dock not laid out -- is no line too long, and nothing to open.</summary>
        [Test]
        public void NoRoom_OpensNothing()
        {
            TimingView view = TimingView.Of(WorkbenchSamples.FullAdder());
            IReadOnlyList<TimingRow> rows = new TimingFold().Of(view.Lines, 0f, Width);
            Assert.IsTrue(rows.All(r => r.Arrow == TimingArrow.None || view.Lines[r.Line].Label == "PATHS"));
        }

        private static string Squeezed(string text) => string.Join(" ", text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
