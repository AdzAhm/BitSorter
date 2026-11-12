using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// A written testbench's run is shown to where it stops (Part 13b), and so is a run whose code was edited since: an edit
    /// of the testbench's text, or of the code it tests, can move its <c>$stop</c>, and the run has to follow it -- found in
    /// 5.1, where "Is it prime?" widened to six bits in its own testbench was still cut off where four bits had stopped.
    /// </summary>
    public class WrittenBenchRunTests
    {
        // The design says when it is done, and the testbench waits for that: an edit of either moves the stop.
        private const string Design =
            "module slow (output logic done);\n" +
            "    initial begin\n" +
            "        done = 1'b0;\n" +
            "        #20 done = 1'b1;\n" +
            "    end\n" +
            "endmodule\n";

        private const string Bench =
            "module slow_tb;\n" +
            "    logic done;\n" +
            "    slow dut (.done(done));\n" +
            "    initial begin\n" +
            "        wait (done);\n" +
            "        #10 $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private static BoardEditor Imported()
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("slow.sv", Design), new SourceFile("slow_tb.sv", Bench) }, "slow_tb");
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Notes));
            Assert.AreEqual(30, import.Project.Stimulus.End, "the import ran it to its $stop");
            return new BoardEditor(import.Project);
        }

        [Test]
        public void ATestbenchEditedToStopLater_IsRunToItsNewStop()
        {
            BoardEditor editor = Imported();
            Assert.IsTrue(editor.SetSource("slow_tb", Bench.Replace("#10 $stop", "#70 $stop")).Done);
            Assert.AreEqual(90, editor.Project.Stimulus.End);
        }

        [Test]
        public void ATestbenchEditedToStopSooner_EndsWhereItNowStops()
        {
            BoardEditor editor = Imported();
            Assert.IsTrue(editor.SetSource("slow_tb", Bench.Replace("#10 $stop", "#1 $stop")).Done);
            Assert.AreEqual(21, editor.Project.Stimulus.End);
        }

        [Test]
        public void AnEditOfTheCodeItTests_ThatMovesTheStop_MovesTheEnd()
        {
            BoardEditor editor = Imported();
            Assert.IsTrue(editor.SetSource("slow", Design.Replace("#20 done", "#50 done")).Done);
            Assert.AreEqual(60, editor.Project.Stimulus.End);
        }

        [Test]
        public void TheEditUndone_PutsTheEndBack_AsOneStep()
        {
            BoardEditor editor = Imported();
            Assert.IsTrue(editor.SetSource("slow_tb", Bench.Replace("#10 $stop", "#70 $stop")).Done);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(30, editor.Project.Stimulus.End);
            Assert.IsFalse(editor.CanUndo, "the text and its end were one step");
        }

        [Test]
        public void AnEditThatLeavesTheStopWhereItWas_LeavesTheEnd()
        {
            BoardEditor editor = Imported();
            Assert.IsTrue(editor.SetSource("slow_tb", Bench.Replace("wait (done);", "wait (done); // the design's own signal")).Done);
            Assert.AreEqual(30, editor.Project.Stimulus.End);
        }
    }
}
