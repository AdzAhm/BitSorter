using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>Where parts' pins are, how wires run between them, and the paths a change takes along a net.</summary>
    public class GeometryTests
    {
        private static Point P(double x, double y) => new Point(x, y);

        private static PinSpot Pin(PartOutline outline, string name)
        {
            Assert.IsTrue(outline.TryPin(name, out PinSpot spot), name);
            return spot;
        }

        [Test]
        public void ATwoInputGate_TakesItsInputsOnTheLeft_AndGivesItsOutputOnTheRight()
        {
            PartOutline and = PartGeometry.Placed(null, new GatePart { Kind = GateKind.And }, new Placement(3, 2));

            Assert.AreEqual(P(2, 2.25), Pin(and, "a").At);
            Assert.AreEqual(P(2, 1.75), Pin(and, "b").At);
            Assert.AreEqual(P(4, 2), Pin(and, "y").At);
            Assert.AreEqual(Facing.Left, Pin(and, "a").Facing);
            Assert.AreEqual(Facing.Right, Pin(and, "y").Facing);
        }

        [Test]
        public void TurnedAndMirrored_ThePinsGoWithThePart()
        {
            var gate = new GatePart { Kind = GateKind.And };

            PartOutline turned = PartGeometry.Placed(null, gate, new Placement(P(0, 0), quarterTurns: 1));
            Assert.AreEqual(P(0, 1), Pin(turned, "y").At, "a quarter turn counter-clockwise points the output up");
            Assert.AreEqual(Facing.Up, Pin(turned, "y").Facing);

            PartOutline mirrored = PartGeometry.Placed(null, gate, new Placement(P(0, 0), flipped: true));
            Assert.AreEqual(P(1, 0.25), Pin(mirrored, "a").At, "mirrored, it takes its inputs on the right");
            Assert.AreEqual(Facing.Right, Pin(mirrored, "a").Facing);
        }

        [Test]
        public void ATriStateGate_TakesItsControlFromUnderneath()
        {
            PartOutline tri = PartGeometry.Placed(null, new GatePart { Kind = GateKind.Bufif1, Inputs = 2 }, new Placement(0, 0));

            Assert.AreEqual(Facing.Down, Pin(tri, "en").Facing);
            Assert.AreEqual(P(0, -0.75), Pin(tri, "en").At);
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void EveryPin_IsOnTheQuarterCellGrid(string board)
        {
            Project project = SampleBoards.Build(board);
            foreach (Module module in project.Modules)
                foreach (Part part in module.Parts)
                {
                    PartOutline local = PartGeometry.Local(project, part);
                    foreach (PinSpot pin in local.Pins)
                    {
                        Assert.AreEqual(0, pin.At.X * 4 % 1, 1e-9, $"{part.Name}.{pin.Pin}");
                        Assert.AreEqual(0, pin.At.Y * 4 % 1, 1e-9, $"{part.Name}.{pin.Pin}");
                    }

                    Assert.AreEqual(part.Pins(project).Count, local.Pins.Count, $"{part.Name}: every pin has a place");
                }
        }

        [Test]
        public void AnAutomaticRoute_TurnsOnlyAtRightAngles_HalfWayAcross()
        {
            List<Point> route = WireGeometry.Orthogonal(P(1, 0), Facing.Right, P(4, 1), Facing.Left);

            CollectionAssert.AreEqual(new[] { P(1, 0), P(2.5, 0), P(2.5, 1), P(4, 1) }, route);
        }

        [Test]
        public void AWireBackwards_GoesRound_RatherThanThroughTheParts()
        {
            List<Point> route = WireGeometry.Orthogonal(P(4, 0), Facing.Right, P(0, 0), Facing.Left);

            Assert.IsTrue(route.Skip(1).Take(route.Count - 2).Any(p => p.Y >= 1), "it goes above, a cell clear");
            for (int i = 1; i < route.Count; i++)
                Assert.IsTrue(route[i].X == route[i - 1].X || route[i].Y == route[i - 1].Y, "right angles only");
        }

        [Test]
        public void AWiresOwnBends_AreDrawnAsMade_AndAPieceGoneAslantByAMove_TurnsAtRightAngles()
        {
            Point[] bends = { P(3, 0), P(3, 2) };
            CollectionAssert.AreEqual(new[] { P(1, 0), P(3, 0), P(3, 2), P(5, 2) },
                WireGeometry.Through(P(1, 0), Facing.Right, bends, P(5, 2), Facing.Left), "every piece straight: as made");

            // The part the wire leaves moved up half a cell, its pin with it, and the bends stayed.
            List<Point> moved = WireGeometry.Through(P(1, 0.5), Facing.Right, bends, P(5, 2), Facing.Left);
            CollectionAssert.AreEqual(new[] { P(1, 0.5), P(3, 0.5), P(3, 2), P(5, 2) }, moved,
                "out of the pin the way it faces, and up at the first bend's column -- not down to it and back");
            NeverDoublesBack(moved);

            CollectionAssert.AreEqual(new[] { P(0, 0), P(2, 0), P(2, 1), P(4, 1) },
                WireGeometry.Through(P(0, 0), null, new[] { P(2, 1) }, P(4, 1), null), "from a tap, across first, one corner");
            CollectionAssert.AreEqual(new[] { P(0, 0), P(0, 2), P(2, 2), P(2, 3) },
                WireGeometry.Through(P(0, 0), null, new[] { P(0, 2), P(2, 3) }, P(2, 3), null),
                "after a run up, the next piece turns across: at right angles to the way it came");
        }

        [Test]
        public void ATapOffAnUprightPiece_LeavesAcross_TowardsWhereItGoes_RatherThanAlongTheWireItTaps()
        {
            Point[] upright = { P(0, 0), P(0, 4) };
            Assert.AreEqual(Facing.Right, WireGeometry.TapFacing(upright, 0.5, P(0, 2), P(3, 1)));
            Assert.AreEqual(Facing.Left, WireGeometry.TapFacing(upright, 0.5, P(0, 2), P(-3, 1)));
            Assert.IsNull(WireGeometry.TapFacing(new[] { P(0, 0), P(4, 0) }, 0.5, P(2, 0), P(3, -2)), "off a piece across: up or down, as always");

            // g1 to g2 rises half-way across; g3's input, below and to the right, taps the rise at its middle.
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not), g3 = b.Gate(GateKind.Not);
            Bench.Must(b.Editor.Move(b.Module, g1, new Placement(0, 0)));
            Bench.Must(b.Editor.Move(b.Module, g2, new Placement(6, 4)));
            Bench.Must(b.Editor.Move(b.Module, g3, new Placement(6, -2)));
            int w = b.Wire(g1, "y", g2, "a");
            List<Point> rise = WireGeometry.Wires(b.Project, b.Current)[w].ToList();
            int up = Enumerable.Range(1, rise.Count - 1).Single(i => rise[i].X == rise[i - 1].X);
            double middle = WireGeometry.Length(rise.Take(up).ToList()) + Math.Abs(rise[up].Y - rise[up - 1].Y) / 2;
            int tap = Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g3, "a"), Endpoint.OnWire(w),
                new Route { TapAt = middle / WireGeometry.Length(rise) }));

            IReadOnlyList<Point> route = WireGeometry.Wires(b.Project, b.Current)[tap];
            Point junction = route[route.Count - 1];
            Assert.AreEqual(rise[up].X, junction.X, 1e-9, "sanity: the junction is on the rise");
            Assert.AreEqual(junction.Y, route[route.Count - 2].Y, 1e-9, $"the tap reaches it across, not up the rise: {string.Join(" ", route)}");
        }

        [Test]
        public void IntoAControl_FromJustBelowIt_TheWireGoesStraightUp_RatherThanPastItAndBack()
        {
            // An enable's pin faces down, and the wire comes from a quarter cell below it: less than
            // the pin's stub, which a route out to the stub's end would overshoot and come back up.
            List<Point> route = WireGeometry.Orthogonal(P(1, -1.25), Facing.Right, P(6, -1), Facing.Down);

            CollectionAssert.AreEqual(new[] { P(1, -1.25), P(6, -1.25), P(6, -1) }, route);
        }

        [Test]
        public void IntoAControl_FromAbove_TheWireGoesRoundUnderThePart_RatherThanThroughIt()
        {
            List<Point> route = WireGeometry.Orthogonal(P(1, 1), Facing.Right, P(6, -1), Facing.Down);

            Assert.IsTrue(route.Where(p => p.X == 6).All(p => p.Y <= -1), "it reaches the pin's column only underneath it");
            NeverDoublesBack(route);
        }

        /// <summary>Every turn is a right angle -- never straight back the way the wire came.</summary>
        private static void NeverDoublesBack(List<Point> route)
        {
            for (int i = 1; i + 1 < route.Count; i++)
            {
                double dot = (route[i].X - route[i - 1].X) * (route[i + 1].X - route[i].X)
                    + (route[i].Y - route[i - 1].Y) * (route[i + 1].Y - route[i].Y);
                Assert.AreEqual(0, dot, 1e-9, $"the wire turns back on itself at {route[i]}");
            }
        }

        [TestCaseSource(typeof(SampleBoards), nameof(SampleBoards.Names))]
        public void EveryNet_HasABranchToEachLoad_FromItsDriversPinToTheLoadsPin(string board)
        {
            Project project = SampleBoards.Build(board);
            foreach (Module module in project.Modules)
            {
                Netlist netlist = Netlist.Of(project, module);
                var spots = module.Parts.ToDictionary(p => p.Id, p => PartGeometry.Placed(project, p, module.Layout.PlacementOf(p.Id)));

                foreach (NetPath path in WireGeometry.Nets(project, module, netlist))
                {
                    if (path.Net.Wires.Count == 0)
                        continue;

                    Assert.AreEqual(path.Net.Pins.Count - 1, path.Branches.Count, path.Net.Name);
                    Point source = Pin(spots[path.Source.Part], path.Source.Pin).At;
                    foreach (NetBranch branch in path.Branches)
                    {
                        Assert.AreEqual(source, branch.Points[0], $"{path.Net.Name} starts at its driver");
                        Assert.AreEqual(Pin(spots[branch.Load.Part], branch.Load.Pin).At, branch.Points[branch.Points.Count - 1], $"{path.Net.Name} ends at {branch.Load}");
                    }
                }
            }
        }

        [Test]
        public void ATap_IsAJunction_AndTheBranchToItsLoadRunsThroughIt()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not), g3 = b.Gate(GateKind.Not);
            Bench.Must(b.Editor.Move(b.Module, g1, new Placement(0, 0)));
            Bench.Must(b.Editor.Move(b.Module, g2, new Placement(6, 0)));
            Bench.Must(b.Editor.Move(b.Module, g3, new Placement(6, -3)));
            int w = b.Wire(g1, "y", g2, "a");
            Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(g3, "a"), Endpoint.OnWire(w), new Route { TapAt = 0.5 }));

            NetPath path = WireGeometry.Nets(b.Project, b.Current, b.Nets).Single(n => n.Source.Part == g1 && n.Source.Pin == "y");

            Point junction = path.Junctions.Single();
            Assert.AreEqual(P(3, 0), junction, "half-way along the wire it taps");
            NetBranch toG3 = path.Branches.Single(x => x.Load.Part == g3);
            Assert.IsTrue(toG3.Points.Contains(junction), "the change for g3 leaves the first wire where the tap does");
        }
    }
}
