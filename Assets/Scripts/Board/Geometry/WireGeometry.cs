using System;
using System.Collections.Generic;

namespace BitSorter.Board
{
    /// <summary>One way along a net: from its driver to one load, through every wire between.</summary>
    public sealed class NetBranch
    {
        internal NetBranch(PinRef load, IReadOnlyList<Point> points)
        {
            Load = load;
            Points = points;
        }

        public PinRef Load { get; }

        /// <summary>The way, in cells, from the driver's pin to the load's.</summary>
        public IReadOnlyList<Point> Points { get; }
    }

    /// <summary>A net as drawn: each branch a change travels down from its driver, and where its wires meet mid-way.</summary>
    public sealed class NetPath
    {
        internal NetPath(Net net, PinRef source, IReadOnlyList<NetBranch> branches, IReadOnlyList<Point> junctions)
        {
            Net = net;
            Source = source;
            Branches = branches;
            Junctions = junctions;
        }

        public Net Net { get; }

        /// <summary>Where a change starts: the pin that drives the net, or its first pin when nothing does.</summary>
        public PinRef Source { get; }

        public IReadOnlyList<NetBranch> Branches { get; }

        /// <summary>Where a wire taps another: a dot.</summary>
        public IReadOnlyList<Point> Junctions { get; }
    }

    /// <summary>
    /// Where a module's wires run, in cells: through the bends the layout keeps for a wire, or -- with
    /// none kept -- an automatic route that leaves each pin the way it faces and turns only at right
    /// angles. And each net as the paths a change takes from its driver to every load, which is what
    /// the workbench animates.
    /// </summary>
    public static class WireGeometry
    {
        /// <summary>How far a wire runs straight out of a pin before it may turn.</summary>
        public const double Stub = 0.5;

        /// <summary>Every wire's points, from its <see cref="Wire.From"/> end to its <see cref="Wire.To"/> end.</summary>
        public static Dictionary<int, IReadOnlyList<Point>> Wires(Project project, Module module)
        {
            var outlines = new Dictionary<int, PartOutline>();
            foreach (Part part in module.Parts)
                outlines[part.Id] = PartGeometry.Placed(project, part, module.Layout.PlacementOf(part.Id));

            var byId = new Dictionary<int, Wire>();
            foreach (Wire wire in module.Wires)
                byId[wire.Id] = wire;

            var done = new Dictionary<int, IReadOnlyList<Point>>();
            var busy = new HashSet<int>();

            IReadOnlyList<Point> RouteOf(Wire wire)
            {
                if (done.TryGetValue(wire.Id, out IReadOnlyList<Point> points))
                    return points;

                busy.Add(wire.Id);
                (Point a, Facing? fa) = End(wire.From, wire.Id);
                (Point b, Facing? fb) = End(wire.To, wire.Id);
                busy.Remove(wire.Id);

                module.Layout.Routes.TryGetValue(wire.Id, out Route kept);
                points = kept != null && kept.Bends.Count > 0 ? Through(a, kept.Bends, b) : Orthogonal(a, fa, b, fb);
                done[wire.Id] = points;
                return points;
            }

            (Point, Facing?) End(Endpoint end, int owner)
            {
                if (!end.IsTap)
                {
                    if (outlines.TryGetValue(end.Pin.Part, out PartOutline outline) && outline.TryPin(end.Pin.Pin, out PinSpot spot))
                        return (spot.At, spot.Facing);
                    return (default, null);
                }

                // A tap joins the tapped wire where the tapping wire's route says, part of the way along it.
                if (!byId.TryGetValue(end.TapOf, out Wire tapped) || busy.Contains(tapped.Id))
                    return (default, null);
                IReadOnlyList<Point> along = RouteOf(tapped);
                double at = module.Layout.Routes.TryGetValue(owner, out Route r) ? r.TapAt : 0.5;
                return (PointAt(along, Length(along) * at), null);
            }

            foreach (Wire wire in module.Wires)
                RouteOf(wire);
            return done;
        }

