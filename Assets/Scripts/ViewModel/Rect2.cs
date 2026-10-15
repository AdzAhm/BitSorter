using System;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A rectangle on the stage, y down: Unity's <c>Rect</c>, for the members the workbench uses, with
    /// its arithmetic. See <see cref="Vec2"/> for why it is not borrowed from anywhere.
    /// </summary>
    public readonly struct Rect2 : IEquatable<Rect2>
    {
        public Rect2(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        /// <summary>The rectangle between two corners, given as its least and greatest x and y.</summary>
        public static Rect2 MinMaxRect(float xmin, float ymin, float xmax, float ymax) =>
            new Rect2(xmin, ymin, xmax - xmin, ymax - ymin);

        public readonly float x;
        public readonly float y;
        public readonly float width;
        public readonly float height;

        public float xMin => x;
        public float yMin => y;
        public float xMax => width + x;
        public float yMax => height + y;

        public Vec2 center => new Vec2(x + width / 2f, y + height / 2f);

        public bool Equals(Rect2 other) =>
            x.Equals(other.x) && y.Equals(other.y) && width.Equals(other.width) && height.Equals(other.height);

        public override bool Equals(object other) => other is Rect2 r && Equals(r);

        public override int GetHashCode() =>
            x.GetHashCode() ^ (width.GetHashCode() << 2) ^ (y.GetHashCode() >> 2) ^ (height.GetHashCode() >> 1);
    }
}
