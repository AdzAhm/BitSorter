using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The transcript (Part 11c): the document's diagnostics first, then the run's messages in time order, appended as
    /// the run is made; a filter by severity, a search and counts. And the hierarchy beside it: the design's scopes as
    /// rows that open and shut, and what a click on one adds to the waveform -- its ports as one group named by its path.
    /// </summary>
    public class TranscriptTests
    {
        /// <summary>A run of a testbench that says something at 5, warns at 10, errs at 15 and finishes at 20: its messages.</summary>
        private static IReadOnlyList<SimMessage> Said()
        {
            SyntaxTree tree = SyntaxReader.ReadText(@"
module t;
    initial begin
        #5 $display(""five"");
        #5 $warning(""ten"");
        #5 $error(""fifteen"");
        #5 $finish;
    end
endmodule", "t.sv", null);
            Assert.IsEmpty(tree.Diagnostics, string.Join("; ", tree.Diagnostics));
            Elaboration elaboration = Elaborator.Elaborate(tree, "t");
            Assert.IsFalse(elaboration.HasErrors, string.Join("; ", elaboration.Diagnostics));
            var sim = new Simulator(elaboration.Design);
            sim.RunTo(100);
            return sim.History.Messages;
        }

        private static readonly BoardDiagnostic[] Document =
        {
            new BoardDiagnostic(DiagnosticSeverity.Warning, "top", "The AND g1 has an input with nothing driving it.", part: 3),
            new BoardDiagnostic(DiagnosticSeverity.Error, "top", "Two outputs drive the net n.", wire: 7),
        };

        [Test]
        public void TheDocumentsDiagnosticsComeFirst_ThenTheRunsMessages_InTimeOrder_Counted()
        {
            IReadOnlyList<SimMessage> said = Said();
            Assert.GreaterOrEqual(said.Count, 3, "sanity: the run said something");

            var transcript = new TranscriptView();
            Assert.IsTrue(transcript.Update(Document, said));

            IReadOnlyList<TranscriptLine> lines = transcript.Lines;
            Assert.AreEqual(Document.Length + said.Count, lines.Count);
            Assert.IsFalse(lines[0].IsMessage, "the document's first");
            Assert.AreEqual(3, lines[0].Part, "with the part it is about");
            Assert.AreEqual(7, lines[1].Wire, "or the wire");
            for (int i = Document.Length + 1; i < lines.Count; i++)
                Assert.GreaterOrEqual(lines[i].Time, lines[i - 1].Time, "the run's in time order");

            StringAssert.Contains("five", lines[2].Text);
            Assert.AreEqual(5, lines[2].Time);
            Assert.AreEqual(1, transcript.Errors - said.Count(m => m.Severity == Severity.Error), "the document's error, counted");
            Assert.AreEqual(said.Count(m => m.Severity == Severity.Warning) + 1, transcript.Warnings);
            Assert.AreEqual("ERROR", TranscriptView.Mark(TranscriptSeverity.Error), "a severity is a word as well as a colour");
        }

        [Test]
        public void AFilterAndASearch_NarrowWhatIsShown_AndTheCountsStay()
        {
            var transcript = new TranscriptView();
            transcript.Update(Document, Said());
            int all = transcript.Lines.Count, errors = transcript.Errors;

            transcript.Filter = TranscriptFilter.Errors;
            Assert.IsTrue(transcript.Lines.All(l => l.Severity == TranscriptSeverity.Error));
            Assert.AreEqual(errors, transcript.Lines.Count);

            transcript.Filter = TranscriptFilter.Messages;
            Assert.IsTrue(transcript.Lines.All(l => l.Severity == TranscriptSeverity.Info));

            transcript.Filter = TranscriptFilter.All;
            transcript.Search = "FIVE";
            Assert.AreEqual(1, transcript.Lines.Count, "a search ignores case");
            Assert.AreEqual(errors, transcript.Errors, "and the counts are of everything");

            transcript.Search = "";
            Assert.AreEqual(all, transcript.Lines.Count);
        }

        [Test]
        public void AsTheRunGrows_ItsNewMessagesAreAppended_AndNothingChangedIsNoChange()
        {
            IReadOnlyList<SimMessage> said = Said();
            var growing = new List<SimMessage> { said[0] };
            var transcript = new TranscriptView();
            transcript.Update(Document, growing);
            int version = transcript.Version;

            Assert.IsFalse(transcript.Update(Document, growing), "nothing new");
            Assert.AreEqual(version, transcript.Version);

            growing.Add(said[1]);
            Assert.IsTrue(transcript.Update(Document, growing), "a message more");
            Assert.AreEqual(Document.Length + 2, transcript.Lines.Count);

            Assert.IsTrue(transcript.Update(Document, new List<SimMessage>()), "another run: its messages, none yet");
            Assert.AreEqual(Document.Length, transcript.Lines.Count);
        }

        // -----------------------------------------------------------------
        // The hierarchy
        // -----------------------------------------------------------------

        private static WorkbenchModel FullAdder()
        {
            RunSession run = RunSession.Open(WorkbenchSamples.FullAdder(), () => 0.0);
            run.Finish();
            return run.Model;
        }

        [Test]
        public void TheHierarchy_IsTheTestbenchTheDesignAndItsBlocks_NeverACell_AndOpensAndShuts()
        {
            var tree = new HierarchyTree(FullAdder().Signals);
            string[] Rows() => tree.Rows.Select(r => new string(' ', r.Depth) + r.Label).ToArray();

            string[] rows = Rows();
            Assert.AreEqual(ScopeNodeKind.Testbench, tree.Rows[0].Node.Kind, "the testbench at the top: " + string.Join(" | ", rows));
            Assert.IsTrue(tree.Rows.Any(r => r.Node.Kind == ScopeNodeKind.Design), "the design under it");
            Assert.AreEqual(2, tree.Rows.Count(r => r.Node.Kind == ScopeNodeKind.Block), "its two half adders: " + string.Join(" | ", rows));
            Assert.IsTrue(tree.Rows.Where(r => r.Node.Kind == ScopeNodeKind.Block).All(r => r.Label.EndsWith("(half_adder)")), "named for their module");
            Assert.IsFalse(tree.Rows.Any(r => r.Node.Kind == ScopeNodeKind.Cell), "and never a gate's cell");

            int design = tree.Rows.ToList().FindIndex(r => r.Node.Kind == ScopeNodeKind.Design);
            Assert.IsTrue(tree.Rows[design].Opens && tree.Rows[design].Open);
            Assert.IsTrue(tree.Toggle(design));
            Assert.IsFalse(tree.Rows.Any(r => r.Node.Kind == ScopeNodeKind.Block), "shut, its blocks are out of sight");
            Assert.IsTrue(tree.Toggle(design));
            Assert.AreEqual(2, tree.Rows.Count(r => r.Node.Kind == ScopeNodeKind.Block), "and open again");
        }

        [Test]
        public void WhatAnImportSaid_IsListedFirst_UntilItIsSetAgain()
        {
            var transcript = new TranscriptView();
            transcript.Update(Document, Said());
            int before = transcript.Lines.Count;

            transcript.Notes = TranscriptView.LinesOf(new[]
            {
                new ImportNote("broken.sv", 3, 1, "There is no module here.", true),
                new ImportNote("gates.sv", 1, 1, "NAND2 is one of BitSorter's own cells.", false),
            });
            Assert.IsTrue(transcript.Update(Document, Said()), "the same document and run, made again for the notes");

            Assert.AreEqual(before + 2, transcript.Lines.Count);
            Assert.AreEqual("broken.sv:3: There is no module here.", transcript.Lines[0].Text);
            Assert.AreEqual(TranscriptSeverity.Error, transcript.Lines[0].Severity);
            Assert.AreEqual(TranscriptSeverity.Info, transcript.Lines[1].Severity);

            transcript.Notes = null;
            transcript.Update(Document, Said());
            Assert.AreEqual(before, transcript.Lines.Count, "and gone once set again");
        }

        [Test]
        public void AClickOnABlock_AddsItsPortsAsOneGroup_NamedByItsPath()
        {
            WorkbenchModel model = FullAdder();
            var tree = new HierarchyTree(model.Signals);
            ScopeNode h1 = tree.Rows.First(r => r.Node.Kind == ScopeNodeKind.Block).Node;

            List<int> ports = tree.SignalsOf(h1);
            CollectionAssert.AreEquivalent(new[] { "h1.a", "h1.b", "h1.s", "h1.c" }, ports.Select(model.Signals.KeyOf), "a half adder's four ports");
            Assert.AreEqual("h1", tree.GroupNameOf(h1));

            var list = new WaveList(model.Signals, new[] { "a", "b", "h1.s" });
            WaveGroup group = list.AddGroup(tree.GroupNameOf(h1), ports);
            Assert.IsNotNull(group);
            Assert.AreEqual("h1", group.Name);
            Assert.AreEqual(4, group.Signals.Count, "the four, h1.s taken out of where it was");
            Assert.AreEqual(1, list.Lines.Count(l => !l.IsHeader && l.Signal.Key == "h1.s"), "and shown once");
            Assert.IsTrue(list.Lines.Any(l => !l.IsHeader && l.Signal.Key == "a"), "the rest left as they were");
        }
    }
}
