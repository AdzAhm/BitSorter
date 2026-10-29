using System;
using System.Linq;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// FILE, BACK TO AS IMPORTED (Part 15h-4d): the whole project put back as one step, locked again; unlocked, Ctrl+Z
    /// brings the changes back.
    /// </summary>
    public class BackToImportedTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        [Test]
        public void PuttingBack_IsOneStep_LocksAgain_AndUnlockedGoesBackAgain()
        {
            Project imported = WorkbenchSamples.FullAdder();
            var session = new WorkbenchSession(imported, Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(20, 20)).Done);
            Project changed = session.Project;

            EditResult put = session.PutBack(imported, locked: true);
            Assert.IsTrue(put.Done, put.Refusal);
            Assert.AreSame(imported, session.Project, "the project as it came in");
            Assert.IsTrue(session.Locked, "locked again, as an import is");
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.WhyNotUndo, "the changes come back only unlocked");

            session.Locked = false;
            Assert.IsTrue(session.Undo(), "one step");
            Assert.AreSame(changed, session.Project, "the changes back");
        }

        /// <summary>Text typed into the code and not applied goes with the put-back: the code as it came in, never broken text left read only.</summary>
        [Test]
        public void PuttingBack_DropsTextTypedAndNotApplied()
        {
            Project imported = WorkbenchSamples.WrittenAdder();
            var session = new WorkbenchSession(imported, Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.StepInto(session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id));
            session.DrawCode(false);
            CodeDocument code = session.Code;
            code.Type(code.Text + "assign broken = ;\n", 100);
            Assert.IsTrue(code.Pending, "sanity: typed, not applied");

            Assert.IsTrue(session.PutBack(imported, locked: true).Done);
            Assert.IsNull(session.PendingCode, "nothing typed left");
            Assert.AreEqual(imported.Find(session.Module).Source, session.Code.Text, "the code as it came in");
        }

        [Test]
        public void PuttingBack_IsTakenEvenLocked()
        {
            Project imported = WorkbenchSamples.FullAdder();
            var session = new WorkbenchSession(imported, Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.SetInput("b", 0, 5, BitSorter.SystemVerilog.LogicVec.FromBinary("1")).Done);
            session.Locked = true;
            Assert.IsTrue(session.PutBack(imported, locked: true).Done, "it puts the circuit back rather than changing it by hand");
            Assert.AreSame(imported, session.Project);
        }
    }
}
