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

        /// <summary>
        /// The code looked at, then the drawing edited -- and the edit undone -- with the drawing up: nothing was typed, so
        /// nothing is left for EXPORT, the hand-in or the lock to apply, and the code is the edit's. The editor's text was
        /// kept from the look and never followed the document while the drawing was up, so each of them put the code from
        /// before the edit back over it (the 16d sweep).
        /// </summary>
        [Test]
        public void TheCodeLookedAt_ThenTheDrawingEdited_LeavesNothingToApplyOverTheEdit()
        {
            WorkbenchSession session = Written(Chooser, "pick");
            session.DrawCode(false);
            Assert.IsNotNull(session.Code, "sanity: its code");

        /// An edit of the drawing after a look at its code leaves no code pending (the review of Part 16d's sweep): the
        /// editor's text, kept while the drawing is up, is the code before the edit, and the editor sets it aside as it next
        /// shows. Applying it -- as leaving a circuit, CHECK, EXPORT and the lock do -- wrote the old code back over the edit.
        /// </summary>
        [Test]
        public void AnEditOfTheDrawing_AfterALookAtItsCode_LeavesNoCodePending()
        {
            WorkbenchSession session = Written(Chooser, "pick");
            session.DrawCode(false);
            CodeDocument code = session.Code;   // CODE looked at: its text is kept while the drawing is up
            Assert.IsNotNull(code, "sanity: CODE shows its text");
            session.DrawCode(true);
            Assert.IsNull(session.PendingCode, "sanity: nothing typed");

            session.Remove(new[] { Mux(session) });
            Assert.IsTrue(session.AnswerRewrite(true).Done);
            string edited = Code(session);
            Assert.AreNotEqual(Chooser, edited, "sanity: the edit is written into the code");
            Assert.IsNull(session.PendingCode, "nothing typed: nothing to apply over the edit");

            Assert.IsTrue(session.Undo());
            Assert.AreEqual(Chooser, Code(session), "sanity: undone");
            Assert.IsNull(session.PendingCode, "nor over its undo");
            session.DrawCode(false);
            Assert.AreEqual(Chooser, session.Code.Text, "and the code shown again is the document's");
        }

        /// <summary>
        /// The question asked of a block's drawing of code, answered after Alt+Up has stepped out of it -- which closes no
        /// question and changes no document -- makes nothing: the edit was the drawing's, and its ids are not the module now
        /// shown's (the 16d sweep: it was made on the parent, deleting whichever of its parts had the ids).
        /// </summary>
        [Test]
        public void TheQuestion_AnsweredAfterAStepOut_MakesNothing()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("pick", Chooser).Done);
            Assert.IsTrue(editor.Add("top", new BlockPart { Name = "p1", Module = "pick" }, new Placement(5, 0)).Done);
            var session = new WorkbenchSession(editor.Project, Ticking(0.01));
            session.Finish();
            Module top = session.Project.TopModule;
            Assert.IsTrue(session.StepInto(top.Parts.OfType<BlockPart>().Single().Id));
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the block's drawing of code");
            int both = session.DrawnModule.Parts.First(p => top.FindPart(p.Id) != null).Id;   // an id the parent's parts have too

            Assert.AreEqual(WorkbenchSession.RewriteAsked, session.Remove(new[] { both }).Refusal);
            Assert.IsTrue(session.StepOut());
            Project before = session.Project;
            EditResult answered = session.AnswerRewrite(true);
            Assert.IsFalse(answered.Done, "nothing made: the edit was the drawing's");
            Assert.AreSame(before, session.Project, "the parent untouched");
            Assert.AreEqual(Chooser, session.Project.Find("pick").Source, "and the code too");
            Assert.IsFalse(session.AsksToRewrite);
        }

            session.Remove(new[] { Mux(session) });
            Assert.IsTrue(session.AnswerRewrite(true).Done);
            string written = Code(session);
            StringAssert.DoesNotContain("?", written, "sanity: the mux taken out of the code");

            Assert.IsNull(session.PendingCode, "the drawing's edit is the document's, and nothing typed is left over it");
            Assert.IsFalse(code.Pending);
            Assert.IsTrue(code.Apply().Done);
            Assert.AreEqual(written, Code(session), "applying the editor's text puts nothing back");
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
        /// A paste on the drawing of code is written into the code and the drawing made again, every part numbered afresh: what
        /// it hands back to be selected is never the ids of the drawing it was made on, which name other parts in the new one
        /// -- a Delete or a turn straight after acted on them (the 16d sweep).
        /// </summary>
        [Test]
        public void APasteOnTheDrawingOfCode_HandsBackNoIdOfTheDrawingBefore()
        {
            WorkbenchSession session = Written(
                "module both (input logic a, b, output logic y);\n    assign y = a | b;\nendmodule\n", "both");
            session.Remove(new int[0], new int[0]);
            session.AnswerRewrite(true);   // the question out of the way

            var other = new BoardEditor(Project.New("other"));
            EditResult and = other.Add("other", new GatePart { Kind = Sim.GateKind.And, Rise = 1, Fall = 1 }, new Placement(0, 0));
            Assert.IsTrue(and.Done, and.Refusal);
            Clip clip = Clipboard.Copy(other.Project, "other", new[] { and.Id });
            var parts = new List<int>();
            var wires = new List<int>();
            EditResult pasted = session.Paste(clip, parts, wires);
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            StringAssert.Contains("g1 (", Code(session), "sanity: the AND written into the code");

            Module drawn = session.DrawnModule;
            foreach (int id in parts)
                Assert.IsTrue(drawn.FindPart(id) is GatePart gate && gate.Kind == Sim.GateKind.And,
                    $"{id} handed back to be selected is the AND pasted, not another part of the drawing made again");
            Assert.IsNotEmpty(parts, "the AND, found again in the drawing made again");
            Assert.IsEmpty(wires, "and no wire, for none was pasted");
        }

        /// <summary>
        /// A part placed, or a wire drawn, that asks the question first is made by its answer, and is let go of as one made is:
        /// the gate stayed in hand, and the next click placed a second; the wire went on being drawn from its pin (the 16d sweep).
        /// </summary>
        [Test]
        public void APlaceOrAWire_ThatAsksFirst_IsLetGoOf_ForTheAnswerToMake()
        {
            WorkbenchSession session = Written(Chooser, "pick");
            var editor = new CanvasEditor(session);
            editor.Take(PartsCatalog.Find(session.Project, session.Module, "and"));
            EditResult? placed = editor.Do(new CanvasCommand(CanvasGesture.Place, new Point(6, 6)));
            Assert.AreEqual(WorkbenchSession.RewriteAsked, placed?.Refusal, "sanity: the question asked");
            Assert.IsNull(editor.InHand, "nothing left in hand for the next click to place a second");
            Assert.IsTrue(session.AnswerRewrite(true).Done);
            editor.Relaid();
            Assert.AreEqual(1, session.Run.Model.Layout.Parts.Count(p => p.Part is GatePart), "placed once, by the answer");

            session = Written("module two (input logic a, output logic y, z);\n    assign y = a;\nendmodule\n", "two");
            editor = new CanvasEditor(session);
            int Port(string name) => session.Run.Model.Layout.Ports.Single(p => p.Name.Split('[')[0] == name).Part;
            editor.Do(new CanvasCommand(CanvasGesture.BeginWire, target: new CanvasTarget(CanvasTargetKind.Pin, part: Port("a"), pin: "out")));
            Assert.IsTrue(editor.Drafting, "sanity: a wire drawn from a");
            EditResult? wired = editor.Do(new CanvasCommand(CanvasGesture.EndWire, target: new CanvasTarget(CanvasTargetKind.Pin, part: Port("z"), pin: "in")));
            Assert.AreEqual(WorkbenchSession.RewriteAsked, wired?.Refusal, "sanity: the question asked");
            Assert.IsFalse(editor.Drafting, "the wire let go of, for the answer to make");
        }

        /// <summary>
        /// What a paste on the drawing of code makes is not selected by the numbers the paste gave it: the code is written from
        /// the drawing and the drawing made again, its parts numbered afresh, and those numbers were other parts of it -- which
        /// a Delete straight after took out of the code (the 16d sweep).
        /// </summary>
        [Test]
        public void APasteOnTheDrawingOfCode_SelectsNoneOfTheDrawingsOtherParts()
        {
            // More parts than the drawing has wires, so the numbers the paste gives them reach the drawing's own parts.
            var source = new BoardEditor(Project.New("from"));
            var gates = new List<int>();
            for (int i = 0; i < 6; i++)
                gates.Add(source.Add("from", new GatePart { Kind = BitSorter.Sim.GateKind.And }, new Placement(4 * i, 0)).Id);
            Assert.IsTrue(source.Connect("from", Endpoint.At(gates[0], "y"), Endpoint.At(gates[1], "a")).Done);
            Clip clip = Clipboard.Copy(source.Project, "from", gates);
            Assert.IsNotNull(clip, "sanity: six ANDs and a wire to paste");

            WorkbenchSession session = Written(Chooser, "pick");
            session.Remove(new int[0], new int[0]);
            session.AnswerRewrite(true);   // the question out of the way
            var editor = new CanvasEditor(session);
            EditResult pasted = editor.Paste(clip);
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            editor.Relaid();

            Module drawn = session.DrawnModule;
            bool IsAnd(int part) => drawn.FindPart(part) is GatePart and && and.Kind == BitSorter.Sim.GateKind.And;
            foreach (int part in editor.Selection.Parts)
                Assert.IsTrue(IsAnd(part), $"selected: {drawn.FindPart(part)?.GetType().Name ?? "nothing"} {part}, which the paste did not make");
            foreach (int id in editor.Selection.Wires)
            {
                Wire wire = drawn.FindWire(id);
                Assert.IsTrue(wire != null && !wire.From.IsTap && !wire.To.IsTap && IsAnd(wire.From.Pin.Part) && IsAnd(wire.To.Pin.Part),
                    $"selected: wire {id}, from {wire?.From} to {wire?.To}, which the paste did not make");
            }
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

        /// <summary>
        /// A part the drawing makes up says what it does in the inspector, by what it is and not its made-up name, and a wire
        /// out of one no signal holds goes by its expression (Part 15h-4e, Ahmad's picture of 2026-10-29: "$p4_y from $p4.y").
        /// </summary>
        [Test]
        public void APartTheDrawingMakesUp_SaysWhatItDoes_AndItsWireGoesByItsExpression()
        {
            WorkbenchSession session = Written(
                "module m (input logic [1:0] a, b, c, output logic [1:0] y);\n    assign y = (a & b) | c;\nendmodule\n", "m");
            OperatorPart and = session.DrawnModule.Parts.OfType<OperatorPart>().Single(p => p.Op == OperatorKind.And);

            Inspection inspection = Inspector.Of(session.DrawnProject, session.Module, new CanvasTarget(CanvasTargetKind.Part, part: and.Id), session.DescribePart);
            Assert.AreEqual("OPERATOR", inspection.Title, "by what it is, not $p");
            Assert.AreEqual("a AND b, bit by bit: 2 bits.", inspection.Says);

            NetLayout wire = session.Run.Model.Layout.Nets.Single(n => n.Source.Part == and.Id);
            Assert.AreEqual("a & b [1:0]   from the & to the |'s a", wire.Says);
            StringAssert.DoesNotContain("$p", wire.Says);
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
