using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using BitSorter.ViewModel;
using UnityEngine;
using static BitSorter.ViewModel.SymbolGeometry;
// The stage's points are the view-model's Vec2, Unity's own Vector2 arithmetic without Unity, so
// what is drawn here is worked out in the same floats the layout and its tests use.
using Vector2 = BitSorter.ViewModel.Vec2;

namespace BitSorter.View
{
    /// <summary>
    /// What each of the workbench's layers draws, from the model at its current moment: the wires and
    /// the changes travelling on them, the parts over them, the card round the stage, the waveform.
    /// </summary>
    /// <remarks>
    /// Text is not drawn here. The panel places TextMeshPro labels at the positions this uses --
    /// <see cref="WaveX"/> and the row constants for the waveform, the model's markers for the rings
    /// -- so a digit and its ring are placed by the same arithmetic.
    /// </remarks>
    internal static class WorkbenchDrawing
    {
        // -----------------------------------------------------------------
        // The waveform's geometry, shared with the panel's labels
        // -----------------------------------------------------------------

        /// <summary>Where the time axis starts, past the rows' names and values.</summary>
        public const float WaveLeft = 132f;

        /// <summary>Where the time axis ends, in a waveform <paramref name="width"/> wide.</summary>
        public static float WaveRight(float width) => width - 8f;

        /// <summary>Where the first row's top is, under the tick numbers, and how tall a row is.</summary>
        public const float WaveTop = 30f;

        /// <inheritdoc cref="WaveTop"/>
        public const float RowHeight = 26f;

        /// <summary>How far a row's high and low levels sit from its middle.</summary>
        public const float LevelOffset = 8f;

        /// <summary>A row's name ends here; its current value starts here.</summary>
        public const float NameRight = 58f;

        /// <inheritdoc cref="NameRight"/>
        public const float ReadoutLeft = 66f;

        /// <summary>How tall the waveform is for <paramref name="rows"/> rows, tick numbers and margin included.</summary>
        public static float WaveHeight(int rows) => WaveTop + rows * RowHeight + 22f;

        /// <summary>How wide the time axis is in a waveform <paramref name="width"/> wide: the width its <see cref="WaveView"/> shows time across.</summary>
        public static float AxisWidth(float width) => WaveRight(width) - WaveLeft;

        /// <summary>Where a moment sits on the time axis, as <paramref name="view"/> shows time: off it either side when the view does not reach it.</summary>
        public static float WaveX(double time, WaveView view) => WaveLeft + view.X(time);

        /// <summary>
        /// The least room between two of the time axis's ticks, so their numbers stay apart: the step is
        /// the finest on the 1-2-5 ladder at least this far apart (<see cref="WaveView.TickStep"/>). The
        /// reference circuit's run, fitted, is ticked every 2 ns as it was before the axis could zoom.
        /// </summary>
        public const float TickPixels = 40f;

        /// <summary>The middle of a row.</summary>
        public static float RowMiddle(int row) => WaveTop + RowHeight * 0.5f + row * RowHeight;

        private static readonly List<Vector2> Shape = new List<Vector2>(64);
        private static readonly List<WaveSpan> Spans = new List<WaveSpan>(64);
        private static readonly List<double> Swallowed = new List<double>(4);

        /// <summary>
        /// The track whose spans are being drawn, and the one delegate that reads its times: made once,
        /// since a lambda capturing a track would be made again, garbage and all, every time it was written.
        /// </summary>
        private static SignalTrack _track;

        /// <inheritdoc cref="_track"/>
        private static readonly Func<int, double> TrackTime = i => _track[i].Time;

        // -----------------------------------------------------------------
        // The card
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // Editing (Part 10a)
        // -----------------------------------------------------------------

        /// <summary>How wide a selected part's outline is drawn, and an alignment guide.</summary>
        public const float SelectedWidth = 1.5f, GuideWidth = 1.5f;

        /// <summary>
        /// The editing overlay (<see cref="CanvasOverlay"/>): what is selected in the accent -- a part's outline, a
        /// wire's band under it -- a selection being dragged where it is going, its wires dashed after it, a box
        /// drawn faintly, the alignment guides in the second cursor's colour, and the part in hand where it would
        /// land. In colours no value is drawn in, so nothing selected reads as a 0, a 1 or an x. Under all of it, the
        /// timing path picked (Part 14b) in the second cursor's colour: a wider band than a selection's, and its parts
        /// outlined further out, so a part both selected and on the path shows both.
        /// </summary>
        public static void Edit(VectorPainter painter, CanvasOverlay overlay, SchematicPalette palette)
        {
            if (overlay == null)
                return;

            foreach (StageRoute wire in overlay.PathWires)
                painter.Stroke(wire, SchematicStyle.OneWidth + PathBand, palette.Over(palette.Cursor, 0.4f));
            foreach (Rect2 part in overlay.PathParts)
                Outlined(painter, part, palette.Cursor, PathWidth);
            foreach (Rect2 port in overlay.PathPorts)
                Outlined(painter, Rect2.MinMaxRect(port.xMin - PathGap, port.yMin - PathGap, port.xMax + PathGap, port.yMax + PathGap), palette.Cursor, PathWidth);

            foreach (Vector2[] wire in overlay.SelectedWires)
                if (wire.Length >= 2)
                    painter.Stroke(new StageRoute(wire), SchematicStyle.OneWidth + 5f, palette.Over(palette.Accent, 0.45f));
            foreach (Rect2 part in overlay.Selected)
                Outlined(painter, part, palette.Accent, SelectedWidth);

            foreach (Vector2[] wire in overlay.GhostWires)
                if (wire.Length >= 2)
                    painter.Dashed(new StageRoute(wire), SelectedWidth, palette.Accent, 5f, 4f);
            foreach (Rect2 part in overlay.Ghosts)
                Outlined(painter, part, palette.Accent, SelectedWidth);
            foreach ((Vector2 from, Vector2 to) in overlay.Guides)
                Guide(painter, from, to, palette.Cursor);

            // A selected wire's corners (Part 10b-2): a square on each, filled with the ground so the wire does not run
            // through it -- something to take hold of, and never a junction's dot, which is round.
            foreach (Vector2 at in overlay.Handles)
            {
                painter.Rect(at.x - Handle, at.y - Handle, at.x + Handle, at.y + Handle, palette.Ground);
                Outlined(painter, Rect2.MinMaxRect(at.x - Handle, at.y - Handle, at.x + Handle, at.y + Handle), palette.Accent, SelectedWidth, corner: 1f);
            }

            if (overlay.Box is Rect2 box)
            {
                painter.Rect(box.xMin, box.yMin, box.xMax, box.yMax, palette.Over(palette.Accent, 0.12f));
                Outlined(painter, box, palette.Accent, 1f, corner: 0f);
            }

            if (overlay.Hand is Rect2 hand)
                Outlined(painter, hand, palette.Accent, SelectedWidth);

            // A wire being drawn (Part 10b): where it would run, a ring on its start, and one on the end it would take --
            // crossed through, and in x's colour, on one the document would refuse, so the shape says it as well.
            if (overlay.Draft != null && overlay.Draft.Length >= 2)
                painter.Stroke(new StageRoute(overlay.Draft), DraftWidth, palette.Accent);
            if (overlay.DraftStart is Vector2 start)
                painter.Ring(start, EndRing, SelectedWidth, palette.Accent);
            if (overlay.DraftEnd is Vector2 end)
            {
                Color colour = overlay.DraftRefused ? palette.Unknown : palette.Accent;
                painter.Ring(end, EndRing + 2f, SelectedWidth + 0.5f, colour);
                if (overlay.DraftRefused)
                    painter.Cross(end, EndRing - 1f, SelectedWidth, colour);
            }
        }

