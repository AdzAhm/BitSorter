using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// PROGRAM on the workbench (Part 15h-6c, Ahmad's choice, 2026-11-01): a processor sample's testbench offers its programs
    /// as a choice; choosing one makes the run again with it, as one undo step; and a locked circuit can still be run on
    /// another program, which changes what is run, never the circuit or its code.
    /// </summary>
    public class ProgramChoiceTests
    {
        private static WorkbenchSession Pipelined()
        {
            double now = 0;
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == "Pipelined RISC-V").Build(), () => now += 0.01);
            session.Finish();
            return session;
        }

        private static string Printed(WorkbenchSession session) =>
            string.Join("\n", session.Run.Model.Session.History.Messages.Select(m => m.Text));

        [Test]
        public void ChoosingAProgram_RunsIt_AndUndoGoesBackToTheLast()
        {
            WorkbenchSession session = Pipelined();
            BenchChoice choice = session.Choices.Single();
            Assert.AreEqual("SUM", choice.Chosen);
            StringAssert.Contains("the sum of 1 to 10: 55", Printed(session));
            long sumEnd = session.Run.Model.End;

            Assert.IsTrue(session.Choose("PROGRAM", "FIBONACCI").Done);
            session.Finish();
            Assert.AreEqual("FIBONACCI", session.Choices.Single().Chosen);
            StringAssert.Contains("ten Fibonacci numbers: 0 1 1 2 3 5 8 13 21 34", Printed(session));
            StringAssert.Contains("PASS: every result as expected", Printed(session));
            Assert.Greater(session.Run.Model.End, sumEnd, "shown to where Fibonacci stops, later than the sum");

            Assert.IsTrue(session.Undo());
            session.Finish();
            Assert.AreEqual("SUM", session.Choices.Single().Chosen);
            StringAssert.Contains("the sum of 1 to 10: 55", Printed(session));
            Assert.AreEqual(sumEnd, session.Run.Model.End);
        }

        [Test]
        public void ALockedCircuit_CanStillBeRunOnAnotherProgram()
        {
            WorkbenchSession session = Pipelined();
            session.Locked = true;
            Assert.IsTrue(session.Choose("PROGRAM", "TEST").Done, "the lock keeps the circuit and its code, not what is run");
            session.Finish();
            StringAssert.Contains("every instruction once: 41 of 41 checks held", Printed(session));
        }

        // Written for these tests: each program stops on a $stop of its own time, and the design's width follows the choice.
        private const string Wide =
            "module wide #(parameter W = 1) (input logic a, output logic [W-1:0] y);\n" +
            "    assign y = {W{a}};\n" +
            "endmodule\n";

        private const string StopBench =
            "module tb;\n" +
            "    typedef enum {SHORT, LONG} program_t;\n" +
            "    parameter program_t PROGRAM = SHORT;\n" +
            "    logic a = 1'b0;\n" +
            "    logic [PROGRAM == SHORT ? 0 : 2:0] y;\n" +
            "    wide #(.W(PROGRAM == SHORT ? 1 : 3)) dut (.a(a), .y(y));\n" +
            "    initial begin\n" +
            "        #(PROGRAM == SHORT ? 10 : 40) a = 1'b1;\n" +
            "        #5 $stop;\n" +
            "    end\n" +
            "endmodule\n";

        private static WorkbenchSession Stopping()
        {
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("wide.sv", Wide), new SourceFile("tb.sv", StopBench) }, "tb");
            Assert.IsTrue(import.Succeeded, string.Join("; ", import.Notes));
            double now = 0;
            var session = new WorkbenchSession(import.Project, () => now += 0.01);
            session.Finish();
            return session;
        }

        [Test]
        public void AnotherProgram_IsShownToWhereItStops_NeverRunOnToTheLastOnesEnd()
        {
            // A $stop can be run on past, and a run that was is replayed as far; one that was not is its own length -- so the
            // short program chosen again after the long one, or the long one undone, is not run on to the long one's end.
            WorkbenchSession session = Stopping();
            long shortEnd = session.Run.Model.End;
            Assert.AreEqual(15, shortEnd, "sanity: shown to the short program's $stop");

            Assert.IsTrue(session.Choose("PROGRAM", "LONG").Done);
            session.Finish();
            Assert.AreEqual(45, session.Run.Model.End);

            Assert.IsTrue(session.Undo());
            session.Finish();
            Assert.AreEqual(shortEnd, session.Run.Model.End, "the undo's run, the short program's own length");

            Assert.IsTrue(session.Redo());
            session.Finish();
            Assert.IsTrue(session.Choose("PROGRAM", "SHORT").Done);
            session.Finish();
            Assert.AreEqual(shortEnd, session.Run.Model.End, "chosen again, its own length");
        }

        [Test]
        public void TheDrawingOfCode_FollowsTheChoice()
        {
            WorkbenchSession session = Stopping();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the written design opens as its drawing");
            Assert.AreEqual(1, session.DrawnModule.Parts.OfType<PortPart>().Single(p => p.Name == "y").Width);

            Assert.IsTrue(session.Choose("PROGRAM", "LONG").Done);
            session.Finish();
            Assert.AreEqual(3, session.DrawnModule.Parts.OfType<PortPart>().Single(p => p.Name == "y").Width,
                "drawn as the long program elaborates it, not as the last drawing was");
        }

        [Test]
        public void TheProgramRunning_ChosenAgain_IsNoStep_AndAChoiceTheTestbenchHasNot_IsRefused()
        {
            WorkbenchSession session = Pipelined();
            Assert.IsTrue(session.Choose("PROGRAM", "SUM").Done);
            Assert.IsNull(session.Replay, "no run made again");
            Assert.IsFalse(session.Undo(), "and no step to undo");

            EditResult member = session.Choose("PROGRAM", "QUICKSORT");
            Assert.IsFalse(member.Done);
            StringAssert.Contains("SUM, FIBONACCI, TEST", member.Refusal);
            Assert.IsFalse(session.Choose("SPEED", "SUM").Done, "a parameter it has no choice of");
            Assert.AreEqual("SUM", session.Choices.Single().Chosen);
        }

        [Test]
        public void ACircuitWithNoChoice_OffersNone()
        {
            double now = 0;
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == "Full adder").Build(), () => now += 0.01);
            session.Finish();
            Assert.IsEmpty(session.Choices);
        }
    }
}
