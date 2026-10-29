using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The drawing of code edited, unlocked (Part 15h-4g): the edit made on the drawing and written into the module's code --
    /// one undo step, the drawing made again from it -- after a question the first time for each module; refused while
    /// locked, with GATES on, and where the drawing is not the code.
    /// </summary>
    public class DrawingEditTests
    {
        private const string Chooser =
            "// A chooser, written for this test.\n" +
            "module pick (\n" +
            "    input  logic [1:0] a, b,\n" +
            "    input  logic       s,\n" +
            "    output logic [1:0] y    // a or b\n" +
            ");\n" +
            "    assign y = s ? b : a;\n" +
            "endmodule\n";

        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        private static WorkbenchSession Written(string source, string name)
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule(name, source).Done);
            var session = new WorkbenchSession(editor.Project with { Top = name }, Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: a written module opens as its drawing");
            return session;
        }

        private static string Code(WorkbenchSession session) => session.Project.Find(session.Module).Source;

        private static int Mux(WorkbenchSession session) => session.Run.Model.Layout.Parts.Single(p => p.Part is MuxPart).Part.Id;

        [Test]
        public void AnEditOfTheDrawing_AsksFirst_ThenIsWrittenIntoTheCode_AndUndoneByOneStep()
        {
            WorkbenchSession session = Written(Chooser, "pick");

            EditResult asked = session.Remove(new[] { Mux(session) });
            Assert.IsFalse(asked.Done);
            Assert.AreEqual(WorkbenchSession.RewriteAsked, asked.Refusal);
            Assert.IsTrue(session.AsksToRewrite);
            Assert.AreEqual(Chooser, Code(session), "nothing written before the answer");

            Assert.IsTrue(session.AnswerRewrite(true).Done);
            Assert.IsFalse(session.AsksToRewrite);
            string code = Code(session);
            StringAssert.StartsWith("// A chooser, written for this test.\n", code, "the comments over the module kept");
            StringAssert.DoesNotContain("?", code, "the mux taken out of the code");
            Assert.IsTrue(session.ShowsDrawingOfCode, "drawn again from the code it wrote");
            Assert.IsFalse(session.Run.Model.Layout.Parts.Any(p => p.Part is MuxPart));

            Assert.IsTrue(session.Undo());
            Assert.AreEqual(Chooser, Code(session), "one Ctrl+Z, the code as it was, to the character");
        }

        [Test]
        public void TheQuestion_IsAskedOnceForAModule()
        {
            WorkbenchSession session = Written(Chooser, "pick");
            session.Remove(new[] { Mux(session) });
            Assert.IsTrue(session.AnswerRewrite(true).Done);
            Assert.IsTrue(session.Undo());

            EditResult again = session.Remove(new[] { Mux(session) });
            Assert.IsTrue(again.Done, again.Refusal);
            Assert.IsFalse(session.AsksToRewrite);
        }

        [Test]
        public void AnsweredNo_TheEditIsDropped_AndAskedAgainNextTime()
        {
            WorkbenchSession session = Written(Chooser, "pick");
            session.Remove(new[] { Mux(session) });
            Assert.IsFalse(session.AnswerRewrite(false).Done);
            Assert.AreEqual(Chooser, Code(session));
            Assert.IsFalse(session.AsksToRewrite);

            Assert.AreEqual(WorkbenchSession.RewriteAsked, session.Remove(new[] { Mux(session) }).Refusal);
        }

        [Test]
        public void APartPlacedAndWired_IsInTheCode()
        {
            WorkbenchSession session = Written(
                "module both (input logic a, b, output logic y);\n    assign y = a | b;\nendmodule\n", "both");
            int or = session.Run.Model.Layout.Parts.Single(p => p.Part is OperatorPart).Part.Id;
            session.Remove(new[] { or });
            Assert.IsTrue(session.AnswerRewrite(true).Done);

            EditResult placed = session.Place(PartsCatalog.Find(session.Project, session.Module, "and"), new Placement(5, 0));
            Assert.IsTrue(placed.Done, placed.Refusal);
            SchematicLayout layout = session.Run.Model.Layout;
            int and = layout.Parts.Single(p => p.Part is GatePart).Part.Id;
            int Port(string name) => session.Run.Model.Layout.Ports.Single(p => p.Name.Split('[')[0] == name).Part;
            Assert.IsTrue(session.Connect(Endpoint.At(Port("a"), "out"), Endpoint.At(and, "a")).Done);
            and = session.Run.Model.Layout.Parts.Single(p => p.Part is GatePart).Part.Id;   // drawn again: its ids are new
            layout = session.Run.Model.Layout;
            Assert.IsTrue(session.Connect(Endpoint.At(Port("b"), "out"), Endpoint.At(and, "b")).Done);
            and = session.Run.Model.Layout.Parts.Single(p => p.Part is GatePart).Part.Id;
            layout = session.Run.Model.Layout;
            EditResult wired = session.Connect(Endpoint.At(and, "y"), Endpoint.At(Port("y"), "in"));
            Assert.IsTrue(wired.Done, wired.Refusal);

            string code = Code(session);
            StringAssert.DoesNotContain("|", code);
            StringAssert.Contains("g1 (.A(a), .B(b), .Z(y));", code, "the AND placed and wired, in the code as the cell it is");
        }

        /// <summary>
        /// A clock's period set in the inspector on the drawing changes the stimulus, which code written from the drawing does
        /// not carry: refused, saying where it is set, rather than reported done and lost (the review of #117).
        /// </summary>
        [Test]
        public void AClocksPeriodSetOnTheDrawing_IsRefused_NotLost()
        {
            var editor = new BoardEditor(Project.New("scratch"));
            Assert.IsTrue(editor.AddWrittenModule("ff",
                "module ff (input logic clk, input logic d, output logic q);\n    always_ff @(posedge clk) q <= d;\nendmodule\n").Done);
            Project project = editor.Project with
            {
                Top = "ff",
                Stimulus = new BitSorter.Board.Stimulus { Clocks = new[] { new ClockTrack { Port = "clk", Period = 10 } }, End = 100 },
            };
            var session = new WorkbenchSession(project, Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode);
            session.Remove(new int[0], new int[0]);
            session.AnswerRewrite(true);   // the question out of the way

            int clk = session.Run.Model.Layout.Ports.Single(p => p.Name.Split('[')[0] == "clk").Part;
            Inspection inspection = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: clk));
            Assert.IsNotNull(inspection, "sanity: the clock's port inspected on the drawing");
            EditResult set = session.Inspect(inspection, new Dictionary<string, string> { ["period"] = "40" });
            Assert.IsFalse(set.Done);
            Assert.AreEqual(WorkbenchSession.BeyondTheCodeRefusal, set.Refusal);
            Assert.AreEqual(10, session.Project.Stimulus.Clocks[0].Period, "nothing changed");
        }

        [Test]
        public void LockedOrWithGatesOn_TheDrawingTakesNoEdit_AndNothingIsAsked()
        {
            WorkbenchSession session = Written(Chooser, "pick");
            session.Locked = true;
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.Remove(new[] { Mux(session) }).Refusal);
            Assert.IsFalse(session.AsksToRewrite);

            session.Locked = false;
            session.DrawGates(true);
            Assert.AreEqual(WorkbenchSession.GatesRefusal, session.Remove(new[] { session.Run.Model.Layout.Parts.First().Part.Id }).Refusal);
            Assert.IsFalse(session.AsksToRewrite);
            Assert.AreEqual(Chooser, Code(session));
        }

        [Test]
        public void ADrawingThatIsNotItsCode_IsRefused_SayingWhy_WithNoQuestion()
        {
            WorkbenchSession session = Written(
                "module late (input logic a, output logic y);\n    assign #2 y = ~a;\nendmodule\n", "late");
            EditResult refused = session.Remove(new[] { session.Run.Model.Layout.Parts.First(p => p.Part is OperatorPart).Part.Id });
            Assert.IsFalse(refused.Done);
            StringAssert.Contains("delay on the assign to y", refused.Refusal);
            Assert.IsFalse(session.AsksToRewrite);
        }
    }
}