        /// <summary>How much wider than a wire the timing path's band is, its outlines' width, and how far round a port it runs.</summary>
        private const float PathBand = 8f, PathWidth = 2f, PathGap = 10f;

        /// <summary>How wide a wire being drawn is, and how big the rings on its ends are.</summary>
        private const float DraftWidth = 3f, EndRing = 6f;

        /// <summary>Half a corner handle's side, in stage units.</summary>
        private const float Handle = 4f;

        /// <summary>The room round the drawing inside the minimap's edge, in window units.</summary>
        public const float MinimapPad = 6f;

        /// <summary>
        /// The least a splitter's bar is wide on the minimap, and the least an inverting gate's bubble's radius is: drawn
        /// at the map's scale they are under a pixel, and a NAND would be an AND.
        /// </summary>
        private const float MinimapBar = 1.5f, MinimapBubble = 1f;

        /// <summary>
        /// The minimap (Part 10d): a chip in the ground's colour, the wires as hairlines and the parts and ports as solid
        /// shapes, all at the map's scale, and the window's rectangle on it in the accent, faintly filled. A part is its
        /// own outline, the one the circuit draws (<see cref="Body"/>) -- an AND's D, an OR's shield, a NOT's triangle and
        /// its bubble -- so the map reads as the circuit it is; plain blocks were the first try, and every gate was the
        /// same block.
        /// </summary>
        public static void Minimap(VectorPainter painter, Minimap map, SchematicLayout layout, SchematicPalette palette)
        {
            if (map == null || layout == null)
                return;

            float s = map.Scale;
            Vector2 P(Vector2 v) => new Vector2(v.x * s + MinimapPad, v.y * s + MinimapPad);
            var edge = Rect2.MinMaxRect(0f, 0f, map.Size.x + 2f * MinimapPad, map.Size.y + 2f * MinimapPad);
            painter.Rect(edge.xMin, edge.yMin, edge.xMax, edge.yMax, palette.Ground);
            Outlined(painter, edge, palette.ChipEdge, 1f, corner: 4f);

            foreach (NetLayout net in layout.Nets)
                foreach (StageRoute branch in net.Branches)
                    for (int i = 1; i < branch.PointCount; i++)
                        painter.Line(P(branch.Point(i - 1)), P(branch.Point(i)), 1f, palette.Muted);

            foreach (PartLayout part in layout.Parts)
            {
                if (part.Symbol == PartSymbol.Splitter || part.Symbol == PartSymbol.Joiner)
                {
                    double bar = part.Local.Top - 0.1;
                    painter.Line(P(part.ToStage(0, bar)), P(part.ToStage(0, -bar)), MinimapBar, palette.Outline);
                    continue;
                }

                Body(Shape, part);
                for (int i = 0; i < Shape.Count; i++)
                    Shape[i] = P(Shape[i]);
                painter.Fill(Shape, palette.Outline);

                if (Inverts(part.Symbol))
                {
                    VectorPainter.Circle(Shape, P(part.ToStage(Tip(part.Symbol) + Bubble(part), 0)), Math.Max(MinimapBubble, SchematicStyle.BubbleRadius * s), 12);
                    painter.Fill(Shape, palette.Outline);
                }
            }

            foreach (PortLayout port in layout.Ports)
            {
                Vector2 min = P(new Vector2(port.Box.xMin, port.Box.yMin));
                VectorPainter.RoundedRect(Shape, min.x, min.y, port.Box.width * s, port.Box.height * s, SchematicStyle.PortCorner * s);
                painter.Fill(Shape, palette.Outline);
            }

            Rect2 w = map.Window;
            var window = Rect2.MinMaxRect(w.xMin + MinimapPad, w.yMin + MinimapPad, w.xMax + MinimapPad, w.yMax + MinimapPad);
            painter.Rect(window.xMin, window.yMin, window.xMax, window.yMax, palette.Over(palette.Accent, 0.15f));
            Outlined(painter, window, palette.Accent, SelectedWidth, corner: 0f);
        }

        /// <summary>
        /// How far a guide runs on past the two points it joins, and its dots. Drawn whole from pin to pin it was a wire
        /// on a schematic -- an input to an output straight through the gate between -- so it is dotted, and runs on past
        /// both, where a wire is whole and stops at its pins.
        /// </summary>
        private const float GuideOverrun = 16f, GuideDot = 3f, GuideGap = 4f;

        private static void Guide(VectorPainter painter, Vector2 from, Vector2 to, Color colour)
        {
            Vector2 along = to - from;
            float length = along.magnitude;
            Vector2 unit = length > 1e-4f ? along / length : new Vector2(1f, 0f);
            painter.Dashed(new StageRoute(new[] { from - unit * GuideOverrun, to + unit * GuideOverrun }), GuideWidth, colour, GuideDot, GuideGap);
        }

        private static void Outlined(VectorPainter painter, Rect2 rect, Color colour, float width, float corner = 6f)
        {
            VectorPainter.RoundedRect(Shape, rect.xMin, rect.yMin, rect.width, rect.height, corner);
            painter.Outline(Shape, width, colour);
        }

        // -----------------------------------------------------------------
        // The legend, along the dock's foot (Part 11b; the card's, as the reference has it, until then), all four values
        // -----------------------------------------------------------------

        /// <summary>Where each value's swatch starts, and where its name and the sentence after them start.</summary>
        public static float LegendSwatchX(int item) => 24f + item * 52f;

