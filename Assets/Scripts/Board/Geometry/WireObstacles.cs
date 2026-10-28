using System;
using System.Collections.Generic;

namespace BitSorter.Board
{
    /// <summary>
    /// What an automatic route keeps clear of (Part 10e): every other net's wires -- a route may not run along one, end
    /// against one on its line, cross one at its corner, nor turn on top of one -- and every part's body and pins. Each
    /// of those reads as two nets joined, or a wire through a part, where none is. Found in the Part 10 browser smoke
    /// test: a tap from a to an AND was routed a cell along b's wire, and a and b read as one net.
    /// </summary>
    /// <remarks>
    /// Pieces are kept by the line they run along -- a piece across by its height, one up or down by its column -- so a
    /// route asks only the pieces on its own lines, and a board of hundreds of wires asks a handful each time.
    ///
    /// **Corners, pins and bodies are kept by the square of the board they are in** (Part 15g), <see cref="Square"/> cells a
    /// side, a body in every square it covers, so a piece asks only the squares under it. Each of the three is a hit only
    /// where it touches the piece's own extent, so the answer is the one asking all of them gives; asking all of them for
    /// every piece of every route tried made routing a 1,000-part drawing take a second, on every edit.
    /// </remarks>
    public sealed class WireObstacles
    {
        /// <summary>How near two pieces on one line may come end to end and still read as two: a quarter of a cell.</summary>
        public const double Apart = 0.25;

        private const double Near = 1e-6;

        private readonly Dictionary<long, List<(double Lo, double Hi, int Net)>> _across = new Dictionary<long, List<(double, double, int)>>();
        private readonly Dictionary<long, List<(double Lo, double Hi, int Net)>> _upright = new Dictionary<long, List<(double, double, int)>>();
        private readonly Dictionary<long, List<(Point At, int Net)>> _corners = new Dictionary<long, List<(Point, int)>>();
        private readonly Dictionary<long, List<Point>> _pins = new Dictionary<long, List<Point>>();
        private readonly Dictionary<long, List<PartOutline>> _bodies = new Dictionary<long, List<PartOutline>>();

        /// <summary>How many cells a side the squares are that corners, pins and bodies are kept by.</summary>
        public const double Square = 2;

        /// <summary>A wire of net <paramref name="net"/>, drawn through <paramref name="points"/>.</summary>
        public void AddWire(IReadOnlyList<Point> points, int net)
        {
            if (points == null)
                return;
            for (int i = 1; i < points.Count; i++)
            {
                Point a = points[i - 1], b = points[i];
                if (Math.Abs(a.Y - b.Y) < Near && Math.Abs(a.X - b.X) >= Near)
                    Line(_across, a.Y).Add((Math.Min(a.X, b.X), Math.Max(a.X, b.X), net));
                else if (Math.Abs(a.X - b.X) < Near && Math.Abs(a.Y - b.Y) >= Near)
                    Line(_upright, a.X).Add((Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), net));
            }

            for (int i = 1; i < points.Count - 1; i++)
                In(_corners, SquareOf(points[i].X), SquareOf(points[i].Y)).Add((points[i], net));
        }

        /// <summary>A part, by its outline: its body, which no route crosses, and its pins, which no route runs over but its own.</summary>
        public void AddPart(PartOutline outline)
        {
            if (outline == null)
                return;
            for (int x = SquareOf(outline.Left - Near); x <= SquareOf(outline.Right + Near); x++)
                for (int y = SquareOf(outline.Bottom - Near); y <= SquareOf(outline.Top + Near); y++)
                    In(_bodies, x, y).Add(outline);
            foreach (PinSpot pin in outline.Pins)
                In(_pins, SquareOf(pin.At.X), SquareOf(pin.At.Y)).Add(pin.At);
        }

