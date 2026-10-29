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

        /// <summary>
        /// TIMING shows the period CHECK holds an exercise to (Part 15g): the minimum period where register to register is the
        /// slowest, and CLOCK PERIOD beside it where an input's or an output's path is -- for every clocked exercise's
        /// solution, the number CHECK's grade gives. The two could differ unseen (decision 6, 2026-10-28).
        /// </summary>
        [Test]
        public void EveryClockedExercise_TimingShowsThePeriodCheckUses()
        {
            int clocked = 0;
            foreach (string id in Board.Tests.ExerciseFiles.Ids())
            {
                Exercise exercise = Board.Tests.ExerciseFiles.Load(id);
                Project solution = Board.Tests.ExerciseFiles.Solution(exercise);
                Grade grade = ExerciseGrader.Grade(exercise, solution);
                if (grade.MinimumPeriod is not long period)
                    continue;
                clocked++;

                TimingView view = TimingView.Of(solution);
                TimingLine shown = view.Lines.FirstOrDefault(l => l.Label == "CLOCK PERIOD") ?? view.Lines.Single(l => l.Label == "MINIMUM PERIOD");
                string unit = solution.Settings?.TimeUnit ?? TimeUnits.Default;
                StringAssert.StartsWith(period.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", shown.Numbers,
                    $"{id}: TIMING's {shown.Label} against CHECK's {period} {unit}");
            }
            Assert.Greater(clocked, 5, "sanity: the clocked exercises were graded");
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

        /// <summary>
        /// A bus's critical path through its second driver lights that driver (the 14f review: the drawing roots a bus at
        /// its first, and the light took the root for every path). The bus says both drivers, and neither as a load.
        /// </summary>
        [Test]
        public void APathThroughABussSecondDriver_LightsThatDriver()
        {
            Project bus = BitSorter.Board.Tests.SampleBoards.Build("bus");
            var editor = new BoardEditor(bus);
            GatePart second = bus.TopModule.Parts.OfType<GatePart>().Last();
            Assert.IsTrue(editor.Change(bus.Top, second with { Rise = 7, Fall = 9 }).Done);
            Project project = editor.Project;

            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            StringAssert.Contains("b → y", critical.Text, "sanity: through the slow one");
            SchematicLayout layout = DocumentSchematic.Build(project, BoardCompiler.Compile(project));
            TimingLight light = TimingLight.Of(layout, critical.Signals);
            Assert.IsTrue(light.Parts.Contains(second.Id), "the driver the path goes through");

            NetLayout net = layout.Nets.Single(n => n.Drivers.Count == 2);
            StringAssert.Contains(" and ", net.Says);
            StringAssert.EndsWith("to output y", net.Says, "no driver read as a load");
        }

        /// <summary>
        /// A device on a bus flashes for its own output, not for the bus (the 14g-4 review): the bus flips for every driver on
        /// it, and the first pin out of a device was its inout. A device with no output of its own flashes for its inout's wire.
        /// </summary>
        [Test]
        public void ADeviceOnABus_FlashesForItsOwnOutput_AndOneWithNoneForItsInout()
        {
            Project shared = BitSorter.Board.Tests.SampleBoards.Build("devices_bus");
            CompiledProject compiled = BoardCompiler.Compile(shared);
            SchematicLayout layout = DocumentSchematic.Build(shared, compiled);
            PartLayout u2 = layout.Parts.Single(p => p.Part is BlockPart && p.Part.Name == "u2");
            Assert.AreEqual(compiled.DesignPath + ".seen", u2.Output, "u2's output, wired to seen");

            var editor = new BoardEditor(Project.New("top"));
            Assert.IsTrue(editor.AddWrittenModule("drv",
                "module drv (\n" +
                "    input logic en,\n" +
                "    input logic d,\n" +
                "    inout wire io\n" +
                ");\n" +
                "    bufif1 #1 g (io, d, en);\n" +
                "endmodule\n").Done);
            int u1 = Add(editor, "top", new BlockPart { Module = "drv" }, 2, 0);
            int y = Add(editor, "top", new PortPart { Name = "y", Direction = PortDirection.Output }, 8, 0);
            Assert.IsTrue(editor.Connect("top", Endpoint.At(u1, "io"), Endpoint.At(y, "in")).Done);
            Project project = editor.Project;
            CompiledProject only = BoardCompiler.Compile(project);
            PartLayout device = DocumentSchematic.Build(project, only).Parts.Single(p => p.Part is BlockPart);
            Assert.AreEqual(only.DesignPath + ".y", device.Output, "no output of its own: its inout's wire");
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

        /// <summary>
        /// The unit changed, the TIMING tab says its times in the new one: the design is the same, but its words are not
        /// (the 14c review: the critical path still read "ps" beside a measured worst in "ns").
        /// </summary>
        [Test]
        public void AChangeOfUnit_IsSaidInTheNewUnit()
        {
            var session = new WorkbenchSession(WorkbenchSamples.FullAdder(), Ticking(0.01));
            session.Finish();
            Assert.AreEqual("ps", Unit(session.Project), "sanity: counted in ps");
            Assert.IsTrue(session.Timing.Lines[session.Timing.Critical].Numbers.EndsWith(" ps"));

            Assert.IsTrue(session.SetUnit("ns").Done);
            StringAssert.EndsWith(" ns", session.Timing.Lines[session.Timing.Critical].Numbers);
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

        /// <summary>
        /// The project's files are part of its design (the 15g-6 review): an include added builds what could not be built, and
        /// one changed builds another circuit, so neither keeps the timing worked out before -- where the same files again,
        /// as a document opened a second time reads them, are the same design. Each view made by the rule the session
        /// applies (<see cref="WorkbenchSession.Timing"/>): the old view for the new project when the design is the same.
        /// </summary>
        [Test]
        public void AFileAddedOrChanged_IsWorkedOutAgain_TheSameFilesAgainAreNot()
        {
            static TimingView Next(TimingView before, Project project) =>
                TimingView.SameDesign(before.Project, project) ? before.For(project) : TimingView.Of(project);
            static ProjectFile[] Defs(string body) => new[] { new ProjectFile { Name = "defs.svh", Text = "`define BODY " + body + "\n" } };

            Project missing = Project.New("top").WithModule(new Module
            {
                Name = "top",
                Source = "`include \"defs.svh\"\nmodule top (input logic a, input logic b, output logic y);\n    `BODY\nendmodule\n",
            });
            TimingView cannot = TimingView.Of(missing);
            Assert.IsFalse(cannot.Analysis.Succeeded, "sanity: without its include the design does not build");

            Project one = missing with { Files = Defs("and g (y, a, b);") };
            Assert.IsTrue(TimingView.Of(one).Analysis.Succeeded, "sanity: with it, it does: " + string.Join(" / ", TimingView.Of(one).Lines));
            TimingView built = Next(cannot, one);
            Assert.IsTrue(built.Analysis.Succeeded, "the include added, it builds: " + string.Join(" / ", built.Lines));

            Project two = one with { Files = Defs("wire n; and g1 (n, a, b); not g2 (y, n);") };
            Assert.AreEqual(2, TimingView.Of(two).Analysis.Gates, "sanity: the changed include is two gates");
            Assert.AreEqual(2, Next(built, two).Analysis.Gates, "the include changed, it is counted again");

            Project again = one with { Files = Defs("and g (y, a, b);") };
            Assert.AreNotSame(one.Files, again.Files, "sanity: the same files, read a second time");
            Assert.AreSame(built.Lines, Next(built, again).Lines, "the same files again are the same design, and cost nothing");
        }

        // -----------------------------------------------------------------
        // A drawing of code (Ahmad, 2026-10-29: "the yellow line indicating critical path isnt in everything")
        // -----------------------------------------------------------------

        private const string WrittenFullAdder =
            "module fa (input logic a, b, cin, output logic s, cout);\n" +
            "    assign #2 s = a ^ b ^ cin;\n" +
            "    assign #3 cout = (a & b) | (cin & (a ^ b));\n" +
            "endmodule\n";

        private const string WrittenTwoBitAdder =
            "module add2 (input logic [1:0] a, b, input logic cin, output logic [1:0] s, output logic cout);\n" +
            "    logic c1;\n" +
            "    fa f0 (.a(a[0]), .b(b[0]), .cin(cin), .s(s[0]), .cout(c1));\n" +
            "    fa f1 (.a(a[1]), .b(b[1]), .cin(c1), .s(s[1]), .cout(cout));\n" +
            "endmodule\n";

        private static Project Written(string top, params (string Name, string Code)[] modules)
        {
            var editor = new BoardEditor(Project.New("top"));
            foreach ((string name, string code) in modules)
                Must(editor.AddWrittenModule(name, code));
            Must(editor.SetTop(top));
            return editor.Project;
        }

        /// <summary>The layout the workbench draws <paramref name="project"/>'s written top as: the drawing of its code.</summary>
        private static SchematicLayout DrawingOf(Project project)
        {
            var session = new WorkbenchSession(project, Ticking(0.01));
            session.Finish();
            session.DrawCode(true);
            Assert.IsTrue(session.ShowsDrawingOfCode, "sanity: the drawing of the code");
            return session.Run.Model.Layout;
        }

        private static string NameIn(SchematicLayout layout, int part) => layout.Parts.Single(p => p.Part.Id == part).Name;

        /// <summary>
        /// One assign is one step of a path, from a signal it reads to the signal it drives, and on its drawing that step is
        /// a run of parts joined by wires no signal holds: a -> cout goes through the ^, the &amp; it feeds and the |. Every one
        /// of them is lit, and the wires between them -- the longest way through the assign, as a path through gates is.
        /// </summary>
        [Test]
        public void APathThroughAnAssign_LightsEveryPartOfItOnTheDrawingOfItsCode()
        {
            Project project = Written("fa", ("fa", WrittenFullAdder));
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            StringAssert.StartsWith("a → cout", critical.Text, "sanity: through the slower assign");
            SchematicLayout layout = DrawingOf(project);

            TimingLight light = TimingLight.Of(layout, critical.Signals);
            CollectionAssert.AreEquivalent(new[] { "^", "&", "|" }, light.Parts.Select(id => NameIn(layout, id)),
                "the gates the path goes through, and only one of each");
            int xor = light.Parts.Single(id => NameIn(layout, id) == "^");
            int and = light.Parts.Single(id => NameIn(layout, id) == "&");
            Assert.IsTrue(layout.Nets.Any(n => n.Source.Part == xor && n.Loads.Any(l => l.Part == and)),
                "the AND lit is the one the XOR feeds: the longer way to cout");
            CollectionAssert.AreEquivalent(new[] { "a", "cout" }, light.Ports.Select(p => layout.Ports[p].Name));
            Assert.AreEqual(4, light.Branches.Count, "a into the ^, the ^ into the &, the & into the |, the | into cout");
        }

        /// <summary>
        /// A path into an instance of written code goes through the slice that takes its bit out of the bus: the slice is lit,
        /// and the instance, and the carry on to the next one -- not the bus's port alone.
        /// </summary>
        [Test]
        public void APathIntoAnInstanceThroughASlice_LightsTheSliceAndTheInstance()
        {
            Project project = Written("add2", ("fa", WrittenFullAdder), ("add2", WrittenTwoBitAdder));
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            StringAssert.StartsWith("a[0] → cout", critical.Text, "sanity: in at the first copy, out of the second's carry");
            SchematicLayout layout = DrawingOf(project);

            TimingLight light = TimingLight.Of(layout, critical.Signals);
            string[] lit = light.Parts.Select(id => layout.Parts.Single(p => p.Part.Id == id).Part.Name).ToArray();   // an instance by its own name
            CollectionAssert.IsSubsetOf(new[] { "f0", "f1" }, lit, "both copies the carry runs through");
            int f0 = layout.Parts.Single(p => p.Part is BlockPart && p.Part.Name == "f0").Part.Id;
            Assert.IsTrue(light.Parts.Any(id => layout.Parts.Single(p => p.Part.Id == id).Part is BoxPart
                    && layout.Nets.Any(n => n.Source.Part == id && n.Loads.Any(l => l.Part == f0))),
                "and the slice that hands a[0] to f0");
            CollectionAssert.AreEquivalent(new[] { "a", "cout" }, light.Ports.Select(p => layout.Ports[p].Name.Split('[')[0]));
        }

        private const string WrittenRipple =
            "module rca (input logic [3:0] a, b, input logic cin, output logic [3:0] s, output logic cout);\n" +
            "    logic [4:0] c;\n" +
            "    assign c[0] = cin;\n" +
            "    genvar i;\n" +
            "    generate for (i = 0; i < 4; i++) begin : bit_loop\n" +
            "        fa u (.a(a[i]), .b(b[i]), .cin(c[i]), .s(s[i]), .cout(c[i+1]));\n" +
            "    end endgenerate\n" +
            "    assign cout = c[4];\n" +
            "endmodule\n";

        /// <summary>
        /// A carry through a generate loop's copies, as in a ripple adder: each copy's carry joined into the bus c and sliced
        /// out of it into the next. The copies are one block since Part 15h-4e, and it is lit, with c along the way.
        /// </summary>
        [Test]
        public void APathThroughAGenerateLoopsCopies_LightsEveryCopy()
        {
            Project project = Written("rca", ("fa", WrittenFullAdder), ("rca", WrittenRipple));
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            StringAssert.StartsWith("a[0] → cout", critical.Text, "sanity: in at the first copy, out of the carry");
            SchematicLayout layout = DrawingOf(project);

            TimingLight light = TimingLight.Of(layout, critical.Signals);
            BlockPart copies = light.Parts.Select(id => layout.Parts.Single(p => p.Part.Id == id).Part).OfType<BlockPart>().Single();
            Assert.AreEqual(4, copies.Copies, "the four copies, one block, lit");
            int bus = Enumerable.Range(0, layout.Nets.Count).Single(n => layout.Nets[n].Name == "c");
            Assert.GreaterOrEqual(light.Branches.Count(b => b.Net == bus), 1, "the carry along c, back into the block");
        }

        /// <summary>
        /// A gate the code names, out of a bus's bit (the review of the fix): its output holds no signal, and the drawing names
        /// it with a $ as every such wire, so the path a[1] -> y[1] is lit through the slice, the gate and the join -- where
        /// left to the netlist it was g1_y, and the light stopped at the port.
        /// </summary>
        [Test]
        public void APathThroughAGateTheCodeNames_LightsTheGate()
        {
            Project project = Written("pr", ("pr",
                "module pr (input logic [1:0] a, b, output logic [1:0] y);\n" +
                "    and #2 g0 (y[0], a[0], b[0]);\n" +
                "    or #3 g1 (y[1], a[1], b[1]);\n" +
                "endmodule\n"));
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            StringAssert.Contains("y[1]", critical.Text, "sanity: through the slower gate");
            SchematicLayout layout = DrawingOf(project);

            TimingLight light = TimingLight.Of(layout, critical.Signals);
            CollectionAssert.Contains(light.Parts.Select(id => layout.Parts.Single(p => p.Part.Id == id).Part.Name).ToArray(), "g1");
            Assert.IsFalse(light.Parts.Any(id => layout.Parts.Single(p => p.Part.Id == id).Part.Name == "g0"), "and not the faster one");
        }

        /// <summary>A part reading one wire on both its pins, as a &amp; a: both wires into it are lit, as a step to one part lights them all.</summary>
        [Test]
        public void APartReadingOneWireTwice_IsLitOnBothItsWires()
        {
            Project project = Written("tw", ("tw",
                "module tw (input logic a, b, output logic y);\n" +
                "    assign #2 y = ~(a & a) ^ b;\n" +
                "endmodule\n"));
            TimingLine critical = TimingView.Of(project).Lines.First(l => l.Label == "CRITICAL PATH");
            SchematicLayout layout = DrawingOf(project);
            int and = layout.Parts.Single(p => p.Name == "&").Part.Id;
            int a = Enumerable.Range(0, layout.Nets.Count).Single(n => layout.Nets[n].Name == "a");
            Assert.AreEqual(2, layout.Nets[a].Loads.Count(l => l.Part == and), "sanity: a into both of the AND's pins");

            TimingLight light = TimingLight.Of(layout, critical.Signals.Take(1).Concat(new[] { "y" }).ToArray());
            Assert.AreEqual(2, light.Branches.Count(b => b.Net == a && layout.Nets[a].Loads[b.Branch].Part == and), "both wires into the AND");
        }
    }
}
