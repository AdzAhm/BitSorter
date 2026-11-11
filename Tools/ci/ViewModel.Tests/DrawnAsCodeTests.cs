using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A drawn circuit shown as its code (5.1, Ahmad: "just a button to switch to the code and edit it", everywhere): CODE shows
    /// it as it prints and changes nothing; the first text typed into it that applies makes it a written module, one step that
    /// Ctrl+Z takes back; text that does not apply is a draft, the circuit still drawn; and DRAWING before anything applies
    /// goes back to the drawing as it was.
    /// </summary>
    public class DrawnAsCodeTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        /// <summary>The full adder sample, its run finished: a drawn top of two half adder blocks and an OR, every module drawn.</summary>
        private static WorkbenchSession FullAdder()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsFalse(session.Project.TopModule.IsWritten, "sanity: drawn");
            return session;
        }

        /// <summary>The code typed and applied at once, as Ctrl+Enter applies it.</summary>
        private static EditResult TypeAndApply(WorkbenchSession session, string text)
        {
            CodeDocument code = session.Code;
            Assert.IsNotNull(code, "sanity: the code shown");
            code.Type(text, 0);
            return code.Apply();
        }

        [Test]
        public void CodeOnADrawnCircuit_ShowsItAsItPrints_AndChangesNothing()
        {
            WorkbenchSession session = FullAdder();
            Project before = session.Project;
            Assert.IsTrue(session.CanShowCode, "a drawn circuit can be shown as code");
            Assert.IsFalse(session.ShowsCode, "sanity: it opens as its drawing");

            session.DrawCode(false);

            Assert.IsTrue(session.ShowsCode && session.DrawnAsCode, "CODE: its code");
            CodeDocument code = session.Code;
            Assert.IsNotNull(code);
            StringAssert.StartsWith("module full_adder", code.Text, "the module as it prints, without the comment that says it was drawn");
            StringAssert.Contains("half_adder h1", code.Text, "its blocks instances of their modules");
            int file = session.Listing.FileOf("full_adder");
            StringAssert.EndsWith(code.Text, session.Listing.Files[file].Text, "the text the CODE tab shows");
            Assert.IsFalse(code.Pending, "nothing typed yet");
            Assert.AreSame(before, session.Project, "and nothing changed");

            session.DrawCode(true);
            Assert.IsFalse(session.ShowsCode, "DRAWING: the drawing again");
            Assert.AreSame(before, session.Project, "as it was, layout and all");
        }

        [Test]
        public void TheFirstTextThatApplies_MakesItWritten_OneStep_ThatUndoTakesBack()
        {
            WorkbenchSession session = FullAdder();
            Module drawn = session.Project.TopModule;
            session.DrawCode(false);
            string typed = "// my full adder, now in code\n" + session.Code.Text;

            EditResult applied = TypeAndApply(session, typed);

            Assert.IsTrue(applied.Done, applied.Refusal);
            Module written = session.Project.TopModule;
            Assert.IsTrue(written.IsWritten, "written now");
            Assert.AreEqual(typed, written.Source, "as typed");
            Assert.AreEqual(drawn.NextId, written.NextId, "its id count kept, so no id is ever given twice");
            Assert.AreEqual(1, session.DrawnWritten);
            Assert.IsTrue(session.ShowsCode && !session.DrawnAsCode, "still its code on screen, a written module's now");
            Assert.IsFalse(session.Code.Pending, "and nothing left to apply");
            session.Finish();
            Assert.Greater(session.Run.Model.End, 0, "the run made again from the code");

            Assert.IsTrue(session.Undo(), "one step");
            Assert.AreSame(drawn, session.Project.TopModule, "Ctrl+Z: the drawn circuit back, layout and all");
            session.DrawCode(true);
            Assert.IsFalse(session.ShowsCode, "and DRAWING shows it");
        }

        [Test]
        public void ANewCircuitShownAsCode_OpensOnTheTemplate_AndIsWrittenFromIt()
        {
            var session = new WorkbenchSession(WorkbenchSamples.Blank(), Ticking(0.01));
            session.DrawCode(false);
            Assert.AreEqual(WrittenModules.Template("my_circuit"), session.Code.Text, "nothing drawn yet: the starter + CODE gives a block");

            EditResult applied = TypeAndApply(session, "module my_circuit (input logic a, input logic b, output logic sum, output logic carry);\n" +
                "    xor #1 g1 (sum, a, b);\n    and #1 g2 (carry, a, b);\nendmodule\n");

            Assert.IsTrue(applied.Done, applied.Refusal);
            Assert.IsTrue(session.Project.TopModule.IsWritten, "a new circuit written in code, no block placed");
        }

        [Test]
        public void TextThatDoesNotApply_IsADraft_TheCircuitStillDrawn_AndComesBackInTheNextSession()
        {
            WorkbenchSession session = FullAdder();
            session.DrawCode(false);
            string broken = session.Code.Text.Replace("endmodule", "");

            EditResult applied = TypeAndApply(session, broken);

            Assert.IsFalse(applied.Done, "it does not read");
            Assert.IsFalse(session.Project.TopModule.IsWritten, "so the circuit is still drawn");
            Assert.AreEqual(broken, session.Drafts["full_adder"], "and the text is its draft, for the save to keep");

            var next = new WorkbenchSession(session.Project, Ticking(0.01), drafts: session.Drafts);
            Assert.IsTrue(next.ShowsCode, "the next session opens on its code");
            Assert.AreEqual(broken, next.Code.Text, "the draft in its editor");
            Assert.IsFalse(next.Project.TopModule.IsWritten);
        }

        [Test]
        public void AnEditOfTheDrawing_SetsTheDraftAside()
        {
            WorkbenchSession session = FullAdder();
            session.DrawCode(false);
            string broken = session.Code.Text.Replace("endmodule", "");
            TypeAndApply(session, broken);
            Assert.AreEqual(1, session.Drafts.Count, "sanity: a draft");

            session.DrawCode(true);
            int or = session.Project.TopModule.Parts.OfType<GatePart>().Single().Id;
            EditResult removed = session.Remove(new[] { or });
            Assert.IsTrue(removed.Done, removed.Refusal);

            Assert.AreEqual(0, session.Drafts.Count, "typed against the drawing before: set aside, never applied over the edit");
            session.DrawCode(false);
            StringAssert.DoesNotContain(" or ", session.Code.Text, "the code shown is the drawing's now, its OR gone");
        }

        [Test]
        public void ApplyingWhatIsTyped_AsExportAndCheckDo_MakesADrawnModuleWritten()
        {
            WorkbenchSession session = FullAdder();
            session.DrawCode(false);
            string typed = "// typed, within the pause\n" + session.Code.Text;
            session.Code.Type(typed, 0);
            Assert.IsTrue(session.Code.Pending, "sanity: typed and not applied");

            EditResult applied = session.ApplyTyping(out string module);

            Assert.IsTrue(applied.Done, applied.Refusal);
            Assert.IsNull(module);
            Assert.AreEqual(typed, session.Project.TopModule.Source, "what EXPORT and CHECK take is what was typed");
        }

        [Test]
        public void ADrawnCircuitShownAsCode_TakesNoEditOfItsDrawing()
        {
            WorkbenchSession session = FullAdder();
            session.DrawCode(false);
            int or = session.Project.TopModule.Parts.OfType<GatePart>().Single().Id;

            EditResult removed = session.Remove(new[] { or });

            Assert.IsFalse(removed.Done, "the drawing behind the code is not edited unseen");
            Assert.AreEqual(WorkbenchSession.CodeShownRefusal, removed.Refusal);
            Assert.AreEqual(WorkbenchSession.CodeShownRefusal, session.WhyNotChange, "MAKE BLOCK refuses before it asks a name");
            Assert.IsFalse(session.CanTidy, "and TIDY is not offered over code");
        }

        [Test]
        public void Locked_ItsCodeIsShown_AndWhatIsTypedIsRefused()
        {
            WorkbenchSession session = FullAdder();
            session.Locked = true;
            session.DrawCode(false);
            Assert.IsTrue(session.ShowsCode, "a locked circuit's code can be read");

            EditResult applied = TypeAndApply(session, "// typed\n" + session.Code.Text);

            Assert.IsFalse(applied.Done);
            Assert.AreEqual(WorkbenchSession.LockedRefusal, applied.Refusal, "refused saying how to unlock it");
            Assert.IsFalse(session.Project.TopModule.IsWritten);
        }

        [Test]
        public void WhereCodeIsNotAllowed_ADrawnCircuitHasNoCode()
        {
            WorkbenchSession session = FullAdder();
            session.CodeAllowed = false;

            Assert.IsFalse(session.CanShowCode, "no CODE switch");
            session.DrawCode(false);
            Assert.IsFalse(session.ShowsCode, "and none shown");
            Assert.IsNull(session.Code);
        }

        [Test]
        public void ADrawnModuleWrittenAsTheSessionSays_ReadsAsThat()
        {
            const string Header = "// the exercise's header: what the answer may be built of\n";
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01),
                writtenAs: (project, module) => module == project.Top ? Header + "module full_adder (input logic a, input logic b, input logic cin, output logic s, output logic cout);\nendmodule\n" : null);
            session.DrawCode(false);

            StringAssert.StartsWith(Header, session.Code.Text, "an exercise's answer reads under its header, as WRITE IT wrote it");
        }

        [Test]
        public void ABlockSteppedInto_BecomesCodeAlone_ItsBlocksStillItsInstances()
        {
            WorkbenchSession session = FullAdder();
            int h1 = session.Project.TopModule.Parts.OfType<BlockPart>().First().Id;
            Assert.IsTrue(session.StepInto(h1));
            Assert.AreEqual("half_adder", session.Module, "sanity: inside the half adder");
            session.DrawCode(false);

            EditResult applied = TypeAndApply(session, "// the half adder, in code\n" + session.Code.Text);

            Assert.IsTrue(applied.Done, applied.Refusal);
            Assert.IsTrue(session.Project.Find("half_adder").IsWritten, "the module stepped into, written");
            Assert.IsFalse(session.Project.TopModule.IsWritten, "the top still drawn");
            CollectionAssert.AreEqual(new[] { "half_adder", "half_adder" },
                session.Project.TopModule.Parts.OfType<BlockPart>().Select(b => b.Module), "its blocks still instances of it");
            session.Finish();
            Assert.Greater(session.Run.Model.End, 0, "and the run made again");
        }
    }
}