        /// <summary>Each net's paths from its driver to every load, through the wires and the taps between.</summary>
        public static List<NetPath> Nets(Project project, Module module, Netlist netlist)
        {
            Dictionary<int, IReadOnlyList<Point>> wires = Wires(project, module);

            // Each wire is cut where other wires tap it; the pieces join pins and tap junctions.
            var edges = new List<(string A, string B, IReadOnlyList<Point> Points)>();
            var junctions = new Dictionary<string, Point>();
            var tapsOn = new Dictionary<int, List<(int Tapping, Endpoint End)>>();

            foreach (Wire wire in module.Wires)
                foreach (Endpoint end in new[] { wire.From, wire.To })
                    if (end.IsTap)
                    {
                        if (!tapsOn.TryGetValue(end.TapOf, out var list))
                            tapsOn[end.TapOf] = list = new List<(int, Endpoint)>();
                        list.Add((wire.Id, end));
                    }

            foreach (Wire wire in module.Wires)
            {
                IReadOnlyList<Point> points = wires[wire.Id];
                double length = Length(points);
                var stops = new List<(double At, string Key)> { (0, Key(wire, wire.From)), (length, Key(wire, wire.To)) };

                if (tapsOn.TryGetValue(wire.Id, out var taps))
                {
                    foreach ((int tapping, Endpoint _) in taps)
                    {
                        double at = module.Layout.Routes.TryGetValue(tapping, out Route r) ? r.TapAt : 0.5;
                        string key = TapKey(tapping);
                        stops.Add((length * at, key));
                        junctions[key] = PointAt(points, length * at);
                    }
                }

                stops.Sort((x, y) => x.At.CompareTo(y.At));
                for (int i = 0; i + 1 < stops.Count; i++)
                    edges.Add((stops[i].Key, stops[i + 1].Key, Slice(points, stops[i].At, stops[i + 1].At)));
            }

            var adjacent = new Dictionary<string, List<(int Edge, bool Forward)>>();
            for (int e = 0; e < edges.Count; e++)
            {
                Link(adjacent, edges[e].A, e, true);
                Link(adjacent, edges[e].B, e, false);
            }

            var paths = new List<NetPath>();
            foreach (Net net in netlist.Nets)
            {
                if (net.Pins.Count == 0)
                    continue;

                PinRef source = net.Drivers.Count > 0 ? net.Drivers[0] : net.Pins[0];
                var from = new Dictionary<string, (string Previous, int Edge, bool Forward)>();
                var queue = new Queue<string>();
                string start = PinKey(source);
                from[start] = (null, -1, true);
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    string at = queue.Dequeue();
                    if (!adjacent.TryGetValue(at, out var links))
                        continue;
                    foreach ((int edge, bool forward) in links)
                    {
                        string next = forward ? edges[edge].B : edges[edge].A;
                        if (from.ContainsKey(next))
                            continue;
                        from[next] = (at, edge, forward);
                        queue.Enqueue(next);
                    }
                }

                var branches = new List<NetBranch>();
                foreach (PinRef pin in net.Pins)
                {
                    if (pin.Equals(source) || !from.ContainsKey(PinKey(pin)))
                        continue;

                    var pieces = new List<IReadOnlyList<Point>>();
                    for (string key = PinKey(pin); from[key].Previous != null; key = from[key].Previous)
                    {
                        (string _, int edge, bool forward) = from[key];
                        pieces.Add(forward ? edges[edge].Points : Reversed(edges[edge].Points));
                    }

                    pieces.Reverse();
                    var points = new List<Point>();
                    foreach (IReadOnlyList<Point> piece in pieces)
                        foreach (Point p in piece)
                            if (points.Count == 0 || !points[points.Count - 1].Equals(p))
                                points.Add(p);
                    branches.Add(new NetBranch(pin, points));
                }

                var dots = new List<Point>();
                foreach (int wire in net.Wires)
                    foreach (KeyValuePair<string, Point> junction in junctions)
                        if (junction.Key == TapKey(wire))
                            dots.Add(junction.Value);

                paths.Add(new NetPath(net, source, branches, dots));
            }

            return paths;
        }

        /// <summary>
        /// A route of right angles from <paramref name="a"/> to <paramref name="b"/>: straight out of each
        /// pin the way it faces, then across half-way. A wire that would have to run back on itself goes
        /// round, a cell above both ends.
        /// </summary>
        public static List<Point> Orthogonal(Point a, Facing? fa, Point b, Facing? fb)
        {
            Point s = fa == null ? a : Add(a, PartGeometry.Step(fa.Value), Stub);
            Point e = fb == null ? b : Add(b, PartGeometry.Step(fb.Value), Stub);
            var points = new List<Point> { a, s };

            bool acrossA = fa == Facing.Left || fa == Facing.Right;
            bool acrossB = fb == Facing.Left || fb == Facing.Right;
            bool backwards = (fa == Facing.Right && e.X < s.X) || (fa == Facing.Left && e.X > s.X);
            if (backwards)
            {
                // Back past where it came from: round, a cell above both ends, never through the parts.
                double y = Math.Max(s.Y, e.Y) + 1;
                points.Add(new Point(s.X, y));
                points.Add(new Point(e.X, y));
            }
            else if (s.X != e.X && s.Y != e.Y)
            {
                if (acrossA && acrossB)
                {
                    // Out of one side and into the other: across half-way.
                    double middle = Snap((s.X + e.X) / 2);
                    points.Add(new Point(middle, s.Y));
                    points.Add(new Point(middle, e.Y));
                }
                else if (acrossA)
                {
                    // Into a pin facing up or down -- an enable, a select -- or onto a tap. How far
                    // out on a pin's own side the wire starts decides the way in; a tap has no side.
                    double beyond = fb == null ? Stub : (s.Y - b.Y) * PartGeometry.Step(fb.Value).Y;

                    if (beyond >= Stub)
                    {
                        // Past the stub's end, or onto a tap: across, then straight in.
                        points.Add(new Point(e.X, s.Y));
                    }
                    else if (beyond > 0)
                    {
                        // Nearer than the stub's end: across at its own height and straight in, the
                        // stub cut short, rather than out past the pin and back.
                        points.Add(new Point(b.X, s.Y));
                        points.Add(b);
                        return Tidy(points);
                    }
                    else
                    {
                        // On the far side: round the part, beside the wire's own start, and in from
                        // the stub's end rather than through the body.
                        points.Add(new Point(s.X, e.Y));
                    }
                }
                else if (acrossB)
                {
                    // Out of a pin facing up or down, or a tap: straight out, then across.
                    points.Add(new Point(s.X, e.Y));
                }
                else
                {
                    double middle = Snap((s.Y + e.Y) / 2);
                    points.Add(new Point(s.X, middle));
                    points.Add(new Point(e.X, middle));
                }
            }

            points.Add(e);
            points.Add(b);
            return Tidy(points);
        }

