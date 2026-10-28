using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BitSorter.Board.Tests
{
    /// <summary>
    /// The index of squares (Part 15g-7) answers every route as asking every corner, pin and body did: a route is clear by
    /// it exactly when it was clear before. Its squares are filed and asked with the same tolerance the checks use, so a
    /// change to one without the other would miss a hit quietly -- this is what would say so. The 15g-7 review held the two to
    /// eight million random cases; these are a few thousand of the same kinds, kept.
    /// </summary>
    public class WireObstaclesTests
    {
        // Square edges and the tolerance either side of them, negative coordinates, and the quarter grid routes run on.
        private static readonly double[] Base = { -6, -4, -2, 0, 2, 4, 6, 8, 40, -40 };
        private static readonly double[] Off = { 0, 0, 0, 1e-9, -1e-9, 9.9e-7, -9.9e-7, 1e-6, -1e-6, 1.1e-6, -1.1e-6, 1e-5, 0.25, -0.25, 0.5, -0.5, 1, -1, 0.75 };

        private static double At(Random random) => Base[random.Next(Base.Length)] + Off[random.Next(Off.Length)];

        private static Part SomePart(Random random)
        {
            switch (random.Next(5))
            {
                case 0: return new PortPart { Name = "p", Width = 1 };
                case 1: return new PortPart { Name = "q", Width = 1, Direction = BitSorter.SystemVerilog.Syntax.PortDirection.Output };
                default: return new GatePart { Kind = Sim.GateKind.And, Inputs = 1 + random.Next(4) };
            }
        }

        private static List<Point> SomeRoute(Random random)
        {
            double x = At(random), y = At(random);
            var points = new List<Point> { new Point(x, y) };
            int pieces = 1 + random.Next(5);
            for (int i = 0; i < pieces; i++)
            {
                if (random.Next(2) == 0)
                    x = random.Next(4) == 0 ? x + 200 * (random.NextDouble() - 0.5) : At(random);
                else
                    y = random.Next(4) == 0 ? y + 200 * (random.NextDouble() - 0.5) : At(random);
                if (random.Next(15) == 0)
                {
                    x = At(random);
                    y = At(random);   // a slanted piece too
                }
                points.Add(new Point(x, y));
            }
            return points;
        }

        [Test]
        public void TheIndexOfSquares_AnswersEveryRouteAsAskingEverythingDid()
        {
            int clear = 0, blocked = 0;
            for (int seed = 0; seed < 2000; seed++)
            {
                var random = new Random(seed);
                var indexed = new WireObstacles();
                var everything = new ObstaclesAskingEverything();
                int parts = random.Next(8), wires = random.Next(8);
                for (int i = 0; i < parts; i++)
                {
                    var placement = new Placement(new Point(At(random), At(random)), random.Next(4), random.Next(2) == 0);
                    PartOutline outline = PartGeometry.Placed(null, SomePart(random), placement);
                    indexed.AddPart(outline);
                    everything.AddPart(outline);
                }
                for (int i = 0; i < wires; i++)
                {
                    List<Point> wire = SomeRoute(random);
                    int net = random.Next(4);
                    indexed.AddWire(wire, net);
                    everything.AddWire(wire, net);
                }

                for (int ask = 0; ask < 20; ask++)
                {
                    List<Point> route = SomeRoute(random);
                    var own = new HashSet<int> { random.Next(4) };
                    bool expected = everything.Clear(route, own);
                    Assert.AreEqual(expected, indexed.Clear(route, own), $"seed {seed}, route {ask}: " + string.Join(" ", route));
                    if (expected)
                        clear++;
                    else
                        blocked++;
                }
            }
            Assert.Greater(clear, 1000, "sanity: plenty clear");
            Assert.Greater(blocked, 1000, "and plenty blocked");
        }
    }
}