        /// <inheritdoc cref="LegendSwatchX"/>
        public static float LegendNameX(int item) => LegendSwatchX(item) + 24f;

        /// <inheritdoc cref="LegendSwatchX"/>
        public static float LegendTextX => LegendSwatchX(4) + 4f;

        /// <summary>A short stretch of each value as a wire draws it, along a line <paramref name="y"/> down: 0, 1, x hatched, z dashed.</summary>
        public static void Legend(VectorPainter painter, SchematicPalette palette, float y)
        {
            const float length = 18f;

            float x = LegendSwatchX(0);
            painter.Line(new Vector2(x, y), new Vector2(x + length, y), SchematicStyle.ZeroWidth, palette.Zero, roundCaps: true);

            x = LegendSwatchX(1);
            painter.Line(new Vector2(x, y), new Vector2(x + length, y), SchematicStyle.OneWidth, palette.One, roundCaps: true);

            x = LegendSwatchX(2);
            HatchedRect(painter, palette, x, y - 4f, x + length, y + 4f);

            x = LegendSwatchX(3);
            DashedLine(painter, x, x + length, y, SchematicStyle.HighZWidth, palette.HighZ);
        }

        // -----------------------------------------------------------------
        // Wires, and the changes on their way down them
        // -----------------------------------------------------------------

        /// <summary>
        /// Draws the wires of nets <paramref name="from"/> to <paramref name="from"/> +
        /// <paramref name="count"/>, each in the value it holds -- with its junctions, a bus's slash and,
        /// for the net pointed at, its band -- and records each one's colour in <paramref name="drawn"/>,
        /// net <paramref name="from"/> first. The changes on their way down them are <see cref="Changes"/>.
        /// </summary>
        public static void Wires(VectorPainter painter, WorkbenchModel model, SchematicPalette palette, Color[] drawn, int highlighted = -1,
            int from = 0, int count = int.MaxValue)
        {
            SchematicLayout layout = model.Layout;
            int last = Last(from, count, layout.Nets.Count);

            for (int i = from; i < last; i++)
            {
                (int, int) mark = painter.Mark();
                NetLayout net = layout.Nets[i];
                LogicVec value = model.NetValue(i);
                Color colour = palette.Of(value);

                // The net pointed at: a soft band of its own colour under every wire of it, so the
                // whole of it reads as one thing, from its driver to every load.
                if (i == highlighted)
                {
                    Color lit = palette.Over(colour, SchematicStyle.HoverStrength);
                    foreach (StageRoute branch in net.Branches)
                        painter.Glow(branch, 0f, branch.Length, SchematicStyle.HoverWidth, SchematicStyle.HoverBlur, lit);
                }

                foreach (StageRoute branch in net.Branches)
                    Wire(painter, branch, 0f, branch.Length, net.Width, value, colour, palette, 1f);

                foreach (Vector2 junction in net.Junctions)
                    painter.Disc(junction, SchematicStyle.JunctionRadius, colour);

                // A bus's width, as a schematic marks one: a short slash across it.
                if (net.Width > 1)
                {
                    StageRoute first = net.Branches[0];
                    Vector2 at = first.PointAt(first.Length * 0.28f);
                    painter.Line(at + new Vector2(-5f, 8f), at + new Vector2(5f, -8f), 1.5f, palette.Muted);
                }

                if (!painter.Fits(mark))
                    break;
                drawn[i - from] = colour;
            }
        }

        /// <summary>
        /// Draws the changes on their way down the wires of nets <paramref name="from"/> onwards: the new
        /// value behind each, its trail and halo, its ring, and a cross over one swallowed.
        /// </summary>
        /// <remarks>
        /// A layer of their own, over every wire: they move every frame of playback, the wires only when
        /// a value arrives, so a wire's mesh is not rebuilt for a ring passing along it.
        /// </remarks>
        public static void Changes(VectorPainter painter, WorkbenchModel model, SchematicPalette palette, int from = 0, int count = int.MaxValue)
        {
            SchematicLayout layout = model.Layout;
            int last = Last(from, count, layout.Nets.Count);
            float k = CanvasMotion.Glow;
            IReadOnlyList<ChangeMarker> markers = model.Markers;
            IReadOnlyList<int> nets = model.MarkerNets;

            for (int i = 0; i < markers.Count; i++)
            {
                int on = nets[i];
                if (on < from || on >= last)
                    continue;

                (int, int) mark = painter.Mark();
                ChangeMarker m = markers[i];

                NetLayout net = layout.Nets[m.Net];
                Color colour = palette.Of(m.Value);

                // The new value, already on the wire behind the change.
                if (!m.Swallowed || m.Alpha > 0.5f)
                    Wire(painter, m.Branch, 0f, m.Distance, net.Width, m.Value, colour, palette, m.Alpha);

                // The trail and the halo: Lively's glow at Ahmad's strength.
                if (!m.Swallowed && k > 0f)
                {
                    float width = SchematicStyle.TrailBaseWidth + SchematicStyle.TrailExtraWidth * k;
                    float tail = Mathf.Min(SchematicStyle.TrailBaseLength + SchematicStyle.TrailExtraLength * k, m.Distance);
                    float blur = SchematicStyle.Blur;

                    // A stroke under a gaussian blur: what is left of its core, a falloff of two
                    // blurs each side, and the peak the blur leaves it.
                    float core = Mathf.Max(0f, width - 2f * blur);
                    float peak = SchematicStyle.TrailOpacity * k * Erf(width / (2.8284f * blur));
                    painter.Glow(m.Branch, m.Distance - tail, m.Distance, core, 2f * blur + width * 0.5f - core * 0.5f,
                        palette.Over(colour, peak));

                    float radius = SchematicStyle.HaloBaseRadius + SchematicStyle.HaloExtraRadius * k;
                    painter.Halo(m.Point, Mathf.Max(0f, radius - 2f * blur), radius + 2f * blur,
                        palette.Over(colour, SchematicStyle.HaloOpacity * k));
                }

                float ring = m.Text.Length > 1 ? SchematicStyle.WideRingRadius : SchematicStyle.RingRadius;
                painter.Disc(m.Point, ring, WithAlpha(palette.Ground, m.Alpha));
                painter.Ring(m.Point, ring, SchematicStyle.RingWidth, WithAlpha(colour, m.Alpha));

                if (m.Swallowed)
                {
                    float half = 6f + 3f * (1f - m.Alpha);
                    painter.Cross(m.Point, half, 2f, WithAlpha(palette.Text, m.Alpha));
                }

                if (!painter.Fits(mark))
                    break;
            }
        }

