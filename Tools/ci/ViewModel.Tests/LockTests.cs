using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BoardPoint = BitSorter.Board.Point;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A document locked against change (Part 15h-4d, asked 2026-10-29): every edit of its circuit and its code refused with
    /// the way to unlock it, an undo or a redo that would change them too, and nothing else -- its inputs, its library and
    /// its unit are tried as before. Locking is no edit: no undo step, and the project as it was.
    /// </summary>
    public class LockTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => now += step;
        }

        private static WorkbenchSession Locked(Project project)
        {
            var session = new WorkbenchSession(project, Ticking(0.01));
            session.Finish();
            session.Locked = true;
            return session;
        }

        private static void Refused(EditResult result, string what)
        {
            Assert.IsFalse(result.Done, what + " refused");
            Assert.AreEqual(WorkbenchSession.LockedRefusal, result.Refusal, what + " says how to unlock it");
        }

        [Test]
        public void ALockedCircuit_RefusesEveryEditOfIt_AndChangesNothing()
        {
            WorkbenchSession session = Locked(WorkbenchSamples.FullAdder());
            Project before = session.Project;
            GatePart gate = session.Project.TopModule.Parts.OfType<GatePart>().First();
            Placement at = session.Project.TopModule.Layout.PlacementOf(gate.Id);

            Refused(session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(20, 20)), "placing");
            Refused(session.Remove(new[] { gate.Id }), "deleting");
            Refused(session.Move(new Dictionary<int, Placement> { [gate.Id] = new Placement(new Point(at.Position.X, at.Position.Y + 3)) }), "moving");
            Refused(session.Turn(new[] { gate.Id }, 1), "turning");
            Wire wire = session.Project.TopModule.Wires.First();
            Refused(session.Connect(wire.From, wire.To), "wiring");
            Refused(session.Reroute(wire.Id, new Route()), "a wire reshaped");
            Refused(session.AutoRoute(new[] { wire.Id }), "routing again");
            Refused(session.Paste(Clipboard.Copy(session.Project, session.Module, new[] { gate.Id }), new List<int>(), new List<int>()), "pasting");
            Refused(session.MakeBlock(new[] { gate.Id }, "lump"), "MAKE BLOCK");
            Refused(session.AddWrittenModule("code1"), "+ CODE");
            Refused(session.ReplaceModule(session.Project.TopModule), "a module put in");
            Refused(session.RemoveModule("half_adder"), "a module deleted");
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.WhyNotRemoveModule("half_adder"), "said before it is asked");
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.WhyNotChange, "MAKE BLOCK told before it asks a name");
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.WhyNotChangeCode, "+ CODE the same");
            Refused(session.SetLibrary(GateLibrary.CourseCells(2)), "a gate library, which retimes the gates");
            Assert.AreSame(before, session.Project, "nothing changed");
            Assert.IsFalse(session.Editor.CanUndo, "and nothing to undo");
        }

        [Test]
        public void ALockedWrittenModule_RefusesItsCode()
        {
            var session = new WorkbenchSession(WorkbenchSamples.WrittenAdder(), Ticking(0.01));
            session.Finish();
            Assert.IsTrue(session.StepInto(session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id));
            session.Locked = true;
            Refused(session.SetSource(session.Project.Find(session.Module).Source + "// a note\n"), "the code");
        }

        [Test]
        public void ALockedCircuit_StillTakesItsInputs_AndItsUnit()
        {
            WorkbenchSession session = Locked(WorkbenchSamples.FullAdder());
            Assert.IsTrue(session.SetInput("b", 0, 5, LogicVec.FromBinary("1")).Done, "an input set on the waves or in INPUTS");
            string other = session.Project.Settings?.TimeUnit == "ns" ? "ps" : "ns";
            Assert.IsTrue(session.SetUnit(other).Done, "the unit");
            Assert.IsTrue(session.Undo(), "the unit taken back");
            Assert.IsTrue(session.Undo(), "and the input");
            Assert.IsFalse(session.Editor.CanUndo);
        }

        /// <summary>
        /// A library chosen before the lock that retimed nothing -- no drawn gate to retime -- goes back while locked: it builds
        /// its list of modules afresh, every module the same, and the modules are compared one by one (the review of the lock).
        /// </summary>
        [Test]
        public void ALibraryThatRetimedNothing_GoesBackWhileLocked()
        {
            // An input wired straight to an output: nothing a library could retime.
            var editor = new BoardEditor(Project.New("t"));
            EditResult a = PartsCatalog.Place(editor, "t", PartsCatalog.Find(editor.Project, "t", "input"), new Placement(1, 0));
            EditResult y = PartsCatalog.Place(editor, "t", PartsCatalog.Find(editor.Project, "t", "output"), new Placement(8, 0));
            Assert.IsTrue(a.Done && y.Done && editor.Connect("t", Endpoint.At(a.Id, "out"), Endpoint.At(y.Id, "in")).Done);
            var session = new WorkbenchSession(editor.Project, Ticking(0.01));
            session.Finish();
            Project before = session.Project;
            EditResult chosen = session.SetLibrary(GateLibrary.CourseCells(2));
            Assert.IsTrue(chosen.Done, chosen.Refusal);
            Assert.AreNotSame(before.Modules, session.Project.Modules, "sanity: its list made afresh");
            session.Locked = true;
            Assert.IsNull(session.WhyNotUndo, "the same modules, one by one");
            Assert.IsTrue(session.Undo());
        }

        [Test]
        public void Locking_IsNoEdit_NoUndoStep_AndTheRunAsItWas()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            Project before = session.Project;

            session.Locked = true;
            Assert.AreSame(before, session.Project, "the project untouched");
            Assert.IsFalse(session.Editor.CanUndo, "no step to undo");
            Assert.IsTrue(session.Locked);
            session.Locked = false;
            Assert.IsTrue(session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(20, 20)).Done, "unlocked, it takes edits");
        }

        /// <summary>
        /// An edit made before locking cannot be taken back while it is locked -- that would change the circuit after all --
        /// while a change to the inputs can; unlocked, the edit goes back as ever.
        /// </summary>
        [Test]
        public void ALockedCircuit_RefusesAnUndoThatWouldChangeIt_ButNotOneOfItsInputs()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            Project original = session.Project;
            Assert.IsTrue(session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(20, 20)).Done);
            Project placed = session.Project;

            session.Locked = true;
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.WhyNotUndo);
            Assert.IsFalse(session.Undo(), "the NOT stays");
            Assert.AreSame(placed, session.Project);

            Assert.IsTrue(session.SetInput("b", 0, 5, LogicVec.FromBinary("1")).Done);
            Assert.IsNull(session.WhyNotUndo, "an input's step goes back");
            Assert.IsTrue(session.Undo());
            Assert.AreSame(placed.Modules, session.Project.Modules);

            session.Locked = false;
            Assert.IsTrue(session.Undo(), "unlocked, the NOT goes");
            Assert.AreSame(original.Modules, session.Project.Modules);
            session.Locked = true;
            Assert.AreEqual(WorkbenchSession.LockedRefusal, session.WhyNotRedo, "and does not come back while locked");
            Assert.IsFalse(session.Redo());
        }

        [Test]
        public void ADragOnALockedCircuit_IsRefusedAsItBegins()
        {
            WorkbenchSession session = Locked(WorkbenchSamples.FullAdder());
            var canvas = new CanvasEditor(session);
            EditResult? begun = canvas.Do(new CanvasCommand(CanvasGesture.BeginMove, from: new BoardPoint(4, 0)));
            Assert.IsTrue(begun.HasValue && !begun.Value.Done, "refused");
            Assert.AreEqual(WorkbenchSession.LockedRefusal, begun.Value.Refusal);
        }
    }
}
