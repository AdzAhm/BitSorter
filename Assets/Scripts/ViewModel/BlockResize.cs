using System;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A block resized by the handles round its selection (Part 16, asked 2026-11-02): which sides each of the eight handles
    /// moves, where they stand, and the placement a drag of one makes. Unity-free, so every drag is a test under dotnet.
    /// </summary>
    /// <remarks>
    /// **The handles are on the selection's outline**, the accent one a little outside the block -- not on the block's own
    /// outline, a press on which moves it, so the two gestures never compete (Ahmad left the choice open). **A drag moves the
    /// sides its handle is on, and the sides across stay put**, in the board's axes whichever way the block is turned. The
    /// width and height go by half cells, so a block placed on the quarter grid keeps its middle and every pin there; never
    /// below its natural size, never above <see cref="PartGeometry.MaxSize"/>; and a size it has naturally is kept as 0, so a
    /// block dragged back to its natural size is the block it was.
    /// </remarks>
    public static class BlockResize
    {
        /// <summary>How many handles a resizable block has: its four corners and the middles of its four sides.</summary>
        public const int Handles = 8;

        /// <summary>
        /// Which sides handle <paramref name="handle"/> moves, in the board's axes: -1 the left or the bottom, 1 the right or
        /// the top, 0 neither. The first is the top left corner, and they go round clockwise.
        /// </summary>
        public static (int X, int Y) Sides(int handle) => handle switch
        {
            0 => (-1, 1),
            1 => (0, 1),
            2 => (1, 1),
            3 => (1, 0),
            4 => (1, -1),
            5 => (0, -1),
            6 => (-1, -1),
            _ => (-1, 0),
        };

        /// <summary>Where handle <paramref name="handle"/> stands on the board: on <paramref name="outline"/> grown by <paramref name="room"/> cells.</summary>
        public static BoardPoint At(PartOutline outline, int handle, double room)
        {
            (int sx, int sy) = Sides(handle);
            double x = sx < 0 ? outline.Left - room : sx > 0 ? outline.Right + room : (outline.Left + outline.Right) / 2;
            double y = sy < 0 ? outline.Bottom - room : sy > 0 ? outline.Top + room : (outline.Bottom + outline.Top) / 2;
            return new BoardPoint(x, y);
        }

        /// <summary>
        /// Where <paramref name="part"/>, placed as <paramref name="start"/>, stands once handle <paramref name="handle"/> is
        /// dragged <paramref name="dx"/>, <paramref name="dy"/> cells across the board.
        /// </summary>
        public static Placement Resized(Project project, Part part, Placement start, int handle, double dx, double dy)
        {
            PartOutline outline = PartGeometry.Placed(project, part, start);
            (int sx, int sy) = Sides(handle);
            (double naturalWidth, double naturalHeight) = PartGeometry.NaturalSize(project, part);

            // Turned a quarter, its own width runs up the board and its height across it.
            bool across = start.QuarterTurns % 2 == 1;
            double leastX = across ? naturalHeight : naturalWidth, leastY = across ? naturalWidth : naturalHeight;

            double w = outline.Right - outline.Left, h = outline.Top - outline.Bottom;
            if (sx != 0)
                w = Size(w + sx * dx, leastX);
            if (sy != 0)
                h = Size(h + sy * dy, leastY);

            double cx = sx > 0 ? outline.Left + w / 2 : sx < 0 ? outline.Right - w / 2 : (outline.Left + outline.Right) / 2;
            double cy = sy > 0 ? outline.Bottom + h / 2 : sy < 0 ? outline.Top - h / 2 : (outline.Bottom + outline.Top) / 2;

            double width = across ? h : w, height = across ? w : h;
            return new Placement(new BoardPoint(cx, cy), start.QuarterTurns, start.Flipped,
                Same(width, naturalWidth) ? 0 : width, Same(height, naturalHeight) ? 0 : height);
        }

        private static double Size(double v, double least) =>
            Math.Max(least, Math.Min(PartGeometry.MaxSize, Math.Round(v / PartGeometry.SizeStep, MidpointRounding.AwayFromZero) * PartGeometry.SizeStep));

        private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-9;
    }
}