        /// <summary>One stretch of one wire, drawn as the value it holds says.</summary>
        private static void Wire(VectorPainter painter, StageRoute route, float from, float to, int width,
            LogicVec value, Color colour, SchematicPalette palette, float alpha)
        {
            colour = WithAlpha(colour, alpha * colour.a);

            if (width > 1)
            {
                if (value.IsAllZ)
                    DashedStretch(painter, route, from, to, SchematicStyle.BusWidth, colour);
                else
                    painter.Stroke(route, from, to, SchematicStyle.BusWidth, colour);
                return;
            }

            switch (value[0])
            {
                case Logic4.Z:
                    DashedStretch(painter, route, from, to, SchematicStyle.HighZWidth, colour);
                    break;

                case Logic4.X:
                    painter.Stroke(route, from, to, SchematicStyle.OneWidth, colour);
                    Hatch(painter, route, from, to, SchematicStyle.OneWidth + 3f, colour, palette, alpha);
                    break;

                case Logic4.One:
                    painter.Stroke(route, from, to, SchematicStyle.OneWidth, colour);
                    break;

                default:
                    painter.Stroke(route, from, to, SchematicStyle.ZeroWidth, colour);
                    break;
            }
        }

        /// <summary>
        /// An unknown wire's hatching: a faint band a little wider than the wire, crossed every few
        /// units by a short bar at 45 degrees -- so x reads by its texture, not by red alone.
        /// </summary>
        private static void Hatch(VectorPainter painter, StageRoute route, float from, float to, float band,
            Color colour, SchematicPalette palette, float alpha)
        {
            Color tint = palette.Over(colour, 0.2f);
            tint.a *= alpha;
            painter.Stroke(route, from, to, band, tint, roundCaps: false);

            Color bar = palette.Over(colour, 0.77f);
            bar.a *= alpha;
            float half = band * 0.5f;

            for (float s = from + 3f; s < to; s += 6f)
            {
                Vector2 p = route.PointAt(s);
                Vector2 along = (route.PointAt(Mathf.Min(s + 0.5f, route.Length)) - route.PointAt(Mathf.Max(s - 0.5f, 0f))).normalized;
                Vector2 across = new Vector2(-along.y, along.x);
                painter.Line(p - (across + along) * half, p + (across + along) * half, 1.8f, bar);
            }
        }

        private static void DashedStretch(VectorPainter painter, StageRoute route, float from, float to, float width, Color colour)
        {
            for (float s = from; s < to; s += SchematicStyle.DashOn + SchematicStyle.DashOff)
                painter.Stroke(route, s, Mathf.Min(s + SchematicStyle.DashOn, to), width, colour, roundCaps: false);
        }

        // -----------------------------------------------------------------
        // Parts
        // -----------------------------------------------------------------

        /// <summary>Draws every part as its symbol, flashing as its output flips, and every port box.</summary>
        /// <remarks>
        /// A symbol is written in the part's own cells, centred and facing right, and every point goes
        /// through <see cref="PartLayout.ToStage"/> -- so a part turned or mirrored on the board is drawn
        /// turned or mirrored, by the same arithmetic that put its pins where its wires end.
        /// </remarks>
        public static void Parts(VectorPainter painter, WorkbenchModel model, SchematicPalette palette, int from = 0, int count = int.MaxValue,
            bool flashes = false)
        {
            SchematicLayout layout = model.Layout;
            float k = CanvasMotion.Glow;
            int last = Last(from, count, layout.Parts.Count);

            for (int i = from; i < last; i++)
            {
                PartLayout part = layout.Parts[i];
                bool lamp = part.Symbol == PartSymbol.Lamp;
                (int, int) mark = painter.Mark();

                // Two passes: every part at rest, which never changes, and over it only what moves --
                // a gate as it flashes, a lamp showing its value -- so a frame redraws the few that do.
                // A lamp's body is all value, so it is drawn by the second pass alone, over its leads:
                // drawn by both, its outline's soft edge was laid down twice.
                LogicVec value = default;
                float flash = flashes ? CanvasMotion.Flash(model.GateFlash(i, out value)) : 0f;
                if (flashes && flash <= 0f && !lamp)
                    continue;
                if (!flashes && lamp)
                {
                    Leads(painter, part, palette.Outline);
                    if (!painter.Fits(mark))
                        break;
                    continue;
                }

                Color stroke = flash > 0f ? palette.Of(value) : palette.Outline;
                float width = SchematicStyle.OutlineWidth + flash * SchematicStyle.FlashExtraWidth;
                bool tinted = flash > 0f && k > 0f;

                if (!flashes)
                    Leads(painter, part, palette.Outline);

                if (part.Symbol == PartSymbol.Splitter || part.Symbol == PartSymbol.Joiner)
                {
                    double bar = part.Local.Top - 0.1;
                    painter.Line(part.ToStage(0, bar), part.ToStage(0, -bar), 4f, stroke, roundCaps: true);
                    continue;
                }

                // An array of a block (Part 12c): its copies stacked behind it, one edge up and to the right.
                if (!flashes && part.Part is BlockPart array && array.Copies > 1)
                {
                    const double behind = ArrayBehind, x0 = -BlockHalfWidth, x1 = BlockHalfWidth;
                    double top = part.Local.Top, bottom = part.Local.Bottom;
                    Vector2 left = part.ToStage(x0 + behind, top + behind), corner = part.ToStage(x1 + behind, top + behind);
                    Vector2 right = part.ToStage(x1 + behind, bottom + behind);
                    painter.Line(part.ToStage(x0 + behind, top), left, SchematicStyle.OutlineWidth, palette.Outline, roundCaps: true);
                    painter.Line(left, corner, SchematicStyle.OutlineWidth, palette.Outline, roundCaps: true);
                    painter.Line(corner, right, SchematicStyle.OutlineWidth, palette.Outline, roundCaps: true);
                    painter.Line(right, part.ToStage(x1, bottom + behind), SchematicStyle.OutlineWidth, palette.Outline, roundCaps: true);
                }

                Body(Shape, part);
                if (!flashes || lamp)
                    painter.Fill(Shape, lamp && flashes ? palette.Of(model.PartValue(i)) : palette.Ground);
                if (tinted && !lamp)
                    painter.Fill(Shape, palette.Over(stroke, SchematicStyle.FlashTint * k * flash));
                painter.Outline(Shape, width, stroke);
                if (tinted)
                    painter.Outline(Shape, SchematicStyle.OutlineWidth, WithAlpha(palette.Outline, 1f - flash));

                // The XOR family's second back curve, a little behind the first.
                if (part.Symbol == PartSymbol.Xor || part.Symbol == PartSymbol.Xnor)
                {
                    double half = PartGeometry.Half(part.Local.Pins.Count - 1);
                    Vector2 previous = part.ToStage(BackX(-0.62, -half, half), -half);
                    for (int s = 1; s <= 12; s++)
                    {
                        double v = -half + 2 * half * s / 12.0;
                        Vector2 next = part.ToStage(BackX(-0.62, v, half), v);
                        painter.Line(previous, next, width, stroke, roundCaps: true);
                        previous = next;
                    }
                }

                if (part.Symbol == PartSymbol.Register)
                {
                    // The clock's wedge on the left edge, at clk's height.
                    Vector2 top = part.ToStage(-0.6, -0.1), point = part.ToStage(-0.42, -0.25), bottom = part.ToStage(-0.6, -0.4);
                    painter.Line(top, point, width, stroke, roundCaps: true);
                    painter.Line(point, bottom, width, stroke, roundCaps: true);
                }

                if (Inverts(part.Symbol))
                {
                    Vector2 bubble = part.ToStage(Tip(part.Symbol) + Bubble(part), 0);
                    VectorPainter.Circle(Shape, bubble, SchematicStyle.BubbleRadius, 24);
                    painter.Fill(Shape, palette.Ground);
                    painter.Outline(Shape, width, stroke);
                }

                if (!painter.Fits(mark))
                    break;
            }

            // The ports are drawn by the first of the parts' chunks, at rest.
            if (from > 0 || flashes)
                return;

            foreach (PortLayout port in layout.Ports)
            {
                (int, int) mark = painter.Mark();
                VectorPainter.RoundedRect(Shape, port.Box.x, port.Box.y, port.Box.width, port.Box.height, SchematicStyle.PortCorner);
                painter.Fill(Shape, palette.Ground);
                painter.Outline(Shape, SchematicStyle.OutlineWidth, palette.Outline);
                if (!painter.Fits(mark))
                    break;
            }
        }

