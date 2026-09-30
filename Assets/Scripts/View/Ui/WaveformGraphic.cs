using UnityEngine;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// Draws the timing diagram's lines: one mesh for every row, rebuilt when a tick is recorded or
    /// the strip changes size.
    /// </summary>
    /// <remarks>
    /// One graphic rather than an Image per segment: a wide strip holds over a hundred ticks of up to
    /// nineteen rows, and thousands of objects to show one picture is what a mesh is for. Unity reuses
    /// the vertex buffers between rebuilds, so a redraw makes no garbage.
    ///
    /// **What each cell means is decided in <see cref="WaveformPanel"/>**, which knows the rows; this
    /// only turns "a 1 here, a collision there" into quads.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WaveformGraphic : MaskableGraphic
    {
        /// <summary>The panel whose rows are drawn.</summary>
        public WaveformPanel Panel { get; set; }

        private const float Stroke = 2f;
        private const float DashLength = 3f;
        private const float DashGap = 4f;

        /// <summary>How far in from its row's edges a level sits, so rows never touch.</summary>
        private const float LevelInset = 4f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            WaveformPanel panel = Panel;

            if (panel == null || panel.RowCount == 0)
                return;

            Rect rect = rectTransform.rect;
            float left = rect.xMin + WaveformPanel.Padding + WaveformPanel.LabelWidth;
            float top = rect.yMax - WaveformPanel.Padding - WaveformPanel.HeaderHeight;
            float bottom = top - panel.RowCount * WaveformPanel.RowHeight;
            int ticks = panel.VisibleTicks;
            int start = panel.WindowStart;
            float width = WaveformPanel.TickWidth;

            Palette palette = Palette.Current;

            // A faint mark every fourth tick, under everything else, where the tick numbers are --
            // in the board's own grid colour, quiet enough not to read as a second playhead.
            for (int i = 0; i < ticks; i++)
            {
                if ((start + i) % WaveformPanel.NumberEvery == 0)
                    Quad(vh, left + i * width, bottom, left + i * width + 1f, top, palette.Grid);
            }

            for (int row = 0; row < panel.RowCount; row++)
            {
                float rowTop = top - row * WaveformPanel.RowHeight;
                float high = rowTop - LevelInset;
                float low = rowTop - WaveformPanel.RowHeight + LevelInset;
                float middle = (high + low) * 0.5f;

                Color line = panel.RowColour(row, palette);
                bool hadValue = false;
                bool wasHigh = false;

                for (int i = 0; i < ticks; i++)
                {
                    int tick = start + i;

                    if (!panel.TryCell(row, tick, out WaveCell cell))
                    {
                        hadValue = false;
                        continue;
                    }

                    float x0 = left + i * width;
                    float x1 = x0 + width;

                    if (cell.Value.HasValue)
                    {
                        bool isHigh = cell.Value.Value == LogicCore.Bit.One;
                        float y = isHigh ? high : low;

                        Quad(vh, x0, y - Stroke * 0.5f, x1, y + Stroke * 0.5f, line);

                        // The edge between two levels, drawn where the second begins.
                        if (hadValue && wasHigh != isHigh)
                            Quad(vh, x0 - Stroke * 0.5f, low - Stroke * 0.5f, x0 + Stroke * 0.5f, high + Stroke * 0.5f, line);

                        hadValue = true;
                        wasHigh = isHigh;
                    }
                    else
                    {
                        // No bit on this tick: a dotted line through the middle of the row.
                        for (float x = x0 + 1f; x + DashLength <= x1; x += DashLength + DashGap)
                            Quad(vh, x, middle - 0.5f, x + DashLength, middle + 0.5f, palette.TextDim);

                        hadValue = false;
                    }

                    if (cell.Collided)
                        Cross(vh, (x0 + x1) * 0.5f, middle, Mathf.Min(width, WaveformPanel.RowHeight) * 0.42f, palette.Scorch);
                }
            }

            // The playhead: the right edge of the latest tick recorded, over every row.
            int last = panel.LastTick - start;

            if (last >= 0 && last < ticks)
            {
                float x = left + (last + 1) * width;
                Quad(vh, x - 1f, bottom, x + 1f, top, palette.Accent);
            }
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color colour)
        {
            int first = vh.currentVertCount;

            vh.AddVert(new Vector3(x0, y0), colour, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), colour, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), colour, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), colour, Vector4.zero);

            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }

        /// <summary>An X, as two bars through a centre. A shape, so it does not rest on colour alone.</summary>
        private static void Cross(VertexHelper vh, float cx, float cy, float half, Color colour)
        {
            Bar(vh, new Vector2(cx - half, cy - half), new Vector2(cx + half, cy + half), colour);
            Bar(vh, new Vector2(cx - half, cy + half), new Vector2(cx + half, cy - half), colour);
        }

        private static void Bar(VertexHelper vh, Vector2 from, Vector2 to, Color colour)
        {
            Vector2 along = (to - from).normalized;
            Vector2 across = new Vector2(-along.y, along.x) * (Stroke * 0.75f);
            int first = vh.currentVertCount;

            vh.AddVert(from - across, colour, Vector4.zero);
            vh.AddVert(from + across, colour, Vector4.zero);
            vh.AddVert(to + across, colour, Vector4.zero);
            vh.AddVert(to - across, colour, Vector4.zero);

            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
