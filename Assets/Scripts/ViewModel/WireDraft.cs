using System;
using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A wire being drawn (Part 10b): where it starts -- a pin, or a tap on a wire -- and the bends clicked on the
    /// way, in the board's cells. It is drawn as it would run to wherever the pointer is, through the very route a
    /// finished wire is drawn by (<see cref="WireGeometry.Through"/>, or the automatic route with no bends), so what
    /// is shown while it is drawn is what is made. Unity-free.
    /// </summary>
    /// <remarks>
    /// **Only the bends clicked are kept.** The corners between them come from the route, as right angles, so a part
    /// moved after the wire is made takes the wire round with it rather than off at a slant. A wire with no bends
    /// clicked keeps no route at all, and is routed automatically wherever its parts go.
    /// </remarks>
    public sealed class WireDraft
    {
        private readonly List<BoardPoint> _bends = new List<BoardPoint>();

        /// <param name="from">The end it starts from.</param>
        /// <param name="start">Where that is on the board: the pin, or the junction on the wire tapped.</param>
        /// <param name="facing">The way the pin faces; null for a tap.</param>
        /// <param name="tapAt">For a tap, how far along the tapped wire the junction is, 0 to 1.</param>
        /// <param name="tapped">For a tap, the tapped wire's points: which way the wire leaves it depends on where it goes (<see cref="WireGeometry.TapFacing"/>).</param>
        public WireDraft(Endpoint from, BoardPoint start, Facing? facing, double tapAt = 0.5, IReadOnlyList<BoardPoint> tapped = null)
        {
            From = from;
            Start = start;
            Facing = facing;
            TapAt = tapAt;
            _tapped = tapped;
        }

        private readonly IReadOnlyList<BoardPoint> _tapped;

        public Endpoint From { get; }
        public BoardPoint Start { get; }
        public Facing? Facing { get; }
        public double TapAt { get; }

        /// <summary>The bends clicked so far, in order.</summary>
        public IReadOnlyList<BoardPoint> Bends => _bends;

        /// <summary>A bend clicked at <paramref name="at"/>; none where the last one is, or at the start.</summary>
        public void Bend(BoardPoint at)
        {
            BoardPoint last = _bends.Count > 0 ? _bends[_bends.Count - 1] : Start;
            if (!Same(last, at))
                _bends.Add(at);
        }

        /// <summary>How the wire would run to <paramref name="end"/>, entering it the way <paramref name="endFacing"/> says (null for a level tap or the bare pointer).</summary>
        public List<BoardPoint> PathTo(BoardPoint end, Facing? endFacing)
        {
            // From a tap, the way out of the junction is the one the finished wire's route will take, towards what comes next.
            BoardPoint next = _bends.Count > 0 ? _bends[0] : end;
            Facing? leaving = From.IsTap ? WireGeometry.TapFacing(_tapped, TapAt, Start, next) : Facing;
            return _bends.Count > 0
                ? WireGeometry.Through(Start, leaving, _bends, end, endFacing)
                : WireGeometry.Orthogonal(Start, leaving, end, endFacing);
        }

        /// <summary>The point the wire would reach an end from: its last bend, or its start.</summary>
        public BoardPoint Last => _bends.Count > 0 ? _bends[_bends.Count - 1] : Start;

        /// <summary>
        /// The route the wire is made with: its bends, and where a tap joins -- the start's, or <paramref name="endTapAt"/>
        /// for a wire that ends on one. Null for a wire with neither, which is routed automatically.
        /// </summary>
        public Route RouteTo(double? endTapAt)
        {
            bool tap = From.IsTap || endTapAt.HasValue;
            if (_bends.Count == 0 && !tap)
                return null;
            return new Route { Bends = _bends.ToArray(), TapAt = From.IsTap ? TapAt : endTapAt ?? 0.5 };
        }

        /// <summary>
        /// Where on <paramref name="wire"/>'s points a tap at <paramref name="along"/> (0 to 1) joins, put on the quarter
        /// grid along the piece it is on, so a wire leaving it runs on the grid -- and how far along it is then.
        /// </summary>
        public static BoardPoint TapPoint(IReadOnlyList<BoardPoint> wire, double along, out double snapped)
        {
            snapped = along;
            if (wire == null || wire.Count < 2)
                return wire != null && wire.Count == 1 ? wire[0] : default;

            double length = WireGeometry.Length(wire);
            double distance = Math.Max(0, Math.Min(1, along)) * length, walked = 0;
            for (int i = 1; i < wire.Count; i++)
            {
                BoardPoint a = wire[i - 1], b = wire[i];
                double step = Distance(a, b);
                if (distance <= walked + step || i == wire.Count - 1)
                {
                    // On this piece: the coordinate that runs along it to the grid, kept within it.
                    double u = step > 0 ? (distance - walked) / step : 0;
                    BoardPoint p = new BoardPoint(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
                    if (Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y))
                        p = new BoardPoint(Clamp(CanvasSnap.Snap(p.X), a.X, b.X), p.Y);
                    else
                        p = new BoardPoint(p.X, Clamp(CanvasSnap.Snap(p.Y), a.Y, b.Y));
                    snapped = length > 0 ? (walked + Distance(a, p)) / length : 0;
                    return p;
                }

                walked += step;
            }

            return wire[wire.Count - 1];
        }

        private static double Clamp(double value, double a, double b) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), value));

        private static double Distance(BoardPoint a, BoardPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static bool Same(BoardPoint a, BoardPoint b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
    }
}
