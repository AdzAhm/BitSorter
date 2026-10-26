using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// MAKE BLOCK on the canvas (Part 12b): the parts selected made into a block of the name given, through the session --
    /// drawn at once, replayed, the run doing exactly what it did -- the new block selected, and one undo taking it back.
    /// </summary>
    public class MakeBlockTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        /// <summary>The full adder's outputs over the whole run, as settled changes: what must not change.</summary>
        private static string[] Outputs(WorkbenchSession session)
        {
            string design = BoardCompiler.Compile(session.Project).DesignPath;
            SimSession run = session.Run.Model.Session;
            var seen = new List<string>();
            foreach (string port in new[] { "s", "cout" })
            {
                Assert.IsTrue(run.Design.TryFind(design + "." + port, out SignalId signal), $"the run has {port}");
                seen.AddRange(run.History.SettledChanges(signal).Select(c => $"{port} {c.Time} {c.Value.Format(Radix.Binary)}"));
            }

            return seen.ToArray();
        }

        [Test]
        public void TheSelectionMadeIntoABlock_IsSelected_RunsAsItDid_AndOneUndoTakesItBack()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            string[] before = Outputs(session);
            Project was = session.Project;
            Module top = session.Project.TopModule;
            int h2 = top.FindPart("h2").Id, or = top.Parts.Single(p => p is GatePart).Id;

            var editor = new CanvasEditor(session);
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: h2), false, false);
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: or), extend: true, toggle: false);
            Assert.AreEqual("block1", editor.FreeBlockName, "the name offered");

            EditResult made = editor.MakeBlock("tail");
            Assert.IsTrue(made.Done, made.Refusal);
            Assert.IsNull(editor.Refusal);
            Assert.IsInstanceOf<BlockPart>(session.Project.TopModule.FindPart(made.Id), "an instance stands where they were");
            CollectionAssert.AreEquivalent(new[] { made.Id }, editor.Selection.Parts, "and is what is selected");
            Module tail = session.Project.Find("tail");
            Assert.IsNotNull(tail, "a module called tail");
            Assert.AreEqual(2, tail.Parts.Count(p => !(p is PortPart)), "of h2 and the OR");
            Assert.IsTrue(session.Replaying, "the circuit is replayed");

            session.Finish();
            CollectionAssert.AreEqual(before, Outputs(session), "and does exactly what it did");

            Assert.IsTrue(session.Undo());
            Assert.AreSame(was, session.Project, "one undo, and the document is as it was");
        }

        [Test]
        public void APortSelected_IsRefused_InTheDocumentsWords_AndChangesNothing()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            Project was = session.Project;
            Module top = session.Project.TopModule;
            var editor = new CanvasEditor(session);
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: top.FindPart("a").Id), false, false);
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: top.FindPart("h1").Id), extend: true, toggle: false);

            EditResult made = editor.MakeBlock("front");
            Assert.IsFalse(made.Done);
            StringAssert.Contains("port", editor.Refusal, "said in the document's words");
            Assert.AreSame(was, session.Project, "and nothing changed");
            Assert.AreEqual(2, editor.Selection.Parts.Count, "the selection still there to put right");

            editor.Selection.Clear();
            Assert.IsFalse(editor.MakeBlock("nothing").Done, "nothing selected is nothing to make");
        }
    }
}
