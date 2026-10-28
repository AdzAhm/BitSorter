using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BitSorter.Board;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// Big circuits stay smooth (Part 15g): a drawing of a thousand parts is routed once an edit rather than three times, a drag
    /// routes only the wires that follow it, and the TIMING tab names each signal once. Ahmad asked whether TIMING would slow
    /// a big project down (2026-10-28); measured, the drawing was the slower of the two.
    /// </summary>
    public class BigDrawingTests
    {
        /// <summary>An <paramref name="bits"/>-bit ripple adder, drawn: a port a bit, five gates a bit -- 8n + 4 parts.</summary>
        private static Project Adder(int bits)
        {
            var s = new StringBuilder("module add(input logic cin");
            for (int i = 0; i < bits; i++)
                s.Append(", input logic a" + i + ", input logic b" + i);
            for (int i = 0; i < bits; i++)
                s.Append(", output logic s" + i);
            s.Append(", output logic cout);\n");
            for (int i = 0; i <= bits; i++)
                s.Append("    wire c" + i + ";\n");
            for (int i = 0; i < bits; i++)
                s.Append("    wire t" + i + ", u" + i + ", v" + i + ";\n");
            s.Append("    buf g0(c0, cin);\n");
            for (int i = 0; i < bits; i++)
                s.Append($"    xor x{i}(t{i}, a{i}, b{i});\n    xor y{i}(s{i}, t{i}, c{i});\n    and p{i}(u{i}, a{i}, b{i});\n    and q{i}(v{i}, t{i}, c{i});\n    or r{i}(c{i + 1}, u{i}, v{i});\n");
            s.Append("    buf g1(cout, c" + bits + ");\nendmodule\n");
            ProjectImport import = ProjectImporter.Import(new[] { new SourceFile("add.sv", s.ToString()) });
            Assert.IsTrue(import.Succeeded, string.Join("\n", import.Notes));
            Assert.IsFalse(import.Project.TopModule.IsWritten, "sanity: drawn, gate by gate");
            return import.Project;
        }

        /// <summary>Two gates moved two cells up and one along, as a drag shows them.</summary>
        private static (Module Moved, List<int> Parts) Dragged(Module module)
        {
            List<int> parts = module.Parts.Where(p => p is GatePart).Take(2).Select(p => p.Id).ToList();
            Layout layout = module.Layout;
            foreach (int id in parts)
            {
                Placement at = module.Layout.PlacementOf(id);
                layout = layout.WithPart(id, new Placement(new Point(at.Position.X + 1, at.Position.Y + 2), at.QuarterTurns, at.Flipped));
            }
            return (module with { Layout = layout }, parts);
        }

        [Test]
        public void ADrag_RoutesOnlyTheWiresThatFollowIt_TheRestWhereTheyLie()
        {
            Project project = Adder(4);
            Module module = project.TopModule;
            Dictionary<int, IReadOnlyList<Point>> before = WireGeometry.Wires(project, module);
            (Module moved, List<int> parts) = Dragged(module);

            Dictionary<int, IReadOnlyList<Point>> during = WireGeometry.Following(project, moved, before, parts);
            int follow = 0;
            foreach (Wire wire in module.Wires)
            {
                bool follows = (!wire.From.IsTap && parts.Contains(wire.From.Pin.Part)) || (!wire.To.IsTap && parts.Contains(wire.To.Pin.Part));
                Assert.IsTrue(during.ContainsKey(wire.Id), "every wire has a route");
                if (!follows)
                {
                    Assert.AreSame(before[wire.Id], during[wire.Id], "a wire that does not follow is kept as it lay");
                    continue;
                }
                follow++;
                IReadOnlyList<Point> route = during[wire.Id];
                foreach ((Endpoint end, Point at) in new[] { (wire.From, route[0]), (wire.To, route[route.Count - 1]) })
                {
                    if (end.IsTap || !parts.Contains(end.Pin.Part))
                        continue;
                    PartOutline now = PartGeometry.Placed(project, moved.Parts.Single(p => p.Id == end.Pin.Part), moved.Layout.PlacementOf(end.Pin.Part));
                    Assert.IsTrue(now.TryPin(end.Pin.Pin, out PinSpot pin));
                    Assert.AreEqual(pin.At, at, "a wire that follows ends on the pin where its part now is");
                }
            }
            Assert.Greater(follow, 0, "sanity: some wires follow");
        }

        [Test]
        public void TheSession_RoutesADocumentOnce_AndAnInputsEditKeepsTheRoutes()
        {
            var session = new WorkbenchSession(Adder(4), () => 0);
            Project project = session.Project;
            IReadOnlyDictionary<int, IReadOnlyList<Point>> routes = session.RoutesOf(project, project.TopModule);
            Assert.AreSame(routes, session.RoutesOf(project, project.TopModule), "the drawing, the hit map and a wire drawn share one routing");

            EditResult set = session.SetStimulus(new BitSorter.Board.Stimulus { End = 40 }, 0);
            Assert.IsTrue(set.Done, set.Refusal);
            Assert.AreNotSame(project, session.Project, "sanity: a new document");
            Assert.AreSame(routes, session.RoutesOf(session.Project, session.Project.TopModule), "an input's steps move no wire");
        }

        [Test]
        public void TheDesktopHint_IsSaidInABrowser_OnceASession_AfterAPauseLongEnoughToNotice()
        {
            Assert.IsTrue(DesktopHint.Due(true, false, 400, 0));
            Assert.IsTrue(DesktopHint.Due(true, false, 0, 400), "timing as well as drawing");
            Assert.IsFalse(DesktopHint.Due(false, false, 4000, 4000), "never on the desktop");
            Assert.IsFalse(DesktopHint.Due(true, true, 4000, 4000), "once");
            Assert.IsFalse(DesktopHint.Due(true, false, DesktopHint.SlowMilliseconds, 0), "and only past a pause a student notices");
        }

        [Test]
        public void TheSession_TimesItsDrawingAndItsTiming_ByItsOwnClock()
        {
            double clock = 0;
            var session = new WorkbenchSession(Adder(2), () => clock += 100);
            Assert.Greater(session.LayoutMilliseconds, 0, "the drawing made as the session opened, timed");
            Assert.AreEqual(0, session.TimingMilliseconds, "nothing timed before the timing is asked for");
            Assert.IsNotNull(session.Timing);
            Assert.Greater(session.TimingMilliseconds, 0);
        }

        /// <summary>
        /// The 128-bit adder, 1,028 parts: drawn, its timing worked out, and a step of a drag. Measured on Ahmad's laptop on
        /// 2026-10-28 before Part 15g-7, 845 ms, 216 ms and 302 ms; after it about 290, 80 and 1. The budgets are generous so
        /// a slower machine passes, except the drag's: routing every wire at each step was three hundred times slower.
        /// </summary>
        [Test]
        public void AThousandPartDrawing_IsDrawnTimedAndDraggedWithinItsBudgets()
        {
            Project project = Adder(128);
            Module module = project.TopModule;
            Assert.AreEqual(1028, module.Parts.Count, "sanity: a thousand parts");
            CompiledProject compiled = BoardCompiler.Compile(project);
            DocumentSchematic.Build(project, compiled);   // warmed

            var watch = Stopwatch.StartNew();
            DocumentSchematic.Build(project, compiled);
            double drawn = watch.Elapsed.TotalMilliseconds;

            watch.Restart();
            TimingView timing = TimingView.Of(project);
            double timed = watch.Elapsed.TotalMilliseconds;
            Assert.IsTrue(timing.Analysis.Succeeded, timing.Analysis.Problem);

            Dictionary<int, IReadOnlyList<Point>> routes = WireGeometry.Wires(project, module);
            (Module moved, List<int> parts) = Dragged(module);
            WireGeometry.Following(project, moved, routes, parts);   // warmed
            watch.Restart();
            WireGeometry.Following(project, moved, routes, parts);
            double dragged = watch.Elapsed.TotalMilliseconds;

            TestContext.WriteLine($"drawn {drawn:F0} ms, timed {timed:F0} ms, a drag step {dragged:F1} ms");
            Assert.Less(drawn, 2000, "drawing a thousand parts");
            Assert.Less(timed, 1000, "the TIMING tab");
            Assert.Less(dragged, 50, "a step of a drag");
        }
    }
}
