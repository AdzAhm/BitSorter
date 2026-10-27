using System;
using System.Linq;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
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

            // Powers of ten are exact (the 14b review: in binary floating point 1 ns read "1000 MHz").
            Assert.AreEqual("1 GHz", TimingView.Frequency(1, "ns"));
            Assert.AreEqual("1 GHz", TimingView.Frequency(1000, "ps"));
            Assert.AreEqual("1 MHz", TimingView.Frequency(1000, "ns"));
            Assert.AreEqual("1 PHz", TimingView.Frequency(1, "fs"));
            Assert.AreEqual("999.9 Hz", TimingView.Frequency(1000100, "ns"));
            Assert.AreEqual("1 kHz", TimingView.Frequency(1000001, "ns"), "999.999 Hz shown is 1 kHz, never 1000 Hz");
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

        // -----------------------------------------------------------------
        // The review's cases
        // -----------------------------------------------------------------

        private static int Must(EditResult result)
        {
            Assert.IsTrue(result.Done, result.Refusal);
            return result.Id;
        }

        private static int Add(BoardEditor editor, string module, Part part, double x, double y) =>
            Must(editor.Add(module, part, new Placement(new BitSorter.Board.Point(x, y))));

        private static void Wire(BoardEditor editor, string module, int from, string fromPin, int to, string toPin) =>
            Must(editor.Connect(module, Endpoint.At(from, fromPin), Endpoint.At(to, toPin)));

        /// <summary>
        /// A register in a block: a NOT outside feeds it, its output leaves for y. From the top, the input-to-register path
        /// lights the NOT, the wire into the block and the block -- the path ends inside it; and a register in a second block
        /// toggling itself through a NOT of its own has its critical path wholly inside the block, which lights.
        /// </summary>
        [Test]
        public void APathIntoABlock_OrWhollyInsideOne_LightsTheBlock()
        {
            var editor = new BoardEditor(Project.New("top"));
            Must(editor.AddModule("keep"));
            int d = Add(editor, "keep", new PortPart { Name = "d" }, 0, 0);
            int kclk = Add(editor, "keep", new PortPart { Name = "clk" }, 0, -2);
            int q = Add(editor, "keep", new PortPart { Name = "q", Direction = PortDirection.Output }, 6, 0);
            int r = Add(editor, "keep", new RegisterPart(), 3, 0);
            Wire(editor, "keep", d, "out", r, "d");
            Wire(editor, "keep", kclk, "out", r, "clk");
            Wire(editor, "keep", r, "q", q, "in");

            Must(editor.AddModule("toggle"));
            int tclk = Add(editor, "toggle", new PortPart { Name = "clk" }, 0, -2);
            int tq = Add(editor, "toggle", new PortPart { Name = "q", Direction = PortDirection.Output }, 8, 0);
            int tr = Add(editor, "toggle", new RegisterPart { Tcq = 1 }, 3, 0);
            int tn = Add(editor, "toggle", new GatePart { Kind = Sim.GateKind.Not, Inputs = 1, Rise = 5, Fall = 5 }, 6, 2);
            Wire(editor, "toggle", tclk, "out", tr, "clk");
            Wire(editor, "toggle", tr, "q", tn, "a");
            Wire(editor, "toggle", tn, "y", tr, "d");
            Wire(editor, "toggle", tr, "q", tq, "in");

            int a = Add(editor, "top", new PortPart { Name = "a" }, 0, 0);
            int clk = Add(editor, "top", new PortPart { Name = "clk" }, 0, -3);
            int y = Add(editor, "top", new PortPart { Name = "y", Direction = PortDirection.Output }, 14, 0);
            int t = Add(editor, "top", new PortPart { Name = "t", Direction = PortDirection.Output }, 14, -6);
            int g = Add(editor, "top", new GatePart { Kind = Sim.GateKind.Not, Inputs = 1, Rise = 1, Fall = 1 }, 3, 0);
            int u = Add(editor, "top", new BlockPart { Name = "u", Module = "keep" }, 7, 0);
            int v = Add(editor, "top", new BlockPart { Name = "v", Module = "toggle" }, 7, -6);
            Wire(editor, "top", a, "out", g, "a");
            Wire(editor, "top", g, "y", u, "d");
            Wire(editor, "top", clk, "out", u, "clk");
            Wire(editor, "top", clk, "out", v, "clk");
            Wire(editor, "top", u, "q", y, "in");
            Wire(editor, "top", v, "q", t, "in");
            Project project = editor.Project;

            TimingView view = TimingView.Of(project);
            SchematicLayout layout = DocumentSchematic.Build(project, BoardCompiler.Compile(project));

            TimingLine inward = view.Lines.Single(l => l.Label == "INPUT TO REGISTER");
            TimingLight into = TimingLight.Of(layout, inward.Paths);
            CollectionAssert.AreEquivalent(new[] { g, u }, into.Parts, "the NOT, and the block the path ends in");
            Assert.AreEqual(2, into.Branches.Count, "a into the NOT, the NOT into the block");
            CollectionAssert.AreEqual(new[] { "a" }, into.Ports.Select(p => layout.Ports[p].Name), "where it comes in, and no port it does not reach");

            TimingLine critical = view.Lines[view.Critical];
            StringAssert.Contains("v.", critical.Text, "the toggle's own loop, the slowest: 1 + 5");
            TimingLight inside = TimingLight.Of(layout, critical.Paths);
            CollectionAssert.AreEquivalent(new[] { v }, inside.Parts, "wholly inside v: v lights");
            Assert.IsEmpty(inside.Ports, "and t, which v's register also drives, is no part of it");
        }

        /// <summary>The counter's critical path is named by where it ends, the register's d bit -- never by its output's bit 0.</summary>
        [Test]
        public void ARegisterPath_IsNamedByTheBitItEndsAt()
        {
            TimingView view = TimingView.Of(WorkbenchSamples.Counter());
            StringAssert.Contains("→ next[2]", view.Lines[view.Critical].Text, "into bit 2 of the register's d");
        }

        /// <summary>
        /// Two lines that read the same -- a through a NOT of 2 or one of 3, then the OR -- are two paths: the one picked is
        /// found again by its signals, never by its words.
        /// </summary>
        [Test]
        public void TwoLinesThatReadTheSame_AreFoundApart()
        {
            var editor = new BoardEditor(Project.New("twins"));
            int a = Add(editor, "twins", new PortPart { Name = "a" }, 0, 0);
            int y = Add(editor, "twins", new PortPart { Name = "y", Direction = PortDirection.Output }, 9, 0);
            int slow = Add(editor, "twins", new GatePart { Kind = Sim.GateKind.Not, Inputs = 1, Rise = 3, Fall = 3 }, 3, 2);
            int fast = Add(editor, "twins", new GatePart { Kind = Sim.GateKind.Not, Inputs = 1, Rise = 2, Fall = 2 }, 3, -2);
            int or = Add(editor, "twins", new GatePart { Kind = Sim.GateKind.Or, Rise = 1, Fall = 1 }, 6, 0);
            Wire(editor, "twins", a, "out", slow, "a");
            Wire(editor, "twins", a, "out", fast, "a");
            Wire(editor, "twins", slow, "y", or, "a");
            Wire(editor, "twins", fast, "y", or, "b");
            Wire(editor, "twins", or, "y", y, "in");

            TimingView view = TimingView.Of(editor.Project);
            TimingLine[] paths = view.Lines.SkipWhile(l => l.Label != "PATHS").Skip(1).ToArray();
            Assert.AreEqual(2, paths.Length);
            Assert.AreEqual(paths[0].Text, paths[1].Text, "sanity: they read alike");

            TimingView again = TimingView.Of(editor.Project);
            int faster = view.Lines.ToList().IndexOf(paths[1]);
            Assert.AreEqual(faster, again.Find(paths[1]), "the faster found as the faster");
        }

        /// <summary>
        /// A part moved times as before: the session's timing is not worked out again (the review timed a processor at 200 ms
        /// a time). A drawn gate four bits wide counts four: the reference's NOT bus is four NOTs.
        /// </summary>
        [Test]
        public void AMove_IsNotWorkedOutAgain_AndAWideGateCountsABit()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            TimingView before = session.Timing;
            GatePart or = session.Project.TopModule.Parts.OfType<GatePart>().Single();
            Placement was = session.Project.TopModule.Layout.PlacementOf(or.Id);
            Assert.IsTrue(session.Move(new System.Collections.Generic.Dictionary<int, Placement>
            {
                [or.Id] = new Placement(new BitSorter.Board.Point(was.Position.X, was.Position.Y - 1)),
            }).Done);
            Assert.AreNotSame(before.Project, session.Project, "sanity: the document changed");
            Assert.AreSame(before.Lines, session.Timing.Lines, "and its timing was not worked out again");

            Project reference = ReferenceCircuit.Document();
            int bits = reference.Modules.SelectMany(m => m.Parts).OfType<GatePart>().Sum(g => g.Width);
            Assert.Greater(bits, reference.Modules.SelectMany(m => m.Parts).OfType<GatePart>().Count(), "sanity: a gate wider than a bit");
            StringAssert.StartsWith(bits + " gates:", TimingView.Of(reference).Lines.Single(l => l.Label == "GATE COUNT").Text);
        }

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
