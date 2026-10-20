using System;
using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A wire reshaped by hand (Part 10b-2): a piece of it dragged across itself, or a corner dragged, in the board's
    /// cells. The ends stay where they are -- a pin, or a tap's junction -- and a piece that reaches one keeps the
    /// pin's stub before it turns, as the automatic route leaves a pin. Every corner is a right angle before and
    /// after. Unity-free.
    /// </summary>
    public static class WireShape
    {
        /// <summary>
        /// <paramref name="points"/> with piece <paramref name="segment"/> (from point <paramref name="segment"/> to the
        /// next) moved across itself by <paramref name="dx"/>, <paramref name="dy"/>: a piece running across moves up
        /// or down, one running up or down moves sideways, and the other part of the move is ignored.
        /// </summary>
        public static List<BoardPoint> MoveSegment(IReadOnlyList<BoardPoint> points, int segment, double dx, double dy)
        {
            if (points == null || points.Count < 2 || segment < 0 || segment > points.Count - 2)
                return points == null ? new List<BoardPoint>() : new List<BoardPoint>(points);

            var offsets = new BoardPoint[points.Count];
            BoardPoint by = Across(points[segment], points[segment + 1], dx, dy);
            offsets[segment] = Add(offsets[segment], by);
            offsets[segment + 1] = Add(offsets[segment + 1], by);
            return Shifted(points, offsets);
        }

        /// <summary>
        /// <paramref name="points"/> with corner <paramref name="corner"/> -- a point between the two ends -- moved by
        /// <paramref name="dx"/>, <paramref name="dy"/>: the two pieces that meet there each moved across itself.
        /// </summary>
        public static List<BoardPoint> MoveCorner(IReadOnlyList<BoardPoint> points, int corner, double dx, double dy)
        {
            if (points == null || corner < 1 || corner > points.Count - 2)
                return points == null ? new List<BoardPoint>() : new List<BoardPoint>(points);

            var offsets = new BoardPoint[points.Count];
            BoardPoint before = Across(points[corner - 1], points[corner], dx, dy);
            BoardPoint after = Across(points[corner], points[corner + 1], dx, dy);
            offsets[corner - 1] = Add(offsets[corner - 1], before);
            offsets[corner] = Add(Add(offsets[corner], before), after);
            offsets[corner + 1] = Add(offsets[corner + 1], after);
            return Shifted(points, offsets);
        }

        /// <summary>The bends a route keeps: its points between the two ends.</summary>
        public static BoardPoint[] Bends(IReadOnlyList<BoardPoint> points)
        {
            if (points == null || points.Count <= 2)
                return Array.Empty<BoardPoint>();
            var bends = new BoardPoint[points.Count - 2];
            for (int i = 1; i < points.Count - 1; i++)
                bends[i - 1] = points[i];
            return bends;
        }

        /// <summary>The part of a move that is across the piece from <paramref name="a"/> to <paramref name="b"/>.</summary>
        private static BoardPoint Across(BoardPoint a, BoardPoint b, double dx, double dy) =>
            Math.Abs(b.Y - a.Y) <= Math.Abs(b.X - a.X) ? new BoardPoint(0, dy) : new BoardPoint(dx, 0);

        /// <summary>
        /// Each point moved by its offset, the two ends kept where they are: an end whose neighbour moved keeps its stub
        /// -- straight out the way the wire left it -- and a short piece joins the stub to where the wire now goes.
        /// </summary>
        private static List<BoardPoint> Shifted(IReadOnlyList<BoardPoint> points, BoardPoint[] offsets)
        {
            int last = points.Count - 1;
            var shaped = new List<BoardPoint> { points[0] };

            if (Moves(offsets[0]))
            {
                BoardPoint stub = Stub(points[0], points[1]);
                shaped.Add(stub);
                shaped.Add(Add(stub, offsets[0]));
            }

            for (int i = 1; i < last; i++)
                shaped.Add(Add(points[i], offsets[i]));

            if (Moves(offsets[last]))
            {
                BoardPoint stub = Stub(points[last], points[last - 1]);
                shaped.Add(Add(stub, offsets[last]));
                shaped.Add(stub);
            }

            shaped.Add(points[last]);
            return WireGeometry.Tidy(shaped);
        }

        /// <summary>Where a wire leaving <paramref name="end"/> towards <paramref name="next"/> may first turn: a stub along, or the whole way when the piece is shorter.</summary>
        private static BoardPoint Stub(BoardPoint end, BoardPoint next)
        {
            double length = Math.Sqrt((next.X - end.X) * (next.X - end.X) + (next.Y - end.Y) * (next.Y - end.Y));
            if (length <= 1e-9)
                return end;
            double along = Math.Min(WireGeometry.Stub, length) / length;
            return new BoardPoint(end.X + (next.X - end.X) * along, end.Y + (next.Y - end.Y) * along);
        }

        private static bool Moves(BoardPoint offset) => Math.Abs(offset.X) > 1e-9 || Math.Abs(offset.Y) > 1e-9;

        private static BoardPoint Add(BoardPoint a, BoardPoint b) => new BoardPoint(a.X + b.X, a.Y + b.Y);
    }
}
