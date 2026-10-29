using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The drawing of code arranged by hand (Part 15h-4f): parts put where they were put, by keys that name them as the code
    /// does, kept with the module and saved, the named ones through a change of the code and the numbered ones not, nothing
    /// left standing on anything, and TIDY one step back to the drawing laid out by itself.
    /// </summary>
    public class ArrangementTests
    {
        private const string Code = "module m (input logic [1:0] a, b, c, output logic [1:0] y);\n    assign y = (a & b) | c;\nendmodule\n";

        private static (BoardEditor Editor, RtlDrawing Drawing) Drawn(string source = Code)
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("m", source).Done);
            editor = new BoardEditor(editor.Project with { Top = "m" });
            return (editor, Draw(editor.Project));
        }

        private static RtlDrawing Draw(Project project)
        {
            CompiledProject compiled = BoardCompiler.Compile(project);
            Elaboration e = Elaborator.Elaborate(compiled.Tree, compiled.Top);
            Assert.IsFalse(e.HasErrors);
            return RtlDrawing.Of(project, RtlExtractor.Of(e.Design, "m"), "m", arranged: project.Find("m"));
        }

        private static Part Or(RtlDrawing drawing) => drawing.Module.Parts.OfType<OperatorPart>().Single(p => p.Op == OperatorKind.Or);

        private static Part And(RtlDrawing drawing) => drawing.Module.Parts.OfType<OperatorPart>().Single(p => p.Op == OperatorKind.And);

        [Test]
        public void APartPutByHand_IsWhereItWasPut_OnTheDrawingAgain_AndKeptByTheNameItDrives()
        {
            (BoardEditor editor, RtlDrawing drawing) = Drawn();
            Assert.AreEqual("sig:y", drawing.KeyOf(Or(drawing).Id), "the | drives y");
            Assert.AreEqual("port:a", drawing.KeyOf(drawing.Module.Parts.OfType<PortPart>().Single(p => p.Name == "a").Id));

            var put = new Placement(new Point(30, 6), 1, false);
            Assert.IsTrue(editor.Arrange("m", new Dictionary<string, Placement> { ["sig:y"] = put }).Done);
            RtlDrawing again = Draw(editor.Project);
            Assert.AreEqual(put, again.Module.Layout.PlacementOf(Or(again).Id));
            Assert.AreEqual(Code, editor.Project.Find("m").Source, "the code untouched");
        }

        [Test]
        public void TheArrangement_IsSavedAndReadAgain()
        {
            (BoardEditor editor, RtlDrawing drawing) = Drawn();
            Assert.IsTrue(editor.Arrange("m", new Dictionary<string, Placement> { ["sig:y"] = new Placement(new Point(30, 6), 2, true) }).Done);
            var problems = new List<string>();
            Project read = ProjectRecord.From(editor.Project).ToProject(problems);
            Assert.IsEmpty(problems);
            Module m = read.Find("m");
            Assert.AreEqual(new Placement(new Point(30, 6), 2, true), m.Arrangement["sig:y"]);
            Assert.AreEqual(Code, m.ArrangedFor);
        }

        [Test]
        public void ACodeChange_KeepsTheNamedPlaces_AndForgetsTheNumbered()
        {
            (BoardEditor editor, RtlDrawing drawing) = Drawn();
            string and = drawing.KeyOf(And(drawing).Id);
            StringAssert.StartsWith("cell:", and, "the & drives no signal: a numbered key");
            var named = new Placement(new Point(30, 6));
            var numbered = new Placement(new Point(30, -6));
            Assert.IsTrue(editor.Arrange("m", new Dictionary<string, Placement> { ["sig:y"] = named, [and] = numbered }).Done);

            Assert.IsTrue(editor.SetSource("m", Code.Replace("| c", "^ c").Replace("(a & b)", "(a & b)")).Done);
            RtlDrawing again = Draw(editor.Project);
            Part xor = again.Module.Parts.OfType<OperatorPart>().Single(p => p.Op == OperatorKind.Xor);
            Assert.AreEqual(named, again.Module.Layout.PlacementOf(xor.Id), "y is still driven from where it was put");
            Assert.AreNotEqual(numbered, again.Module.Layout.PlacementOf(And(again).Id), "a numbered place is the code's before only");
        }

        [Test]
        public void APartPutOnAnother_LeavesNothingStandingOnAnything()
        {
            (BoardEditor editor, RtlDrawing drawing) = Drawn();
            Placement onTheAnd = drawing.Module.Layout.PlacementOf(And(drawing).Id);
            Assert.IsTrue(editor.Arrange("m", new Dictionary<string, Placement> { ["sig:y"] = onTheAnd }).Done);
            RtlDrawing again = Draw(editor.Project);
            Assert.AreEqual(onTheAnd, again.Module.Layout.PlacementOf(Or(again).Id), "the one put by hand where it was put");
            List<PartOutline> outlines = again.Module.Parts.Select(p => PartGeometry.Placed(again.Project, p, again.Module.Layout.PlacementOf(p.Id))).ToList();
            for (int i = 0; i < outlines.Count; i++)
                for (int j = i + 1; j < outlines.Count; j++)
                    Assert.IsFalse(outlines[i].Left < outlines[j].Right && outlines[j].Left < outlines[i].Right
                        && outlines[i].Bottom < outlines[j].Top && outlines[j].Bottom < outlines[i].Top,
                        $"{again.Module.Parts[i].Name} and {again.Module.Parts[j].Name} stand apart");
        }

        [Test]
        public void Tidy_ClearsTheArrangement_InOneStep()
        {
            (BoardEditor editor, RtlDrawing drawing) = Drawn();
            Assert.IsFalse(editor.Arrange("m", null).Done, "nothing to tidy yet");
            Assert.IsTrue(editor.Arrange("m", new Dictionary<string, Placement> { ["sig:y"] = new Placement(new Point(30, 6)) }).Done);
            Assert.IsTrue(editor.Arrange("m", null).Done);
            Assert.AreEqual(0, editor.Project.Find("m").Arrangement.Count);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(1, editor.Project.Find("m").Arrangement.Count, "one Ctrl+Z, the arrangement back");
        }
    }
}