        /// <summary>
        /// The short lead of each pin nothing is wired to, from the pin to the body. A wired pin's lead
        /// is the end of its wire (<see cref="DocumentSchematic"/> runs every wire on to the body), so
        /// it is drawn in the value the wire holds rather than as a grey stub the wire stops short of.
        /// </summary>
        private static void Leads(VectorPainter painter, PartLayout part, Color colour)
        {
            foreach (PinSpot pin in part.Local.Pins)
            {
                if (part.IsWired(pin.Pin))
                    continue;
                (double x, double y) = LeadEnd(part, pin);
                if (Math.Abs(x - pin.At.X) + Math.Abs(y - pin.At.Y) < 1e-6)
                    continue;
                painter.Line(part.ToStage(pin.At.X, pin.At.Y), part.ToStage(x, y), SchematicStyle.OutlineWidth, colour);
            }
        }

        /// <summary>A part's outline, as the textbook draws its kind, into <paramref name="into"/> on the stage.</summary>
        private static void Body(List<Vector2> into, PartLayout part)
        {
            into.Clear();
            double half = PartGeometry.Half(part.Local.Pins.Count - 1);

            switch (part.Symbol)
            {
                case PartSymbol.And:
                case PartSymbol.Nand:
                    // The D: a flat back, straight top and bottom, a round front.
                    into.Add(part.ToStage(-0.5, half));
                    for (int i = 0; i <= 16; i++)
                    {
                        double angle = Math.PI / 2 - Math.PI * i / 16;
                        into.Add(part.ToStage(0.5 * Math.Cos(angle), half * Math.Sin(angle)));
                    }
                    into.Add(part.ToStage(-0.5, -half));
                    break;

                case PartSymbol.Or:
                case PartSymbol.Nor:
                case PartSymbol.Xor:
                case PartSymbol.Xnor:
                    // The shield: two curves from the back corners to a point at the front, and a hollow back.
                    for (int i = 0; i <= 12; i++)
                    {
                        double u = i / 12.0;
                        into.Add(part.ToStage(-0.5 + u, half * (1 - u * u)));
                    }
                    for (int i = 11; i >= 0; i--)
                    {
                        double u = i / 12.0;
                        into.Add(part.ToStage(-0.5 + u, -half * (1 - u * u)));
                    }
                    for (int i = 1; i < 12; i++)
                    {
                        double v = -half + 2 * half * i / 12.0;
                        into.Add(part.ToStage(BackX(-0.5, v, half), v));
                    }
                    break;

                case PartSymbol.Not:
                case PartSymbol.Buffer:
                case PartSymbol.TriBuffer:
                case PartSymbol.TriInverter:
                    into.Add(part.ToStage(-0.5, 0.32));
                    into.Add(part.ToStage(Tip(part.Symbol), 0));
                    into.Add(part.ToStage(-0.5, -0.32));
                    break;

                case PartSymbol.Mux:
                {
                    double tall = PartGeometry.Half(part.Local.Pins.Count - 2) + 0.25;
                    into.Add(part.ToStage(-0.5, tall));
                    into.Add(part.ToStage(0.5, tall - 0.3));
                    into.Add(part.ToStage(0.5, -(tall - 0.3)));
                    into.Add(part.ToStage(-0.5, -tall));
                    break;
                }

                case PartSymbol.Register:
                    Box(into, part, -0.6, -0.75, 0.6, 0.75, 0.06);
                    break;

                case PartSymbol.Constant:
                    Box(into, part, part.Local.Left, -0.3, 0.5, 0.3, 0.12);
                    break;

                case PartSymbol.Lamp:
                    for (int i = 0; i < 24; i++)
                    {
                        double angle = Math.PI * 2 * i / 24;
                        into.Add(part.ToStage(0.3 * Math.Cos(angle), 0.3 * Math.Sin(angle)));
                    }
                    break;

                default:
                    Box(into, part, -BlockHalfWidth, part.Local.Bottom, BlockHalfWidth, part.Local.Top, 0.12);
                    break;
            }
        }

        /// <summary>Half a block's box, in cells: its outline stops short of the ends of its pins.</summary>
        private const double BlockHalfWidth = 1.25;

        /// <summary>How far, in cells, an array's copies are stacked behind its box, up and to the right; its name is raised as far.</summary>
        public const double ArrayBehind = 0.18;

        /// <summary>A box with rounded corners, in the part's own cells, so it turns with the part.</summary>
        private static void Box(List<Vector2> into, PartLayout part, double x0, double y0, double x1, double y1, double corner)
        {
            void Arc(double cx, double cy, double start)
            {
                for (int i = 0; i <= 4; i++)
                {
                    double angle = (start + 90.0 * i / 4) * Math.PI / 180;
                    into.Add(part.ToStage(cx + corner * Math.Cos(angle), cy + corner * Math.Sin(angle)));
                }
            }

            Arc(x1 - corner, y1 - corner, 0);
            Arc(x0 + corner, y1 - corner, 90);
            Arc(x0 + corner, y0 + corner, 180);
            Arc(x1 - corner, y0 + corner, 270);
        }

