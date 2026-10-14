using UnityEngine;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// One layer of the workbench's picture, as one UI mesh: the card, the wires, the parts or the
    /// waveform. What each draws is <see cref="WorkbenchDrawing"/>'s.
    /// </summary>
    /// <remarks>
    /// Layers rather than one mesh so the ring digits, which are TextMeshPro labels, can sit between
    /// the wires and the parts: a change arriving at a gate goes under the gate, as in the mock,
    /// while its digit stays over its ring. The mesh is rebuilt only when the panel says the moment
    /// changed, and Unity reuses its buffers, so a frame of playback makes no garbage here.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorkbenchGraphic : MaskableGraphic
    {
        /// <summary>The layers, back to front.</summary>
        public enum Layer { Card, Wires, Parts, Timing }

        /// <summary>Which layer this one draws.</summary>
        public Layer Draws { get; set; }

        /// <summary>The run and the moment it is showing.</summary>
        public WorkbenchModel Model { get; set; }

        /// <summary>The net the pointer is on, lit; -1 for none. The wires layer's alone.</summary>
        public int Highlighted { get; set; } = -1;

        private readonly VectorPainter _painter = new VectorPainter();

        /// <summary>The colour each net was last drawn in, by its index in the layout.</summary>
        private Color[] _drawn = new Color[0];

        /// <summary>
        /// The colour a signal's wire was drawn in when the wires layer last drew: what the screen
        /// is showing, read back from the drawing itself rather than worked out again.
        /// </summary>
        public bool TryDrawnColour(string signal, out Color colour)
        {
            colour = default;
            if (Model == null)
                return false;

            for (int i = 0; i < Model.Layout.Nets.Count && i < _drawn.Length; i++)
            {
                if (Model.Layout.Nets[i].Signal == signal || Model.Layout.Nets[i].Name == signal)
                {
                    colour = _drawn[i];
                    return true;
                }
            }

            return false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            // One screen pixel in this graphic's units: how wide the edges' smoothing is.
            float scale = Mathf.Abs(rectTransform.lossyScale.x);
            _painter.Begin(vh, scale > 1e-5f ? 1f / scale : 1f);

            SchematicPalette palette = SchematicPalette.Current;

            if (Draws == Layer.Card)
            {
                WorkbenchDrawing.Card(_painter, rectTransform.rect.size, palette);
                return;
            }

            if (Model == null)
                return;

            switch (Draws)
            {
                case Layer.Wires:
                    if (_drawn.Length != Model.Layout.Nets.Count)
                        _drawn = new Color[Model.Layout.Nets.Count];
                    WorkbenchDrawing.Wires(_painter, Model, palette, _drawn, Highlighted);
                    break;

                case Layer.Parts:
                    WorkbenchDrawing.Parts(_painter, Model, palette);
                    break;

                case Layer.Timing:
                    WorkbenchDrawing.Timing(_painter, Model, palette, rectTransform.rect.width);
                    break;
            }
        }
    }
}
