using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Stepping inside a block (Part 12a): the session draws and edits the module of the instance stepped into, its wires
    /// read under that instance's path, while the run stays the whole design's; editing inside edits the module; and an
    /// edit that takes the instance away steps out of it.
    /// </summary>
    public class SteppingTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static WorkbenchSession FullAdder()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            return session;
        }

        private static int PartNamed(WorkbenchSession session, string name) => session.Project.Find(session.Module).FindPart(name).Id;

        private static string DesignPath(WorkbenchSession session) => BoardCompiler.Compile(session.Project).DesignPath;

        [Test]
        public void StepIntoABlock_DrawsItsModule_ReadUnderTheInstance_AndStepOut_DrawsTheTopAgain()
        {
            WorkbenchSession session = FullAdder();
            string design = DesignPath(session);
            int steps = session.StepVersion, layouts = session.LayoutVersion;
            RunSession run = session.Run;

            Assert.IsTrue(session.StepInto(PartNamed(session, "h1")));
            CollectionAssert.AreEqual(new[] { "h1" }, session.Path);
            Assert.AreEqual("half_adder", session.Module, "the canvas edits the half adder");
            Assert.AreEqual(steps + 1, session.StepVersion);
            Assert.AreEqual(layouts + 1, session.LayoutVersion, "and draws it");
            Assert.AreSame(run, session.Run, "the run is the whole design's, and goes on");
            Assert.IsFalse(session.Replaying, "stepping in changes nothing the circuit does");

            SchematicLayout inside = session.Run.Model.Layout;
            CollectionAssert.AreEquivalent(new[] { "a", "b", "s", "c" }, inside.Ports.Select(p => p.Name), "the half adder's ports");
            Design ran = session.Run.Model.Session.Design;
            foreach (PortLayout port in inside.Ports)
            {
                StringAssert.StartsWith(design + ".h1.", port.Signal, "read under h1");
                Assert.IsTrue(ran.TryFind(port.Signal, out _), $"{port.Signal} is a signal of the run");
            }

            Assert.IsTrue(session.StepOut());
            Assert.IsEmpty(session.Path);
            Assert.AreEqual("full_adder", session.Module);
            CollectionAssert.Contains(session.Run.Model.Layout.Ports.Select(p => p.Name).ToList(), "cout", "the top drawn again");
            Assert.IsFalse(session.StepOut(), "and there is nothing round the top");
        }

        [Test]
        public void OnlyABlockIsSteppedInto_AndAScopeOfTheDesign_ByItsPath()
        {
            WorkbenchSession session = FullAdder();
            string design = DesignPath(session);
            int or = session.Project.TopModule.Parts.Single(p => p is GatePart).Id;

            Assert.IsFalse(session.StepInto(or), "a gate is not a block");
            Assert.IsFalse(session.StepInto(12345), "nor is a part the module has not");
            Assert.IsEmpty(session.Path);

            Assert.IsTrue(session.StepToScope(design + ".h2"), "h2, as the hierarchy lists it");
            CollectionAssert.AreEqual(new[] { "h2" }, session.Path);
            Assert.IsFalse(session.StepToScope(design.Substring(0, design.LastIndexOf('.'))), "the testbench is not the design");
            Assert.IsFalse(session.StepToScope(design + ".h2.nothing"), "nor is anything that is not a block of it");
            CollectionAssert.AreEqual(new[] { "h2" }, session.Path, "and a refused step leaves the canvas where it was");

            Assert.IsTrue(session.StepToScope(design), "the design's own path is the top");
            Assert.IsEmpty(session.Path);
        }

        [Test]
        public void AnEditInside_EditsTheModule_SoEveryInstanceChanges()
        {
            WorkbenchSession session = FullAdder();
            string design = DesignPath(session);
            Assert.AreEqual(2, session.InstancesOf("half_adder"), "h1 and h2");
            int h1Before = SignalsUnder(session, design + ".h1."), h2Before = SignalsUnder(session, design + ".h2.");

            Assert.IsTrue(session.StepInto(PartNamed(session, "h1")));
            int parts = session.Project.Find("half_adder").Parts.Count, top = session.Project.TopModule.Parts.Count;
            int drawn = session.Run.Model.Layout.Parts.Count;

            Assert.IsTrue(session.Place(PartsCatalog.Find(session.Project, session.Module, "not"), new Placement(4, -4)).Done);
            Assert.AreEqual(parts + 1, session.Project.Find("half_adder").Parts.Count, "the half adder has the gate");
            Assert.AreEqual(top, session.Project.TopModule.Parts.Count, "and the top has nothing new");
            Assert.AreEqual(drawn + 1, session.Run.Model.Layout.Parts.Count, "drawn inside at once");

            session.Finish();
            CollectionAssert.AreEqual(new[] { "h1" }, session.Path, "the replay that took over is drawn inside too");
            Assert.AreEqual(drawn + 1, session.Run.Model.Layout.Parts.Count);
            int h1Added = SignalsUnder(session, design + ".h1.") - h1Before, h2Added = SignalsUnder(session, design + ".h2.") - h2Before;
            Assert.Greater(h1Added, 0, "the run has the gate's nets in h1");
            Assert.AreEqual(h1Added, h2Added, "and in h2: an edit inside is the module's, and every instance of it changes");
        }

        private static int SignalsUnder(WorkbenchSession session, string prefix) =>
            session.Run.Model.Session.Design.Signals.Count(s => s.Name.StartsWith(prefix, StringComparison.Ordinal));

        [Test]
        public void AnEditThatTakesTheInstanceAway_StepsOutOfIt()
        {
            WorkbenchSession session = FullAdder();
            int h1 = PartNamed(session, "h1");
            Assert.IsTrue(session.Remove(new[] { h1 }).Done);
            Assert.IsTrue(session.Undo(), "h1 back");
            Assert.IsTrue(session.StepInto(h1));
            int steps = session.StepVersion;

            Assert.IsTrue(session.Redo(), "h1 taken away again, while inside it");
            Assert.IsEmpty(session.Path, "stepped out to the top, the last module still there");
            Assert.AreEqual("full_adder", session.Module);
            Assert.Greater(session.StepVersion, steps, "which the view hears as a step");
        }

        [Test]
        public void TheCanvasLetsGoOfTheSelection_WhenItSteps()
        {
            WorkbenchSession session = FullAdder();
            var editor = new CanvasEditor(session);
            int or = session.Project.TopModule.Parts.Single(p => p is GatePart).Id;
            editor.Selection.Pick(new CanvasTarget(CanvasTargetKind.Part, part: or), false, false);

            Assert.IsFalse(editor.StepInto(new CanvasTarget(CanvasTargetKind.Part, part: or)), "the OR is not a block");
            Assert.IsTrue(editor.Selection.HasPart(or), "and a refused step changes nothing");

            Assert.IsTrue(editor.StepInto(new CanvasTarget(CanvasTargetKind.Part, part: PartNamed(session, "h1"))));
            Assert.IsTrue(editor.Selection.IsEmpty, "a part's id in one module is another's in the next");
            Assert.IsTrue(editor.StepOut());
            Assert.IsFalse(editor.StepOut());
        }

        [Test]
        public void AnArray_IsSteppedIntoAtItsFirstCopy_OrAtAnyByItsScope()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FourHalfAdders(), Ticking(0.01));
            session.Finish();
            string design = DesignPath(session);
            int ha = session.Project.TopModule.Parts.OfType<BlockPart>().Single().Id;

            Assert.IsTrue(session.StepInto(ha));
            CollectionAssert.AreEqual(new[] { "ha[0]" }, session.Path, "an array is stepped into at its first copy");
            Assert.AreEqual("half_adder", session.Module);
            foreach (PortLayout port in session.Run.Model.Layout.Ports)
            {
                StringAssert.StartsWith(design + ".ha[0].u.", port.Signal, "read under the copy's generate block and the instance in it");
                Assert.IsTrue(session.Run.Model.Session.Design.TryFind(port.Signal, out _), port.Signal);
            }

            Assert.AreEqual(4, session.InstancesOf("half_adder"), "an edit inside is every copy's");
            Assert.IsTrue(session.StepToScope(design + ".ha[2].u"), "a copy by its instance's scope");
            CollectionAssert.AreEqual(new[] { "ha[2]" }, session.Path);
            Assert.IsTrue(session.StepToScope(design + ".ha[3]"), "or by its generate block's");
            CollectionAssert.AreEqual(new[] { "ha[3]" }, session.Path);
            Assert.IsFalse(session.StepToScope(design + ".ha[4]"), "and no copy past the last");
        }

        [Test]
        public void ABlocksCopies_AreInItsInspector_AndAppliedThere()
        {
            var editor = new BoardEditor(WorkbenchSamples.FullAdder());
            Module top = editor.Project.TopModule;
            int placed = editor.Add(top.Name, new BlockPart { Module = "half_adder" }, new Placement(4, -6)).Id;

            Inspection inspection = Inspector.Of(editor.Project, top.Name, new CanvasTarget(CanvasTargetKind.Part, part: placed));
            Assert.AreEqual("1", inspection.Fields.Single(f => f.Key == "copies").Text);
            CollectionAssert.Contains(inspection.Quick.Select(f => f.Key).ToList(), "copies", "at hand in the quick panel");

            Assert.IsTrue(Inspector.Apply(editor, top.Name, inspection, new Dictionary<string, string> { ["copies"] = "4" }).Done);
            Assert.AreEqual(4, ((BlockPart)editor.Project.TopModule.FindPart(placed)).Copies);
            StringAssert.Contains("half_adder ×4", DocumentSchematic.NameOf(editor.Project.TopModule.FindPart(placed)), "labelled with its count");

            inspection = Inspector.Of(editor.Project, top.Name, new CanvasTarget(CanvasTargetKind.Part, part: placed));
            Assert.IsFalse(Inspector.Apply(editor, top.Name, inspection, new Dictionary<string, string> { ["copies"] = "65" }).Done, "64 at most");
        }

        [Test]
        public void InsideABlock_ThePartsListOffersNoModuleThatHoldsIt()
        {
            WorkbenchSession session = FullAdder();
            Assert.IsTrue(PartsCatalog.For(session.Project, session.Module).Any(e => e.Module == "half_adder"), "at the top, the half adder is a part to place");

            Assert.IsTrue(session.StepInto(PartNamed(session, "h1")));
            Assert.IsFalse(PartsCatalog.For(session.Project, session.Module).Any(e => e.Kind == CatalogKind.Block),
                "inside the half adder, neither itself nor the full adder round it: either would put a module inside itself");
        }

        [Test]
        public void InsideABlock_ThePartsListOffersNoWrittenModuleWhoseTextHoldsIt()
        {
            WorkbenchSession session = FullAdder();
            string wrapper = "module wrapper (\n    input logic a, b,\n    output logic s, c\n);\n    half_adder h (.a(a), .b(b), .s(s), .c(c));\nendmodule\n";
            Assert.IsTrue(session.Editor.AddWrittenModule("wrapper", wrapper).Done, string.Join("; ", session.Editor.Diagnostics));
            Assert.IsTrue(PartsCatalog.For(session.Project, session.Module).Any(e => e.Module == "wrapper"), "at the top it is a part to place");

            Assert.IsTrue(session.StepInto(PartNamed(session, "h1")));
            Assert.IsFalse(PartsCatalog.For(session.Project, session.Module).Any(e => e.Module == "wrapper"),
                "inside the half adder, a module whose text instantiates the half adder would put it inside itself");
        }

        [Test]
        public void AltUp_StepsOut_IsTheCircuitsKey_AndAltKeysTheBrowserKeepsAreRefused()
        {
            KeyBinding up = WorkbenchKeys.For(WorkbenchCommand.StepOut);
            Assert.AreEqual("UpArrow", up.Key);
            Assert.IsTrue(up.Alt && !up.Ctrl && !up.Shift);
            Assert.IsNull(WorkbenchKeys.WhyRefused(up));
            Assert.AreEqual("CIRCUIT", KeyMap.GroupOf(WorkbenchCommand.StepOut));

            Assert.IsNotNull(WorkbenchKeys.WhyRefused(new KeyBinding("LeftArrow", WorkbenchCommand.StepOut, "ALT LEFT", "back", alt: true)),
                "Alt+Left is the browser's back");
            Assert.IsTrue(WorkbenchKeys.All.Any(b => b.Key == "UpArrow" && !b.Alt && b.Command == WorkbenchCommand.PageUp),
                "Up alone still scrolls the waves");
        }
    }
}
