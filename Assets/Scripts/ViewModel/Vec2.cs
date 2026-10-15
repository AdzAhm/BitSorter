using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// A point or a direction on the stage: Unity's <c>Vector2</c>, operation for operation, without
    /// Unity.
    /// </summary>
    /// <remarks>
    /// **Its own type, not <c>System.Numerics.Vector2</c>, which the plan named.** The point of this
    /// layer is that a test run by <c>dotnet test</c> checks the same numbers the editor and the browser
    /// draw, and the shots are held to the pixel. <c>System.Numerics</c> is implemented twice -- once in
    /// the .NET CI runs, once in the older class library Unity ships -- and the two have not always
    /// computed <c>Lerp</c> or a division by a scalar the same way: a point could differ in its last
    /// bit between a test and the game. These are Unity's own formulas (its <c>Vector2.cs</c>, as
    /// published), so the floats are the ones the workbench drew before it moved here.
    ///
    /// Lower-case members, as Unity's, so a view file that does its arithmetic on these can name the
    /// type <c>Vector2</c> with a <c>using</c> alias and read as it did. **Its <c>==</c> is Unity's
    /// too**: two points within 1e-5 of each other are equal, which <see cref="SchematicView"/>'s
    /// "did anything move" answers rely on. <see cref="Equals(Vec2)"/> is exact, as Unity's is.
    ///
    /// **Held to it, not trusted:** an Edit Mode test computes every member here and its Unity
    /// counterpart for a million random cases and compares the bits, since a published source is not
    /// the compiled code -- <see cref="Distance"/> was the one that differed. Every member is marked
    /// for inlining, as Unity's are, because the painter does its arithmetic in these on every frame.
    /// </remarks>
    public struct Vec2 : IEquatable<Vec2>
    {
        /// <summary>Unity's tolerance: shorter than this is no length, and nearer than this is the same point.</summary>
        public const float Epsilon = 0.00001f;

        public float x;
        public float y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vec2 zero => new Vec2(0f, 0f);
        public static Vec2 one => new Vec2(1f, 1f);

        public float magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (float)Math.Sqrt(x * x + y * y);
        }

        public float sqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => x * x + y * y;
        }

        /// <summary>This direction at length 1, or zero when it has no length to speak of.</summary>
        public Vec2 normalized
        {
            get
            {
                var v = new Vec2(x, y);
                v.Normalize();
                return v;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            float mag = magnitude;
            if (mag > Epsilon)
                this = this / mag;
            else
                this = zero;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vec2 lhs, Vec2 rhs) => lhs.x * rhs.x + lhs.y * rhs.y;

        /// <summary>
        /// How far apart two points are: the length of the difference, which is what Unity's
        /// <c>Distance</c> computes to the bit. Its published source subtracts and squares inline,
        /// and written that way here it differed from the editor's in the last bit for about one pair
        /// in eleven: the sum was rounded to a float before its square root, which Unity's is not.
        /// <c>WorkbenchTests.Vec2_ComputesWhatUnitysVector2Computes_ToTheBit</c> found it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Vec2 a, Vec2 b) => (a - b).magnitude;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => LerpUnclamped(a, b, Arith.Clamp01(t));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 LerpUnclamped(Vec2 a, Vec2 b, float t) =>
            new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator *(Vec2 a, Vec2 b) => new Vec2(a.x * b.x, a.y * b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator /(Vec2 a, Vec2 b) => new Vec2(a.x / b.x, a.y / b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator *(Vec2 a, float d) => new Vec2(a.x * d, a.y * d);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator *(float d, Vec2 a) => new Vec2(a.x * d, a.y * d);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator /(Vec2 a, float d) => new Vec2(a.x / d, a.y / d);

        /// <summary>Unity's equality: nearer than <see cref="Epsilon"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vec2 lhs, Vec2 rhs)
        {
            float dx = lhs.x - rhs.x;
            float dy = lhs.y - rhs.y;
            return dx * dx + dy * dy < Epsilon * Epsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vec2 lhs, Vec2 rhs) => !(lhs == rhs);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Vec2 other) => x == other.x && y == other.y;

        public override bool Equals(object other) => other is Vec2 v && Equals(v);

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

        public override string ToString() =>
            "(" + x.ToString("F2", CultureInfo.InvariantCulture) + ", " + y.ToString("F2", CultureInfo.InvariantCulture) + ")";
    }
}
