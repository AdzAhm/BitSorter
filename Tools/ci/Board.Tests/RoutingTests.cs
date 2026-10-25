using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Sim;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The automatic route kept clear of the other nets' wires and of the parts (Part 10e): found in the Part 10 browser smoke test, where a tap from a to an AND ran
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

            Assert.IsFalse(Pieces(route).Any(mine => Pieces(bRoute).Any(theirs => Overlap(mine, theirs))),
                $"the tap runs along b's wire: {string.Join(" ", route)}");
            Assert.AreEqual(P(5, -2.75), route[0], "still from the AND's first input");
            Assert.AreEqual(P(1.5, 0.25), route[route.Count - 1], "still to the junction a cell along a's wire");
            Assert.IsEmpty(ThroughParts(b.Project, b.Current, route), "and round the parts, not through them");
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
