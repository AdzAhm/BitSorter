using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The automatic route kept clear of the other nets' wires and of the parts (Part 10e,
    /// <see cref="WireGeometry.Avoiding"/>): found in the Part 10 browser smoke test, where a tap from a to an AND ran
    /// a cell along b's wire and a and b read as one net.
    /// </summary>
    public class RoutingTests
    {
        private static Point P(double x, double y) => new Point(x, y);

        /// <summary>
        /// The smoke test's half adder, laid out so it happens: b's pin level with the AND's first input, and b's wire
        /// running across at that height before it rises to the XOR. Returns a's wire and b's.
        /// </summary>
        private static (Bench Bench, int And, int AWire, int BWire) LevelWithAnotherNet()
        {
            var b = new Bench();
            int a = b.Input("a"), bb = b.Input("b"), xor = b.Gate(GateKind.Xor), and = b.Gate(GateKind.And);
            Bench.Must(b.Editor.Move(b.Module, a, new Placement(0, 0.25)));
            Bench.Must(b.Editor.Move(b.Module, bb, new Placement(0, -2.75)));
            Bench.Must(b.Editor.Move(b.Module, xor, new Placement(6, 0)));
            Bench.Must(b.Editor.Move(b.Module, and, new Placement(6, -3)));
            int aWire = b.Wire(a, "out", xor, "a");
            int bWire = b.Wire(bb, "out", xor, "b");
            return (b, and, aWire, bWire);
        }

        [Test]
        public void ATapToAPinLevelWithAnotherNetsWire_GoesRoundIt_RatherThanAlongIt()
        {
            (Bench b, int and, int aWire, int bWire) = LevelWithAnotherNet();
            IReadOnlyList<Point> bRoute = WireGeometry.Wires(b.Project, b.Current)[bWire];
            Assert.IsTrue(Enumerable.Range(1, bRoute.Count - 1).Any(i => bRoute[i].Y == -2.75 && bRoute[i - 1].Y == -2.75),
                $"sanity: b's wire runs across at the AND's first input's height: {string.Join(" ", bRoute)}");

            // The junction a cell along a's wire, as the smoke test's was.
            int tap = Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(and, "a"), Endpoint.OnWire(aWire), new Route { TapAt = 1.0 / 4.5 }));
            IReadOnlyList<Point> route = WireGeometry.Wires(b.Project, b.Current)[tap];

            Assert.IsEmpty(SharedLines(b.Project, b.Current), $"two nets share a line; the tap ran {string.Join(" ", route)}");
            Assert.AreEqual(P(5, -2.75), route[0], "still from the AND's first input");
            Assert.AreEqual(P(1.5, 0.25), route[route.Count - 1], "still to the junction a cell along a's wire");
            Assert.IsEmpty(ThroughParts(b.Project, b.Current, route), "and round the parts, not through them");
        }

        [Test]
        public void AWireAdded_NeverMovesOneAlreadyThere()
        {
            (Bench b, int and, int aWire, int bWire) = LevelWithAnotherNet();
            Dictionary<int, IReadOnlyList<Point>> before = WireGeometry.Wires(b.Project, b.Current);

            Bench.Must(b.Editor.Connect(b.Module, Endpoint.At(and, "a"), Endpoint.OnWire(aWire), new Route { TapAt = 1.0 / 4.5 }));
            Dictionary<int, IReadOnlyList<Point>> after = WireGeometry.Wires(b.Project, b.Current);

            CollectionAssert.AreEqual(before[aWire], after[aWire], "a's wire");
            CollectionAssert.AreEqual(before[bWire], after[bWire], "b's wire: routed before the tap, so the tap goes round it");
        }

        [Test]
        public void ARouteWithNothingInItsWay_IsTheAutomaticOne()
        {
            var b = new Bench();
            int g1 = b.Gate(GateKind.Not), g2 = b.Gate(GateKind.Not);
            Bench.Must(b.Editor.Move(b.Module, g1, new Placement(0, 0)));
            Bench.Must(b.Editor.Move(b.Module, g2, new Placement(6, 4)));
            int w = b.Wire(g1, "y", g2, "a");

            Assert.AreEqual(WireGeometry.Orthogonal(P(1, 0), Facing.Right, P(5, 4), Facing.Left), WireGeometry.Wires(b.Project, b.Current)[w]);
        }

        [Test]
        public void ARouteThatCannotBeClear_IsTheAutomaticOneStill()
        {
            // Another net runs the whole way along the only line into the pin: no route of right angles comes in clear.
            var around = new WireObstacles();
            around.AddWire(new[] { P(-10, 0), P(10, 0) }, net: 7);
            List<Point> route = WireGeometry.Avoiding(P(0, 0), Facing.Right, P(4, 0), Facing.Left, around, new HashSet<int> { 1 });

            Assert.AreEqual(WireGeometry.Orthogonal(P(0, 0), Facing.Right, P(4, 0), Facing.Left), route);
        }

        [Test]
        public void AWireOfItsOwnNet_IsNoObstacle()
        {
            var around = new WireObstacles();
            around.AddWire(new[] { P(0, 0), P(8, 0) }, net: 3);
            List<Point> along = WireGeometry.Orthogonal(P(1, 0), Facing.Right, P(5, 0), Facing.Left);

            Assert.IsFalse(around.RunsAlongAnother(along, new HashSet<int> { 3 }), "its own net's wire");
            Assert.IsTrue(around.RunsAlongAnother(along, new HashSet<int> { 4 }), "another net's");
            Assert.IsTrue(around.RunsAlongAnother(new[] { P(8.1, 0), P(9, 0) }, new HashSet<int> { 4 }),
                "end to end on one line, under a quarter cell apart, reads as one wire");
            Assert.IsFalse(around.RunsAlongAnother(new[] { P(8.25, 0), P(9, 0) }, new HashSet<int> { 4 }), "a quarter cell apart reads as two");
        }

        [Test]
        public void AClearRoute_CrossesAnotherNetsWireOnlyAtRightAngles_AndStaysAQuarterCellOffItsLine()
        {
            var around = new WireObstacles();
            around.AddWire(new[] { P(0, 2), P(4, 2), P(4, 6) }, net: 9);

            Assert.IsTrue(around.Clear(new[] { P(2, 0), P(2, 4) }, null), "across it, away from its corner");
            Assert.IsFalse(around.Clear(new[] { P(4, 0), P(4, 1.9), P(6, 1.9) }, null), "on its line, under a quarter cell from its end");
            Assert.IsTrue(around.Clear(new[] { P(4, 0), P(4, 1.75), P(6, 1.75) }, null), "on its line, a quarter cell from its end");
            Assert.IsFalse(around.Clear(new[] { P(1, 2), P(3, 2) }, null), "along it");
            Assert.IsFalse(around.Clear(new[] { P(4, 0), P(4, 4) }, null), "through its corner");
        }

        [Test]
        public void AClearRoute_RunsOverNoPinButItsOwnEnds_AndThroughNoPart()
        {
            var around = new WireObstacles();
            around.AddPart(PartGeometry.Placed(null, new GatePart { Kind = GateKind.And }, new Placement(3, 0)));

            Assert.IsFalse(around.Clear(new[] { P(0, 0), P(6, 0) }, null), "through the gate");
            Assert.IsFalse(around.Clear(new[] { P(2, -2), P(2, 2) }, null), "over its inputs' pins");
            Assert.IsTrue(around.Clear(new[] { P(0, 0.25), P(2, 0.25) }, null), "into its own end, a pin");
            Assert.IsTrue(around.Clear(new[] { P(0, 2), P(6, 2) }, null), "above it");
        }

        /// <summary>Every pair of wires of different nets with a piece on one line, overlapping: by what each is, and where.</summary>
        public static List<string> SharedLines(Project project, Module module, bool automaticOnly = false)
        {
            Dictionary<int, IReadOnlyList<Point>> wires = WireGeometry.Wires(project, module);
            Dictionary<int, int> nets = WireGeometry.WireNets(module);
            var found = new List<string>();
            List<int> ids = wires.Keys.OrderBy(id => id).ToList();
            foreach (int w in ids)
            {
                if (automaticOnly && module.Layout.Routes.TryGetValue(w, out Route kept) && kept.Bends.Count > 0)
                    continue;
                foreach (int other in ids.Where(o => o < w && nets[o] != nets[w]))
                    foreach ((Point A, Point B) mine in Pieces(wires[w]))
                        foreach ((Point A, Point B) theirs in Pieces(wires[other]))
                            if (Overlap(mine, theirs))
                                found.Add($"wire {w} along wire {other} from {mine.A} to {mine.B}");
            }

            return found;
        }

        private static IEnumerable<(Point A, Point B)> Pieces(IReadOnlyList<Point> points)
        {
            for (int i = 1; i < points.Count; i++)
                yield return (points[i - 1], points[i]);
        }

        private static bool Overlap((Point A, Point B) p, (Point A, Point B) q)
        {
            bool pAcross = Math.Abs(p.A.Y - p.B.Y) < 1e-9, qAcross = Math.Abs(q.A.Y - q.B.Y) < 1e-9;
            if (pAcross != qAcross)
                return false;
            if (pAcross)
                return Math.Abs(p.A.Y - q.A.Y) < 1e-9 && Math.Min(Math.Max(p.A.X, p.B.X), Math.Max(q.A.X, q.B.X)) - Math.Max(Math.Min(p.A.X, p.B.X), Math.Min(q.A.X, q.B.X)) > 1e-6;
            return Math.Abs(p.A.X - q.A.X) < 1e-9 && Math.Min(Math.Max(p.A.Y, p.B.Y), Math.Max(q.A.Y, q.B.Y)) - Math.Max(Math.Min(p.A.Y, p.B.Y), Math.Min(q.A.Y, q.B.Y)) > 1e-6;
        }

        /// <summary>The parts a route runs inside the outline of, by id.</summary>
        private static List<int> ThroughParts(Project project, Module module, IReadOnlyList<Point> route)
        {
            var through = new List<int>();
            foreach (Part part in module.Parts)
            {
                PartOutline o = PartGeometry.Placed(project, part, module.Layout.PlacementOf(part.Id));
                foreach ((Point A, Point B) piece in Pieces(route))
                {
                    double left = Math.Min(piece.A.X, piece.B.X), right = Math.Max(piece.A.X, piece.B.X);
                    double bottom = Math.Min(piece.A.Y, piece.B.Y), top = Math.Max(piece.A.Y, piece.B.Y);
                    if (left < o.Right - 1e-6 && right > o.Left + 1e-6 && bottom < o.Top - 1e-6 && top > o.Bottom + 1e-6)
                    {
                        through.Add(part.Id);
                        break;
                    }
                }
            }

            return through;
        }
    }
}