        // -----------------------------------------------------------------
        // The waveform
        // -----------------------------------------------------------------

        /// <summary>
        /// Each row's waveform across the time <paramref name="view"/> shows, in its values' colours:
        /// drawn when the view moves, and revealed up to the moment by <see cref="WorkbenchLayers"/>,
        /// as a pen draws, so a frame of playback rebuilds nothing here. The grid, the swallowed
        /// glitches' marks and the playhead are <see cref="Grid"/>, <see cref="Glitches"/> and
        /// <see cref="Playhead"/>.
        /// </summary>
        /// <remarks>
        /// **Only the window is drawn** (Part 9b-2a): a run zoomed in can be millions of pixels long,
        /// and the changes drawn are bounded by the window's pixels rather than by the run's, since
        /// changes closer than <see cref="WaveView.BusyPixels"/> are drawn as one busy block
        /// (<see cref="WaveView.Spans"/>). A box the window cuts is drawn square at the cut, so it
        /// does not look as if the value changed there.
        /// </remarks>
        public static void Timing(VectorPainter painter, WorkbenchModel model, WaveView view, SchematicPalette palette,
            int from = 0, int count = int.MaxValue)
        {
            int last = Last(from, count, model.RowCount);
            double start = view.Start, end = Math.Min(view.End, model.End);

            for (int row = from; row < last; row++)
            {
                (int, int) mark = painter.Mark();
                float middle = RowMiddle(row), high = middle - LevelOffset, low = middle + LevelOffset;
                RowSpans(model, view, row, start, end, Spans);

                if (model.RowIsBus(row))
                    BusRow(painter, palette, view, model.End, middle, high, low);
                else
                    BitRow(painter, palette, view, middle, high, low);

                if (!painter.Fits(mark))
                    break;
            }
        }

        /// <summary>
        /// A row's stretches from <paramref name="from"/> to <paramref name="to"/>, as
        /// <paramref name="view"/> can draw them (<see cref="WaveView.Spans"/>): what the rows are drawn
        /// from, and what the panel writes the buses' values over. Each span's value is its track's
        /// change <see cref="WaveSpan.First"/> (<see cref="WorkbenchModel.RowTrack"/>).
        /// </summary>
        public static void RowSpans(WorkbenchModel model, WaveView view, int row, double from, double to, List<WaveSpan> into)
        {
            into.Clear();
            _track = model.RowTrack(row);
            if (_track != null)
                WaveView.Spans(view, TrackTime, _track.Count, from, to, into);
        }

        /// <summary>
        /// A cross on each row of <paramref name="from"/> on where a change on its way to it was
        /// swallowed, once it has been: drawn up to the moment, never revealed with the rows. A cross
        /// reaches either side of its moment, so the reveal showed one early and cut it in half at its
        /// own moment; and the marks are few, so redrawing them as they appear costs little.
        /// </summary>
        public static void Glitches(VectorPainter painter, WorkbenchModel model, WaveView view, SchematicPalette palette,
            int from = 0, int count = int.MaxValue)
        {
            int last = Last(from, count, model.RowCount);

            for (int row = from; row < last; row++)
            {
                (int, int) mark = painter.Mark();
                float middle = RowMiddle(row);
                model.RowSwallowed(row, Swallowed);
                foreach (double at in Swallowed)
                    if (view.Shows(at))
                        painter.Cross(new Vector2(WaveX(at, view), middle), 4f, 1.6f, palette.Unknown);

                if (!painter.Fits(mark))
                    break;
            }
        }

        /// <summary>The waveform's time grid, under its rows: a line at each tick the view shows (<see cref="TickPixels"/>), drawn when it moves.</summary>
        public static void Grid(VectorPainter painter, WorkbenchModel model, WaveView view, SchematicPalette palette)
        {
            float bottom = WaveHeight(model.RowCount) - 6f;
            double step = view.TickStep(TickPixels);
            (long first, long last) = view.TicksInView(step);

            for (long k = first; k <= last; k++)
            {
                double t = k * step;
                if (t > model.End + 1e-9)
                    break;

                float x = WaveX(t, view);
                painter.Line(new Vector2(x, WaveTop - 4f), new Vector2(x, bottom), 1f, palette.Grid);
            }
        }

        /// <summary>
        /// The playhead over the waveform's rows, and the second cursor and the bracket between them
        /// when it is placed (<paramref name="cursors"/>): lines, redrawn with every moment, and each
        /// left out while the view does not reach it.
        /// </summary>
        /// <remarks>
        /// The bracket runs along the margin under the rows, from one cursor to the other, cut at the
        /// edges of the axis when either is out of view; the time it spans is the panel's label over
        /// it. C2 is in its own colour (<see cref="SchematicPalette.Cursor"/>), never red, which means x.
        /// </remarks>
        public static void Playhead(VectorPainter painter, WorkbenchModel model, WaveView view, SchematicPalette palette, WaveCursors cursors = null,
            Breakpoints breakpoints = null)
        {
            float bottom = WaveHeight(model.RowCount) - 6f;
            if (breakpoints != null && breakpoints.Count > 0)
                Flags(painter, model, view, palette, breakpoints, bottom);

            if (view.Shows(model.Time))
            {
                float now = WaveX(model.Time, view);
                painter.Line(new Vector2(now, WaveTop - 2f), new Vector2(now, bottom), 1.3f, palette.Playhead);
            }

            if (cursors?.Second == null)
                return;

            double second = cursors.Second.Value;
            if (view.Shows(second))
            {
                float x = WaveX(second, view);
                painter.Line(new Vector2(x, WaveTop - 2f), new Vector2(x, bottom), 1.3f, palette.Cursor);
            }

            // The bracket, from the earlier cursor to the later, kept to the axis.
            float left = WaveLeft, right = WaveLeft + view.Width;
            float a = Mathf.Clamp(WaveX(Math.Min(second, model.Time), view), left, right);
            float b = Mathf.Clamp(WaveX(Math.Max(second, model.Time), view), left, right);
            if (b - a < 1f)
                return;

            float y = BracketY(model.RowCount);
            painter.Line(new Vector2(a, y), new Vector2(b, y), 1.1f, palette.Cursor);
            painter.Line(new Vector2(a, y - 4f), new Vector2(a, y + 4f), 1.1f, palette.Cursor);
            painter.Line(new Vector2(b, y - 4f), new Vector2(b, y + 4f), 1.1f, palette.Cursor);
        }

