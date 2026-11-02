using System;
using System.Collections.Generic;

namespace BitSorter.Board
{
    /// <summary>A point on the board, in cells. Placement snaps to a quarter of a cell unless the player places freely.</summary>
    public readonly struct Point : IEquatable<Point>
    {
        public Point(double x, double y)
        {
            X = x;
            Y = y;
        }

        public readonly double X;
        public readonly double Y;

        public bool Equals(Point other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Point other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>
    /// Where a part stands: its position, turned a number of quarter turns, and mirrored or not -- and, for a block, the size
    /// it was dragged to (Part 16, resizable blocks).
    /// </summary>
    /// <remarks>
    /// **The size is the part's own**, width along its pins' sides and height down them, as it stands facing right and
    /// unturned, in cells: 0 is its natural size, which every part but a block always has (<see cref="PartGeometry.Local"/>).
    /// Kept here, beside the module and never in it, so resizing a block can never change what the circuit does. Every edit
    /// that moves, turns or mirrors a part keeps it, through the helpers below.
    /// </remarks>
    public readonly struct Placement : IEquatable<Placement>
    {
        public Placement(Point position, int quarterTurns = 0, bool flipped = false, double width = 0, double height = 0)
        {
            Position = position;
            QuarterTurns = ((quarterTurns % 4) + 4) % 4;
            Flipped = flipped;
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
        }

        public Placement(double x, double y) : this(new Point(x, y)) { }

        public readonly Point Position;
        public readonly int QuarterTurns;
        public readonly bool Flipped;

        /// <summary>The part's width and height as it was resized, in its own cells; 0 for natural.</summary>
        public readonly double Width, Height;

        /// <summary>Whether it was resized at all.</summary>
        public bool HasSize => Width > 0 || Height > 0;

        /// <summary>The same placement at <paramref name="position"/>.</summary>
        public Placement WithPosition(Point position) => new Placement(position, QuarterTurns, Flipped, Width, Height);

        /// <summary>The same placement moved by <paramref name="dx"/>, <paramref name="dy"/> cells.</summary>
        public Placement Moved(double dx, double dy) => WithPosition(new Point(Position.X + dx, Position.Y + dy));

        /// <summary>The same placement, turned to <paramref name="quarterTurns"/> and mirrored or not, at <paramref name="position"/>.</summary>
        public Placement Turned(Point position, int quarterTurns, bool flipped) => new Placement(position, quarterTurns, flipped, Width, Height);

        /// <summary>The same placement at a size of its own; 0 for natural.</summary>
        public Placement WithSize(double width, double height) => new Placement(Position, QuarterTurns, Flipped, width, height);

        public bool Equals(Placement other) => Position.Equals(other.Position) && QuarterTurns == other.QuarterTurns && Flipped == other.Flipped
            && Width.Equals(other.Width) && Height.Equals(other.Height);
        public override bool Equals(object obj) => obj is Placement other && Equals(other);
        public override int GetHashCode() => ((Position.GetHashCode() * 31 + QuarterTurns * 2 + (Flipped ? 1 : 0)) * 397) ^ Width.GetHashCode() * 17 ^ Height.GetHashCode();
    }

    /// <summary>How a wire is drawn: its bends, whether it is drawn smooth through them, and for a tap, how far along the tapped wire it joins.</summary>
    public sealed record Route
    {
        public IReadOnlyList<Point> Bends { get; init; } = Array.Empty<Point>();
        public bool Smooth { get; init; }

        /// <summary>For a wire that taps another: how far along that one the junction is, 0 to 1.</summary>
        public double TapAt { get; init; } = 0.5;
    }

    /// <summary>
    /// Where everything on a module is drawn, kept beside the module and never in it (ADR-7): nothing
    /// that compiles or prints reads it, so moving a part can never change what the circuit does.
    /// </summary>
    public sealed record Layout
    {
        public static readonly Layout Empty = new Layout();

        public IReadOnlyDictionary<int, Placement> Parts { get; init; } = new Dictionary<int, Placement>();
        public IReadOnlyDictionary<int, Route> Routes { get; init; } = new Dictionary<int, Route>();
        public Point Pan { get; init; }
        public double Zoom { get; init; } = 1;

        public Placement PlacementOf(int part) => Parts.TryGetValue(part, out Placement p) ? p : default;

        public Layout WithPart(int part, Placement placement) => this with { Parts = With(Parts, part, placement) };
        public Layout WithoutPart(int part) => this with { Parts = Without(Parts, part) };
        public Layout WithRoute(int wire, Route route) => this with { Routes = With(Routes, wire, route) };
        public Layout WithoutRoute(int wire) => this with { Routes = Without(Routes, wire) };

        private static IReadOnlyDictionary<int, T> With<T>(IReadOnlyDictionary<int, T> from, int key, T value)
        {
            var copy = new Dictionary<int, T>(from.Count + 1);
            foreach (KeyValuePair<int, T> entry in from)
                copy[entry.Key] = entry.Value;
            copy[key] = value;
            return copy;
        }

        private static IReadOnlyDictionary<int, T> Without<T>(IReadOnlyDictionary<int, T> from, int key)
        {
            if (!from.ContainsKey(key))
                return from;

            var copy = new Dictionary<int, T>(from.Count);
            foreach (KeyValuePair<int, T> entry in from)
                if (entry.Key != key)
                    copy[entry.Key] = entry.Value;
            return copy;
        }
    }
}
