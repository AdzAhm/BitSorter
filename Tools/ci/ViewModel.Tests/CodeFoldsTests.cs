using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BitSorter.Ci;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Where SystemVerilog folds (5.1, the +/- in the code editor's margin), and the code shown with some of it folded: every
    /// edit of the shown text lands where it shows, and a fold the edit left alone stays folded.
    /// </summary>
    public class CodeFoldsTests
    {
        /// <summary>The fixture both builds' folds are held to: each file and its folds, [first, last] from 1.</summary>
        public static IEnumerable<TestCaseData> Fixture()
        {
            string path = RepoRoot.Combine("Tools/codemirror/test/folds.fixture.json");
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement file in fixture.RootElement.GetProperty("files").EnumerateArray())
            {
                string code = file.GetProperty("code").GetString();
                string folds = string.Join(" ", file.GetProperty("folds").EnumerateArray()
                    .Select(f => f[0].GetInt32() + "-" + f[1].GetInt32()));
                yield return new TestCaseData(code, folds).SetName("Folds: " + file.GetProperty("name").GetString());
            }
        }

        [TestCaseSource(nameof(Fixture))]
        public void TheFixturesFolds_AreFound(string code, string folds)
        {
            Assert.AreEqual(folds, string.Join(" ", CodeFolds.Of(code).Select(f => f.ToString())));
        }

        private const string Module =
            "module ha (\n" +           // 1
            "    input a, b,\n" +        // 2
            "    output s, c\n" +        // 3
            ");\n" +                     // 4
            "    always_comb begin\n" +  // 5
            "        s = a ^ b;\n" +     // 6
            "        c = a & b;\n" +     // 7
            "    end\n" +                // 8
            "endmodule\n";               // 9

        [Test]
        public void AFoldedBlock_ShowsAsItsFirstLine_AMark_AndItsLastLinesText()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });   // line 5, from 0: the always's begin

            Assert.AreEqual("module ha (\n    input a, b,\n    output s, c\n);\n    always_comb begin … end\nendmodule\n", view.Shown);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 8, 9 }, view.RealLines(), "the margin's numbers skip the folded lines");
            Assert.IsTrue(view.IsFolded(4));
            Assert.AreSame(view, view.Toggled(1), "a line no block starts on toggles nothing");
        }

        [Test]
        public void AModuleFolded_HidesTheBlocksInside_AndTheyShowFoldedOnceItOpens()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 0, 4 });

            Assert.AreEqual("module ha ( … endmodule\n", view.Shown, "the module's fold hides the port list and the always");
            FoldedView opened = view.Toggled(0);
            StringAssert.Contains("always_comb begin … end", opened.Shown, "the begin folded inside it still folded");
        }

        [Test]
        public void TextTypedOnAFoldedLine_LandsWhereItShows_AndTheFoldStays()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });
            string shown = view.Shown.Replace("always_comb begin", "always_comb /* yes */ begin");

            (string real, int caret, FoldedView after) = view.Edit(shown);

            Assert.AreEqual(Module.Replace("always_comb begin", "always_comb /* yes */ begin"), real);
            Assert.AreEqual(real.IndexOf("/* yes */") + "/* yes */ ".Length, caret);
            Assert.IsTrue(after.IsFolded(4), "the fold is left alone");
            Assert.AreEqual(shown, after.Shown);
        }

        [Test]
        public void AnEditAboveAFold_MovesIt_AndOneBelowLeavesIt()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });

            (string real, _, FoldedView after) = view.Edit("// a new first line\n" + view.Shown);
            Assert.AreEqual("// a new first line\n" + Module, real);
            Assert.IsTrue(after.IsFolded(5), "the fold moved down a line with its block");

            (string below, _, FoldedView left) = view.Edit(view.Shown + "// the end\n");
            Assert.AreEqual(Module + "// the end\n", below);
            Assert.IsTrue(left.IsFolded(4));
        }

        [Test]
        public void DeletingTheMark_DeletesTheBlockItHides_AndTypingInItGoesAfterTheBlock()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });
            int mark = view.Shown.IndexOf('…');

            (string deleted, _, FoldedView afterDelete) = view.Edit(view.Shown.Remove(mark, 1));
            Assert.AreEqual(Module.Substring(0, Module.IndexOf("begin") + "begin".Length) + "end\nendmodule\n", deleted,
                "the lines it hid go with it, as Notepad++ deletes a folded block");
            Assert.IsFalse(afterDelete.Folded.Any(), "and nothing is folded that was not");

            (string typed, int caret, FoldedView afterTyping) = view.Edit(view.Shown.Insert(mark, "x"));
            Assert.AreEqual(Module.Replace("    end\n", "    xend\n"), typed, "typed inside the mark: after the block it hides");
            Assert.AreEqual(typed.IndexOf("xend") + 1, caret);
            Assert.IsFalse(afterTyping.IsFolded(4), "and the fold opened, to show where it went");
        }

        [Test]
        public void ASelectionAcrossAFold_IsDeletedWithEverythingItHides()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });
            int from = view.Shown.IndexOf("begin"), to = view.Shown.IndexOf("end\nendmodule");

            (string real, _, _) = view.Edit(view.Shown.Remove(from, to - from));

            Assert.AreEqual(Module.Substring(0, Module.IndexOf("begin")) + "end\nendmodule\n", real);
        }

        [Test]
        public void TheCaret_IsMappedBothWays_AndOneSentIntoAFoldOpensIt()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });
            int endmodule = Module.IndexOf("endmodule");
            Assert.AreEqual(endmodule, view.RealOf(view.ShownOf(endmodule)), "a visible offset there and back");

            int inside = Module.IndexOf("c = a & b");
            Assert.AreEqual(view.Shown.IndexOf('…') - 1, view.ShownOf(inside), "a hidden offset shows at its fold's mark");
            Assert.IsFalse(view.Showing(inside).IsFolded(4), "and going there opens the fold");
            Assert.AreEqual(4, view.Around(inside)?.FirstLine, "Ctrl+Shift+[ there folds the begin, the innermost block");
        }

        [Test]
        public void AnEditThatBreaksABlock_DropsItsFold_AndUnfoldedCodeIsShownAsItIs()
        {
            FoldedView view = FoldedView.Of(Module, new[] { 4 });
            (string real, _, FoldedView after) = view.Edit(view.Shown.Replace("always_comb begin", "always_comb"));

            Assert.IsFalse(after.Folded.Any(), "no begin, no fold");
            Assert.AreEqual(real, after.Shown, "and nothing folded shows the code as it is");
            Assert.AreEqual(Module, FoldedView.Of(Module, null).Shown);
        }

        [Test]
        public void FoldAll_AndOpenAll()
        {
            FoldedView all = FoldedView.Of(Module, null).All(true);
            Assert.AreEqual("module ha ( … endmodule\n", all.Shown);
            Assert.AreEqual(Module, all.All(false).Shown);
        }
    }
}