        /// <summary>
        /// Every breakpoint, in the cursor's colour because red is x's (Part 9b-3): a moment's as a flag on
        /// the axis, between its numbers and the first row, over a thin pole down the waves, a signal's as a flag in the gutter before its row's wave.
        /// </summary>
        private static void Flags(VectorPainter painter, WorkbenchModel model, WaveView view, SchematicPalette palette, Breakpoints breakpoints,
            float bottom)
        {
            for (int i = 0; i < breakpoints.Count; i++)
            {
                Breakpoint b = breakpoints[i];
                if (b.Kind == BreakKind.Time)
                {
                    if (!view.Shows(b.Time))
                        continue;
                    float x = WaveX(b.Time, view);
                    // Under the axis's number, never over it: a flag on a labelled moment hid half its digits.
                    painter.Line(new Vector2(x, WaveTop - 2f), new Vector2(x, bottom), 0.8f, palette.Cursor);
                    Flag(painter, new Vector2(x, WaveTop - 8f), palette.Cursor);
                    continue;
                }

                for (int row = 0; row < model.RowCount; row++)
                    if (model.RowSignal(row) == b.Signal)
                        Flag(painter, new Vector2(WaveLeft - 12f, RowMiddle(row) - 6f), palette.Cursor);
            }
        }

        private static readonly List<Vector2> FlagPoints = new List<Vector2>(3);

        /// <summary>A flag on a short pole whose top is <paramref name="top"/>, flying right.</summary>
        private static void Flag(VectorPainter painter, Vector2 top, Color colour)
        {
            painter.Line(top, top + new Vector2(0f, 12f), 1.2f, colour);
            FlagPoints.Clear();
            FlagPoints.Add(top);
            FlagPoints.Add(top + new Vector2(8f, 3f));
            FlagPoints.Add(top + new Vector2(0f, 6f));
            painter.Fill(FlagPoints, colour);
        }

        /// <summary>Where the bracket between the cursors runs: in the margin under the last row.</summary>
        public static float BracketY(int rows) => WaveTop + rows * RowHeight + 8f;

        /// <summary>One past the last item of a range, kept inside the list.</summary>
        private static int Last(int from, int count, int total) => (int)System.Math.Min((long)from + count, total);

        // -----------------------------------------------------------------
        // How much a chunk can hold
        // -----------------------------------------------------------------

        // These are estimates, for deciding where a chunk is cut: each shape is counted as it is when
        // its curves are cut as finely as they ever are (VectorPainter.CapVertices and its kind), but
        // how many changes a wire carries at once has no bound -- a fast input into a slow gate leaves
        // one swallowed after another fading -- so none of them can promise a chunk fits a mesh. What
        // does is VectorPainter.Fits: a chunk that outgrows one draws as many whole items as fit.

        /// <summary>A stroke along a route of <paramref name="points"/>, with its two round caps.</summary>
        private static int StrokeVertices(int points) => points * 4 + 2 * VectorPainter.CapVertices;

        /// <summary>A cross: two strokes with round caps.</summary>
        private const int CrossVertices = 2 * (VectorPainter.LineVertices + 2 * VectorPainter.CapVertices);

        /// <summary>
        /// About the most vertices a net's wires take: every wire stroked, hatched as x with a bar every
        /// six units, dashed as z and lit by the pointer at once, and every junction a disc.
        /// </summary>
        public static int NetVertices(NetLayout net)
        {
            int vertices = 64 + VectorPainter.LineVertices;
            foreach (StageRoute branch in net.Branches)
            {
                int bars = (int)(branch.Length / 6f) + 1, dashes = (int)(branch.Length / 11f) + 1;
                vertices += 2 * StrokeVertices(branch.PointCount) + branch.PointCount * 4 + bars * VectorPainter.LineVertices
                    + (branch.PointCount + 2 * dashes) * 4;
            }

            return vertices + net.Junctions.Count * VectorPainter.DiscVertices;
        }

        /// <summary>
        /// About the most vertices a net's changes in flight take with two on each of its wires at once,
        /// the most a wire usually carries: each with its stretch of new value, hatched, its trail, halo,
        /// disc and ring, and a cross for one swallowed.
        /// </summary>
        public static int ChangeVertices(NetLayout net)
        {
            int vertices = 0;
            foreach (StageRoute branch in net.Branches)
            {
                int bars = (int)(branch.Length / 6f) + 1;
                vertices += 2 * (3 * StrokeVertices(branch.PointCount) + bars * VectorPainter.LineVertices
                    + 2 * VectorPainter.DiscVertices + VectorPainter.RingVertices + CrossVertices);
            }

            return vertices;
        }

        /// <summary>
        /// About the most vertices a part takes, at rest or flashing: its leads, its body filled and
        /// tinted, outlined twice, and whatever its symbol adds -- the XOR's second back, a register's
        /// clock wedge, a bubble.
        /// </summary>
        public static int PartVertices(PartLayout part)
        {
            Body(Shape, part);
            int outline = Shape.Count;
            int vertices = 64 + part.Local.Pins.Count * VectorPainter.LineVertices + 2 * (outline + 1) + 2 * outline * 4;

            if (part.Symbol == PartSymbol.Xor || part.Symbol == PartSymbol.Xnor)
                vertices += 12 * (VectorPainter.LineVertices + 2 * VectorPainter.CapVertices);
            if (part.Symbol == PartSymbol.Register || part.Symbol == PartSymbol.Splitter || part.Symbol == PartSymbol.Joiner)
                vertices += 2 * (VectorPainter.LineVertices + 2 * VectorPainter.CapVertices);
            if (Inverts(part.Symbol))
                vertices += 2 * (24 + 1) + 24 * 4;
            return vertices;
        }

        /// <summary>About the most vertices the ports take, drawn by the first chunk of the parts.</summary>
        public static int PortVertices(SchematicLayout layout) => layout.Ports.Count * 400;

        /// <summary>
        /// About the most vertices a row of the waveform takes across a time axis <paramref name="axis"/>
        /// wide: every stretch its own step or box, hatched where it is x. Bounded by the axis as well
        /// as by the changes, since a window draws no more stretches than it has pixels for -- one
        /// at least <see cref="WaveView.BusyPixels"/> wide, then perhaps a busy block, and so on.
        /// </summary>
        public static int RowVertices(WorkbenchModel model, int row, float axis)
        {
            long most = 2L * (long)(axis / WaveView.BusyPixels) + 2;
            long spans = Math.Min(model.RowChangeCount(row), most);
            return (int)Math.Min(int.MaxValue, 1200 + (long)(axis * 1.5f) + spans * 96);
        }

