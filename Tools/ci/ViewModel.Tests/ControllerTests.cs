using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Board.Tests;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// A controller shown as the textbook draws one (Part 15h-6e): stepping into the multicycle processor's controller opens on
    /// its diagram, the single-cycle's on its tables, the drawing one click away and an edit there refused; the diagram's
    /// lit state and table row follow the run; and the layout keeps every box apart with the ways forward going forward.
    /// </summary>
    public class ControllerTests
    {
        /// <summary>Every written module of every sample read or saying why, and every reading what the engine makes of it.</summary>
        [Test]
        public void EverySamplesModule_IsReadOrSaysWhy_AndEveryReadingIsWhatTheEngineMakes()
        {
            var read = new List<string>();
            foreach ((string name, string _, Func<Project> build) in WorkbenchSamples.All)
            {
                CompiledProject compiled = BoardCompiler.Compile(build());
                if (!compiled.Succeeded)
                    continue;
                foreach (ModuleDeclaration module in compiled.Tree.Modules)
                {
                    ControllerReading reading = ControllerReader.Read(compiled.Tree, module, out string why);
                    if (reading == null)
                    {
                        Assert.IsNotEmpty(why, $"{name} {module.Name}");
                        continue;
                    }
                    ControllerEngine.Hold(compiled.Tree, module.Name, reading);
                    read.Add(module.Name);
                }
            }
            CollectionAssert.IsSupersetOf(read, new[] { "seq101", "scpu_control", "mcpu_control", "pcpu_control" });
        }

        /// <summary>The sequence detector's states are localparams, not an enum: read all the same, its Mealy output in ONE0's box.</summary>
        [Test]
        public void TheSequenceDetector_IsItsThreeStates_EachArrowItsInputsBit()
        {
            CompiledProject compiled = BoardCompiler.Compile(WorkbenchSamples.All.Single(s => s.Name == "Sequence detector").Build());
            ControllerReading reading = ControllerReader.Read(compiled.Tree, compiled.Tree.Modules.Single(m => m.Name == "seq101"), out string why);
            Assert.IsNotNull(reading, why);
            MachineReading machine = reading.Machine;
            CollectionAssert.AreEqual(new[] { "NONE", "ONE", "ONE0" }, machine.States);
            Assert.AreEqual(0, machine.Start);
            CollectionAssert.AreEqual(new[] { "0 -> 1 [x]", "0 -> 0 [otherwise]", "1 -> 1 [x]", "1 -> 2 [otherwise]", "2 -> 1 [x]", "2 -> 0 [otherwise]" },
                machine.Arrows.Select(a => a.ToString()).ToArray());
            CollectionAssert.AreEqual(new[] { "found = x" }, machine.Settings[2].Select(s => s.ToString()).ToArray());
            Assert.IsTrue(reading.Whole);
        }

        [Test]
        public void SteppingIntoTheMulticycleController_OpensOnItsDiagram_TheDrawingOneClickAway()
        {
            WorkbenchSession session = InsideTheControl("Multicycle RISC-V");
            Assert.IsTrue(session.ShowsController, "a whole controller opens on its diagram");
            Assert.AreEqual(11, session.Controller.Machine.States.Count);

            int part = session.DrawnModule.Parts.First().Id;
            Assert.AreEqual(WorkbenchSession.ControllerRefusal, session.Remove(new[] { part }).Refusal, "the diagram takes no edit");
            Assert.AreEqual(WorkbenchSession.ControllerRefusal, session.Move(new Dictionary<int, Placement> { [part] = new Placement(3, 3) }).Refusal);

            session.DrawController(false);
            Assert.IsFalse(session.ShowsController, "DRAWING");
            Assert.AreNotEqual(WorkbenchSession.ControllerRefusal, session.Move(new Dictionary<int, Placement> { [part] = new Placement(3, 3) }).Refusal,
                "the drawing takes a move again");
            Assert.IsTrue(session.StepOut());
            Assert.IsTrue(session.StepInto(ControlOf(session)));
            Assert.IsFalse(session.ShowsController, "kept for the module, as CODE is");
            session.DrawController(true);
            Assert.IsTrue(session.ShowsController, "DIAGRAM");
        }

        /// <summary>The state lit is the run's, and the one it came from the state before it: FETCH, DECODE, ... through the program.</summary>
        [Test]
        public void TheDiagram_FollowsTheRun()
        {
            WorkbenchSession session = InsideTheControl("Multicycle RISC-V", "ISA");   // every instruction: every state
            WorkbenchModel model = session.Run.Model;
            MachineReading machine = session.Controller.Machine;
            var seen = new List<int>();
            int last = -1;
            for (long t = 25; t < model.End; t += 10)   // a clock of 10, sampled between its edges, the whole run
            {
                model.Seek(t);
                ControllerMoment now = session.ControllerNow();
                Assert.GreaterOrEqual(now.State, 0, $"at {t} a state");
                if (last >= 0 && now.State != last)
                    Assert.AreEqual(last, now.CameFrom, $"at {t}, {machine.States[now.State]} came from {machine.States[last]}");
                if (!seen.Contains(now.State))
                    seen.Add(now.State);
                last = now.State;
            }
            CollectionAssert.IsSubsetOf(new[] { "FETCH", "DECODE" }, seen.Select(s => machine.States[s]).ToArray(), "every instruction is fetched and decoded");
            Assert.AreEqual(machine.States.Count, seen.Count, "the program takes it through every state: " + string.Join(", ", seen.Select(s => machine.States[s])));
        }

        [Test]
        public void SteppingIntoTheSingleCycleController_OpensOnItsTables_TheRowOfTheInstructionRunningLit()
        {
            WorkbenchSession session = InsideTheControl("Single-cycle RISC-V", "ISA");
            Assert.IsTrue(session.ShowsController);
            Assert.IsNull(session.Controller.Machine);
            Assert.AreEqual(2, session.Controller.Tables.Count);
            var rows = new HashSet<int>();
            for (long t = 25; t < 600; t += 10)
            {
                session.Run.Model.Seek(t);
                rows.Add(session.ControllerNow().Rows[1]);
            }
            Assert.IsFalse(rows.Contains(-1), "every instruction picks a row");
            Assert.GreaterOrEqual(rows.Count, 4, "the program runs several kinds of instruction");
        }

        [Test]
        public void TheLayout_KeepsEveryBoxApart_TheWaysForwardGoingForward_TheTablesBeside()
        {
            WorkbenchSession session = InsideTheControl("Multicycle RISC-V");
            ControllerLayout layout = ControllerLayout.Of(session.Controller, (text, bold) => text.Length * (bold ? 9f : 8f), 16f);
            Assert.AreEqual(11, layout.Boxes.Count);
            for (int a = 0; a < layout.Boxes.Count; a++)
                for (int b = a + 1; b < layout.Boxes.Count; b++)
                    Assert.IsFalse(Overlap(layout.Boxes[a].Min, layout.Boxes[a].Max, layout.Boxes[b].Min, layout.Boxes[b].Max),
                        $"{layout.Boxes[a].Lines[0]} and {layout.Boxes[b].Lines[0]} overlap");
            float fetch = layout.Boxes[0].Min.x;
            Assert.IsTrue(layout.Boxes.All(b => b.Min.x >= fetch), "FETCH, the start, on the left");

            foreach (ControllerArrowShape arrow in layout.Arrows.Where(a => a.Curved))
            {
                for (int i = 1; i < arrow.Points.Count; i++)
                    Assert.Greater(arrow.Points[i].x, arrow.Points[i - 1].x, $"arrow {arrow.Arrow} goes forward");
                for (int i = 1; i < arrow.Points.Count - 1; i++)
                    Assert.IsFalse(layout.Boxes.Any(b => Overlap(b.Min, b.Max, arrow.Points[i], arrow.Points[i])), $"arrow {arrow.Arrow} passes through a box");
            }

            float right = layout.Boxes.Max(b => b.Max.x);
            Assert.AreEqual(1, layout.Tables.Count);
            Assert.Greater(layout.Tables[0].Min.x, right, "the ALU decoder beside the diagram");
            Assert.AreEqual(8, layout.Tables[0].Rows.Count);
        }

        /// <summary>
        /// A one-output case is a function, which its drawing and the LOGIC tab show better: the seven-segment sample opens on its
        /// drawing, though a reading is there for DIAGRAM; "Is it prime?", worked out by a loop since 5.1, has no reading at all;
        /// the sequence detector, a state machine, opens on its diagram.
        /// </summary>
        [TestCase("Is it prime?", false)]
        [TestCase("Seven segments", false)]
        [TestCase("Sequence detector", true)]
        public void ASampleOpens_AsItsDrawing_UnlessItIsAMachineOrAControlTable(string sample, bool diagram)
        {
            double now = 0;
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == sample).Build(), () => now += 0.01);
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the drawing of its code");
            Assert.AreEqual(diagram, session.ShowsController);
            if (!diagram && session.Controller != null)
            {
                session.DrawController(true);
                Assert.IsTrue(session.ShowsController, "DIAGRAM shows its table");
            }
        }

        /// <summary>The check the session makes before it shows a reading refuses one the engine does not bear out: an arrow taken away.</summary>
        [Test]
        public void AReadingTheEngineDoesNotBearOut_IsRefused()
        {
            CompiledProject compiled = BoardCompiler.Compile(WorkbenchSamples.All.Single(s => s.Name == "Sequence detector").Build());
            ControllerReading reading = ControllerReader.Read(compiled.Tree, compiled.Tree.Modules.Single(m => m.Name == "seq101"), out _);
            Elaboration elaboration = Elaborator.Elaborate(compiled.Tree, "seq101");
            Design design = elaboration.Design;
            string top = design.Scopes.Single(s => s.Kind == ScopeKind.Top).Path;
            RtlNetlist netlist = RtlExtractor.Of(design, top);
            Assert.IsNull(ControllerCheck.Why(reading, design, netlist, top), "as read, it holds");

            MachineReading machine = reading.Machine;
            var fewer = new MachineReading(machine.Register, machine.Next, machine.States, machine.Codes, machine.Start,
                machine.Arrows.Skip(1).ToList(), machine.Settings);
            StringAssert.Contains("no arrow", ControllerCheck.Why(new ControllerReading(reading.Module, fewer, reading.Tables, reading.Whole), design, netlist, top));
        }

        private const string MachineWithARom = @"
