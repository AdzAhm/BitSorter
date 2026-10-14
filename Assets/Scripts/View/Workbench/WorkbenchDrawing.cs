using System;
using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.SystemVerilog;
using UnityEngine;

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

        /// <summary>Where a moment sits on the time axis.</summary>
        public static float WaveX(double time, long end, float width) =>
            WaveLeft + (WaveRight(width) - WaveLeft) * (float)(time / end);

        /// <summary>How far apart the time axis's ticks are: about a dozen across the run, whatever its length.</summary>
        public static long TickEvery(long end) => System.Math.Max(1, (end + 12) / 13);

        /// <summary>The middle of a row.</summary>
        public static float RowMiddle(int row) => WaveTop + RowHeight * 0.5f + row * RowHeight;

        private static readonly List<Vector2> Shape = new List<Vector2>(64);
        private static readonly List<WaveSegment> Segments = new List<WaveSegment>(16);
        private static readonly List<double> Swallowed = new List<double>(4);

        // -----------------------------------------------------------------
        // The card
        // -----------------------------------------------------------------

        public static void Card(VectorPainter painter, Vector2 size, SchematicPalette palette)
        {
            VectorPainter.RoundedRect(Shape, 0.5f, 0.5f, size.x - 1f, size.y - 1f, 12f);
            painter.Outline(Shape, 1f, palette.CardEdge);
            Legend(painter, palette, LegendMiddle(size.y));
        }

        // -----------------------------------------------------------------
        // The legend, along the card's foot as the reference has it, with all four values
        // -----------------------------------------------------------------

        /// <summary>How much of the card's foot the legend takes.</summary>
        public const float LegendHeight = 30f;

        /// <summary>Where the legend's line runs, for a card this tall.</summary>
        public static float LegendMiddle(float cardHeight) => cardHeight - LegendHeight * 0.5f - 4f;

        /// <summary>Where each value's swatch starts, and where its name and the sentence after them start.</summary>
        public static float LegendSwatchX(int item) => 24f + item * 52f;

        /// <inheritdoc cref="LegendSwatchX"/>
        public static float LegendNameX(int item) => LegendSwatchX(item) + 24f;

        /// <inheritdoc cref="LegendSwatchX"/>
        public static float LegendTextX => LegendSwatchX(4) + 4f;

        /// <summary>A short stretch of each value as a wire draws it: 0, 1, x hatched, z dashed.</summary>
        private static void Legend(VectorPainter painter, SchematicPalette palette, float y)
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

        /// <summary>Draws every wire in the value it holds, then every change travelling, and records each wire's colour.</summary>
        public static void Wires(VectorPainter painter, WorkbenchModel model, SchematicPalette palette, Color[] drawn)
        {
            SchematicLayout layout = model.Layout;

            for (int i = 0; i < layout.Nets.Count; i++)
            {
                NetLayout net = layout.Nets[i];
                LogicVec value = model.NetValue(i);
                Color colour = palette.Of(value);
                drawn[i] = colour;

                foreach (Route branch in net.Branches)
                    Wire(painter, branch, 0f, branch.Length, net.Width, value, colour, palette, 1f);

                foreach (Vector2 junction in net.Junctions)
                    painter.Disc(junction, SchematicStyle.JunctionRadius, colour);

                // A bus's width, as a schematic marks one: a short slash across it.
                if (net.Width > 1)
                {
                    Route first = net.Branches[0];
                    Vector2 at = first.PointAt(first.Length * 0.28f);
                    painter.Line(at + new Vector2(-5f, 8f), at + new Vector2(5f, -8f), 1.5f, palette.Muted);
                }
            }

            float k = SchematicStyle.GlowStrength;
            IReadOnlyList<ChangeMarker> markers = model.Markers;

            for (int i = 0; i < markers.Count; i++)
            {
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
            }
        }

        /// <summary>One stretch of one wire, drawn as the value it holds says.</summary>
        private static void Wire(VectorPainter painter, Route route, float from, float to, int width,
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
        private static void Hatch(VectorPainter painter, Route route, float from, float to, float band,
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

        private static void DashedStretch(VectorPainter painter, Route route, float from, float to, float width, Color colour)
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
        public static void Parts(VectorPainter painter, WorkbenchModel model, SchematicPalette palette)
        {
            SchematicLayout layout = model.Layout;
            float k = SchematicStyle.GlowStrength;

            for (int i = 0; i < layout.Parts.Count; i++)
            {
                PartLayout part = layout.Parts[i];
                float flash = model.GateFlash(i, out LogicVec value);
                Color stroke = flash > 0f ? palette.Of(value) : palette.Outline;
                float width = SchematicStyle.OutlineWidth + flash * SchematicStyle.FlashExtraWidth;
                bool tinted = flash > 0f && k > 0f;

                Leads(painter, part, palette.Outline);

                if (part.Symbol == PartSymbol.Splitter || part.Symbol == PartSymbol.Joiner)
                {
                    double bar = part.Local.Top - 0.1;
                    painter.Line(part.ToStage(0, bar), part.ToStage(0, -bar), 4f, stroke, roundCaps: true);
                    continue;
                }

                Body(Shape, part);
                Color fill = part.Symbol == PartSymbol.Lamp ? palette.Of(model.PartValue(i)) : palette.Ground;
                painter.Fill(Shape, fill);
                if (tinted && part.Symbol != PartSymbol.Lamp)
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
            }

            foreach (PortLayout port in layout.Ports)
            {
                VectorPainter.RoundedRect(Shape, port.Box.x, port.Box.y, port.Box.width, port.Box.height, SchematicStyle.PortCorner);
                painter.Fill(Shape, palette.Ground);
                painter.Outline(Shape, SchematicStyle.OutlineWidth, palette.Outline);
            }
        }

        private static bool Inverts(PartSymbol symbol) =>
            symbol == PartSymbol.Not || symbol == PartSymbol.Nand || symbol == PartSymbol.Nor ||
            symbol == PartSymbol.Xnor || symbol == PartSymbol.TriInverter;

        private static bool Shield(PartSymbol symbol) =>
            symbol == PartSymbol.Or || symbol == PartSymbol.Nor || symbol == PartSymbol.Xor || symbol == PartSymbol.Xnor;

        /// <summary>Where a gate's body ends at the front, in cells: a bubble, if any, sits just past it.</summary>
        private static double Tip(PartSymbol symbol) =>
            symbol == PartSymbol.Not || symbol == PartSymbol.Buffer || symbol == PartSymbol.TriBuffer || symbol == PartSymbol.TriInverter
                ? 0.25 : 0.5;

        /// <summary>How far past the tip a bubble's middle is, in cells.</summary>
        private static double Bubble(PartLayout part) => SchematicStyle.BubbleRadius / part.Cell;

        /// <summary>The OR family's hollow back, <paramref name="at"/> cells from the middle at its ends, bowed in by a seventh of a cell.</summary>
        private static double BackX(double at, double v, double half) => at + 0.15 * (1 - (v * v) / (half * half));

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

        /// <summary>Where a pin's lead meets the body, in the part's own cells: where its symbol's outline is, which is this drawing's to say.</summary>
        internal static (double X, double Y) LeadEnd(PartLayout part, PinSpot pin)
        {
            double x = pin.At.X, y = pin.At.Y;
            bool left = pin.Facing == Facing.Left, down = pin.Facing == Facing.Down;

            switch (part.Symbol)
            {
                case PartSymbol.Register:
                    return down ? (x, -0.75) : (left ? -0.6 : 0.6, y);

                case PartSymbol.Mux:
                {
                    double half = PartGeometry.Half(part.Local.Pins.Count - 2) + 0.25;
                    return down ? (x, -(half - 0.15)) : (left ? -0.5 : 0.5, y);
                }

                case PartSymbol.Constant:
                    return (x, y);

                case PartSymbol.Splitter:
                case PartSymbol.Joiner:
                    return (0, y);

                case PartSymbol.Lamp:
                    return (-0.3, y);

                case PartSymbol.Block:
                    return (left ? -1.25 : 1.25, y);

                case PartSymbol.TriBuffer:
                case PartSymbol.TriInverter:
                    // The control meets the triangle's lower edge where it crosses the middle.
                    if (down)
                        return (x, -0.32 + 0.32 * (x + 0.5) / (Tip(part.Symbol) + 0.5));
                    return left ? (-0.5, y) : (Tip(part.Symbol) + (Inverts(part.Symbol) ? 2 * Bubble(part) : 0), y);

                default:
                    if (left)
                        return (Shield(part.Symbol) ? BackX(-0.5, y, PartGeometry.Half(part.Local.Pins.Count - 1)) : -0.5, y);
                    return (Tip(part.Symbol) + (Inverts(part.Symbol) ? 2 * Bubble(part) : 0), y);
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
                    Box(into, part, -1.25, part.Local.Bottom, 1.25, part.Local.Top, 0.12);
                    break;
            }
        }

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

        /// <summary>The time grid, each row's waveform in its values' colours, the swallowed glitches and the playhead.</summary>
        public static void Timing(VectorPainter painter, WorkbenchModel model, SchematicPalette palette, float width)
        {
            int rows = model.Layout.Rows.Count;
            float bottom = WaveHeight(rows) - 6f;
            long end = model.End;

            for (long t = 0; t <= end; t += TickEvery(end))
            {
                float x = WaveX(t, end, width);
                painter.Line(new Vector2(x, WaveTop - 4f), new Vector2(x, bottom), 1f, palette.Grid);
            }

            for (int row = 0; row < rows; row++)
            {
                float middle = RowMiddle(row), high = middle - LevelOffset, low = middle + LevelOffset;
                model.RowSegments(row, Segments);

                if (model.RowIsBus(row))
                    BusRow(painter, palette, end, width, middle, high, low);
                else
                    BitRow(painter, palette, end, width, middle, high, low);

                model.RowSwallowed(row, Swallowed);
                foreach (double at in Swallowed)
                    painter.Cross(new Vector2(WaveX(at, end, width), middle), 4f, 1.6f, palette.Unknown);
            }

            float now = WaveX(model.Time, end, width);
            painter.Line(new Vector2(now, WaveTop - 2f), new Vector2(now, bottom), 1.3f, palette.Playhead);
        }

        private static void BitRow(VectorPainter painter, SchematicPalette palette, long end, float width, float middle, float high, float low)
        {
            float? previous = null;

            foreach (WaveSegment segment in Segments)
            {
                float x0 = WaveX(segment.From, end, width), x1 = WaveX(segment.To, end, width);

                switch (segment.Value[0])
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
                        bool one = segment.Value[0] == Logic4.One;
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

        private static void BusRow(VectorPainter painter, SchematicPalette palette, long end, float width, float middle, float high, float low)
        {
            foreach (WaveSegment segment in Segments)
            {
                float x0 = WaveX(segment.From, end, width), x1 = WaveX(segment.To, end, width);
                if (x1 - x0 < 0.5f)
                    continue;

                if (segment.Value.IsAllZ)
                {
                    DashedLine(painter, x0, x1, middle, 1f, palette.HighZ);
                    continue;
                }

                float chamfer = Mathf.Min(4f, (x1 - x0) * 0.5f);
                Shape.Clear();
                Shape.Add(new Vector2(x0, middle));
                Shape.Add(new Vector2(x0 + chamfer, high));
                Shape.Add(new Vector2(x1 - chamfer, high));
                Shape.Add(new Vector2(x1, middle));
                Shape.Add(new Vector2(x1 - chamfer, low));
                Shape.Add(new Vector2(x0 + chamfer, low));

                bool unknown = !segment.Value.IsKnown;
                if (unknown)
                    HatchedRect(painter, palette, x0 + chamfer, high, x1 - chamfer, low);

                painter.Outline(Shape, 1.1f, palette.Of(segment.Value));
            }
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
