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
