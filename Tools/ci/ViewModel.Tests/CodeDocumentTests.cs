using System;
using System.Collections.Generic;
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

        /// <summary>The written adder, its run made, stepped inside its block and CODE chosen: a written block opens as its drawing (Part 15h-4d).</summary>
        private static WorkbenchSession Inside()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepInto(add));
            session.DrawCode(false);
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

        /// <summary>
        /// A burst of typing that comes back to the text it began from -- a letter typed and taken out -- is no step: Ctrl+Z
        /// passes over it rather than doing nothing and moving the caret (the 14g review).
        /// </summary>
        [Test]
        public void ABurstThatComesBackToWhereItBegan_IsNoStep()
        {
            CodeDocument code = Inside().Code;
            string start = code.Text;
            code.Type(start + "x", 0);
            code.Type(start, 200);
            Assert.IsFalse(code.UndoTyping(300), "nothing to go back to: the text is where it began");
            Assert.AreEqual(start, code.Text);

            code.Type(start + "y", 2000);
            code.Type(start + "yz", 4000);
            code.Type(start + "y", 4100);
            Assert.IsTrue(code.UndoTyping(4200));
            Assert.AreEqual(start, code.Text, "the step back to \"y\", the text as it is, is passed over");
        }

        /// <summary>
        /// A written block opens as its drawing (Part 15h-4d: Ahmad wanted the drawing first, CODE a click away), and CODE
        /// shows its text, nothing drawn.
        /// </summary>
        [Test]
        public void SteppingIntoAWrittenBlock_ShowsItsDrawing_AndCodeItsText_NothingDrawn()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsFalse(session.ShowsCode, "the top is drawn");
            Assert.IsNull(session.Code);

            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            Assert.IsTrue(session.StepInto(add));
            Assert.IsTrue(session.ShowsDrawingOfCode, "the drawing first");
            Assert.IsNull(session.Code);
            session.DrawCode(false);
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
            Assert.IsTrue(session.ShowsCode, "CODE, chosen for add4, is remembered");
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

        /// <summary>
        /// Text typed with an error never applies, and stays the editor's: stepping out of the block and in again shows it as
        /// it was left, marked, never the last code that applied (the 16d sweep: it was dropped without a word, and so was text
        /// typed within the pause, which applies once the code is shown again and the pause is over).
        /// </summary>
        [Test]
        public void TextTyped_IsKept_AcrossAStepOutAndInAgain()
        {
            WorkbenchSession session = Inside();
            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            CodeDocument code = session.Code;
            string applied = code.Applied;
            string broken = Replace(code.Text, "= a + b;", "= a + ;");
            code.Type(broken, 0);
            code.Frame(CodeDocument.PauseMilliseconds + 1);
            Assert.IsTrue(code.HasErrors, "sanity: an error, so it never applies");

            Assert.IsTrue(session.StepOut());
            Assert.IsNull(session.Code, "the top is drawn");
            Assert.IsTrue(session.StepInto(add));
            Assert.IsTrue(session.ShowsCode, "sanity: CODE, as it was chosen for it");
            Assert.AreEqual(broken, session.Code.Text, "the text typed is there again, never lost on stepping out");
            Assert.IsTrue(session.Code.Pending && session.Code.HasErrors, "and marked as not applied");
            Assert.AreEqual(applied, session.Project.Find(session.Module).Source, "the document keeps the last code that worked");

            // Typed without one, and stepped out of within the pause: kept too, and applied once it is shown again.
            string fixedUp = Replace(broken, "= a + ;", "= b + a;");
            session.Code.Type(fixedUp, 2000);
            Assert.IsTrue(session.StepToScope(session.Run.Model.Layout.DesignPath));
            Assert.IsTrue(session.StepInto(add));
            Assert.AreEqual(fixedUp, session.Code.Text, "within the pause: kept");
            Assert.IsTrue(session.Code.Frame(2000 + CodeDocument.PauseMilliseconds + 1));
            Assert.AreEqual(fixedUp, session.Project.Find(session.Module).Source, "and applied once the pause is over");
        }

        /// <summary>
        /// What is typed and not applied is the session's drafts, by module (Ahmad, 2026-11-03: kept in the save): a session
        /// opened on them shows each in its module's code, marked, and one with no error applies once it is shown.
        /// </summary>
        [Test]
        public void TheDrafts_AreWhatIsTypedAndNotApplied_AndASessionOpensOnThem()
        {
            WorkbenchSession session = Inside();
            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            CodeDocument code = session.Code;
            Assert.IsEmpty(session.Drafts, "nothing typed");
            string broken = Replace(code.Text, "= a + b;", "= a + ;");
            int version = session.DraftsVersion;
            code.Type(broken, 0);
            Assert.AreNotEqual(version, session.DraftsVersion, "typing changes the drafts, for the workbench to keep");
            Assert.IsTrue(session.StepOut());
            CollectionAssert.AreEquivalent(new Dictionary<string, string> { ["add4"] = broken }, session.Drafts, "kept, stepped out of");

            var reopened = new WorkbenchSession(session.Project, Ticking(0.01), drafts: session.Drafts);
            reopened.Finish();
            Assert.IsTrue(reopened.StepInto(add));
            Assert.IsTrue(reopened.ShowsCode, "a module with a draft opens on its code, to be seen");
            Assert.AreEqual(broken, reopened.Code.Text);
            Assert.IsTrue(reopened.Code.Pending && reopened.Code.HasErrors, "marked, not applied");
            reopened.Code.Frame(0);
            Assert.AreEqual(WorkbenchSamples.AdderSource, reopened.Project.Find("add4").Source, "with an error, it never applies");

            string fixedUp = Replace(broken, "= a + ;", "= b + a;");
            var clean = new WorkbenchSession(session.Project, Ticking(0.01),
                drafts: new Dictionary<string, string> { ["add4"] = fixedUp, ["nothing"] = "x", ["adder"] = "not written" });
            clean.Finish();
            Assert.IsTrue(clean.StepInto(add));
            Assert.IsTrue(clean.Code.Frame(0), "its pause long over");
            Assert.AreEqual(fixedUp, clean.Project.Find("add4").Source, "with none, applied as it is shown");
            Assert.IsEmpty(clean.Drafts, "and a draft of a module there is not, or that is drawn, is no draft");
        }

        /// <summary>
        /// What EXPORT, the hand-in, the lock and CHECK do first: everything typed and not applied, on screen or not, applied --
        /// or refused at the first that does not apply, saying whose it is, and nothing changed.
        /// </summary>
        [Test]
        public void ApplyingTheTyping_AppliesEveryModulesText_OrRefusesSayingWhose()
        {
            WorkbenchSession session = Inside();
            int add = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;
            string broken = Replace(session.Code.Text, "= a + b;", "= a + ;");
            session.Code.Type(broken, 0);
            Assert.IsTrue(session.StepOut());

            EditResult refused = session.ApplyTyping(out string whose);
            Assert.IsFalse(refused.Done);
            Assert.AreEqual("add4", whose);
            StringAssert.Contains("line", refused.Refusal, "what is wrong with it, at its line");
            Assert.AreEqual(WorkbenchSamples.AdderSource, session.Project.Find("add4").Source, "nothing changed");

            Assert.IsTrue(session.StepInto(add));
            string fixedUp = Replace(broken, "= a + ;", "= b + a;");
            session.Code.Type(fixedUp, 0);
            Assert.IsTrue(session.StepOut());
            Assert.IsTrue(session.ApplyTyping(out whose).Done, "applied, stepped out of or not");
            Assert.IsNull(whose);
            Assert.AreEqual(fixedUp, session.Project.Find("add4").Source);
            Assert.IsEmpty(session.Drafts, "nothing typed left");
            Assert.IsTrue(session.ApplyTyping(out _).Done, "and with nothing typed, nothing to do");
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

        /// <summary>
        /// Typed code is applied as BitSorter loses the focus in a desktop build alone (the review of Part 16d's sweep): in the
        /// editor a test run's focus comes and goes, and in a browser the focus may go to the code editor's own box over the
        /// canvas, so a click into it would have applied code typed a moment before, or put a refusal on the chip.
        /// </summary>
        [Test]
        public void TypedCode_IsAppliedAsTheFocusGoes_InADesktopBuildAlone()
        {
            Assert.IsTrue(CodeDocument.AppliedAsTheFocusGoes(inEditor: false, inBrowser: false), "a desktop build put behind another window");
            Assert.IsFalse(CodeDocument.AppliedAsTheFocusGoes(inEditor: true, inBrowser: false), "the editor");
            Assert.IsFalse(CodeDocument.AppliedAsTheFocusGoes(inEditor: true, inBrowser: true), "the editor, a browser build chosen");
            Assert.IsFalse(CodeDocument.AppliedAsTheFocusGoes(inEditor: false, inBrowser: true), "a browser build");
        }
    }
}
