using System.Collections.Generic;
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

        /// <summary>The waveform's width, and where its time axis runs from and to.</summary>
        public const float WaveWidth = 750f;

        /// <inheritdoc cref="WaveWidth"/>
        public const float WaveLeft = 132f;

        /// <inheritdoc cref="WaveWidth"/>
        public const float WaveRight = 742f;

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
        public static float WaveX(double time, long end) =>
            WaveLeft + (WaveRight - WaveLeft) * (float)(time / end);

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

                if (net.Junction.HasValue)
                    painter.Disc(net.Junction.Value, SchematicStyle.JunctionRadius, colour);

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

        /// <summary>Draws every gate, flashing as its output flips, and every port box.</summary>
        public static void Parts(VectorPainter painter, WorkbenchModel model, SchematicPalette palette)
        {
            SchematicLayout layout = model.Layout;
            float k = SchematicStyle.GlowStrength;

            for (int i = 0; i < layout.Gates.Count; i++)
            {
                GateLayout gate = layout.Gates[i];
                float flash = model.GateFlash(i, out LogicVec value);
                Color stroke = flash > 0f ? palette.Of(value) : palette.Outline;
                float width = SchematicStyle.OutlineWidth + flash * SchematicStyle.FlashExtraWidth;
                bool tinted = flash > 0f && k > 0f;

                Symbol(Shape, gate);
                painter.Fill(Shape, palette.Ground);
                if (tinted)
                    painter.Fill(Shape, palette.Over(stroke, SchematicStyle.FlashTint * k * flash));
                painter.Outline(Shape, width, stroke);
                if (tinted)
                    painter.Outline(Shape, SchematicStyle.OutlineWidth, WithAlpha(palette.Outline, 1f - flash));

                if (gate.Shape == PartShape.Not)
                {
                    Vector2 bubble = gate.At + new Vector2(38f, 0f);
                    VectorPainter.Circle(Shape, bubble, SchematicStyle.BubbleRadius, 24);
                    painter.Fill(Shape, palette.Ground);
                    painter.Outline(Shape, width, stroke);
                }
            }

            foreach (PortLayout port in layout.Ports)
            {
                VectorPainter.RoundedRect(Shape, port.At.x, port.At.y - SchematicStyle.PortHeight * 0.5f,
                    port.Width, SchematicStyle.PortHeight, SchematicStyle.PortCorner);
                painter.Fill(Shape, palette.Ground);
                painter.Outline(Shape, SchematicStyle.OutlineWidth, palette.Outline);
            }
        }

        /// <summary>A gate's outline, as the textbook draws its kind.</summary>
        private static void Symbol(List<Vector2> into, GateLayout gate)
        {
            into.Clear();
            float x = gate.At.x, y = gate.At.y;

            switch (gate.Shape)
            {
                case PartShape.And:
                    // The D: a flat back, straight top and bottom, a round front.
                    into.Add(new Vector2(x, y - 24f));
                    for (int i = 0; i <= 16; i++)
                    {
                        float angle = (-90f + 180f * i / 16f) * Mathf.Deg2Rad;
                        into.Add(new Vector2(x + 22f + Mathf.Cos(angle) * 24f, y + Mathf.Sin(angle) * 24f));
                    }
                    into.Add(new Vector2(x, y + 24f));
                    break;

                case PartShape.Not:
                    into.Add(new Vector2(x, y - 15f));
                    into.Add(new Vector2(x + 32f, y));
                    into.Add(new Vector2(x, y + 15f));
                    break;

                default:
                    into.Add(new Vector2(x, y - 16f));
                    into.Add(new Vector2(x + 32f, y));
                    into.Add(new Vector2(x, y + 16f));
                    break;
            }
        }

        // -----------------------------------------------------------------
        // The waveform
        // -----------------------------------------------------------------

        /// <summary>The time grid, each row's waveform in its values' colours, the swallowed glitches and the playhead.</summary>
        public static void Timing(VectorPainter painter, WorkbenchModel model, SchematicPalette palette)
        {
            int rows = model.Layout.Rows.Count;
            float bottom = WaveHeight(rows) - 6f;
            long end = model.End;

            for (long t = 0; t <= end; t += 2)
            {
                float x = WaveX(t, end);
                painter.Line(new Vector2(x, WaveTop - 4f), new Vector2(x, bottom), 1f, palette.Grid);
            }

            for (int row = 0; row < rows; row++)
            {
                float middle = RowMiddle(row), high = middle - LevelOffset, low = middle + LevelOffset;
                model.RowSegments(row, Segments);

                if (model.RowIsBus(row))
                    BusRow(painter, palette, end, middle, high, low);
                else
                    BitRow(painter, palette, end, middle, high, low);

                model.RowSwallowed(row, Swallowed);
                foreach (double at in Swallowed)
                    painter.Cross(new Vector2(WaveX(at, end), middle), 4f, 1.6f, palette.Unknown);
            }

            float now = WaveX(model.Time, end);
            painter.Line(new Vector2(now, WaveTop - 2f), new Vector2(now, bottom), 1.3f, palette.Playhead);
        }

        private static void BitRow(VectorPainter painter, SchematicPalette palette, long end, float middle, float high, float low)
        {
            float? previous = null;

            foreach (WaveSegment segment in Segments)
            {
                float x0 = WaveX(segment.From, end), x1 = WaveX(segment.To, end);

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

        private static void BusRow(VectorPainter painter, SchematicPalette palette, long end, float middle, float high, float low)
        {
            foreach (WaveSegment segment in Segments)
            {
                float x0 = WaveX(segment.From, end), x1 = WaveX(segment.To, end);
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
