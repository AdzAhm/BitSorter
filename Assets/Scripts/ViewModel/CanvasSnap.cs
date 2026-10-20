using System;
using System.Collections.Generic;
using BitSorter.Board;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Where a part placed or moved lands (Part 10): on the quarter-cell grid every pin is on, or -- with the
    /// free-place key, Alt -- wherever the pointer is.
    /// </summary>
    public static class CanvasSnap
    {
        /// <summary>The grid: a quarter of a cell, as the document's layout keeps it (<see cref="PartGeometry"/>).</summary>
        public const double Grid = 0.25;

        public static double Snap(double value) => Math.Round(value / Grid) * Grid;

        public static BoardPoint Snap(BoardPoint p) => new BoardPoint(Snap(p.X), Snap(p.Y));

        /// <summary>
        /// How far a selection moves for a drag of <paramref name="dx"/> by <paramref name="dy"/> cells: snapped so the
        /// part taken hold of, at <paramref name="anchor"/>, lands on the grid -- and every other part keeps its place
        /// beside it, on the grid or not -- or, <paramref name="free"/>, exactly as dragged.
        /// </summary>
        public static (double Dx, double Dy) Offset(BoardPoint anchor, double dx, double dy, bool free)
        {
            if (free)
                return (dx, dy);
            return (Snap(anchor.X + dx) - anchor.X, Snap(anchor.Y + dy) - anchor.Y);
        }
    }

    /// <summary>A line drawn while a part is moved, through the pin or the middle it lines up with.</summary>
    public readonly struct Guide
    {
        public Guide(bool vertical, double at, double from, double to)
        {
            Vertical = vertical;
            At = at;
            From = Math.Min(from, to);
            To = Math.Max(from, to);
        }

        /// <summary>Whether it runs up and down, at x = <see cref="At"/>; across, at y = <see cref="At"/>, when not.</summary>
        public bool Vertical { get; }

        public double At { get; }

        /// <summary>Where it starts and stops along its length, in cells, the two points it joins.</summary>
        public double From { get; }

        /// <inheritdoc cref="From"/>
        public double To { get; }

        public override string ToString() => (Vertical ? "x=" : "y=") + At + " " + From + ".." + To;
    }

    /// <summary>
    /// Alignment guides (the brief's D3): while a selection is moved, a pin or a part's middle that lines up with
    /// another part's -- across or up and down -- is shown by a line through both. A free move (Alt) is caught to
    /// such a line within <see cref="Reach"/>, since nothing else would line it up; a move on the grid is not,
    /// since the grid lines up everything on it already, and a catch to a part placed freely would take it off.
    /// </summary>
    public static class AlignmentGuides
    {
        /// <summary>How near, in cells, a free move has to bring a pin or a middle to another's for it to be caught there.</summary>
        public const double Reach = 0.2;

        private const double Exact = 1e-6;

        /// <summary>
        /// The move of <paramref name="moving"/> by <paramref name="dx"/>, <paramref name="dy"/> as it lands --
        /// caught to a line-up when <paramref name="catchUp"/> -- and into <paramref name="guides"/> every line-up
        /// it then makes with <paramref name="others"/>.
        /// </summary>
        public static (double Dx, double Dy) Align(IReadOnlyList<PartOutline> moving, double dx, double dy, IReadOnlyList<PartOutline> others,
            bool catchUp, List<Guide> guides)
        {
            var mine = new List<BoardPoint>();
            foreach (PartOutline outline in moving)
                Points(outline, mine);
            var theirs = new List<BoardPoint>();
            foreach (PartOutline outline in others)
                Points(outline, theirs);

            if (catchUp)
            {
                dx += Nearest(mine, theirs, dx, true);
                dy += Nearest(mine, theirs, dy, false);
            }

            guides.Clear();
            foreach (BoardPoint m in mine)
            {
                var at = new BoardPoint(m.X + dx, m.Y + dy);
                foreach (BoardPoint o in theirs)
                {
                    if (Math.Abs(at.X - o.X) < Exact)
                        Add(guides, new Guide(true, o.X, at.Y, o.Y));
                    if (Math.Abs(at.Y - o.Y) < Exact)
                        Add(guides, new Guide(false, o.Y, at.X, o.X));
                }
            }

            return (dx, dy);
        }

        /// <summary>What lines up on a part: every pin, and its middle.</summary>
        private static void Points(PartOutline outline, List<BoardPoint> into)
        {
            foreach (PinSpot pin in outline.Pins)
                into.Add(pin.At);
            into.Add(new BoardPoint((outline.Left + outline.Right) / 2, (outline.Bottom + outline.Top) / 2));
        }

        /// <summary>The nudge along one axis that brings the nearest pair within <see cref="Reach"/> into line, or 0.</summary>
        private static double Nearest(List<BoardPoint> mine, List<BoardPoint> theirs, double offset, bool across)
        {
            double best = Reach + Exact, nudge = 0;
            foreach (BoardPoint m in mine)
            {
                double at = (across ? m.X : m.Y) + offset;
                foreach (BoardPoint o in theirs)
                {
                    double gap = (across ? o.X : o.Y) - at;
                    if (Math.Abs(gap) < best)
                    {
                        best = Math.Abs(gap);
                        nudge = gap;
                    }
                }
            }

            return nudge;
        }

        /// <summary>A guide, joined to one already on the same line rather than drawn twice.</summary>
        private static void Add(List<Guide> guides, Guide guide)
        {
            for (int i = 0; i < guides.Count; i++)
            {
                Guide g = guides[i];
                if (g.Vertical == guide.Vertical && Math.Abs(g.At - guide.At) < Exact)
                {
                    guides[i] = new Guide(g.Vertical, g.At, Math.Min(g.From, guide.From), Math.Max(g.To, guide.To));
                    return;
                }
            }

            guides.Add(guide);
        }
    }

    /// <summary>
    /// What is selected on the canvas (Part 10): parts and wires, by id. A click picks one -- with Shift adds
    /// it, with Ctrl puts it in or takes it out -- and a box picks what it touches. Ids are the document's own,
    /// never reused, so a selection kept across an edit means what it meant, less whatever the edit took away.
    /// </summary>
    public sealed class CanvasSelection
    {
        private readonly HashSet<int> _parts = new HashSet<int>();
        private readonly HashSet<int> _wires = new HashSet<int>();

        public IReadOnlyCollection<int> Parts => _parts;
        public IReadOnlyCollection<int> Wires => _wires;

        public bool IsEmpty => _parts.Count == 0 && _wires.Count == 0;

        /// <summary>Counts every change: what tells the view to draw it again.</summary>
        public int Version { get; private set; }

        public bool HasPart(int part) => _parts.Contains(part);
        public bool HasWire(int wire) => _wires.Contains(wire);

        /// <summary>Whether <paramref name="target"/> is selected: its part, or its wire.</summary>
        public bool Has(CanvasTarget target) =>
            target.IsPart ? _parts.Contains(target.Part) : target.Kind == CanvasTargetKind.Wire && _wires.Contains(target.Wire);

        public void Clear()
        {
            if (IsEmpty)
                return;
            _parts.Clear();
            _wires.Clear();
            Version++;
        }

        /// <summary>A click on <paramref name="target"/>: it alone, or added (<paramref name="extend"/>), or in or out (<paramref name="toggle"/>). A click on nothing clears, but with either key held.</summary>
        public void Pick(CanvasTarget target, bool extend, bool toggle)
        {
            if (target.Kind == CanvasTargetKind.Nothing)
            {
                if (!extend && !toggle)
                    Clear();
                return;
            }

            HashSet<int> set = target.IsPart ? _parts : _wires;
            int id = target.IsPart ? target.Part : target.Wire;

            if (toggle)
            {
                if (!set.Remove(id))
                    set.Add(id);
            }
            else
            {
                if (!extend)
                {
                    _parts.Clear();
                    _wires.Clear();
                }

                set.Add(id);
            }

            Version++;
        }

        /// <summary>A box let go: what it touched, in place of what was selected, or added with <paramref name="extend"/>.</summary>
        public void Box(IEnumerable<int> parts, IEnumerable<int> wires, bool extend)
        {
            if (!extend)
            {
                _parts.Clear();
                _wires.Clear();
            }

            foreach (int part in parts)
                _parts.Add(part);
            foreach (int wire in wires)
                _wires.Add(wire);
            Version++;
        }

        /// <summary>Every part and wire of <paramref name="module"/>: Ctrl+A.</summary>
        public void All(Module module)
        {
            _parts.Clear();
            _wires.Clear();
            foreach (Part part in module.Parts)
                _parts.Add(part.Id);
            foreach (Wire wire in module.Wires)
                _wires.Add(wire.Id);
            Version++;
        }

        /// <summary>Lets go of whatever <paramref name="module"/> no longer has: what an edit or an undo took away.</summary>
        public void Keep(Module module)
        {
            int before = _parts.Count + _wires.Count;
            _parts.RemoveWhere(part => module?.FindPart(part) == null);
            _wires.RemoveWhere(wire => module?.FindWire(wire) == null);
            if (_parts.Count + _wires.Count != before)
                Version++;
        }
    }
}
