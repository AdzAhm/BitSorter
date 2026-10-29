using System;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Stepping into the instances written code makes (Part 15h-4), held to the code as it is now: from a block on the drawing
    /// of the code through the canvas, as a double-click does; through an undo, a redo or an edit that changes what an instance
    /// is or takes it away; and from the hierarchy, through a copy of a block array on the way (the review of #109).
    /// </summary>
    public class SteppingThroughCodeTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        private static string HalfAdder(string name) =>
            "module " + name + " (input logic a, b, output logic s, c);\n" +
            "    assign s = a ^ b;\n" +
            "    assign c = a & b;\n" +
            "endmodule\n";

        private static string FullAdder(string half) =>
            "module fa (input logic a, b, cin, output logic s, cout);\n" +
            "    logic s1, c1, c2;\n" +
            "    " + half + " h1 (.a(a), .b(b), .s(s1), .c(c1));\n" +
            "    " + half + " h2 (.a(s1), .b(cin), .s(s), .c(c2));\n" +
            "    assign cout = c1 | c2;\n" +
            "endmodule\n";

        private const string FullAdderOfGates =
            "module fa (input logic a, b, cin, output logic s, cout);\n" +
            "    assign s = a ^ b ^ cin;\n" +
            "    assign cout = a & b | cin & (a ^ b);\n" +
            "endmodule\n";

        /// <summary>A written full adder of two written half adders, ha, with a second half adder, hb, beside them.</summary>
        private static BoardEditor Written()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("ha", HalfAdder("ha")).Done);
            Assert.IsTrue(editor.AddWrittenModule("hb", HalfAdder("hb")).Done);
            Assert.IsTrue(editor.AddWrittenModule("fa", FullAdder("ha")).Done);
            return editor;
        }

        private static WorkbenchSession Session(Project project)
        {
            var session = new WorkbenchSession(project, Ticking(0.01));
            session.Finish();
            return session;
        }

        private static WorkbenchSession WrittenFullAdder()
        {
            BoardEditor editor = Written();
            EditResult top = editor.SetTop("fa");
            Assert.IsTrue(top.Done, top.Refusal);
            return Session(editor.Project);
        }

        private static PartLayout BlockOnTheDrawing(WorkbenchSession session, string name) =>
            session.Run.Model.Layout.Parts.Single(p => p.Part is BlockPart b && b.Name == name);

        private static Vec2 Middle(PartLayout part) =>
            part.ToStage((part.Local.Left + part.Local.Right) / 2, (part.Local.Bottom + part.Local.Top) / 2);

        /// <summary>A double-click on a block of the drawing finds the block where it is drawn, and steps inside it.</summary>
        [Test]
        public void ABlockOfTheDrawing_IsWhatThePointerIsOn_AndIsSteppedInto()
        {
            WorkbenchSession session = WrittenFullAdder();
            session.DrawCode(true);
            var editor = new CanvasEditor(session);
            editor.Relaid();

            PartLayout h1 = BlockOnTheDrawing(session, "h1");
            CanvasTarget target = editor.TargetAt(Middle(h1), 1f);
            Assert.AreEqual(CanvasTargetKind.Part, target.Kind, "the block, where it is drawn");
            Assert.AreEqual(h1.Part.Id, target.Part);
            Assert.IsTrue(session.CanStepInto(target.Part));
            Assert.IsTrue(editor.StepInto(target));
            CollectionAssert.AreEqual(new[] { "h1" }, session.Path);
            Assert.AreEqual("ha", session.Module);
        }

        /// <summary>
        /// The canvas made while the code is shown, then DRAWING pressed -- the session's step, not the canvas's -- and the
        /// drawing's block is what the pointer is on all the same, and a double-click steps into it (the 15h-4 Play Mode test).
        /// </summary>
        [Test]
        public void TheCanvasMadeOverTheCode_FindsTheDrawingsBlocks_OnceDrawingIsPressed()
        {
            WorkbenchSession session = WrittenFullAdder();
            session.DrawCode(false);
            var editor = new CanvasEditor(session);
            Assert.IsTrue(session.ShowsCode, "sanity: made over the code");

            session.DrawCode(true);
            PartLayout h1 = BlockOnTheDrawing(session, "h1");
            CanvasTarget target = editor.TargetAt(Middle(h1), 1f);
            Assert.AreEqual(CanvasTargetKind.Part, target.Kind, "the drawing's block, though the canvas did not take the step");
            Assert.AreEqual(h1.Part.Id, target.Part);
            editor.Do(new CanvasCommand(CanvasGesture.Select, target: target));
            CollectionAssert.AreEqual(new[] { h1.Part.Id }, editor.Selection.Parts.ToArray());

            // A step out by the session's hand leaves nothing of the drawing selected.
            Assert.IsTrue(session.StepToScope(session.Run.Model.Layout.DesignPath + ".h1"));
            editor.TargetAt(Middle(h1), 1f);   // the pointer moved, inside h1 now
            Assert.IsEmpty(editor.Selection.Parts, "what was selected was the module before's");
        }

        /// <summary>A drag on the drawing is refused as it begins, and a copy of it too; a click still selects.</summary>
        [Test]
        public void ADragOnTheDrawing_Begins_ButACopyIsRefused()
        {
            WorkbenchSession session = WrittenFullAdder();
            session.DrawCode(true);
            var editor = new CanvasEditor(session);
            editor.Relaid();

            PartLayout h1 = BlockOnTheDrawing(session, "h1");
            CanvasTarget target = editor.TargetAt(Middle(h1), 1f);
            editor.Do(new CanvasCommand(CanvasGesture.Select, target: target));
            CollectionAssert.AreEqual(new[] { h1.Part.Id }, editor.Selection.Parts.ToArray(), "selected");

            EditResult? begun = editor.Do(new CanvasCommand(CanvasGesture.BeginMove, from: h1.Placement.Position, target: target));
            Assert.IsFalse(begun.HasValue && !begun.Value.Done, "a drag on the drawing begins: it is arranged by hand (Part 15h-4f)");
            Assert.IsTrue(editor.Moving);

            Assert.IsNull(editor.Copy());
            StringAssert.StartsWith("The drawing of code is not copied", editor.Refusal);
        }

        /// <summary>An undo that changes which module an instance is of, while inside it: inside, the module the code now makes it of.</summary>
        [Test]
        public void AnUndoThatChangesWhatAnInstanceIs_ShowsTheModuleTheCodeNowMakesItOf()
        {
            WorkbenchSession session = WrittenFullAdder();
            EditResult made = session.SetSource(FullAdder("hb"));
            Assert.IsTrue(made.Done, made.Refusal);
            session.Finish();
            session.DrawCode(true);
            Assert.IsTrue(session.StepInto(BlockOnTheDrawing(session, "h1").Part.Id));
            Assert.AreEqual("hb", session.Module, "sanity: h1 is an hb");

            Assert.IsTrue(session.Undo());
            session.Finish();
            CollectionAssert.AreEqual(new[] { "h1" }, session.Path, "h1 is still there");
            Assert.AreEqual("ha", session.Module, "and an ha again");
            Assert.AreEqual("ha", session.CodeSteps[0]);
            StringAssert.EndsWith(".h1", session.Run.Model.Layout.InstancePath);
        }

        /// <summary>A redo that takes away the instance stepped into steps out of it.</summary>
        [Test]
        public void ARedoThatTakesTheInstanceAway_StepsOutOfIt()
        {
            WorkbenchSession session = WrittenFullAdder();
            Assert.IsTrue(session.SetSource(FullAdderOfGates).Done);
            session.Finish();
            Assert.IsTrue(session.Undo());
            session.Finish();
            session.DrawCode(true);
            Assert.IsTrue(session.StepInto(BlockOnTheDrawing(session, "h1").Part.Id));

            Assert.IsTrue(session.Redo());
            session.Finish();
            Assert.IsEmpty(session.Path, "no h1 to be inside");
            Assert.IsEmpty(session.CodeSteps);
            Assert.AreEqual("fa", session.Module);
        }

        /// <summary>A step from the hierarchy follows the code as it is now, not a step taken through it before.</summary>
        [Test]
        public void AStepFromTheHierarchy_FollowsTheCodeAsItIsNow()
        {
            WorkbenchSession session = WrittenFullAdder();
            session.DrawCode(true);
            Assert.IsTrue(session.StepInto(BlockOnTheDrawing(session, "h1").Part.Id));
            Assert.IsTrue(session.StepOut());
            session.DrawCode(false);
            Assert.IsTrue(session.SetSource(FullAdder("hb")).Done);
            session.Finish();

            string design = session.Run.Model.Layout.DesignPath;
            Assert.IsTrue(session.StepToScope(design + ".h1"));
            CollectionAssert.AreEqual(new[] { "h1" }, session.Path);
            Assert.AreEqual("hb", session.Module);
        }

        /// <summary>
        /// An instance in the code of a copy of a block array, stepped to from the hierarchy and from the drawing: one step for
        /// the copy, as an array is always stepped into, and its values read under the copy's one instance.
        /// </summary>
        [Test]
        public void AnInstanceInsideACopyOfABlockArray_IsSteppedTo_FromTheHierarchy_AsFromTheDrawing()
        {
            BoardEditor editor = Written();
            Assert.IsTrue(editor.AddModule("adders").Done);
            Assert.IsTrue(editor.Add("adders", new BlockPart { Name = "bits", Module = "fa", Copies = 3 }, new Placement(4, 0)).Done);
            Assert.IsTrue(editor.SetTop("adders").Done);
            WorkbenchSession session = Session(editor.Project);
            string design = session.Run.Model.Layout.DesignPath;

            Assert.IsTrue(session.StepToScope(design + ".bits[1]." + BoardCompiler.ArrayInstance + ".h1"));
            CollectionAssert.AreEqual(new[] { "bits[1]", "h1" }, session.Path);
            CollectionAssert.AreEqual(new[] { null, "ha" }, session.CodeSteps.ToArray());
            Assert.AreEqual("ha", session.Module);
            Assert.AreEqual(design + ".bits[1]." + BoardCompiler.ArrayInstance + ".h1", session.Run.Model.Layout.InstancePath);

            Assert.IsTrue(session.StepToScope(design));
            Assert.IsTrue(session.StepInto(session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id));
            session.DrawCode(true);
            Assert.IsTrue(session.StepInto(BlockOnTheDrawing(session, "h1").Part.Id));
            CollectionAssert.AreEqual(new[] { "bits[0]", "h1" }, session.Path);
            Assert.AreEqual(design + ".bits[0]." + BoardCompiler.ArrayInstance + ".h1", session.Run.Model.Layout.InstancePath);
        }

        /// <summary>A drawn block array inside a drawn module the code makes an instance of: a block step after a step through code.</summary>
        [Test]
        public void ABlockArrayInsideAnInstanceTheCodeMakes_IsSteppedTo_FromTheHierarchy()
        {
            BoardEditor editor = Written();
            Assert.IsTrue(editor.AddModule("pair").Done);
            Assert.IsTrue(editor.Add("pair", new BlockPart { Name = "bits", Module = "ha", Copies = 2 }, new Placement(4, 0)).Done);
            Assert.IsTrue(editor.AddWrittenModule("w", "module w;\n    pair p ();\nendmodule\n").Done);
            EditResult top = editor.SetTop("w");
            Assert.IsTrue(top.Done, top.Refusal);
            WorkbenchSession session = Session(editor.Project);
            string design = session.Run.Model.Layout.DesignPath;

            Assert.IsTrue(session.StepToScope(design + ".p.bits[1]." + BoardCompiler.ArrayInstance));
            CollectionAssert.AreEqual(new[] { "p", "bits[1]" }, session.Path);
            CollectionAssert.AreEqual(new[] { "pair", null }, session.CodeSteps.ToArray());
            Assert.AreEqual("ha", session.Module);
            Assert.AreEqual(design + ".p.bits[1]." + BoardCompiler.ArrayInstance, session.Run.Model.Layout.InstancePath);
        }
    }
}
