using System;
using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>What the pointer is on, on the canvas.</summary>
    public enum CanvasTargetKind
    {
        Nothing,

        /// <summary>A part's body.</summary>
        Part,

        /// <summary>One of a part's pins: where a wire starts or ends (Part 10b).</summary>
        Pin,

        /// <summary>A wire.</summary>
        Wire,

        /// <summary>A corner of a selected wire, by its point (<see cref="CanvasTarget.Segment"/>): what a drag reshapes it by (Part 10b-2).</summary>
        Bend,
    }

    /// <summary>What a point of the canvas is on: a part, one of its pins, a wire -- and where along it -- or nothing.</summary>
    public readonly struct CanvasTarget : IEquatable<CanvasTarget>
    {
        public CanvasTarget(CanvasTargetKind kind, int part = 0, string pin = null, int wire = 0, int segment = -1, double along = 0)
        {
            Kind = kind;
            Part = part;
            Pin = pin;
            Wire = wire;
            Segment = segment;
            Along = along;
        }

        public static readonly CanvasTarget Nothing = new CanvasTarget(CanvasTargetKind.Nothing);

        public CanvasTargetKind Kind { get; }

        /// <summary>The part, for a part or one of its pins.</summary>
        public int Part { get; }

        /// <summary>The pin's name, for a pin.</summary>
        public string Pin { get; }

        /// <summary>The wire, for a wire.</summary>
        public int Wire { get; }

        /// <summary>Which of the wire's straight pieces, the first 0.</summary>
        public int Segment { get; }

        /// <summary>How far along the wire, 0 at its first end and 1 at its other: where a tap on it would join.</summary>
        public double Along { get; }

        /// <summary>Whether it is on a part, its body or a pin.</summary>
        public bool IsPart => Kind == CanvasTargetKind.Part || Kind == CanvasTargetKind.Pin;

        public bool Equals(CanvasTarget other) =>
            Kind == other.Kind && Part == other.Part && Pin == other.Pin && Wire == other.Wire && Segment == other.Segment;

        public override bool Equals(object obj) => obj is CanvasTarget other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397 ^ Part) * 31 ^ Wire;

        public override string ToString() => Kind switch
        {
            CanvasTargetKind.Part => "part " + Part,
            CanvasTargetKind.Pin => "pin " + Part + "." + Pin,
            CanvasTargetKind.Wire => "wire " + Wire + " segment " + Segment,
            CanvasTargetKind.Bend => "wire " + Wire + " corner " + Segment,
            _ => "nothing",
        };
    }

    /// <summary>
    /// The top module as the pointer finds things on it (Part 10), in the board's own cells: the same outlines
    /// and routes the drawing and the compiler use (<see cref="PartGeometry"/>, <see cref="WireGeometry"/>),
    /// so a click lands on what is drawn under it.
    /// </summary>
    /// <remarks>
    /// **A pin before a body, a body before a wire.** A pin within reach wins, the nearest; then a part
    /// whose outline holds the point, the one placed last when parts overlap, as it is drawn on top; then
    /// the nearest wire within reach. Made once for a document, so moving the pointer only measures.
    /// </remarks>
    public sealed class CanvasHit
    {
        private readonly List<(int Id, PartOutline Outline)> _parts = new List<(int, PartOutline)>();
        private readonly Dictionary<int, PartOutline> _outlines = new Dictionary<int, PartOutline>();
        private readonly List<(int Id, IReadOnlyList<BoardPoint> Points)> _wires = new List<(int, IReadOnlyList<BoardPoint>)>();

        public CanvasHit(Project project, Module module)
        {
            if (module == null)
                return;

            foreach (Part part in module.Parts)
            {
                PartOutline outline = PartGeometry.Placed(project, part, module.Layout.PlacementOf(part.Id));
                _parts.Add((part.Id, outline));
                _outlines[part.Id] = outline;
            }

            Dictionary<int, IReadOnlyList<BoardPoint>> routes = WireGeometry.Wires(project, module);
            foreach (Wire wire in module.Wires)
                if (routes.TryGetValue(wire.Id, out IReadOnlyList<BoardPoint> points) && points.Count >= 2)
                    _wires.Add((wire.Id, points));
        }

        /// <summary>A part's outline as placed, or null for none.</summary>
        public PartOutline OutlineOf(int part) => _outlines.TryGetValue(part, out PartOutline outline) ? outline : null;

        /// <summary>A wire's points as routed, or null for none.</summary>
        public IReadOnlyList<BoardPoint> WireOf(int wire)
        {
            foreach ((int id, IReadOnlyList<BoardPoint> points) in _wires)
                if (id == wire)
                    return points;
            return null;
        }

        /// <summary>What <paramref name="p"/> is on, within <paramref name="reach"/> cells of a pin or a wire.</summary>
        public CanvasTarget At(BoardPoint p, double reach)
        {
            double best = reach * reach;
            CanvasTarget found = CanvasTarget.Nothing;

            foreach ((int id, PartOutline outline) in _parts)
            {
                foreach (PinSpot pin in outline.Pins)
                {
                    double squared = Squared(p, pin.At);
                    if (squared <= best)
                    {
                        best = squared;
                        found = new CanvasTarget(CanvasTargetKind.Pin, part: id, pin: pin.Pin);
                    }
                }
            }

            if (found.Kind != CanvasTargetKind.Nothing)
                return found;

            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                PartOutline outline = _parts[i].Outline;
                if (p.X >= outline.Left && p.X <= outline.Right && p.Y >= outline.Bottom && p.Y <= outline.Top)
                    return new CanvasTarget(CanvasTargetKind.Part, part: _parts[i].Id);
            }

            best = reach * reach;
            foreach ((int id, IReadOnlyList<BoardPoint> points) in _wires)
            {
                double length = WireGeometry.Length(points), walked = 0;
                for (int s = 1; s < points.Count; s++)
                {
                    double squared = SegmentSquared(p, points[s - 1], points[s], out double u);
                    double step = Distance(points[s - 1], points[s]);
                    if (squared <= best)
                    {
                        best = squared;
                        found = new CanvasTarget(CanvasTargetKind.Wire, wire: id, segment: s - 1, along: length > 0 ? (walked + step * u) / length : 0);
                    }

                    walked += step;
                }
            }

            return found;
        }

        /// <summary>Everything a box touches: each part whose outline meets it, and each wire with a piece in it.</summary>
        public void In(double left, double bottom, double right, double top, List<int> parts, List<int> wires)
        {
            double l = Math.Min(left, right), r = Math.Max(left, right), b = Math.Min(bottom, top), t = Math.Max(bottom, top);

            foreach ((int id, PartOutline outline) in _parts)
                if (outline.Right >= l && outline.Left <= r && outline.Top >= b && outline.Bottom <= t)
                    parts.Add(id);

            foreach ((int id, IReadOnlyList<BoardPoint> points) in _wires)
            {
                for (int s = 1; s < points.Count; s++)
                {
                    if (SegmentMeetsBox(points[s - 1], points[s], l, b, r, t))
                    {
                        wires.Add(id);
                        break;
                    }
                }
            }
        }

        private static double Squared(BoardPoint a, BoardPoint b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

        private static double Distance(BoardPoint a, BoardPoint b) => Math.Sqrt(Squared(a, b));

        /// <summary>The squared distance from <paramref name="p"/> to the piece from <paramref name="a"/> to <paramref name="b"/>, and how far along it the nearest point is.</summary>
        private static double SegmentSquared(BoardPoint p, BoardPoint a, BoardPoint b, out double u)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
            u = length > 1e-12 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length)) : 0;
            return Squared(p, new BoardPoint(a.X + dx * u, a.Y + dy * u));
        }

        /// <summary>Whether the piece from <paramref name="a"/> to <paramref name="b"/> has a point in the box: clipped against it, Liang and Barsky's way.</summary>
        private static bool SegmentMeetsBox(BoardPoint a, BoardPoint b, double l, double bottom, double r, double t)
        {
            double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;

            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-12)
                    return q >= 0;
                double ratio = q / p;
                if (p < 0)
                {
                    if (ratio > t1) return false;
                    if (ratio > t0) t0 = ratio;
                }
                else
                {
                    if (ratio < t0) return false;
                    if (ratio < t1) t1 = ratio;
                }

                return true;
            }

            return Clip(-dx, a.X - l) && Clip(dx, r - a.X) && Clip(-dy, a.Y - bottom) && Clip(dy, t - a.Y);
        }
    }
}
