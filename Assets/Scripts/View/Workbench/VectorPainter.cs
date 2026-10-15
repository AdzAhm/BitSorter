using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

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
    /// Reuses its own buffers, so a frame of drawing makes no garbage. It writes a
    /// <see cref="MeshBuffer"/> rather than uGUI's <c>VertexHelper</c>, for speed: see there.
    /// </remarks>
    internal sealed class VectorPainter
    {
        private MeshBuffer _mesh;
        private float _pixel = 1f;
        private readonly List<Vector2> _path = new List<Vector2>(128);

        /// <summary>Starts drawing into <paramref name="mesh"/>, where one screen pixel is <paramref name="pixel"/> units.</summary>
        public void Begin(MeshBuffer mesh, float pixel)
        {
            _mesh = mesh;
            _pixel = Mathf.Max(1e-3f, pixel);
            Overflowed = false;
        }

        /// <summary>How many units a screen pixel is.</summary>
        public float Pixel => _pixel;

        /// <summary>Where the mesh has got to: what <see cref="Fits"/> takes back to.</summary>
        public (int Vertices, int Indices) Mark() => (_mesh.VertexCount, _mesh.IndexCount);

        /// <summary>
        /// Whether everything drawn so far still fits one UI mesh. If not, what was drawn since
        /// <paramref name="mark"/> is taken back and <see cref="Overflowed"/> says so: a chunk draws as
        /// many whole items as fit -- a wire, a change, a part, a row -- rather than nothing at all.
        /// </summary>
        public bool Fits((int Vertices, int Indices) mark)
        {
            if (_mesh.VertexCount <= WorkbenchGraphic.MeshLimit)
                return true;

            _mesh.VertexCount = mark.Vertices;
            _mesh.IndexCount = mark.Indices;
            Overflowed = true;
            return false;
        }

        /// <summary>Whether something was left out since <see cref="Begin"/> because it did not fit.</summary>
        public bool Overflowed { get; private set; }

        // -----------------------------------------------------------------
        // Strokes
        // -----------------------------------------------------------------

        /// <summary>The stretch of <paramref name="route"/> from <paramref name="from"/> to <paramref name="to"/>, stroked.</summary>
        public void Stroke(Route route, float from, float to, float width, Color colour, bool roundCaps = true)
        {
            if (!Slice(route, from, to, out int first))
                return;

            Strip(_path, false, width, colour, 0f, roundCaps, route, first);
        }

        /// <summary>A whole route, stroked.</summary>
        public void Stroke(Route route, float width, Color colour) => Stroke(route, 0f, route.Length, width, colour);

        /// <summary>
        /// A soft glow along part of a route: a core <paramref name="width"/> wide and a falloff of
        /// <paramref name="blur"/> each side, like a stroke under a blur filter.
        /// </summary>
        public void Glow(Route route, float from, float to, float width, float blur, Color colour)
        {
            if (!Slice(route, from, to, out int first))
                return;

            Strip(_path, false, width, colour, blur, roundCaps: true, route, first);
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
        /// <remarks>
        /// The closed stroke <see cref="Strip"/> would draw round the circle's points, written out for a
        /// circle, because a ring is drawn for every change in flight on every frame. Round a regular
        /// polygon the mitred normal at each point is the radius through it, longer by a mitre's
        /// 1 / cos(half a step), so each point's four vertices lie on that radius and need no square
        /// root: the same vertices, to the last few bits of a float, in the same order.
        /// </remarks>
        public void Ring(Vector2 centre, float radius, float width, Color colour)
        {
            // A ring of no size drew nothing as a stroke, every normal coming out zero; it still does.
            if (radius < 1e-4f)
                return;

            int steps = Steps(radius, Mathf.PI * 2f);
            Vector2[] circle = UnitCircle(steps);
            float mitre = MitreOf[steps];

            Edges(width, 0f, ref colour, out float inner, out float outer);
            Color32 edge = Transparent(colour), solid = colour;
            float r0 = radius - outer * mitre, r1 = radius - inner * mitre, r2 = radius + inner * mitre, r3 = radius + outer * mitre;
            float cx = centre.x, cy = centre.y;

            int first = _mesh.Reserve(steps * 4, steps * 18);
            MeshBuffer.Vertex[] vs = _mesh.Vertices;
            int v = first;
            for (int i = 0; i < steps; i++)
            {
                Vector2 u = circle[i];
                vs[v].Position = new Vector3(cx + u.x * r0, -(cy + u.y * r0), 0f); vs[v++].Colour = edge;
                vs[v].Position = new Vector3(cx + u.x * r1, -(cy + u.y * r1), 0f); vs[v++].Colour = solid;
                vs[v].Position = new Vector3(cx + u.x * r2, -(cy + u.y * r2), 0f); vs[v++].Colour = solid;
                vs[v].Position = new Vector3(cx + u.x * r3, -(cy + u.y * r3), 0f); vs[v++].Colour = edge;
            }

            _mesh.VertexCount = v;
            Bands(first, steps, steps);
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

            Color32 fill = colour;
            int first = _mesh.VertexCount;
            _mesh.Vert(Flip(centre), fill);
            foreach (Vector2 p in points) _mesh.Vert(Flip(p), fill);

            for (int i = 0; i < points.Count; i++)
                _mesh.Triangle(first, first + 1 + i, first + 1 + (i + 1) % points.Count);
        }

        /// <summary>An axis-aligned rectangle, filled, with no smoothing: a grid line or a band.</summary>
        public void Rect(float x0, float y0, float x1, float y1, Color colour)
        {
            Color32 fill = colour;
            int first = _mesh.VertexCount;
            _mesh.Vert(Flip(new Vector2(x0, y0)), fill);
            _mesh.Vert(Flip(new Vector2(x1, y0)), fill);
            _mesh.Vert(Flip(new Vector2(x1, y1)), fill);
            _mesh.Vert(Flip(new Vector2(x0, y1)), fill);
            _mesh.Triangle(first, first + 1, first + 2);
            _mesh.Triangle(first, first + 2, first + 3);
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
            if (steps >= MinSteps && steps <= MaxSteps)
            {
                Vector2[] circle = UnitCircle(steps);
                for (int i = 0; i < steps; i++)
                    into.Add(centre + circle[i] * radius);
                return;
            }

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
        /// <remarks>
        /// For a stretch of a <paramref name="route"/>, cut by <see cref="Slice"/>, the points between
        /// its two cut ends are the route's own from <paramref name="first"/> on, and every one whose
        /// neighbours are route points too takes its normal from the route rather than working it out
        /// again: the same normal, of the same three points by the same arithmetic. The cut ends and
        /// the points beside them are worked out here.
        /// </remarks>
        private void Strip(List<Vector2> points, bool closed, float width, Color colour, float blur, bool roundCaps,
            Route route = null, int first = 0)
        {
            int count = points.Count;
            if (count < 2)
                return;

            Color core = colour;
            Edges(width, blur, ref core, out float inner, out float outer);

            Color32 edge = Transparent(core), solid = core;
            int segments = closed ? count : count - 1;

            int start = _mesh.Reserve(count * 4, segments * 18);
            MeshBuffer.Vertex[] vs = _mesh.Vertices;
            int v = start;
            for (int i = 0; i < count; i++)
            {
                Vector2 normal = route != null && i >= 2 && i <= count - 3 ? route.NormalAt(first + i - 1) : Normal(points, i, closed);
                Vector2 p = points[i];
                float ox = normal.x * outer, oy = normal.y * outer, ix = normal.x * inner, iy = normal.y * inner;

                vs[v].Position = new Vector3(p.x + ox, -(p.y + oy), 0f); vs[v++].Colour = edge;
                vs[v].Position = new Vector3(p.x + ix, -(p.y + iy), 0f); vs[v++].Colour = solid;
                vs[v].Position = new Vector3(p.x - ix, -(p.y - iy), 0f); vs[v++].Colour = solid;
                vs[v].Position = new Vector3(p.x - ox, -(p.y - oy), 0f); vs[v++].Colour = edge;
            }

            _mesh.VertexCount = v;
            Bands(start, count, segments);

            if (roundCaps && !closed)
            {
                Cap(points[0], points[0] - points[1], inner, outer, core);
                Cap(points[count - 1], points[count - 1] - points[count - 2], inner, outer, core);
            }
        }

        /// <summary>
        /// How far each side of a stroke's middle its solid core and its fading edge reach. With no
        /// <paramref name="blur"/> the edge is a pixel wide and centred on the stroke's edge, so the
        /// stroke keeps its width; with one, it is that wide, outside the core.
        /// </summary>
        private void Edges(float width, float blur, ref Color core, out float inner, out float outer)
        {
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
        }

        /// <summary>
        /// The triangles of a strip of <paramref name="points"/> rows of four vertices from
        /// <paramref name="first"/> -- edge, core, core, edge across it -- for <paramref name="segments"/>
        /// pieces, the last joining back to the first when the strip is closed: three bands of two
        /// triangles each between a row and the next.
        /// </summary>
        private void Bands(int first, int points, int segments)
        {
            ushort[] ix = _mesh.Indices;
            int n = _mesh.IndexCount;
            for (int i = 0; i < segments; i++)
            {
                int a = first + i * 4;
                int b = first + ((i + 1) % points) * 4;

                for (int k = 0; k < 3; k++)
                {
                    ix[n] = (ushort)(a + k);
                    ix[n + 1] = (ushort)(b + k);
                    ix[n + 2] = (ushort)(b + k + 1);
                    ix[n + 3] = (ushort)(a + k);
                    ix[n + 4] = (ushort)(b + k + 1);
                    ix[n + 5] = (ushort)(a + k + 1);
                    n += 6;
                }
            }

            _mesh.IndexCount = n;
        }

        /// <summary>The direction a point's sides lie in, mitred at a bend so the stroke keeps its width round it.</summary>
        private static Vector2 Normal(List<Vector2> points, int i, bool closed)
        {
            int count = points.Count;
            bool hasPrevious = closed || i > 0;
            bool hasNext = closed || i < count - 1;

            Vector2 previous = hasPrevious ? points[(i - 1 + count) % count] : points[i];
            Vector2 next = hasNext ? points[(i + 1) % count] : points[i];
            return Route.MitredNormal(previous, points[i], next, hasPrevious, hasNext);
        }

        /// <summary>A half disc on a stroke's end, pointing along <paramref name="outward"/>.</summary>
        private void Cap(Vector2 centre, Vector2 outward, float inner, float outer, Color core)
        {
            if (outward.sqrMagnitude < 1e-8f || outer <= 0f)
                return;

            outward.Normalize();
            float start = Mathf.Atan2(outward.y, outward.x) - Mathf.PI * 0.5f;
            int steps = Steps(outer, Mathf.PI);
            Vector2[] half = HalfCircle(start, steps, keep: outward.x == 0f || outward.y == 0f);
            Color32 edge = Transparent(core), solid = core;
            float cx = centre.x, cy = centre.y;

            int first = _mesh.Reserve(1 + (steps + 1) * 2, steps * 9);
            MeshBuffer.Vertex[] vs = _mesh.Vertices;
            int v = first;
            vs[v].Position = new Vector3(cx, -cy, 0f); vs[v++].Colour = solid;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 d = half[i];
                vs[v].Position = new Vector3(cx + d.x * inner, -(cy + d.y * inner), 0f); vs[v++].Colour = solid;
                vs[v].Position = new Vector3(cx + d.x * outer, -(cy + d.y * outer), 0f); vs[v++].Colour = edge;
            }

            _mesh.VertexCount = v;

            ushort[] ix = _mesh.Indices;
            int n = _mesh.IndexCount;
            for (int i = 0; i < steps; i++)
            {
                int a = first + 1 + i * 2, b = a + 2;
                ix[n] = (ushort)first; ix[n + 1] = (ushort)a; ix[n + 2] = (ushort)b;
                ix[n + 3] = (ushort)a; ix[n + 4] = (ushort)(a + 1); ix[n + 5] = (ushort)(b + 1);
                ix[n + 6] = (ushort)a; ix[n + 7] = (ushort)(b + 1); ix[n + 8] = (ushort)b;
                n += 9;
            }

            _mesh.IndexCount = n;
        }

        private void Fan(Vector2 centre, float radius, Color colour)
        {
            if (radius <= 0f)
                return;

            Vector2[] circle = UnitCircle(Steps(radius, Mathf.PI * 2f));
            int steps = circle.Length;
            Color32 fill = colour;
            float cx = centre.x, cy = centre.y;

            int first = _mesh.Reserve(steps + 1, steps * 3);
            MeshBuffer.Vertex[] vs = _mesh.Vertices;
            int v = first;
            vs[v].Position = new Vector3(cx, -cy, 0f); vs[v++].Colour = fill;
            for (int i = 0; i < steps; i++)
            {
                Vector2 c = circle[i];
                vs[v].Position = new Vector3(cx + c.x * radius, -(cy + c.y * radius), 0f); vs[v++].Colour = fill;
            }

            _mesh.VertexCount = v;

            ushort[] ix = _mesh.Indices;
            int n = _mesh.IndexCount;
            for (int i = 0; i < steps; i++)
            {
                ix[n] = (ushort)first;
                ix[n + 1] = (ushort)(first + 1 + i);
                ix[n + 2] = (ushort)(first + 1 + (i + 1) % steps);
                n += 3;
            }

            _mesh.IndexCount = n;
        }

        private void Annulus(Vector2 centre, float inner, float outer, Color innerColour, Color outerColour)
        {
            Vector2[] circle = UnitCircle(Steps(outer, Mathf.PI * 2f));
            int steps = circle.Length;
            Color32 near = innerColour, far = outerColour;
            float cx = centre.x, cy = centre.y;

            int first = _mesh.Reserve(steps * 2, steps * 6);
            MeshBuffer.Vertex[] vs = _mesh.Vertices;
            int v = first;
            for (int i = 0; i < steps; i++)
            {
                Vector2 d = circle[i];
                vs[v].Position = new Vector3(cx + d.x * inner, -(cy + d.y * inner), 0f); vs[v++].Colour = near;
                vs[v].Position = new Vector3(cx + d.x * outer, -(cy + d.y * outer), 0f); vs[v++].Colour = far;
            }

            _mesh.VertexCount = v;

            ushort[] ix = _mesh.Indices;
            int n = _mesh.IndexCount;
            for (int i = 0; i < steps; i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % steps) * 2;
                ix[n] = (ushort)a; ix[n + 1] = (ushort)(a + 1); ix[n + 2] = (ushort)(b + 1);
                ix[n + 3] = (ushort)a; ix[n + 4] = (ushort)(b + 1); ix[n + 5] = (ushort)b;
                n += 6;
            }

            _mesh.IndexCount = n;
        }

        /// <summary>
        /// The stretch of a route between two distances, as points, into <see cref="_path"/>, and the
        /// route's index of the first point between the two cut ends. False when there is nothing to draw.
        /// </summary>
        private bool Slice(Route route, float from, float to, out int first)
        {
            first = 0;
            from = Mathf.Max(0f, from);
            to = Mathf.Min(route.Length, to);
            if (to - from < 1e-3f)
                return false;

            _path.Clear();
            _path.Add(route.PointAt(from));

            // The points strictly between, found from the first past the start rather than by walking
            // the whole route: a dashed or hatched wire slices one route dozens of times.
            first = route.FirstAfter(from + 1e-3f);
            for (int i = first; i < route.PointCount; i++)
            {
                if (route.DistanceAt(i) >= to - 1e-3f)
                    break;
                _path.Add(route.Point(i));
            }

            _path.Add(route.PointAt(to));
            return true;
        }

        /// <summary>How many pieces a curve of this radius and sweep needs to look round at this size.</summary>
        private int Steps(float radius, float sweep) =>
            Mathf.Clamp(Mathf.CeilToInt(sweep * Mathf.Sqrt(Mathf.Max(1f, radius / _pixel)) * 1.5f), MinSteps, MaxSteps);

        /// <summary>The fewest and the most pieces a curve is cut into.</summary>
        internal const int MinSteps = 6, MaxSteps = 64;

        /// <summary>
        /// The most vertices each shape can take, when its curves are cut as finely as they ever are:
        /// what a chunk's estimates are written in (<see cref="WorkbenchDrawing.NetVertices"/>).
        /// </summary>
        internal const int CapVertices = 1 + 2 * (MaxSteps + 1), DiscVertices = 1 + MaxSteps + 2 * MaxSteps,
            RingVertices = 4 * MaxSteps, LineVertices = 8;

        /// <summary>
        /// The points of a circle of radius 1 cut into <paramref name="steps"/>, from +x round: worked
        /// out once for every number of steps a circle can take, so a ring drawn every frame costs no
        /// sine or cosine. Each point is exactly what the angle's own <c>Cos</c> and <c>Sin</c> give,
        /// so a circle is drawn to the same bit as it was when each was worked out where it was drawn.
        /// </summary>
        private static Vector2[] UnitCircle(int steps) => UnitCircles[steps];

        private static readonly Vector2[][] UnitCircles = MakeUnitCircles();

        /// <summary>A mitre's length round a regular polygon of each number of steps: 1 / cos(half a step).</summary>
        private static readonly float[] MitreOf = MakeMitres();

        private static float[] MakeMitres()
        {
            var mitres = new float[MaxSteps + 1];
            for (int steps = MinSteps; steps <= MaxSteps; steps++)
                mitres[steps] = 1f / Mathf.Cos(Mathf.PI / steps);
            return mitres;
        }

        /// <summary>
        /// The directions round a half circle from <paramref name="start"/>, in <paramref name="steps"/>:
        /// a round cap's. Those of a level or an upright stroke -- nearly every wire on a schematic --
        /// are kept once worked out (<paramref name="keep"/>), since such a cap starts at one of four
        /// angles; each is the angle's own <c>Cos</c> and <c>Sin</c>, so a cap is drawn to the same bit
        /// either way. A cap on a curve starts anywhere, so it is worked out where it is drawn, into a
        /// buffer kept for that: only the first <paramref name="steps"/> + 1 directions are a cap's.
        /// </summary>
        private static Vector2[] HalfCircle(float start, int steps, bool keep)
        {
            if (keep && HalfCircles.TryGetValue((start, steps), out Vector2[] kept))
                return kept;

            keep = keep && HalfCircles.Count < MaxHalfCircles;
            Vector2[] half = keep ? new Vector2[steps + 1] : Unkept;
            for (int i = 0; i <= steps; i++)
            {
                float angle = start + Mathf.PI * i / steps;
                half[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            if (keep)
                HalfCircles[(start, steps)] = half;
            return half;
        }

        /// <summary>Four directions and every number of steps a cap can take: a bound, never reached.</summary>
        private const int MaxHalfCircles = 4 * (MaxSteps - MinSteps + 1);

        private static readonly Dictionary<(float, int), Vector2[]> HalfCircles = new Dictionary<(float, int), Vector2[]>();

        private static readonly Vector2[] Unkept = new Vector2[MaxSteps + 1];

        private static Vector2[][] MakeUnitCircles()
        {
            var circles = new Vector2[MaxSteps + 1][];
            for (int steps = MinSteps; steps <= MaxSteps; steps++)
            {
                circles[steps] = new Vector2[steps];
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * Mathf.PI * 2f / steps;
                    circles[steps][i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
            }

            return circles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 Flip(Vector2 p) => new Vector3(p.x, -p.y, 0f);

        private static Color Transparent(Color colour) => new Color(colour.r, colour.g, colour.b, 0f);
    }

    /// <summary>
    /// A UI mesh being drawn: positions and colours, and the triangles between them -- what
    /// <see cref="VectorPainter"/> writes and <see cref="WorkbenchGraphic"/> hands to its renderer.
    /// </summary>
    /// <remarks>
    /// **It replaces uGUI's <c>VertexHelper</c>, for speed**: a frame of playback on 300 wires went from
    /// 35 ms to 22 when it did (<c>WorkbenchPerformanceTests</c>). That keeps eight channels for every
    /// vertex -- position, colour, four sets of texture coordinates, a normal and a tangent -- in eight
    /// lists, a call deep each, and copies all eight into a mesh before the renderer copies them again. The workbench draws in flat colour on
    /// the canvas's white texture, so it needs two: a position and a colour. Arrays rather than lists,
    /// grown as needed and kept, so a frame makes no garbage; and one buffer serves every graphic,
    /// as uGUI's own helper does, since a renderer keeps a copy of what it was given.
    ///
    /// Handing a mesh over this way changes nothing in it: the same vertices in the same order, the
    /// same triangles, each colour turned into bytes exactly as <c>VertexHelper.AddVert</c> was handed
    /// it. The painter writes every position as the same float arithmetic its <c>Vector2</c> operators
    /// did, spelled out; only its ring is worked out another way (<see cref="VectorPainter.Ring"/>).
    /// </remarks>
    internal sealed class MeshBuffer
    {
        /// <summary>One vertex as the mesh holds it: a position and a colour, interleaved, 16 bytes.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public Vector3 Position;
            public Color32 Colour;
        }

        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        /// <summary>Nothing to check: every index was written here, against vertices written here.</summary>
        private const MeshUpdateFlags Unchecked = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

        private Vertex[] _vertices = new Vertex[4096];
        private ushort[] _indices = new ushort[12288];

        /// <summary>How many vertices have been written since <see cref="Clear"/>.</summary>
        public int VertexCount;

        /// <summary>How many triangle corners have been written since <see cref="Clear"/>.</summary>
        public int IndexCount;

        /// <summary>
        /// The vertices and the triangle corners, written straight into by the painter after a
        /// <see cref="Reserve"/>: a shape drawn for every change on every frame cannot afford a call and
        /// a bounds check for each vertex. Only the first <see cref="VertexCount"/> and
        /// <see cref="IndexCount"/> are drawn.
        /// </summary>
        public Vertex[] Vertices => _vertices;

        /// <inheritdoc cref="Vertices"/>
        public ushort[] Indices => _indices;

        public void Clear()
        {
            VertexCount = 0;
            IndexCount = 0;
        }

        /// <summary>
        /// Makes room for <paramref name="vertices"/> more vertices and <paramref name="indices"/> more
        /// triangle corners, so the writes after it need no check, and says where the vertices begin.
        /// </summary>
        public int Reserve(int vertices, int indices)
        {
            if (VertexCount + vertices > _vertices.Length)
                System.Array.Resize(ref _vertices, Mathf.Max(_vertices.Length * 2, VertexCount + vertices));
            if (IndexCount + indices > _indices.Length)
                System.Array.Resize(ref _indices, Mathf.Max(_indices.Length * 2, IndexCount + indices));
            return VertexCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Vert(Vector3 position, Color32 colour)
        {
            if (VertexCount == _vertices.Length)
                System.Array.Resize(ref _vertices, _vertices.Length * 2);

            ref Vertex v = ref _vertices[VertexCount++];
            v.Position = position;
            v.Colour = colour;
        }

        /// <summary>
        /// A triangle between three vertices. Held as 16-bit indices, a mesh's own format: a chunk over
        /// <see cref="WorkbenchGraphic.MeshLimit"/> vertices is never handed over, so none is cut short.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Triangle(int a, int b, int c)
        {
            if (IndexCount + 3 > _indices.Length)
                System.Array.Resize(ref _indices, _indices.Length * 2);

            _indices[IndexCount] = (ushort)a;
            _indices[IndexCount + 1] = (ushort)b;
            _indices[IndexCount + 2] = (ushort)c;
            IndexCount += 3;
        }

        /// <summary>Puts what has been drawn into <paramref name="mesh"/>, its bounds worked out from it, as uGUI's helper does.</summary>
        public void FillMesh(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertexBufferParams(VertexCount, Layout);
            mesh.SetVertexBufferData(_vertices, 0, 0, VertexCount, 0, Unchecked);
            mesh.SetIndexBufferParams(IndexCount, IndexFormat.UInt16);
            mesh.SetIndexBufferData(_indices, 0, 0, IndexCount, Unchecked);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, IndexCount), MeshUpdateFlags.DontValidateIndices);
        }
    }
}