        /// <summary>Whether a piece of <paramref name="route"/> runs along, or ends against on its line, a wire of a net not in <paramref name="own"/>.</summary>
        public bool RunsAlongAnother(IReadOnlyList<Point> route, ICollection<int> own)
        {
            for (int i = 1; i < route.Count; i++)
            {
                Point a = route[i - 1], b = route[i];
                bool across = Math.Abs(a.Y - b.Y) < Near;
                if (across && Math.Abs(a.X - b.X) < Near)
                    continue;
                var lines = across ? _across : _upright;
                double at = across ? a.Y : a.X;
                double lo = across ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y), hi = across ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
                if (!lines.TryGetValue(Key(at), out var pieces))
                    continue;
                foreach ((double Lo, double Hi, int Net) piece in pieces)
                    if (!Owns(own, piece.Net) && lo < piece.Hi + Apart - Near && hi > piece.Lo - Apart + Near)
                        return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="route"/> keeps clear of everything: along no other net's wire, through no other net's
        /// corner, turning on no other net's wire, over no pin but its own two ends, and through no part's body.
        /// </summary>
        public bool Clear(IReadOnlyList<Point> route, ICollection<int> own)
        {
            if (route.Count < 2 || RunsAlongAnother(route, own))
                return false;

            Point start = route[0], end = route[route.Count - 1];
            for (int i = 1; i < route.Count; i++)
            {
                Point a = route[i - 1], b = route[i];
                // The squares under the piece, a hair wider than it: nothing outside them can touch it.
                int x0 = SquareOf(Math.Min(a.X, b.X) - Near), x1 = SquareOf(Math.Max(a.X, b.X) + Near);
                int y0 = SquareOf(Math.Min(a.Y, b.Y) - Near), y1 = SquareOf(Math.Max(a.Y, b.Y) + Near);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long square = SquareKey(x, y);
                        if (_corners.TryGetValue(square, out var corners))
                            foreach ((Point At, int Net) corner in corners)
                                if (!Owns(own, corner.Net) && OnPiece(corner.At, a, b))
                                    return false;
                        if (_pins.TryGetValue(square, out var pins))
                            foreach (Point pin in pins)
                                if (!Same(pin, start) && !Same(pin, end) && OnPiece(pin, a, b))
                                    return false;
                        if (_bodies.TryGetValue(square, out var bodies))
                            foreach (PartOutline body in bodies)
                                if (Through(body, a, b))
                                    return false;
                    }
            }

            for (int i = 1; i < route.Count - 1; i++)
                if (OnAnotherWire(route[i], own))
                    return false;
            return true;
        }

        /// <summary>Whether <paramref name="p"/> lies on a wire of a net not in <paramref name="own"/>.</summary>
        private bool OnAnotherWire(Point p, ICollection<int> own)
        {
            if (_across.TryGetValue(Key(p.Y), out var across))
                foreach ((double Lo, double Hi, int Net) piece in across)
                    if (!Owns(own, piece.Net) && p.X > piece.Lo - Near && p.X < piece.Hi + Near)
                        return true;
            if (_upright.TryGetValue(Key(p.X), out var upright))
                foreach ((double Lo, double Hi, int Net) piece in upright)
                    if (!Owns(own, piece.Net) && p.Y > piece.Lo - Near && p.Y < piece.Hi + Near)
                        return true;
            return false;
        }

        private static bool Owns(ICollection<int> own, int net) => own != null && own.Contains(net);

        /// <summary>Whether <paramref name="p"/> lies on the piece from <paramref name="a"/> to <paramref name="b"/>, its ends included.</summary>
        private static bool OnPiece(Point p, Point a, Point b) =>
            p.X > Math.Min(a.X, b.X) - Near && p.X < Math.Max(a.X, b.X) + Near &&
            p.Y > Math.Min(a.Y, b.Y) - Near && p.Y < Math.Max(a.Y, b.Y) + Near;

        /// <summary>Whether the piece from <paramref name="a"/> to <paramref name="b"/> runs inside a part's body, not only along its edge.</summary>
        private static bool Through(PartOutline body, Point a, Point b)
        {
            double left = Math.Min(a.X, b.X), right = Math.Max(a.X, b.X), bottom = Math.Min(a.Y, b.Y), top = Math.Max(a.Y, b.Y);
            return left < body.Right - Near && right > body.Left + Near && bottom < body.Top - Near && top > body.Bottom + Near;
        }

        private static bool Same(Point a, Point b) => Math.Abs(a.X - b.X) < Near && Math.Abs(a.Y - b.Y) < Near;

        private static List<(double, double, int)> Line(Dictionary<long, List<(double, double, int)>> lines, double at)
        {
            long key = Key(at);
            if (!lines.TryGetValue(key, out var pieces))
                lines[key] = pieces = new List<(double, double, int)>();
            return pieces;
        }

        /// <summary>A line's key: its height or column to a thousandth of a cell, finer than any grid a wire runs on.</summary>
        private static long Key(double at) => (long)Math.Round(at * 1000);

        private static int SquareOf(double at) => (int)Math.Floor(at / Square);

        private static long SquareKey(int x, int y) => (long)x << 32 | (uint)y;

        private static List<T> In<T>(Dictionary<long, List<T>> squares, int x, int y)
        {
            long key = SquareKey(x, y);
            if (!squares.TryGetValue(key, out List<T> there))
                squares[key] = there = new List<T>();
            return there;
        }
    }
}
