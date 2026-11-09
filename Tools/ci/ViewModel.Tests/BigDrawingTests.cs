using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BitSorter.Board;
using NUnit.Framework;
using BoardPoint = BitSorter.Board.Point;

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

        /// <summary>
        /// A drag of the selection by an offset (the 16d sweep): a wire between two parts of it, with no bends, is carried along
        /// as it lay; one with bends still runs through them, as the drop routes it; one out to the rest is routed to where its
        /// part has got to; and the rest lie where they lay.
        /// </summary>
        [Test]
        public void ADragByAnOffset_CarriesTheWiresBetweenItsParts_AndRoutesThoseOutToTheRest()
        {
            Project project = Adder(4);
            Module module = project.TopModule;
            List<int> parts = module.Parts.Where(p => new[] { "x0", "y0", "p0", "q0", "r0" }.Contains(p.Name)).Select(p => p.Id).ToList();
            Assert.AreEqual(5, parts.Count, "sanity: bit 0's five gates");
            bool Moved(Endpoint end) => !end.IsTap && parts.Contains(end.Pin.Part);
            Wire bent = module.Wires.First(w => Moved(w.From) && Moved(w.To));
            module = module with { Layout = module.Layout.WithRoute(bent.Id, new Route { Bends = new[] { new Point(-3, -3), new Point(-3, 7) } }) };

            Dictionary<int, IReadOnlyList<Point>> before = WireGeometry.Wires(project, module);
            const double dx = 1, dy = 2;
            Layout layout = module.Layout.WithParts(parts.ToDictionary(id => id, id => module.Layout.PlacementOf(id).Moved(dx, dy)));
            Module moved = module with { Layout = layout };
            Dictionary<int, IReadOnlyList<Point>> during = WireGeometry.Following(project, moved, before, parts, dx, dy);

            int carried = 0, routed = 0;
            foreach (Wire wire in module.Wires)
            {
                IReadOnlyList<Point> route = during[wire.Id];
                if (wire.Id == bent.Id)
                {
                    CollectionAssert.AreEqual(WireGeometry.Wires(project, moved)[wire.Id], route, "through its bends, which a move leaves where they are, as the drop routes it");
                    CollectionAssert.AreNotEqual(before[wire.Id].Select(p => new Point(p.X + dx, p.Y + dy)), route, "sanity: not carried");
                }
                else if (Moved(wire.From) && Moved(wire.To))
                {
                    carried++;
                    CollectionAssert.AreEqual(before[wire.Id].Select(p => new Point(p.X + dx, p.Y + dy)), route, "carried along: wire " + wire.Id);
                }
                else if (Moved(wire.From) || Moved(wire.To))
                {
                    routed++;
                    foreach ((Endpoint end, Point at) in new[] { (wire.From, route[0]), (wire.To, route[route.Count - 1]) })
                    {
                        if (!Moved(end))
                            continue;
                        PartOutline now = PartGeometry.Placed(project, moved.FindPart(end.Pin.Part), layout.PlacementOf(end.Pin.Part));
                        Assert.IsTrue(now.TryPin(end.Pin.Pin, out PinSpot pin));
                        Assert.AreEqual(pin.At, at, "routed to the pin where its part now is");
                    }
                }
                else
                {
                    Assert.AreSame(before[wire.Id], route, "the rest lie where they lay");
                }
            }
            Assert.Greater(carried, 0, "sanity: wires carried");
            Assert.Greater(routed, 0, "sanity: wires routed");
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

        /// <summary>A layout edit, an undo and a redo each route afresh, and the routes are the ones routing from nothing gives.</summary>
        [Test]
        public void TheSessionsRoutes_FollowAMoveAndItsUndo()
        {
            var session = new WorkbenchSession(Adder(4), () => 0);
            void SameAsFresh(string when)
            {
                Project project = session.Project;
                IReadOnlyDictionary<int, IReadOnlyList<Point>> held = session.RoutesOf(project, project.TopModule);
                Dictionary<int, IReadOnlyList<Point>> fresh = WireGeometry.Wires(project, project.TopModule);
                CollectionAssert.AreEquivalent(fresh.Keys, held.Keys, when);
                foreach (KeyValuePair<int, IReadOnlyList<Point>> route in fresh)
                    CollectionAssert.AreEqual(route.Value, held[route.Key], when + ": wire " + route.Key);
            }

            SameAsFresh("opened");
            Module module = session.Project.TopModule;
            int gate = module.Parts.First(p => p is GatePart).Id;
            Placement at = module.Layout.PlacementOf(gate);
            EditResult moved = session.Move(new Dictionary<int, Placement> { [gate] = new Placement(new Point(at.Position.X, at.Position.Y + 3)) });
            Assert.IsTrue(moved.Done, moved.Refusal);
            SameAsFresh("moved");
            Assert.IsTrue(session.Undo());
            SameAsFresh("undone");
        }

        /// <summary>
        /// The canvas's hit map and the drawing take the routes they are given: the hit map hands back the very routes, and the
        /// drawing asks once and draws what it draws routing for itself.
        /// </summary>
        [Test]
        public void TheHitMapAndTheDrawing_TakeTheRoutesTheyAreGiven()
        {
            Project project = Adder(4);
            Module module = project.TopModule;
            Dictionary<int, IReadOnlyList<Point>> routes = WireGeometry.Wires(project, module);
            var hit = new CanvasHit(project, module, routes);
            foreach (Wire wire in module.Wires)
                Assert.AreSame(routes[wire.Id], hit.WireOf(wire.Id));

            CompiledProject compiled = BoardCompiler.Compile(project);
            int asked = 0;
            SchematicLayout given = DocumentSchematic.Build(project, compiled, null, null, m => { asked++; return routes; });
            SchematicLayout own = DocumentSchematic.Build(project, compiled);
            Assert.AreEqual(1, asked, "asked once");
            Assert.AreEqual(own.Nets.Count, given.Nets.Count);
            for (int n = 0; n < own.Nets.Count; n++)
            {
                Assert.AreEqual(own.Nets[n].Branches.Count, given.Nets[n].Branches.Count);
                for (int b = 0; b < own.Nets[n].Branches.Count; b++)
                {
                    StageRoute mine = own.Nets[n].Branches[b], theirs = given.Nets[n].Branches[b];
                    Assert.AreEqual(mine.PointCount, theirs.PointCount, "net " + n + " branch " + b);
                    for (int i = 0; i < mine.PointCount; i++)
                        Assert.AreEqual(mine.Point(i), theirs.Point(i), "net " + n + " branch " + b + " point " + i);
                }
            }
        }

        [Test]
        public void TheDesktopHint_IsSaidInABrowser_OnceASession_AfterAPauseLongEnoughToNotice()
        {
            Assert.IsTrue(DesktopHint.Due(true, false, 400));
            Assert.IsFalse(DesktopHint.Due(false, false, 4000), "never on the desktop");
            Assert.IsFalse(DesktopHint.Due(true, true, 4000), "once");
            Assert.IsFalse(DesktopHint.Due(true, false, DesktopHint.SlowMilliseconds), "and only past a pause a student notices");
        }

        /// <summary>
        /// The hint speaks of the design, never of a drawing: written code is as slow to work out, and on Ahmad's imported ALU --
        /// code, with no drawing at all -- it spoke of a drawing (2026-10-28).
        /// </summary>
        [Test]
        public void TheDesktopHint_SpeaksOfTheDesign_NotADrawing()
        {
            StringAssert.Contains("design", DesktopHint.Words);
            StringAssert.DoesNotContain("drawing", DesktopHint.Words);
        }

        [Test]
        public void TheSession_TimesItsDrawingAndItsTiming_ByItsOwnClock_TheSlowestUntilAsked()
        {
            double clock = 0;
            var session = new WorkbenchSession(Adder(2), () => clock += 100);
            Assert.Greater(session.TakeSlowestMilliseconds(), 0, "the drawing made as the session opened, timed");
            Assert.AreEqual(0, session.TakeSlowestMilliseconds(), "and forgotten once told");
            Assert.IsNotNull(session.Timing);
            Assert.Greater(session.TakeSlowestMilliseconds(), 0, "the timing worked out, timed");
        }

        /// <summary>
        /// A slow edit is heard though the document is drawn twice for it, the second time from the routes the first made: the
        /// 15g-7 review removed a gate of the thousand-part adder, 500 ms by the clock, and the hint was told 6.
        /// </summary>
        [Test]
        public void AnEditsSlowDrawing_IsWhatTheHintIsTold()
        {
            var session = new WorkbenchSession(Adder(128), () => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
            session.TakeSlowestMilliseconds();   // the opening's
            int gate = session.Project.TopModule.Parts.First(p => p is GatePart).Id;
            var watch = Stopwatch.StartNew();
            EditResult removed = session.Remove(new[] { gate });
            double took = watch.Elapsed.TotalMilliseconds;
            Assert.IsTrue(removed.Done, removed.Refusal);
            double told = session.TakeSlowestMilliseconds();
            TestContext.WriteLine($"the edit took {took:F0} ms, the hint was told {told:F0}");
            Assert.Greater(told, took / 4, "the drawing that routed every wire, not the one after it that reused the routes");
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
            DocumentSchematic.Build(project, compiled);   // warmed, as each is: a first call pays for compiling the code it runs
            double drawn = Fastest(() => DocumentSchematic.Build(project, compiled));

            TimingView timing = TimingView.Of(project);
            Assert.IsTrue(timing.Analysis.Succeeded, timing.Analysis.Problem);
            double timed = Fastest(() => TimingView.Of(project));

            Dictionary<int, IReadOnlyList<Point>> routes = WireGeometry.Wires(project, module);
            (Module moved, List<int> parts) = Dragged(module);
            WireGeometry.Following(project, moved, routes, parts);   // warmed
            double dragged = Fastest(() => WireGeometry.Following(project, moved, routes, parts));

            TestContext.WriteLine($"drawn {drawn:F0} ms, timed {timed:F0} ms, a drag step {dragged:F1} ms");
            Assert.Less(drawn, 2000, "drawing a thousand parts");
            Assert.Less(timed, 1000, "the TIMING tab");
            Assert.Less(dragged, 50, "a step of a drag");
        }

        /// <summary>
        /// A drawn import, a paste and a MAKE BLOCK of a big circuit ask the rules once, of the document each ends on, and wire
        /// without working the whole netlist out again for every wire (the 16d sweep). Measured under dotnet on 2026-11-09 for the
        /// 64-bit adder, 516 parts and 707 wires: its import made 2,527 MB, a paste of its 322 gates and 385 wires 2,363 MB and
        /// MAKE BLOCK of them 3,971 MB -- in a browser, which frees memory only between frames, a page's death -- and after, 40,
        /// 44 and 92 MB. Each grew four times with the circuit's size doubled. The budget is a quarter of a gigabyte each.
        /// </summary>
        [Test]
        public void ABigCircuit_IsImportedPastedAndMadeABlock_AskingTheRulesOnceEach()
        {
            const long Budget = 250L * 1000 * 1000;   // written so: Repo.Tests reads nine digits together as an ID
            long made = System.GC.GetAllocatedBytesForCurrentThread();
            Project project = Adder(64);
            long imported = System.GC.GetAllocatedBytesForCurrentThread() - made;

            var editor = new BoardEditor(project);
            Module module = project.TopModule;
            List<int> gates = module.Parts.Where(p => !(p is PortPart)).Select(p => p.Id).ToList();
            Clip clip = Clipboard.Copy(project, module.Name, gates);
            made = System.GC.GetAllocatedBytesForCurrentThread();
            EditResult pasted = Clipboard.Paste(editor, module.Name, clip, new List<int>(), new List<int>());
            long paste = System.GC.GetAllocatedBytesForCurrentThread() - made;
            Assert.IsTrue(pasted.Done, pasted.Refusal);
            Assert.AreEqual(module.Parts.Count + gates.Count, editor.Project.TopModule.Parts.Count, "sanity: every gate pasted");

            Assert.IsTrue(editor.Undo());
            made = System.GC.GetAllocatedBytesForCurrentThread();
            EditResult block = BlockMaker.Make(editor, module.Name, gates, "adder_inside");
            long blocked = System.GC.GetAllocatedBytesForCurrentThread() - made;
            Assert.IsTrue(block.Done, block.Refusal);

            TestContext.WriteLine($"import {imported / 1e6:F0} MB, paste {paste / 1e6:F0} MB, make block {blocked / 1e6:F0} MB");
            Assert.Less(imported, Budget, "the import");
            Assert.Less(paste, Budget, "the paste");
            Assert.Less(blocked, Budget, "MAKE BLOCK");
        }

        /// <summary>
        /// A selection of a thousand parts dragged, moved and taken away copies the placements once, never once a part (the 16d
        /// sweep: a copy of every placement for each part selected). Measured under dotnet on 2026-11-09 for the 128-bit adder's
        /// 1,028 parts: MoveParts made 68.3 MB, RemoveParts 30.9 MB, and a step of the drag's overlay 68.6 MB more than the
        /// wires it routes; after, 0.1, 0.8 and 0.5 MB. The wires a drag routes are their own cost, 126 MB a step of a select-all
        /// (<see cref="WireGeometry.Following"/>), measured here so the overlay is held to the rest.
        /// </summary>
        [Test]
        public void AThousandPartSelection_IsDraggedMovedAndTakenAway_CopyingItsPlacementsOnce()
        {
            Project project = Adder(128);
            var session = new WorkbenchSession(project, () => 0.0);
            session.Finish();
            Module module = session.Project.TopModule;
            var editor = new CanvasEditor(session);
            editor.SelectAll();
            Placement first = module.Layout.PlacementOf(module.Parts[0].Id);
            var from = new Point(first.Position.X, first.Position.Y);
            editor.Do(new CanvasCommand(CanvasGesture.BeginMove, from: from));
            editor.Do(new CanvasCommand(CanvasGesture.MoveTo, new Point(from.X + 1, from.Y), from));
            var overlay = new CanvasOverlay();
            editor.Overlay(overlay);   // warmed
            Assert.AreEqual(module.Parts.Count, overlay.Ghosts.Count, "sanity: every part dragged");

            long made = System.GC.GetAllocatedBytesForCurrentThread();
            editor.Overlay(overlay);
            long step = System.GC.GetAllocatedBytesForCurrentThread() - made;
            var moved = new Dictionary<int, Placement>();
            foreach (Part part in module.Parts)
                moved[part.Id] = module.Layout.PlacementOf(part.Id).Moved(1, 0);
            var placements = new Dictionary<int, Placement>(module.Layout.Parts);
            foreach (KeyValuePair<int, Placement> part in moved)
                placements[part.Key] = part.Value;
            Layout movedLayout = module.Layout with { Parts = placements };
            IReadOnlyDictionary<int, IReadOnlyList<Point>> kept = session.RoutesOf(session.Project, module);
            made = System.GC.GetAllocatedBytesForCurrentThread();
            WireGeometry.Following(session.Project, module with { Layout = movedLayout }, kept, moved.Keys);
            long routing = System.GC.GetAllocatedBytesForCurrentThread() - made;

            var board = new BoardEditor(project);
            made = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.IsTrue(board.MoveParts(module.Name, moved).Done);
            long move = System.GC.GetAllocatedBytesForCurrentThread() - made;
            made = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.IsTrue(board.RemoveParts(module.Name, module.Parts.Where(p => !(p is PortPart)).Select(p => p.Id).ToList()).Done);
            long remove = System.GC.GetAllocatedBytesForCurrentThread() - made;

            TestContext.WriteLine($"overlay {step / 1e6:F1} MB (routing {routing / 1e6:F1}), move {move / 1e6:F1} MB, remove {remove / 1e6:F1} MB");
            Assert.Less(step - routing, 8_000_000, "a step of the drag, beyond the wires it routes");
            Assert.Less(move, 4_000_000, "MoveParts");
            Assert.Less(remove, 8_000_000, "RemoveParts");
        }

        /// <summary>
        /// Ctrl+A on the 1,028-part adder, dragged: a step of the drag -- the overlay drawn again where the selection has got
        /// to -- within a frame's budget. Every wire has both its ends on moved parts, so each is only carried along by the
        /// drag, and needs no routing; and the moved layout is made once a step, not once a part. Measured before the 16d
        /// sweep's fixes at 491 ms and 186 MB a step (canvas-6, canvas-7): each wire routed again round the rest, and a copy
        /// of the whole layout made for each part moved.
        /// </summary>
        [Test]
        public void AThousandPartDrawing_AllOfItDragged_TakesAStepWithinAFrame()
        {
            var session = new WorkbenchSession(Adder(128), () => 0.0);
            var editor = new CanvasEditor(session);
            Module module = session.Project.TopModule;
            editor.SelectAll();
            Assert.AreEqual(module.Parts.Count, editor.Selection.Parts.Count, "sanity: everything selected");

            Placement first = module.Layout.PlacementOf(module.Parts[0].Id);
            var from = new BoardPoint(first.Position.X, first.Position.Y);
            editor.Do(new CanvasCommand(CanvasGesture.BeginMove, from: from));
            var overlay = new CanvasOverlay();
            int step = 0;
            void Step()
            {
                step++;
                editor.Do(new CanvasCommand(CanvasGesture.MoveTo, new BoardPoint(from.X + step, from.Y + 2 * step), from));
                editor.Overlay(overlay);
            }

            Step();   // warmed
            Assert.AreEqual(module.Wires.Count, overlay.GhostWires.Count, "every wire follows the drag");
            Assert.AreEqual(module.Parts.Count, overlay.Ghosts.Count, "and every part");
            long before = GC.GetAllocatedBytesForCurrentThread();
            Step();
            long made = GC.GetAllocatedBytesForCurrentThread() - before;
            double took = Fastest(Step);

            // The wires are where the drop routes them from: each moved by the drag, end to end, as its parts were.
            Dictionary<int, IReadOnlyList<Point>> lay = WireGeometry.Wires(session.Project, module);
            Wire wire = module.Wires[0];
            Vec2 ghost = overlay.GhostWires[0][0];
            SchematicLayout.Frame frame = session.Run.Model.Layout.Board;
            Vec2 now = frame.ToStage(new BoardPoint(lay[wire.Id][0].X + step, lay[wire.Id][0].Y + 2 * step));
            Assert.AreEqual(now.x, ghost.x, 1e-3, "a wire carried along, its first end where its pin has got to");
            Assert.AreEqual(now.y, ghost.y, 1e-3);

            // What a step makes holds it, as no load on the machine moves it (186 MB before); the time, 18 to 30 ms here alone,
            // came past 50 once with other test runs on the machine, and is held to a fifth of the 491 ms it took.
            TestContext.WriteLine($"a step of dragging all of it: {took:F1} ms, {made / 1024.0:F0} KB");
            Assert.Less(made, 4 << 20, "what a step makes");
            Assert.Less(took, 100, "a step of the drag");
        }

        /// <summary>
        /// Ctrl+A, Ctrl+C and Ctrl+V on a 516-part drawing: the paste is one step, checked once, made in good time -- every part
        /// and wire made again, each part named afresh, each input of the top given 0 from the start, and one Ctrl+Z taking it
        /// all back. Each part and wire was an edit of its own inside the step, each checking the whole project and copying the
        /// module, so a paste took time and garbage as the square of its size: 13 s and 9.4 GB here before the 16d sweep
        /// (canvas-14), the UI frozen for it.
        /// </summary>
        [Test]
        public void ABigDrawing_AllOfItPasted_IsOneStepCheckedOnce()
        {
            Project project = Adder(64);
            Module module = project.TopModule;
            Assert.AreEqual(516, module.Parts.Count, "sanity: five hundred parts");
            var all = module.Parts.Select(p => p.Id).ToList();
            Clipboard.Paste(new BoardEditor(project), module.Name, Clipboard.Copy(project, module.Name, all.Take(3).ToList()), null, null);   // warmed

            var editor = new BoardEditor(project);
            Clip clip = Clipboard.Copy(project, module.Name, all);
            var parts = new List<int>();
            var wires = new List<int>();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            EditResult pasted = Clipboard.Paste(editor, module.Name, clip, parts, wires);
            double took = watch.Elapsed.TotalMilliseconds;
            long made = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(pasted.Done, pasted.Refusal);

            Module after = editor.Project.TopModule;
            Assert.AreEqual(2 * module.Parts.Count, after.Parts.Count, "every part made again");
            Assert.AreEqual(2 * module.Wires.Count, after.Wires.Count, "and every wire");
            Assert.AreEqual(module.Parts.Count, parts.Count, "the parts made, to select");
            Assert.AreEqual(module.Wires.Count, wires.Count, "and the wires");
            Assert.AreEqual(after.Parts.Count, after.Parts.Select(p => p.Name).Distinct().Count(), "each part named afresh");
            int inputs = module.Parts.Count(p => p is PortPart port && port.Direction == BitSorter.SystemVerilog.Syntax.PortDirection.Input);
            var pastedNames = new HashSet<string>(after.Parts.Where(p => parts.Contains(p.Id)).Select(p => p.Name));
            Track[] given = editor.Project.Stimulus.Tracks.Where(t => pastedNames.Contains(t.Port)).ToArray();
            Assert.AreEqual(inputs, given.Length, "each input pasted given 0 from the start, as the strip gives one");
            Assert.IsTrue(given.All(t => t.Steps.Count == 1 && t.Steps[0].Time == 0 && t.Steps[0].Value.TryGetUInt64(out ulong zero) && zero == 0));
            Assert.IsTrue(editor.Undo());
            Assert.AreSame(project, editor.Project, "one Ctrl+Z takes it all back");

            TestContext.WriteLine($"{module.Parts.Count} parts and {module.Wires.Count} wires pasted: {took:F0} ms, {made >> 20} MB");
            Assert.Less(took, 500, "a paste of five hundred parts, in milliseconds");
            Assert.Less(made, 64L << 20, "what it makes");
        }

        /// <summary>The fastest of three runs: what the work costs, without whatever else the machine was doing in one of them.</summary>
        private static double Fastest(System.Action work)
        {
            double fastest = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                var watch = Stopwatch.StartNew();
                work();
                fastest = System.Math.Min(fastest, watch.Elapsed.TotalMilliseconds);
            }
            return fastest;
        }
    }
}