module lookup (input logic clk, rst, go, output logic [7:0] out);
    typedef enum logic [1:0] {IDLE, RUN, DONE} st_t;
    st_t state, nxt;
    logic [7:0] rom [0:3];
    assign out = rom[state];
    always_ff @(posedge clk, posedge rst)
        if (rst) state <= IDLE;
        else state <= nxt;
    always_comb begin
        nxt = state;
        case (state)
            IDLE: if (go) nxt = RUN;
            RUN: nxt = DONE;
            default: nxt = IDLE;
        endcase
    end
endmodule
";

        /// <summary>
        /// A machine beside a memory it reads is held to the engine and shown, never thrown from the frame that asks for it; and a
        /// check that throws leaves no reading behind to be shown unchecked (the 16d sweep: the memory's words had no values, the
        /// check threw, and every later ask returned the reading it never finished checking).
        /// </summary>
        [Test]
        public void AMachineBesideAMemory_IsShown_HeldToTheEngine()
        {
            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("lookup", MachineWithARom).Done);
            EditResult top = editor.SetTop("lookup");
            Assert.IsTrue(top.Done, top.Refusal);
            double now = 0;
            var session = new WorkbenchSession(editor.Project, () => now += 0.01);
            session.Finish();
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the drawing of its code");

            ControllerReading reading = null;
            Assert.DoesNotThrow(() => reading = session.Controller);
            Assert.IsNotNull(reading?.Machine, "the machine, borne out by the engine");
            CollectionAssert.AreEqual(new[] { "IDLE", "RUN", "DONE" }, reading.Machine.States);
        }

        private static bool Overlap(Vec2 aMin, Vec2 aMax, Vec2 bMin, Vec2 bMax) =>
            aMin.x <= bMax.x && bMin.x <= aMax.x && aMin.y <= bMax.y && bMin.y <= aMax.y;

        private static WorkbenchSession InsideTheControl(string sample, string program = null)
        {
            double now = 0;
            var session = new WorkbenchSession(WorkbenchSamples.All.Single(s => s.Name == sample).Build(), () => now += 0.01);
            if (program != null)
                Assert.IsTrue(session.Choose("PROGRAM", program).Done);
            session.Finish();
            Assert.IsTrue(session.StepInto(ControlOf(session)));
            Assert.IsTrue(session.ShowsDrawingOfCode, "the controller drawn");
            return session;
        }

        private static int ControlOf(WorkbenchSession session) =>
            session.DrawnModule.Parts.OfType<BlockPart>().Single(b => b.Module.EndsWith("_control", StringComparison.Ordinal)).Id;
    }
}