        /// <summary>The wire's own bends between its two ends.</summary>
        public static List<Point> Through(Point a, IReadOnlyList<Point> bends, Point b)
        {
            var points = new List<Point> { a };
            points.AddRange(bends);
            points.Add(b);
            return Tidy(points);
        }

        public static double Length(IReadOnlyList<Point> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
                length += Distance(points[i - 1], points[i]);
            return length;
        }

        /// <summary>The point <paramref name="distance"/> along a run of points, clamped to its ends.</summary>
        public static Point PointAt(IReadOnlyList<Point> points, double distance)
        {
            if (points.Count == 0)
                return default;
            for (int i = 1; i < points.Count; i++)
            {
                double step = Distance(points[i - 1], points[i]);
                if (distance <= step)
                {
                    double u = step > 0 ? distance / step : 0;
                    return new Point(points[i - 1].X + (points[i].X - points[i - 1].X) * u, points[i - 1].Y + (points[i].Y - points[i - 1].Y) * u);
                }

                distance -= step;
            }

            return points[points.Count - 1];
        }

        private static List<Point> Slice(IReadOnlyList<Point> points, double from, double to)
        {
            var slice = new List<Point> { PointAt(points, from) };
            double walked = 0;
            for (int i = 1; i < points.Count; i++)
            {
                walked += Distance(points[i - 1], points[i]);
                if (walked > from && walked < to)
                    slice.Add(points[i]);
            }

            Point end = PointAt(points, to);
            if (!slice[slice.Count - 1].Equals(end))
                slice.Add(end);
            return slice;
        }

        private static List<Point> Reversed(IReadOnlyList<Point> points)
        {
            var reversed = new List<Point>(points);
            reversed.Reverse();
            return reversed;
        }

        /// <summary>Without points that repeat the one before, or sit in a straight line between their neighbours.</summary>
        private static List<Point> Tidy(List<Point> points)
        {
            var tidy = new List<Point>();
            foreach (Point p in points)
            {
                if (tidy.Count > 0 && Distance(tidy[tidy.Count - 1], p) < 1e-9)
                    continue;
                if (tidy.Count >= 2)
                {
                    Point a = tidy[tidy.Count - 2], b = tidy[tidy.Count - 1];
                    double cross = (b.X - a.X) * (p.Y - b.Y) - (b.Y - a.Y) * (p.X - b.X);
                    double dot = (b.X - a.X) * (p.X - b.X) + (b.Y - a.Y) * (p.Y - b.Y);
                    if (Math.Abs(cross) < 1e-9 && dot >= 0)
                        tidy.RemoveAt(tidy.Count - 1);
                }

                tidy.Add(p);
            }

            return tidy;
        }

        private static void Link(Dictionary<string, List<(int, bool)>> adjacent, string key, int edge, bool forward)
        {
            if (!adjacent.TryGetValue(key, out var list))
                adjacent[key] = list = new List<(int, bool)>();
            list.Add((edge, forward));
        }

        private static string Key(Wire wire, Endpoint end) => end.IsTap ? TapKey(wire.Id) : PinKey(end.Pin);
        private static string PinKey(PinRef pin) => "p" + pin.Part + "." + pin.Pin;
        private static string TapKey(int tappingWire) => "t" + tappingWire;

        private static Point Add(Point a, Point step, double by) => new Point(a.X + step.X * by, a.Y + step.Y * by);
        private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        private static double Snap(double value) => Math.Round(value * 4) / 4;
    }
}
