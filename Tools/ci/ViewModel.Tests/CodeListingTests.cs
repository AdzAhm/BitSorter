using System;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The dock's CODE tab (Part 12e): the document as the SystemVerilog it prints as -- the cells, each module after those it
    /// uses, the testbench last -- a written module as its own text, and printed again as the document is drawn.
    /// </summary>
    public class CodeListingTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        [Test]
        public void AListing_IsEveryFile_InTheOrderASimulatorReadsThem()
        {
            CodeListing listing = CodeListing.Of(WorkbenchSamples.FullAdder());
            string[] captions = Enumerable.Range(0, listing.Files.Count).Select(listing.Caption).ToArray();
            CollectionAssert.AreEqual(new[] { "CELLS", "half_adder", "full_adder", "TESTBENCH" }, captions,
                "the cells, the half adder before the full adder that uses it, the testbench last");

            int full = listing.FileOf("full_adder");
            Assert.AreEqual(2, full);
            StringAssert.Contains("module full_adder", listing.Files[full].Text);
            StringAssert.Contains("half_adder h1 (", listing.Files[full].Text, "a block is an instance of its module");
            Assert.AreEqual(-1, listing.FileOf("nothing"));
        }

        [Test]
        public void AWrittenModule_IsListedAsItsOwnText()
        {
            CodeListing listing = CodeListing.Of(WorkbenchSamples.WrittenAdder());
            string text = listing.Files[listing.FileOf("add4")].Text;
            StringAssert.EndsWith(WorkbenchSamples.AdderSource, text, "as written, comments and spacing kept");
            StringAssert.StartsWith("// add4: written in BitSorter.", text);
        }

        [Test]
        public void TheSessionsListing_IsPrintedAgain_OnlyWhenTheDocumentChanges()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            CodeListing before = session.Listing;
            Assert.AreSame(before, session.Listing, "nothing changed, nothing printed again");

            Assert.IsTrue(session.Place(PartsCatalog.For(session.Project, session.Module).Single(e => e.Key == "and"), new Placement(20, 20)).Done);
            CodeListing after = session.Listing;
            Assert.AreNotSame(before, after, "an edit prints it again");
            string placed = session.Project.TopModule.Parts.OfType<GatePart>().Last().Name;
            StringAssert.Contains(placed, after.Files[after.FileOf("full_adder")].Text, "with the gate just placed");
        }
    }
}
