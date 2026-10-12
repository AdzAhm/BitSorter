using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// Draws the workbench's picture into a UI mesh: anti-aliased strokes along routes and paths,
    /// discs, rings, soft glows and filled shapes.
    /// </summary>
    /// <remarks>
    /// **Edges are smoothed by the mesh itself**: every stroke and disc carries a strip about a
    /// pixel wide along its edge that fades to nothing, so a wire is equally smooth at any size and
    /// in a browser build, which has no MSAA to lean on. A glow is the same strip made wide.
    ///
    /// Positions are the stage's own units, y down, as the layout is written; the painter flips
    /// them for the canvas, whose y runs up from the graphic's top-left pivot. <see cref="Begin"/>
    /// is told how many units a screen pixel is, which is how wide the smoothing strip is.
    ///
    /// Reuses its own buffers, so a frame of drawing makes no garbage.
    /// </remarks>
    internal sealed class VectorPainter
    {
        private VertexHelper _vh;
        private float _pixel = 1f;
        private readonly List<Vector2> _path = new List<Vector2>(128);

        /// <summary>Starts drawing into <paramref name="vh"/>, where one screen pixel is <paramref name="pixel"/> units.</summary>
        public void Begin(VertexHelper vh, float pixel)
        {
            _vh = vh;
            _pixel = Mathf.Max(1e-3f, pixel);
        }

        /// <summary>How many units a screen pixel is.</summary>
        public float Pixel => _pixel;

        // -----------------------------------------------------------------
        // Strokes
        // -----------------------------------------------------------------

        /// <summary>The stretch of <paramref name="route"/> from <paramref name="from"/> to <paramref name="to"/>, stroked.</summary>
        public void Stroke(Route route, float from, float to, float width, Color colour, bool roundCaps = true)
        {
            if (!Slice(route, from, to))
                return;

            Strip(_path, false, width, colour, 0f, roundCaps);
        }

        /// <summary>A whole route, stroked.</summary>
        public void Stroke(Route route, float width, Color colour) => Stroke(route, 0f, route.Length, width, colour);

        /// <summary>
        /// A soft glow along part of a route: a core <paramref name="width"/> wide and a falloff of
        /// <paramref name="blur"/> each side, like a stroke under a blur filter.
        /// </summary>
        public void Glow(Route route, float from, float to, float width, float blur, Color colour)
        {
            if (!Slice(route, from, to))
                return;

            Strip(_path, false, width, colour, blur, roundCaps: true);
        }

        /// <summary>A route drawn in dashes, as an undriven wire is.</summary>
        public void Dashed(Route route, float width, Color colour, float on, float off)
        {
            for (float s = 0f; s < route.Length; s += on + off)
                Stroke(route, s, Mathf.Min(s + on, route.Length), width, colour, roundCaps: false);
        }

        /// <summary>A straight line, with square ends.</summary>
        public void Line(Vector2 a, Vector2 b, float width, Color colour, bool roundCaps = false)
        {
            _path.Clear();
            _path.Add(a);
            _path.Add(b);
            Strip(_path, false, width, colour, 0f, roundCaps);
        }

        /// <summary>A closed outline through <paramref name="points"/>, stroked.</summary>
        public void Outline(List<Vector2> points, float width, Color colour) =>
            Strip(points, true, width, colour, 0f, false);

        /// <summary>A cross: two strokes through a centre. Says "cancelled" by shape, not colour alone.</summary>
        public void Cross(Vector2 centre, float half, float width, Color colour)
        {
            Line(centre + new Vector2(-half, -half), centre + new Vector2(half, half), width, colour, roundCaps: true);
            Line(centre + new Vector2(-half, half), centre + new Vector2(half, -half), width, colour, roundCaps: true);
        }

        // -----------------------------------------------------------------
        // Round things and filled shapes
        // -----------------------------------------------------------------

        /// <summary>A filled disc.</summary>
        public void Disc(Vector2 centre, float radius, Color colour)
        {
            float inner = Mathf.Max(0f, radius - _pixel * 0.5f);
            Fan(centre, inner, colour);
            Annulus(centre, inner, radius + _pixel * 0.5f, colour, Transparent(colour));
        }

        /// <summary>A circle's outline.</summary>
        public void Ring(Vector2 centre, float radius, float width, Color colour)
        {
            _path.Clear();
            int steps = Steps(radius, Mathf.PI * 2f);
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                _path.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }

            Strip(_path, true, width, colour, 0f, false);
        }

        /// <summary>
        /// A soft halo: full <paramref name="colour"/> out to <paramref name="solid"/>, fading to
        /// nothing at <paramref name="radius"/>, as a disc under a blur filter.
        /// </summary>
        public void Halo(Vector2 centre, float solid, float radius, Color colour)
        {
            Fan(centre, solid, colour);
            Annulus(centre, solid, radius, colour, Transparent(colour));
        }

        /// <summary>A convex shape, filled. Its edge is left for an outline drawn over it to smooth.</summary>
        public void Fill(List<Vector2> points, Color colour)
        {
            if (points.Count < 3)
                return;

            Vector2 centre = Vector2.zero;
            foreach (Vector2 p in points) centre += p;
            centre /= points.Count;

            int first = _vh.currentVertCount;
            _vh.AddVert(Flip(centre), colour, Vector4.zero);
            foreach (Vector2 p in points) _vh.AddVert(Flip(p), colour, Vector4.zero);

            for (int i = 0; i < points.Count; i++)
                _vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % points.Count);
        }

        /// <summary>An axis-aligned rectangle, filled, with no smoothing: a grid line or a band.</summary>
        public void Rect(float x0, float y0, float x1, float y1, Color colour)
        {
            int first = _vh.currentVertCount;
            _vh.AddVert(Flip(new Vector2(x0, y0)), colour, Vector4.zero);
            _vh.AddVert(Flip(new Vector2(x1, y0)), colour, Vector4.zero);
            _vh.AddVert(Flip(new Vector2(x1, y1)), colour, Vector4.zero);
            _vh.AddVert(Flip(new Vector2(x0, y1)), colour, Vector4.zero);
            _vh.AddTriangle(first, first + 1, first + 2);
            _vh.AddTriangle(first, first + 2, first + 3);
        }

        // -----------------------------------------------------------------
        // Shapes as point lists, for Fill and Outline
        // -----------------------------------------------------------------

        /// <summary>A rounded rectangle's outline, clockwise from its top-left.</summary>
        public static void RoundedRect(List<Vector2> into, float x, float y, float width, float height, float corner)
        {
            into.Clear();
            corner = Mathf.Min(corner, Mathf.Min(width, height) * 0.5f);
            Corner(into, new Vector2(x + width - corner, y + corner), corner, -90f);
            Corner(into, new Vector2(x + width - corner, y + height - corner), corner, 0f);
            Corner(into, new Vector2(x + corner, y + height - corner), corner, 90f);
            Corner(into, new Vector2(x + corner, y + corner), corner, 180f);
        }

        /// <summary>A circle's outline, for a filled disc under a stroked ring.</summary>
        public static void Circle(List<Vector2> into, Vector2 centre, float radius, int steps = 32)
        {
            into.Clear();
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                into.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        /// <summary>A quarter circle's points, starting <paramref name="startDegrees"/> round from +x (y down, so clockwise on screen).</summary>
        public static void Corner(List<Vector2> into, Vector2 centre, float radius, float startDegrees, int steps = 6)
        {
            for (int i = 0; i <= steps; i++)
            {
                float angle = (startDegrees + 90f * i / steps) * Mathf.Deg2Rad;
                into.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }

        // -----------------------------------------------------------------
        // The strip everything is made of
        // -----------------------------------------------------------------

        /// <summary>
        /// A stroke along <paramref name="points"/>: a solid core and a strip each side fading to
        /// nothing. With no <paramref name="blur"/> the strip is a pixel wide and centred on the
        /// stroke's edge, so the stroke keeps its width; with one, it is that wide, outside the core.
        /// </summary>
        private void Strip(List<Vector2> points, bool closed, float width, Color colour, float blur, bool roundCaps)
        {
            int count = points.Count;
            if (count < 2)
                return;

            float inner, outer;
            Color core = colour;

            if (blur > 0f)
            {
                inner = width * 0.5f;
                outer = inner + blur;
            }
            else if (width >= _pixel)
            {
                inner = (width - _pixel) * 0.5f;
                outer = (width + _pixel) * 0.5f;
            }
            else
            {
                // Thinner than a pixel: a pixel wide, as faint as it is thin.
                core.a *= width / _pixel;
                inner = 0f;
                outer = _pixel * 0.5f;
            }

            Color edge = Transparent(core);
            int first = _vh.currentVertCount;

            for (int i = 0; i < count; i++)
            {
                Vector2 normal = Normal(points, i, closed);
                Vector2 p = points[i];

                _vh.AddVert(Flip(p + normal * outer), edge, Vector4.zero);
                _vh.AddVert(Flip(p + normal * inner), core, Vector4.zero);
                _vh.AddVert(Flip(p - normal * inner), core, Vector4.zero);
                _vh.AddVert(Flip(p - normal * outer), edge, Vector4.zero);
            }

            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 4;
                int b = first + ((i + 1) % count) * 4;

                for (int k = 0; k < 3; k++)
                {
                    _vh.AddTriangle(a + k, b + k, b + k + 1);
                    _vh.AddTriangle(a + k, b + k + 1, a + k + 1);
                }
            }

            if (roundCaps && !closed)
            {
                Cap(points[0], points[0] - points[1], inner, outer, core);
                Cap(points[count - 1], points[count - 1] - points[count - 2], inner, outer, core);
            }
        }

        /// <summary>The direction a point's sides lie in, mitred at a bend so the stroke keeps its width round it.</summary>
        private static Vector2 Normal(List<Vector2> points, int i, bool closed)
        {
            int count = points.Count;
            bool hasPrevious = closed || i > 0;
            bool hasNext = closed || i < count - 1;

            Vector2 previous = hasPrevious ? points[(i - 1 + count) % count] : points[i];
            Vector2 next = hasNext ? points[(i + 1) % count] : points[i];

            Vector2 into = hasPrevious ? (points[i] - previous).normalized : Vector2.zero;
            Vector2 outOf = hasNext ? (next - points[i]).normalized : Vector2.zero;

            if (!hasPrevious) into = outOf;
            if (!hasNext) outOf = into;

            Vector2 n1 = new Vector2(-into.y, into.x), n2 = new Vector2(-outOf.y, outOf.x);
            Vector2 mitre = (n1 + n2);

            if (mitre.sqrMagnitude < 1e-6f)
                return n1;

            mitre.Normalize();
            float cos = Vector2.Dot(mitre, n1);
            return mitre / Mathf.Max(cos, 0.5f);
        }

        /// <summary>A half disc on a stroke's end, pointing along <paramref name="outward"/>.</summary>
        private void Cap(Vector2 centre, Vector2 outward, float inner, float outer, Color core)
        {
            if (outward.sqrMagnitude < 1e-8f || outer <= 0f)
                return;

            outward.Normalize();
            float start = Mathf.Atan2(outward.y, outward.x) - Mathf.PI * 0.5f;
            int steps = Steps(outer, Mathf.PI);
            Color edge = Transparent(core);
            int first = _vh.currentVertCount;

            _vh.AddVert(Flip(centre), core, Vector4.zero);
            for (int i = 0; i <= steps; i++)
            {
                float angle = start + Mathf.PI * i / steps;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _vh.AddVert(Flip(centre + direction * inner), core, Vector4.zero);
                _vh.AddVert(Flip(centre + direction * outer), edge, Vector4.zero);
            }

            for (int i = 0; i < steps; i++)
            {
                int a = first + 1 + i * 2, b = a + 2;
                _vh.AddTriangle(first, a, b);
                _vh.AddTriangle(a, a + 1, b + 1);
                _vh.AddTriangle(a, b + 1, b);
            }
        }

        private void Fan(Vector2 centre, float radius, Color colour)
        {
            if (radius <= 0f)
                return;

            int steps = Steps(radius, Mathf.PI * 2f);
            int first = _vh.currentVertCount;
            _vh.AddVert(Flip(centre), colour, Vector4.zero);

            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                _vh.AddVert(Flip(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius), colour, Vector4.zero);
            }

            for (int i = 0; i < steps; i++)
                _vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % steps);
        }

        private void Annulus(Vector2 centre, float inner, float outer, Color innerColour, Color outerColour)
        {
            int steps = Steps(outer, Mathf.PI * 2f);
            int first = _vh.currentVertCount;

            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _vh.AddVert(Flip(centre + direction * inner), innerColour, Vector4.zero);
                _vh.AddVert(Flip(centre + direction * outer), outerColour, Vector4.zero);
            }

            for (int i = 0; i < steps; i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % steps) * 2;
                _vh.AddTriangle(a, a + 1, b + 1);
                _vh.AddTriangle(a, b + 1, b);
            }
        }

        /// <summary>
        /// The stretch of a route between two distances, as points, into <see cref="_path"/>.
        /// False when there is nothing to draw.
        /// </summary>
        private bool Slice(Route route, float from, float to)
        {
            from = Mathf.Max(0f, from);
            to = Mathf.Min(route.Length, to);
            if (to - from < 1e-3f)
                return false;

            _path.Clear();
            _path.Add(route.PointAt(from));

            for (int i = 0; i < route.PointCount; i++)
            {
                float d = route.DistanceAt(i);
                if (d > from + 1e-3f && d < to - 1e-3f)
                    _path.Add(route.Point(i));
            }

            _path.Add(route.PointAt(to));
            return true;
        }

        /// <summary>How many pieces a curve of this radius and sweep needs to look round at this size.</summary>
        private int Steps(float radius, float sweep) =>
            Mathf.Clamp(Mathf.CeilToInt(sweep * Mathf.Sqrt(Mathf.Max(1f, radius / _pixel)) * 1.5f), 6, 64);

        private static Vector3 Flip(Vector2 p) => new Vector3(p.x, -p.y, 0f);

        private static Color Transparent(Color colour) => new Color(colour.r, colour.g, colour.b, 0f);
    }
}
