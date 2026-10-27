using System;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A written block edited (Part 12d): stepping inside one shows its text; typing applies itself once it pauses, one
    /// undo step, and the run follows; text with an error is marked at its line and kept from the document; what the text
    /// would break outside itself is said; and an undo is followed.
    /// </summary>
    public class CodeDocumentTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        /// <summary>The written adder, its run made, stepped inside its block.</summary>
        private static WorkbenchSession Inside()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepInto(add));
            return session;
        }

        private static string Replace(string text, string from, string to)
        {
            StringAssert.Contains(from, text);
            return text.Replace(from, to);
        }

        /// <summary>
        /// The typing's own history (Part 14g): a burst of typing is one step back, an edit of whole lines one of its own, a
        /// step forward again after, and what it comes to applies once it pauses, as typing does; an undo of the document
        /// starts it afresh.
        /// </summary>
        [Test]
        public void CtrlZInTheEditor_StepsBackThroughTheTyping_ABurstAStep_AndAppliesAsTypingDoes()
        {
            WorkbenchSession session = Inside();
            CodeDocument code = session.Code;
            string start = code.Text;
            Assert.IsFalse(code.CanUndoTyping, "nothing typed yet");

            code.Type(start + "// a", 0);
            code.Type(start + "// ab", 300);
            code.Type(start + "// abc", 600);   // one burst
            code.Type(start + "// abc d", 2000);   // after a pause: a step of its own
            code.Edit(CodeEdits.Indent(code.Text, 0, 0).Text, 2100);
            string indented = code.Text;

            Assert.IsTrue(code.UndoTyping(2200));
            Assert.AreEqual(start + "// abc d", code.Text, "the indent was a step of its own");
            Assert.IsTrue(code.UndoTyping(2300));
            Assert.AreEqual(start + "// abc", code.Text, "the pause made a step");
            Assert.IsTrue(code.UndoTyping(2400));
            Assert.AreEqual(start, code.Text, "the burst was one step");
            Assert.IsFalse(code.UndoTyping(2500), "and nothing before it");

            Assert.IsTrue(code.RedoTyping(2600));
            Assert.AreEqual(start + "// abc", code.Text);
            code.Type(start + "// abcX", 2700);
            Assert.IsFalse(code.CanRedoTyping, "typing after a step back drops the steps forward");
            Assert.AreNotEqual(indented, code.Text);

            // What it comes to applies once it pauses, as typing does.
            Assert.IsTrue(code.Frame(2700 + CodeDocument.PauseMilliseconds + 1));
            Assert.AreEqual(start + "// abcX", code.Applied);
            Assert.IsTrue(code.CanUndoTyping, "applying keeps the typing's history");

            // The document undone under the editor: the text is not what the typing remembers.
            Assert.IsTrue(session.Undo());
            code.Frame(5000);
            Assert.AreEqual(start, code.Text, "the editor follows the document");
            Assert.IsFalse(code.CanUndoTyping, "and its typing starts afresh");
        }

        [Test]
        public void SteppingIntoAWrittenBlock_ShowsItsText_AndNothingDrawn()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsFalse(session.ShowsCode, "the top is drawn");
            Assert.IsNull(session.Code);

            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepInto(add));
            Assert.IsTrue(session.ShowsCode);
            CodeDocument code = session.Code;
            Assert.AreEqual("add4", code.Module);
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Text);
            Assert.IsFalse(code.Pending);
            Assert.IsEmpty(code.Marks);
            Assert.IsEmpty(session.Run.Model.Layout.Parts, "nothing is drawn inside written code");
            Assert.AreSame(code, session.Code, "the same text while the same module is shown");

            Assert.IsTrue(session.StepOut());
            Assert.IsNull(session.Code);
        }

        [Test]
        public void Typing_AppliesItselfOnceItPauses_AsOneStep_AndTheRunFollows()
        {
            WorkbenchSession session = Inside();
            CodeDocument code = session.Code;
            string xor = Replace(code.Text, "assign #3 {cout, s} = a + b;", "assign #3 {cout, s} = {1'b0, a ^ b};");

            code.Type(xor, 1000);
            Assert.IsTrue(code.Pending);
            Assert.IsFalse(code.Frame(1000 + CodeDocument.PauseMilliseconds / 2), "still typing");
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Applied, "nothing applied while typing goes on");

            Assert.IsTrue(code.Frame(1000 + CodeDocument.PauseMilliseconds));
            Assert.AreEqual(xor, code.Applied, "applied once it paused");
            Assert.IsFalse(code.Pending);
            Assert.IsTrue(session.Replaying, "and the run made again");

            session.Finish();
            Assert.IsTrue(session.StepOut(), "the top's rows, to read s from");
            WorkbenchModel model = session.Run.Model;
            int s = model.Layout.Rows.ToList().FindIndex(r => r.Label == "s[3:0]");
            Assert.GreaterOrEqual(s, 0);
            Track a = session.Project.Stimulus.Tracks.Single(t => t.Port == "a"), b = session.Project.Stimulus.Tracks.Single(t => t.Port == "b");
            Assert.IsTrue(a.Steps[2].Value.TryGetUInt64(out ulong x));
            Assert.IsTrue(b.Steps[2].Value.TryGetUInt64(out ulong y));
            model.Seek(a.Steps[2].Time + WorkbenchSamples.WrittenAdderStep - 0.01);
            Assert.IsTrue(model.RowValue(s, out _).TryGetUInt64(out ulong sum));
            Assert.AreEqual(x ^ y, sum, "the waves are the new code's");

            Assert.IsTrue(session.StepInto(session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id));
            code = session.Code;
            Assert.AreEqual(xor, code.Text);
            Assert.IsTrue(session.Undo());
            Assert.IsTrue(code.Frame(5000), "the editor follows the undo");
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Text, "one step back is the text before the typing");
        }

        [Test]
        public void TextWithAnError_IsMarkedAtItsLine_AndKeptFromTheDocument()
        {
            WorkbenchSession session = Inside();
            CodeDocument code = session.Code;
            int line = code.Text.Split('\n').ToList().FindIndex(l => l.Contains("assign")) + 1;
            string broken = Replace(code.Text, "a + b;", "a + ;");

            code.Type(broken, 0);
            Assert.IsTrue(code.Frame(CodeDocument.PauseMilliseconds));
            Assert.IsTrue(code.HasErrors);
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Applied, "the document keeps the text that worked");
            Assert.IsFalse(session.Replaying, "and the run is the one it had");
            CodeMark mark = code.Marks.First(m => m.IsError);
            Assert.AreEqual(line, mark.Line);
            StringAssert.StartsWith($"line {line}: ", code.FirstProblem);
            Assert.IsEmpty(code.Elsewhere, "a text that does not read has no pins for a while, and its block's wires are not listed for it");
            Assert.AreEqual(code.Text.Split('\n')[line - 1].Length >= mark.Column - 1, true);

            EditResult again = code.Apply();
            Assert.IsFalse(again.Done, "Ctrl+Enter is refused too, with the fault");
            StringAssert.Contains($"line {line}", again.Refusal);

            code.Type(Replace(code.Text, "a + ;", "a - b;"), 10_000);
            Assert.IsTrue(code.Apply().Done, "mended, Ctrl+Enter applies at once");
            Assert.IsEmpty(code.Marks.Where(m => m.IsError));
            Assert.IsFalse(code.Pending);
        }

        [Test]
        public void AHeaderThatWouldLeaveAWireWithoutItsPin_IsSaid_AndNotApplied()
        {
            WorkbenchSession session = Inside();
            CodeDocument code = session.Code;
            string noCarry = Replace(Replace(code.Text, "s,     // their sum\n    output logic         cout   // and its carry out", "s      // their sum"),
                "{cout, s}", "s");

            code.Type(noCarry, 0);
            Assert.IsFalse(code.Apply().Done);
            Assert.IsEmpty(code.Marks.Where(m => m.IsError), "the text itself is sound");
            Assert.IsNotEmpty(code.Elsewhere, "but the top's wire to cout would lose its pin");
            StringAssert.StartsWith("adder: ", code.Elsewhere[0]);
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Applied);

            code.Revert();
            Assert.AreEqual(WorkbenchSamples.AdderSource, code.Text);
            Assert.IsFalse(code.HasErrors);
        }

        [Test]
        public void AMarksPlace_IsAnOffsetInTheText()
        {
            WorkbenchSession session = Inside();
            CodeDocument code = session.Code;
            code.Type("ab\ncde\n\nf", 0);
            Assert.AreEqual(0, code.OffsetOf(1, 1));
            Assert.AreEqual(4, code.OffsetOf(2, 2));
            Assert.AreEqual(6, code.OffsetOf(2, 99), "no further than its line's end");
            Assert.AreEqual(7, code.OffsetOf(3, 1));
            Assert.AreEqual(9, code.OffsetOf(9, 1), "a line past the end is the end");
        }

        [Test]
        public void ANewWrittenBlock_StartsFromTheTemplate_AndIsAPartToPlace()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.AddWrittenModule("code1").Done);
            Assert.AreEqual(WrittenModules.Template("code1"), session.Project.Find("code1").Source);
            CatalogEntry entry = PartsCatalog.For(session.Project, session.Module).Single(e => e.Module == "code1");
            Assert.IsTrue(session.Place(entry, new Placement(20, 20)).Done);
            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.Undo());
            Assert.IsNull(session.Project.Find("code1"), "two steps: the module, then its block");
            Assert.IsFalse(session.AddWrittenModule("full_adder").Done, "a name taken");
        }
    }
}
