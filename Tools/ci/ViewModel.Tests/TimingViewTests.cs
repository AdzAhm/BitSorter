using System;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The TIMING tab (Part 14b): a document's timing as lines -- the critical path, the minimum period and the frequency it
    /// gives, the gate count, every path -- and what of a path picked the canvas lights, outside a block and inside it. The
    /// workbench's own samples, each delay worked out by hand.
    /// </summary>
    public class TimingViewTests
    {
        private static Func<double> Ticking(double step)
        {
            double now = 0;
            return () => { double read = now; now += step; return read; };
        }

        private static string Unit(Project project) => project.Settings?.TimeUnit ?? TimeUnits.Default;

        /// <summary>
        /// The full adder: each half adder's XOR 2 and AND 1, the OR 1. a through both XORs to s is 4, and through h1's XOR,
        /// h2's AND and the OR to cout is 4 as well; cout comes first of the two by name. Five gates.
        /// </summary>
        [Test]
        public void TheFullAdder_ItsCriticalPath_GateCount_AndPaths()
        {
            Project project = WorkbenchSamples.FullAdder();
            TimingView view = TimingView.Of(project);

            TimingLine critical = view.Lines[view.Critical];
            Assert.AreEqual("CRITICAL PATH", critical.Label);
            StringAssert.StartsWith("a → cout", critical.Text);
            StringAssert.Contains("through XOR2, AND2, OR2", critical.Text);
            Assert.AreEqual("rise 4 · fall 4 " + Unit(project), critical.Numbers);
            CollectionAssert.AreEqual(new[] { "a", "h1.a" }, critical.Signals.Take(2), "named from the design's top");

            Assert.AreEqual("5 gates: 2 AND2, 2 XOR2, 1 OR2", view.Lines.Single(l => l.Label == "GATE COUNT").Text);
            Assert.IsFalse(view.Lines.Any(l => l.Label == "MINIMUM PERIOD"), "no registers, no clock");

            TimingLine[] paths = view.Lines.SkipWhile(l => l.Label != "PATHS").Skip(1).ToArray();
            Assert.That(paths, Has.All.Property(nameof(TimingLine.CanPick)).True);
            StringAssert.StartsWith("a → cout, b → cout", paths[0].Text, "a and b alike, through the same gates");
        }

        /// <summary>
        /// The counter: q[0] through the AND (1) and the second XOR (2) into the register -- 3 of logic, the register ideal, so
        /// the minimum period is 3. A clock to Q of 2 and a setup of 1 make it 6, and the frequency follows.
        /// </summary>
        [Test]
        public void TheCounter_ItsMinimumPeriod_AndFrequency_FollowItsRegistersTiming()
        {
            Project project = WorkbenchSamples.Counter();
            string unit = Unit(project);
            TimingView view = TimingView.Of(project);

            TimingLine period = view.Lines.Single(l => l.Label == "MINIMUM PERIOD");
            Assert.AreEqual("3 " + unit, period.Numbers);
            Assert.AreEqual("clock to Q 0 + logic 3 + setup 0 " + unit, period.Text);
            Assert.AreEqual("CRITICAL PATH", view.Lines[view.Critical].Label);

            Module top = project.TopModule;
            Project timed = project.WithModule(top with
            {
                Parts = top.Parts.Select(p => p is RegisterPart r ? r with { Tcq = 2, Tsetup = 1 } : p).ToList(),
            });
            TimingView again = TimingView.Of(timed);
            Assert.AreEqual("6 " + unit, again.Lines.Single(l => l.Label == "MINIMUM PERIOD").Numbers);
            Assert.AreEqual(TimingView.Frequency(6, unit), again.Lines.Single(l => l.Label == "MAXIMUM FREQUENCY").Numbers);
        }

        [Test]
        public void AFrequency_IsInTheUnitThatKeepsItOverOne()
        {
            Assert.AreEqual("166.7 MHz", TimingView.Frequency(6, "ns"));
            Assert.AreEqual("2.5 GHz", TimingView.Frequency(400, "ps"));
            Assert.AreEqual("1 THz", TimingView.Frequency(1, "ps"));
            Assert.AreEqual("50 Hz", TimingView.Frequency(20, "ms"));
        }

        [Test]
        public void ACircuitWithErrors_SaysSo_AndHasNothingToPick()
        {
            Project project = WorkbenchSamples.FullAdder();
            Module top = project.TopModule;
            Project broken = project.WithModule(top with { Parts = top.Parts.Select(p => p is PortPart port && port.Name == "s" ? port with { Name = "a" } : p).ToList() });

            TimingView view = TimingView.Of(broken);
            Assert.AreEqual(TimingLineKind.Problem, view.Lines.Single().Kind);
            Assert.AreEqual(-1, view.Critical);
        }

        /// <summary>
        /// Drawn from the top, the full adder's critical path passes through both blocks and the OR, from port a to port
        /// cout; drawn inside h1, the same path is h1's XOR, between h1's own a and s.
        /// </summary>
        [Test]
        public void ThePathPicked_LightsWhatEachDrawingHasOfIt()
        {
            Project project = WorkbenchSamples.FullAdder();
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            CompiledProject compiled = BoardCompiler.Compile(project);

            SchematicLayout top = DocumentSchematic.Build(project, compiled);
            TimingLight light = TimingLight.Of(top, critical.Signals);
            Module module = project.TopModule;
            CollectionAssert.AreEquivalent(new[] { "h1", "h2", module.Parts.OfType<GatePart>().Single().Name },
                light.Parts.Select(id => module.FindPart(id).Name));
            CollectionAssert.AreEquivalent(new[] { "a", "cout" }, light.Ports.Select(p => top.Ports[p].Name));
            Assert.AreEqual(4, light.Branches.Count, "a into h1, h1's sum into h2, h2's carry into the OR, the OR into cout");

            SchematicLayout inside = DocumentSchematic.Build(project, compiled, "half_adder", compiled.DesignPath + ".h1");
            TimingLight within = TimingLight.Of(inside, critical.Signals);
            Module half = project.Find("half_adder");
            CollectionAssert.AreEqual(new[] { EngineGateName(Sim.GateKind.Xor) },
                within.Parts.Select(id => ((GatePart)half.FindPart(id)).Kind.ToString()));
            CollectionAssert.AreEquivalent(new[] { "a", "s" }, within.Ports.Select(p => inside.Ports[p].Name));

            SchematicLayout second = DocumentSchematic.Build(project, compiled, "half_adder", compiled.DesignPath + ".h2");
            TimingLight there = TimingLight.Of(second, critical.Signals);
            CollectionAssert.AreEqual(new[] { EngineGateName(Sim.GateKind.And) },
                there.Parts.Select(id => ((GatePart)half.FindPart(id)).Kind.ToString()), "in h2 it is the AND, to the carry");
        }

        private static string EngineGateName(Sim.GateKind kind) => kind.ToString();

        /// <summary>The counter's critical path starts at the register and ends at it: both ends light the register.</summary>
        [Test]
        public void ARegisterToRegisterPath_LightsTheRegister()
        {
            Project project = WorkbenchSamples.Counter();
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            SchematicLayout layout = DocumentSchematic.Build(project, BoardCompiler.Compile(project));

            TimingLight light = TimingLight.Of(layout, critical.Signals);
            Module module = project.TopModule;
            Assert.IsTrue(light.Parts.Any(id => module.FindPart(id) is RegisterPart), "the register it starts and ends at");
            Assert.IsTrue(light.Parts.Any(id => module.FindPart(id) is GatePart g && g.Kind == Sim.GateKind.And), "and the AND on the carry");
        }

        [Test]
        public void TheSessionsTiming_IsWorkedOutAgain_OnlyWhenTheDocumentChanges()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            TimingView before = session.Timing;
            Assert.AreSame(before, session.Timing, "nothing changed, nothing worked out again");

            TimingLine picked = before.Lines[before.Critical];
            Assert.IsTrue(session.Place(PartsCatalog.For(session.Project, session.Module).Single(e => e.Key == "and"), new Placement(20, 20)).Done);
            TimingView after = session.Timing;
            Assert.AreNotSame(before, after, "an edit works it out again");
            Assert.AreEqual(picked.Text, after.Lines[after.Find(picked)].Text, "and the line picked is found again");
        }
    }
}