        /// <summary>The most vertices a row's glitch marks take: a cross for every change on its way to it that is swallowed.</summary>
        public static int GlitchVertices(WorkbenchModel model, int row) => 64 + model.RowSwallowedCount(row) * CrossVertices;


        private static void BitRow(VectorPainter painter, SchematicPalette palette, WaveView view, float middle, float high, float low)
        {
            float? previous = null;

            foreach (WaveSpan span in Spans)
            {
                float x0 = WaveX(span.From, view), x1 = WaveX(span.To, view);

                if (span.Busy)
                {
                    BusyBlock(painter, palette, span, palette.One, x0, x1, high, low);
                    previous = null;
                    continue;
                }

                LogicVec value = _track[span.First].Value;
                switch (value[0])
                {
                    case Logic4.X:
                        HatchedRect(painter, palette, x0, high, x1, low);
                        previous = null;
                        break;

                    case Logic4.Z:
                        DashedLine(painter, x0, x1, middle, 1.3f, palette.HighZ);
                        previous = middle;
                        break;

                    default:
                        bool one = value[0] == Logic4.One;
                        float level = one ? high : low;
                        Color colour = one ? palette.One : palette.Zero;

                        if (previous.HasValue && !Mathf.Approximately(previous.Value, level))
                            painter.Line(new Vector2(x0, previous.Value), new Vector2(x0, level), 1.3f, colour);
                        if (x1 > x0)
                            painter.Line(new Vector2(x0, level), new Vector2(x1, level), one ? 1.7f : 1.3f, colour);

                        previous = level;
                        break;
                }
            }
        }

        private static void BusRow(VectorPainter painter, SchematicPalette palette, WaveView view, long runEnd, float middle, float high, float low)
        {
            foreach (WaveSpan span in Spans)
            {
                float x0 = WaveX(span.From, view), x1 = WaveX(span.To, view);

                if (span.Busy)
                {
                    BusyBlock(painter, palette, span, palette.Bus, x0, x1, high, low);
                    continue;
                }

                if (x1 - x0 < 0.5f)
                    continue;

                LogicVec value = _track[span.First].Value;
                if (value.IsAllZ)
                {
                    DashedLine(painter, x0, x1, middle, 1f, palette.HighZ);
                    continue;
                }

                // Square where the window cuts the value off rather than where it changes: its value
                // began before the window, or goes on past it. The run's own end closes the box.
                bool cutLeft = _track[span.First].Time < span.From - 1e-9;
                bool cutRight = span.To < runEnd - 1e-9 &&
                    (span.First + 1 >= _track.Count || _track[span.First + 1].Time > span.To + 1e-9);

                float chamfer = Mathf.Min(4f, (x1 - x0) * 0.5f);
                float left = cutLeft ? 0f : chamfer, right = cutRight ? 0f : chamfer;

                // Clockwise from the left end: a point where a value begins or ends, a square corner
                // pair where the window cuts it.
                Shape.Clear();
                if (cutLeft)
                    Shape.Add(new Vector2(x0, low));
                else
                    Shape.Add(new Vector2(x0, middle));
                Shape.Add(new Vector2(x0 + left, high));
                Shape.Add(new Vector2(x1 - right, high));
                if (!cutRight)
                    Shape.Add(new Vector2(x1, middle));
                Shape.Add(new Vector2(x1 - right, low));
                if (!cutLeft)
                    Shape.Add(new Vector2(x0 + left, low));

                if (!value.IsKnown)
                    HatchedRect(painter, palette, x0 + left, high, x1 - right, low);

                painter.Outline(Shape, 1.1f, palette.Of(value));
            }
        }

        /// <summary>
        /// Changes too close together to draw apart (<see cref="WaveView.BusyPixels"/>): one block from
        /// the first to the last, at least a pixel wide, filled faintly in the row's colour between its
        /// two levels -- or hatched as x if any of them is unknown, since an x is never hidden.
        /// </summary>
        private static void BusyBlock(VectorPainter painter, SchematicPalette palette, WaveSpan span, Color colour,
            float x0, float x1, float high, float low)
        {
            x1 = Mathf.Max(x1, x0 + painter.Pixel);

            for (int i = span.First; i <= span.Last; i++)
            {
                if (!_track[i].Value.IsKnown)
                {
                    HatchedRect(painter, palette, x0, high, Mathf.Max(x1, x0 + 0.5f), low);
                    return;
                }
            }

            painter.Rect(x0, high, x1, low, palette.Over(colour, 0.45f));
            painter.Line(new Vector2(x0, high), new Vector2(x1, high), 1.1f, colour);
            painter.Line(new Vector2(x0, low), new Vector2(x1, low), 1.1f, colour);
        }

        /// <summary>A stretch of unknown on the waveform: a faint red box crossed by diagonal bars.</summary>
        private static void HatchedRect(VectorPainter painter, SchematicPalette palette, float x0, float top, float x1, float bottom)
        {
            if (x1 - x0 < 0.5f)
                return;

            painter.Rect(x0, top, x1, bottom, palette.Over(palette.Unknown, 0.18f));

            float height = bottom - top;
            Color bar = palette.Over(palette.Unknown, 0.8f);

            for (float c = x0 - height; c < x1; c += 6f)
            {
                // From the bottom left to the top right, clipped to the box.
                float startX = c, startY = bottom, endX = c + height, endY = top;

                if (startX < x0) { startY -= x0 - startX; startX = x0; }
                if (endX > x1) { endY += endX - x1; endX = x1; }
                if (endX - startX < 0.3f) continue;

                painter.Line(new Vector2(startX, startY), new Vector2(endX, endY), 1.4f, bar);
            }
        }

        private static void DashedLine(VectorPainter painter, float x0, float x1, float y, float width, Color colour)
        {
            for (float x = x0; x < x1; x += 9f)
                painter.Line(new Vector2(x, y), new Vector2(Mathf.Min(x + 5f, x1), y), width, colour);
        }

        // -----------------------------------------------------------------

        private static Color WithAlpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        /// <summary>The error function, to the third decimal place (Abramowitz and Stegun 7.1.26): how much of a stroke's peak a blur leaves.</summary>
        private static float Erf(float x)
        {
            float t = 1f / (1f + 0.3275911f * Mathf.Abs(x));
            float y = 1f - ((((1.061405429f * t - 1.453152027f) * t + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return x >= 0f ? y : -y;
        }
    }
}
