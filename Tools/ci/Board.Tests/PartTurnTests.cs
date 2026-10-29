using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using BitSorter.SystemVerilog;
using BitSorter.SystemVerilog.Syntax;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// Every kind of part turns to any side (Part 15h-3, asked 2026-10-28): placed at each quarter turn, mirrored and not, each
    /// pin is on its outline's edge on the side it faces, and a wire reaches it from that side without crossing the part -- so
    /// a drawing can face the way the textbook draws it.
    /// </summary>
    public class PartTurnTests
    {
        private static IEnumerable<(string Kind, Part Part)> Kinds()
        {
            yield return ("an input", new PortPart { Name = "p", Width = 2 });
            yield return ("an output", new PortPart { Name = "p", Direction = PortDirection.Output, Width = 2 });
            yield return ("a gate", new GatePart { Kind = GateKind.And, Inputs = 3 });
            yield return ("a tri-state gate", new GatePart { Kind = GateKind.Bufif1, Inputs = 2 });
            yield return ("a constant", new ConstantPart { Value = LogicVec.FromUInt64(5, 3) });
            yield return ("a register", new RegisterPart { Width = 2, HasReset = true });
            yield return ("a mux", new MuxPart { Inputs = 4, Width = 2 });
            yield return ("a splitter", new SplitterPart { Width = 3 });
            yield return ("a joiner", new JoinerPart { Width = 3 });
            yield return ("a lamp", new LampPart { Width = 2 });
            yield return ("a block", new BlockPart { Module = "inner" });
            yield return ("an adder", new OperatorPart { Op = OperatorKind.Add, Width = 4 });
            yield return ("a shifter", new OperatorPart { Op = OperatorKind.ShiftLeft, Width = 4, BWidth = 2 });
            yield return ("a reduction", new OperatorPart { Op = OperatorKind.ReduceXor, Width = 4 });
        }

        private static IEnumerable<string> KindNames() => Kinds().Select(k => k.Kind);

        [TestCaseSource(nameof(KindNames))]
        public void APart_TurnedToAnySide_HasEachPinOnItsEdge_AndAWireReachesIt(string kind)
        {
            Part part = Kinds().Single(k => k.Kind == kind).Part;
            int checkedPins = 0;
            for (int turns = 0; turns < 4; turns++)
            {
                foreach (bool flipped in new[] { false, true })
                {
                    string how = $"{kind} at {turns} quarter turns{(flipped ? ", mirrored" : "")}";
                    var editor = new BoardEditor(Project.New("m"));
                    SampleBoards.Must(editor.AddModule("inner"));
                    var inner = new SampleBoards.Builder(editor, "inner");
                    int ia = inner.In("a"), ib = inner.In("b"), iy = inner.Out("y");
                    int g = inner.Gate(GateKind.Xor);
                    inner.Wire(ia, "out", g, "a");
                    inner.Wire(ib, "out", g, "b");
                    inner.Wire(g, "y", iy, "in");

                    var placement = new Placement(new Point(0, 0), turns, flipped);
                    int id = SampleBoards.Must(editor.Add("m", part, placement));
                    Project project = editor.Project;
                    PartOutline outline = PartGeometry.Placed(project, project.Find("m").Parts.Single(p => p.Id == id), placement);

                    // Each pin on the edge it faces, and something wired to it from four cells out that way.
                    var wired = new List<(int Wire, PinSpot Pin)>();
                    foreach (Pin pin in part.Pins(project))
                    {
                        Assert.IsTrue(outline.TryPin(pin.Name, out PinSpot spot), $"{how}: {pin.Name} has a place");
                        double edge = spot.Facing == Facing.Left ? outline.Left : spot.Facing == Facing.Right ? outline.Right
                            : spot.Facing == Facing.Up ? outline.Top : outline.Bottom;
                        double at = spot.Facing == Facing.Left || spot.Facing == Facing.Right ? spot.At.X : spot.At.Y;
                        Assert.AreEqual(edge, at, 1e-9, $"{how}: {pin.Name} faces {spot.Facing} and is on that edge");

                        Point step = PartGeometry.Step(spot.Facing);
                        var far = new Point(spot.At.X + 4 * step.X, spot.At.Y + 4 * step.Y);
                        bool input = pin.Direction != PinDirection.Out;
                        int other = SampleBoards.Must(editor.Add("m", new PortPart
                        {
                            Name = (input ? "i_" : "o_") + pin.Name,
                            Direction = input ? PortDirection.Input : PortDirection.Output,
                            Width = pin.Width,
                        }, new Placement(far)));
                        EditResult wire = input
                            ? editor.Connect("m", Endpoint.At(other, "out"), Endpoint.At(id, pin.Name))
                            : editor.Connect("m", Endpoint.At(id, pin.Name), Endpoint.At(other, "in"));
                        wired.Add((SampleBoards.Must(wire), spot));
                    }

                    project = editor.Project;
                    Dictionary<int, IReadOnlyList<Point>> routes = WireGeometry.Wires(project, project.Find("m"));
                    foreach ((int wire, PinSpot spot) in wired)
                    {
                        IReadOnlyList<Point> route = routes[wire];
                        bool atStart = Near(route[0], spot.At);
                        Assert.IsTrue(atStart || Near(route[route.Count - 1], spot.At), $"{how}: {spot.Pin}'s wire ends on it");
                        Point next = atStart ? route[1] : route[route.Count - 2];
                        Point step = PartGeometry.Step(spot.Facing);
                        double along = (next.X - spot.At.X) * step.X + (next.Y - spot.At.Y) * step.Y;
                        double across = (next.X - spot.At.X) * step.Y - (next.Y - spot.At.Y) * step.X;
                        Assert.Greater(along, 0, $"{how}: {spot.Pin}'s wire leaves it the way it faces");
                        Assert.AreEqual(0, across, 1e-9, $"{how}: {spot.Pin}'s wire leaves it straight");
                        for (int i = 0; i + 1 < route.Count; i++)
                            for (int k = 1; k < 8; k++)
                            {
                                double t = k / 8.0;
                                var p = new Point(route[i].X + t * (route[i + 1].X - route[i].X), route[i].Y + t * (route[i + 1].Y - route[i].Y));
                                Assert.IsFalse(p.X > outline.Left + 1e-6 && p.X < outline.Right - 1e-6 && p.Y > outline.Bottom + 1e-6 && p.Y < outline.Top - 1e-6,
                                    $"{how}: {spot.Pin}'s wire crosses the part at {p}");
                            }
                        checkedPins++;
                    }
                }
            }

            Assert.Greater(checkedPins, 0);
        }

        /// <summary>
        /// A wire being drawn, its end on or beside the pin it starts from, is routed whatever way each end faces: the check of
        /// which way a route leaves its pins read a second point of a route tidied down to one (the Play Mode run of 15h-3,
        /// every wire drawn with the mouse).
        /// </summary>
        [Test]
        public void AWireBeingDrawn_OnOrBesideItsPin_IsRouted()
        {
            var facings = new Facing?[] { null, Facing.Left, Facing.Right, Facing.Up, Facing.Down };
            var start = new Point(1, 1);
            foreach (Point end in new[] { start, new Point(1.25, 1), new Point(1, 0.75), new Point(0.5, 1.5) })
                foreach (Facing? fa in facings)
                    foreach (Facing? fb in facings)
                    {
                        List<Point> route = WireGeometry.Orthogonal(start, fa, end, fb);
                        Assert.IsTrue(Near(route[0], start) && Near(route[route.Count - 1], end), $"{fa} to {fb}, {start} to {end}");
                    }
        }

        private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
    }
}
