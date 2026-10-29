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
    /// angles, and keeps clear of the other nets' wires and the parts (<see cref="Avoiding"/>). And each
    /// net as the paths a change takes from its driver to every load, which is what the workbench animates.
    /// </summary>
    /// <remarks>
    /// Wires are routed in the module's order, each clear of those before it, so a wire added never moves one
    /// already there.
    /// </remarks>
    public static class WireGeometry
    {
        /// <summary>How far a wire runs straight out of a pin before it may turn.</summary>
        public const double Stub = 0.5;

        /// <summary>Every wire's points, from its <see cref="Wire.From"/> end to its <see cref="Wire.To"/> end.</summary>
        public static Dictionary<int, IReadOnlyList<Point>> Wires(Project project, Module module) => Route(project, module, null, null);

        /// <summary>
        /// The wires on <paramref name="moved"/> parts routed again, as <paramref name="module"/>'s layout now places them, and
        /// every other wire kept where <paramref name="kept"/> has it: what a drag shows as it goes (Part 15g). Routing every
        /// wire again at each step of a drag took a third of a second a step on a 1,000-part drawing. The rest keep still,
        /// so a wire that follows keeps clear of them where they are, not where routing them all again might move them --
        /// a preview, which the drop then routes whole. A kept wire that taps one that follows keeps its old junction too:
        /// only the wires that follow are meant to be drawn from it.
        /// </summary>
        public static Dictionary<int, IReadOnlyList<Point>> Following(Project project, Module module, IReadOnlyDictionary<int, IReadOnlyList<Point>> kept,
            ICollection<int> moved)
        {
            var again = new HashSet<int>();
            foreach (Wire wire in module.Wires)
                if ((!wire.From.IsTap && moved.Contains(wire.From.Pin.Part)) || (!wire.To.IsTap && moved.Contains(wire.To.Pin.Part)))
                    again.Add(wire.Id);
            return Route(project, module, kept, again);
        }

        /// <summary>The routing itself: every wire, or only those <paramref name="again"/> names with the rest as <paramref name="kept"/> has them.</summary>
        private static Dictionary<int, IReadOnlyList<Point>> Route(Project project, Module module, IReadOnlyDictionary<int, IReadOnlyList<Point>> kept,
            HashSet<int> again)
        {
            var outlines = new Dictionary<int, PartOutline>();
            foreach (Part part in module.Parts)
                outlines[part.Id] = PartGeometry.Placed(project, part, module.Layout.PlacementOf(part.Id));

            var byId = new Dictionary<int, Wire>();
            foreach (Wire wire in module.Wires)
                byId[wire.Id] = wire;

            var done = new Dictionary<int, IReadOnlyList<Point>>();
            var busy = new HashSet<int>();
            Dictionary<int, int> nets = WireNets(module);
            var around = new WireObstacles();
            foreach (PartOutline outline in outlines.Values)
                around.AddPart(outline);
            var own = new HashSet<int>();

            // The wires kept where they are, in the way of those routed again from the start.
            if (again != null)
                foreach (Wire wire in module.Wires)
                    if (!again.Contains(wire.Id) && kept != null && kept.TryGetValue(wire.Id, out IReadOnlyList<Point> keep))
                    {
                        done[wire.Id] = keep;
                        around.AddWire(keep, nets[wire.Id]);
                    }

            IReadOnlyList<Point> RouteOf(Wire wire)
            {
                if (done.TryGetValue(wire.Id, out IReadOnlyList<Point> points))
                    return points;

                module.Layout.Routes.TryGetValue(wire.Id, out Route kept);
                bool bent = kept != null && kept.Bends.Count > 0;

                // A tap leaves its junction towards the next point along its own wire, so the pin's end is found first.
                busy.Add(wire.Id);
                Point a = default, b = default;
                Facing? fa = null, fb = null;
                if (!wire.From.IsTap)
                    (a, fa) = End(wire.From, wire.Id, default);
                (b, fb) = End(wire.To, wire.Id, bent ? kept.Bends[kept.Bends.Count - 1] : a);
                if (wire.From.IsTap)
                    (a, fa) = End(wire.From, wire.Id, bent ? kept.Bends[0] : b);
                busy.Remove(wire.Id);

                own.Clear();
                own.Add(nets[wire.Id]);
                points = bent ? Through(a, fa, kept.Bends, b, fb) : Avoiding(a, fa, b, fb, around, own);
                done[wire.Id] = points;
                around.AddWire(points, nets[wire.Id]);
                return points;
            }

            (Point, Facing?) End(Endpoint end, int owner, Point toward)
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
                Point junction = PointAt(along, Length(along) * at);
                return (junction, TapFacing(along, at, junction, toward));
            }

            foreach (Wire wire in module.Wires)
                if (again == null || again.Contains(wire.Id))
                    RouteOf(wire);
            return done;
        }

        /// <summary>Each net's paths from its driver to every load, through the wires and the taps between.</summary>
        public static List<NetPath> Nets(Project project, Module module, Netlist netlist) => Nets(module, netlist, Wires(project, module));

        /// <summary>The same, from routes already made (<see cref="Wires"/>): the workbench routes a document once for all that asks.</summary>
        public static List<NetPath> Nets(Module module, Netlist netlist, IReadOnlyDictionary<int, IReadOnlyList<Point>> wires)
        {

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

                // A wire's tap junction, by its key: every junction asked of every wire was a string made for each pair.
                var dots = new List<Point>();
                foreach (int wire in net.Wires)
                    if (junctions.TryGetValue(TapKey(wire), out Point dot))
                        dots.Add(dot);

                paths.Add(new NetPath(net, source, branches, dots));
            }

            return paths;
        }

        /// <summary>
        /// The way a tap leaves its junction, <paramref name="at"/> of the way along the wire it taps (0 to 1), for a
        /// wire going on towards <paramref name="toward"/>: across, to that side, off a piece of the tapped wire that
        /// runs up or down -- or a tap leaving the way it was going to would run along the very wire it taps, and
        /// the two be drawn as one (Part 10b). Off a piece running across, none: it leaves up or down as it always has.
        /// </summary>
        public static Facing? TapFacing(IReadOnlyList<Point> tapped, double at, Point junction, Point toward)
        {
            if (tapped == null || tapped.Count < 2)
                return null;

            double distance = Math.Max(0, Math.Min(1, at)) * Length(tapped), walked = 0;
            for (int i = 1; i < tapped.Count; i++)
            {
                double step = Distance(tapped[i - 1], tapped[i]);
                if (distance <= walked + step + 1e-9 || i == tapped.Count - 1)
                {
                    bool upright = Math.Abs(tapped[i].X - tapped[i - 1].X) < 1e-9 && step > 0;
                    if (!upright || Math.Abs(toward.X - junction.X) < 1e-9)
                        return null;
                    return toward.X > junction.X ? Facing.Right : Facing.Left;
                }

                walked += step;
            }

            return null;
        }

        /// <summary>
        /// Which net each wire is part of, as a number shared by the wires of one net: two wires are one net when they
        /// meet at a pin, or when one taps the other.
        /// </summary>
        public static Dictionary<int, int> WireNets(Module module)
        {
            var parent = new Dictionary<int, int>();
            int Find(int id)
            {
                while (parent[id] != id)
                    id = parent[id] = parent[parent[id]];
                return id;
            }

            void Join(int a, int b)
            {
                if (parent.ContainsKey(a) && parent.ContainsKey(b))
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra != rb)
                        parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
            }

            foreach (Wire wire in module.Wires)
                parent[wire.Id] = wire.Id;
            var atPin = new Dictionary<string, int>();
            foreach (Wire wire in module.Wires)
                foreach (Endpoint end in new[] { wire.From, wire.To })
                {
                    if (end.IsTap)
                        Join(wire.Id, end.TapOf);
                    else if (atPin.TryGetValue(PinKey(end.Pin), out int other))
                        Join(wire.Id, other);
                    else
                        atPin[PinKey(end.Pin)] = wire.Id;
                }

            var nets = new Dictionary<int, int>();
            foreach (Wire wire in module.Wires)
                nets[wire.Id] = Find(wire.Id);
            return nets;
        }

        /// <summary>
        /// Everything in a module a wire being drawn keeps clear of: every part, and every wire as
        /// <paramref name="wires"/> has it, by its net (<see cref="WireNets"/>) -- what the wire, once made and routed
        /// after all of them, will keep clear of.
        /// </summary>
        public static WireObstacles Obstacles(Project project, Module module, IReadOnlyDictionary<int, IReadOnlyList<Point>> wires, IReadOnlyDictionary<int, int> nets)
        {
            var around = new WireObstacles();
            foreach (Part part in module.Parts)
                around.AddPart(PartGeometry.Placed(project, part, module.Layout.PlacementOf(part.Id)));
            foreach (Wire wire in module.Wires)
                if (wires.TryGetValue(wire.Id, out IReadOnlyList<Point> points) && nets.TryGetValue(wire.Id, out int net))
                    around.AddWire(points, net);
            return around;
        }

        /// <summary>How far past its two ends a route may go round, in cells, looking for a way clear.</summary>
        public const double SearchReach = 3;

        /// <summary>
        /// The automatic route (<see cref="Orthogonal"/>) -- unless it runs along a wire of a net not in
        /// <paramref name="own"/>, when it is the shortest route of right angles, with the fewest corners, that keeps
        /// clear of everything <paramref name="around"/> holds (<see cref="WireObstacles.Clear"/>): out of each pin the
        /// way it faces, then across or up and down once, wherever on the quarter grid is clear, within
        /// <see cref="SearchReach"/> of its ends. When no route is clear it is the automatic one still.
        /// </summary>
        public static List<Point> Avoiding(Point a, Facing? fa, Point b, Facing? fb, WireObstacles around, ICollection<int> own)
        {
            List<Point> route = Orthogonal(a, fa, b, fb);
            if (around == null || !around.RunsAlongAnother(route, own))
                return route;

            Point s = fa == null ? a : Add(a, PartGeometry.Step(fa.Value), Stub);
            Point e = fb == null ? b : Add(b, PartGeometry.Step(fb.Value), Stub);

            // Each try is kept as what makes it -- across at a column, or up and down at a height -- and what it is sorted by,
            // and its points are made again only for the tries asked whether they are clear: a wire across a drawing hundreds
            // of cells wide has thousands, and a list of points apiece made routing a 1,000-part drawing take half a second
            // (Part 15g). Sorted as before, by the same keys from the same tries in the same order, it picks the same route.
            var raw = new List<Point>(6);
            var tidy = new List<Point>(6);
            var tries = new List<(bool Across, double At, int Corners, double Length, double Off)>();

            // Across, up or down at a column, and across again.
            double middle = Snap((s.X + e.X) / 2);
            for (double m = Snap(Math.Min(s.X, e.X) - SearchReach); m <= Math.Max(s.X, e.X) + SearchReach + 1e-9; m += 0.25)
                Try(true, m, Math.Abs(m - middle));

            // Up or down, across at a height, and up or down again.
            middle = Snap((s.Y + e.Y) / 2);
            for (double m = Snap(Math.Min(s.Y, e.Y) - SearchReach); m <= Math.Max(s.Y, e.Y) + SearchReach + 1e-9; m += 0.25)
                Try(false, m, Math.Abs(m - middle));

            tries.Sort((p, q) =>
            {
                int byCorners = p.Corners.CompareTo(q.Corners);
                if (byCorners != 0)
                    return byCorners;
                int byLength = p.Length.CompareTo(q.Length);
                return byLength != 0 ? byLength : p.Off.CompareTo(q.Off);
            });
            foreach ((bool Across, double At, int Corners, double Length, double Off) tried in tries)
            {
                Made(tried.Across, tried.At);
                if (around.Clear(tidy, own))
                    return new List<Point>(tidy);
            }
            return route;

            // The try at <paramref name="at"/>, tidied, into tidy.
            void Made(bool across, double at)
            {
                raw.Clear();
                raw.Add(a);
                raw.Add(s);
                raw.Add(across ? new Point(at, s.Y) : new Point(s.X, at));
                raw.Add(across ? new Point(at, e.Y) : new Point(e.X, at));
                raw.Add(e);
                raw.Add(b);
                TidyInto(raw, tidy);
            }

            void Try(bool across, double at, double off)
            {
                Made(across, at);
                if (tidy.Count >= 2 && Ends(tidy[0], tidy[1], fa) && Ends(tidy[tidy.Count - 1], tidy[tidy.Count - 2], fb))
                    tries.Add((across, at, tidy.Count - 2, Length(tidy), off));
            }
        }

        /// <summary>
        /// Whether a route's piece at one of its ends, from <paramref name="end"/> to <paramref name="next"/>, leaves the
        /// way the pin there faces -- the stub, or the stub run on -- and not back over it; and from a tap, which has no
        /// facing, up or down, off the wire it taps rather than along it.
        /// </summary>
        private static bool Ends(Point end, Point next, Facing? facing)
        {
            double dx = next.X - end.X, dy = next.Y - end.Y;
            if (facing == null)
                return Math.Abs(dx) < 1e-9;
            Point step = PartGeometry.Step(facing.Value);
            return dx * step.X + dy * step.Y > 1e-9 && Math.Abs(dx * step.Y - dy * step.X) < 1e-9;
        }

        /// <summary>
        /// A route of right angles from <paramref name="a"/> to <paramref name="b"/>: straight out of each
        /// pin the way it faces, then across half-way. A wire that would have to run back on itself goes
        /// round, a cell above both ends -- and where that would come back over a pin, into a pin facing up
        /// or down or out of one, round at whichever column or height leaves both pins the way they face
        /// (<see cref="Round"/>).
        /// </summary>
        public static List<Point> Orthogonal(Point a, Facing? fa, Point b, Facing? fb)
        {
            // A route of one point -- a wire being drawn, still on the pin it starts from -- has no piece to go wrong.
            List<Point> route = Straight(a, fa, b, fb);
            if (route.Count < 2 || ((fa == null || Ends(route[0], route[1], fa)) && (fb == null || Ends(route[route.Count - 1], route[route.Count - 2], fb))))
                return route;
            return Round(a, fa, b, fb) ?? route;
        }

        /// <summary>
        /// The route of right angles that leaves each pin the way it faces, round a column or a height: half-way between the
        /// stubs' ends, or a cell beyond both -- the fewest corners, then the shortest. Null when none does. A pin turned to face
        /// up or down (Part 15h-3) came back over itself through its part when the route to it went round a cell above both
        /// ends: a select wired from below and to the right came down into the mux from inside it.
        /// </summary>
        private static List<Point> Round(Point a, Facing? fa, Point b, Facing? fb)
        {
            Point s = fa == null ? a : Add(a, PartGeometry.Step(fa.Value), Stub);
            Point e = fb == null ? b : Add(b, PartGeometry.Step(fb.Value), Stub);
            var raw = new List<Point>(6);
            var tidy = new List<Point>(6);
            List<Point> best = null;
            int bestCorners = int.MaxValue;
            double bestLength = double.MaxValue;

            void Try(bool across, double at)
            {
                raw.Clear();
                raw.Add(a);
                raw.Add(s);
                raw.Add(across ? new Point(at, s.Y) : new Point(s.X, at));
                raw.Add(across ? new Point(at, e.Y) : new Point(e.X, at));
                raw.Add(e);
                raw.Add(b);
                TidyInto(raw, tidy);
                if (tidy.Count < 2 || !Ends(tidy[0], tidy[1], fa) || !Ends(tidy[tidy.Count - 1], tidy[tidy.Count - 2], fb))
                    return;
                int corners = tidy.Count - 2;
                double length = Length(tidy);
                if (corners < bestCorners || (corners == bestCorners && length < bestLength - 1e-9))
                {
                    best = new List<Point>(tidy);
                    bestCorners = corners;
                    bestLength = length;
                }
            }

            foreach (double at in new[] { Snap((s.X + e.X) / 2), Snap(Math.Max(s.X, e.X) + 1), Snap(Math.Min(s.X, e.X) - 1) })
                Try(true, at);
            foreach (double at in new[] { Snap((s.Y + e.Y) / 2), Snap(Math.Max(s.Y, e.Y) + 1), Snap(Math.Min(s.Y, e.Y) - 1) })
                Try(false, at);
            return best;
        }

        /// <summary>The route <see cref="Orthogonal"/> takes first: out of each pin, across half-way, or round a cell above both ends.</summary>
        private static List<Point> Straight(Point a, Facing? fa, Point b, Facing? fb)
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

        /// <summary>
        /// The wire's own bends between its two ends, <paramref name="fa"/> and <paramref name="fb"/> the way each end's
        /// pin faces (null for a tap). A piece that runs straight from one to the next is drawn straight, as it always
        /// was. One gone aslant -- a part moved after its wire's bends were made, and its pin with it (Part 10b) -- is
        /// drawn with right angles: out of a pin the way it faces and round, as an automatic route is, and between two
        /// bends with one corner, turning at the first of them.
        /// </summary>
        public static List<Point> Through(Point a, Facing? fa, IReadOnlyList<Point> bends, Point b, Facing? fb)
        {
            var points = new List<Point> { a };
            Point at = a;
            Facing? leaving = fa;
            for (int i = 0; i <= bends.Count; i++)
            {
                Point next = i < bends.Count ? bends[i] : b;
                Facing? entering = i < bends.Count ? null : fb;

                if (Aligned(at, next))
                {
                    points.Add(next);
                }
                else if (leaving != null || entering != null)
                {
                    List<Point> round = Orthogonal(at, leaving, next, entering);
                    for (int r = 1; r < round.Count; r++)
                        points.Add(round[r]);
                }
                else
                {
                    points.Add(Corner(points, at, next));
                    points.Add(next);
                }

                at = next;
                leaving = null;
            }

            return Tidy(points);
        }

        /// <summary>
        /// Where a piece from <paramref name="at"/> to <paramref name="next"/>, which is not straight, turns: at right
        /// angles to the way the wire came into <paramref name="at"/> -- up or down after a run across, across after
        /// a run up or down, or after nothing.
        /// </summary>
        private static Point Corner(List<Point> points, Point at, Point next)
        {
            bool cameAcross = points.Count >= 2 && Math.Abs(points[points.Count - 2].Y - at.Y) < 1e-9;
            return cameAcross ? new Point(at.X, next.Y) : new Point(next.X, at.Y);
        }

        private static bool Aligned(Point a, Point b) => Math.Abs(a.X - b.X) < 1e-9 || Math.Abs(a.Y - b.Y) < 1e-9;

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

        /// <summary>
        /// Without points that repeat the one before, sit in a straight line between their neighbours, or are where a
        /// wire runs on and comes straight back: a bend that a part's move left behind its pin (Part 10b) is no
        /// corner, and drawn it would be a spike.
        /// </summary>
        public static List<Point> Tidy(List<Point> points)
        {
            var tidy = new List<Point>();
            TidyInto(points, tidy);
            return tidy;
        }

        /// <summary><see cref="Tidy"/>, into a list of the caller's, emptied first: a route tried thousands of times allocates nothing.</summary>
        private static void TidyInto(List<Point> points, List<Point> tidy)
        {
            tidy.Clear();
            foreach (Point p in points)
            {
                if (tidy.Count > 0 && Distance(tidy[tidy.Count - 1], p) < 1e-9)
                    continue;
                while (tidy.Count >= 2)
                {
                    Point a = tidy[tidy.Count - 2], b = tidy[tidy.Count - 1];
                    double cross = (b.X - a.X) * (p.Y - b.Y) - (b.Y - a.Y) * (p.X - b.X);
                    if (Math.Abs(cross) >= 1e-9)
                        break;
                    tidy.RemoveAt(tidy.Count - 1);
                }

                if (tidy.Count > 0 && Distance(tidy[tidy.Count - 1], p) < 1e-9)
                    continue;
                tidy.Add(p);
            }
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
