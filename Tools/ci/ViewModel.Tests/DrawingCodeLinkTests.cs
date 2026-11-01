using System;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The drawing of code and its code linked (Part 15h-5, Ahmad's choice, 2026-11-01): what is selected on the drawing gives
    /// the lines of the code it comes from, a line of the code selects the parts it makes, and the inspector says where a part
    /// comes from. The CODE tab numbers a written module's lines as its editor does, so a line is one line everywhere.
    /// </summary>
    public class DrawingCodeLinkTests
    {
        private const string Code =
            "// A chooser and a parity, written for this test.\n" +   // 1
            "module pick (\n" +                                        // 2
            "    input  logic [1:0] a, b,\n" +                         // 3
            "    input  logic       s,\n" +                            // 4
            "    output logic [1:0] y,\n" +                            // 5
            "    output logic       p\n" +                             // 6
            ");\n" +                                                   // 7
            "    assign y = s ? b : a;\n" +                            // 8
            "    always_comb begin\n" +                                // 9
            "        p = ^a;\n" +                                      // 10
            "    end\n" +                                              // 11
            "endmodule\n";                                             // 12

        private static WorkbenchSession Session()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("pick", Code).Done);
            double now = 0;
            var session = new WorkbenchSession(editor.Project with { Top = "pick" }, () => now += 0.01);
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: a written module opens as its drawing");
            return session;
        }

        private static int Mux(WorkbenchSession session) => session.DrawnModule.Parts.OfType<MuxPart>().Single().Id;

        private static int Port(WorkbenchSession session, string name) => session.DrawnModule.Parts.OfType<PortPart>().Single(p => p.Name == name).Id;

        [Test]
        public void APartSelected_GivesTheLineItComesFrom_AndAWireTheLineOfThePartDrivingIt()
        {
            WorkbenchSession session = Session();
            var editor = new CanvasEditor(session);
            Assert.AreEqual(8, session.LineOfPart(Mux(session)), "assign y = s ? b : a");
            Assert.AreEqual(4, session.LineOfPart(Port(session, "s")), "s's declaration");

            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: Mux(session)), extend: false, toggle: false);
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: Port(session, "a")), extend: true, toggle: false);
            CollectionAssert.AreEqual(new[] { 3, 8 }, editor.LinesOfSelection(), "in order, each once");

            Wire fromMux = session.DrawnModule.Wires.First(w => !w.From.IsTap && w.From.Pin.Part == Mux(session));
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Wire, wire: fromMux.Id), extend: false, toggle: false);
            CollectionAssert.AreEqual(new[] { 8 }, editor.LinesOfSelection(), "a wire, the line of what drives it");
        }

        [Test]
        public void ALineOfTheCode_SelectsThePartsItMakes_AndALineMakingNoneLeavesTheSelection()
        {
            WorkbenchSession session = Session();
            var editor = new CanvasEditor(session);
            Assert.AreEqual(1, editor.SelectLine(8));
            CollectionAssert.AreEquivalent(new[] { Mux(session) }, editor.Selection.Parts);

            int parity = editor.SelectLine(10);
            Assert.GreaterOrEqual(parity, 1, "p = ^a");
            Assert.IsTrue(editor.Selection.Parts.All(p => session.LineOfPart(p) == 10));

            int version = editor.Version;
            Assert.AreEqual(0, editor.SelectLine(11), "end");
            Assert.AreEqual(version, editor.Version, "the selection left as it was");
            Assert.AreEqual(parity, editor.Selection.Parts.Count);
        }

        [Test]
        public void TheInspector_SaysWhereAPartComesFrom()
        {
            WorkbenchSession session = Session();
            Inspection mux = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: Mux(session)),
                session.DescribePart, session.LineOfPart);
            StringAssert.EndsWith(" From line 8.", mux.Says);
            Inspection port = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: Port(session, "y")),
                session.DescribePart, session.LineOfPart);
            StringAssert.EndsWith("From line 5.", port.Says);
        }

        [Test]
        public void TheCodeTab_NumbersAWrittenModulesLines_AsItsEditorDoes()
        {
            WorkbenchSession session = Session();
            CodeListing listing = session.Listing;
            int file = listing.FileOf("pick");
            int body = listing.BodyLine(file);
            Assert.Greater(body, 1, "BitSorter's comment over it");
            string[] lines = listing.Files[file].Text.Split('\n');
            Assert.AreEqual("    assign y = s ? b : a;", lines[body - 1 + 8 - 1], "line 8 of the code is line 8 under the listing's numbers");
        }

        [Test]
        public void OverAnythingButTheDrawing_ThereAreNoLines()
        {
            WorkbenchSession session = Session();
            int mux = Mux(session);
            session.DrawCode(false);
            Assert.IsFalse(session.ShowsDrawingOfCode, "sanity: its code shown");
            Assert.AreEqual(0, session.LineOfPart(mux), "over the code itself");
            Assert.AreEqual(0, session.PartsOnLine(8).Count);
        }
    }
}
