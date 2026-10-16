using UnityEngine;
using UnityEngine.UI;
using BitSorter.ViewModel;

namespace BitSorter.View
{
    /// <summary>
    /// One piece of the workbench's picture, as one UI mesh: the card, or a chunk of the wires, of
    /// the changes on their way down them, of the parts or of the waveform. What each draws is
    /// <see cref="WorkbenchDrawing"/>'s, and how the layers are cut into chunks is
    /// <see cref="WorkbenchLayers"/>'.
    /// </summary>
    /// <remarks>
    /// Layers rather than one mesh so the ring digits, which are TextMeshPro labels, can sit between
    /// the wires and the parts: a change arriving at a gate goes under the gate, as in the mock,
    /// while its digit stays over its ring. The mesh is rebuilt only when the panel says the moment
    /// changed, into a <see cref="MeshBuffer"/> shared by every graphic, so a frame of playback makes
    /// no garbage here.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorkbenchGraphic : MaskableGraphic
    {
        /// <summary>The layers, back to front.</summary>
        public enum Layer { Card, Wires, Changes, Parts, Flashes, Grid, Timing, Glitches, Playhead }

        /// <summary>How many layers there are: what the per-layer diagnostics are sized by.</summary>
        public static readonly int LayerCount = System.Enum.GetValues(typeof(Layer)).Length;

        /// <summary>Which layer this one draws.</summary>
        public Layer Draws { get; set; }

        /// <summary>The first of its layer's items this one draws -- a net, a part or a row -- and how many.</summary>
        public int From { get; set; }

        /// <inheritdoc cref="From"/>
        public int Count { get; set; } = int.MaxValue;

        /// <summary>How many vertices it took the last time it drew: what the chunks are cut to keep under uGUI's limit.</summary>
        public int VertexCount { get; private set; }

        /// <summary>How many times it has drawn its mesh: what says a still chunk stayed still.</summary>
        public int Rebuilds { get; private set; }

        /// <summary>The most vertices a UI mesh can hold (uGUI's limit), and so the most a chunk may take.</summary>
        public const int MeshLimit = 65000;

        private bool _overflowed;

        /// <summary>The run and the moment it is showing.</summary>
        public WorkbenchModel Model { get; set; }

        /// <summary>The time the waveform's layers show, and how it is laid across them; none for the schematic's.</summary>
        public WaveView Wave { get; set; }

        /// <summary>The net the pointer is on, lit; -1 for none. The wires layer's alone.</summary>
        public int Highlighted { get; set; } = -1;

        private readonly VectorPainter _painter = new VectorPainter();

        /// <summary>What every graphic draws into, one at a time: a renderer keeps its own copy.</summary>
        private static readonly MeshBuffer Buffer = new MeshBuffer();

        /// <summary>The mesh every graphic hands its renderer, as uGUI's own graphics share one.</summary>
        private static Mesh _scratch;

        /// <summary>
        /// Time spent by every graphic since <see cref="ResetSpent"/>, drawing its mesh and handing it
        /// to its renderer, in milliseconds: how the performance test says where a frame went.
        /// </summary>
        public static double DrawingMilliseconds { get; private set; }

        /// <inheritdoc cref="DrawingMilliseconds"/>
        public static double HandingOverMilliseconds { get; private set; }

        /// <summary>The same drawing time and the vertices drawn, layer by layer (indexed by <see cref="Layer"/>).</summary>
        public static readonly double[] DrawingByLayer = new double[LayerCount];

        /// <inheritdoc cref="DrawingByLayer"/>
        public static readonly long[] VerticesByLayer = new long[LayerCount];

        /// <inheritdoc cref="DrawingMilliseconds"/>
        public static void ResetSpent()
        {
            DrawingMilliseconds = HandingOverMilliseconds = 0;
            System.Array.Clear(DrawingByLayer, 0, DrawingByLayer.Length);
            System.Array.Clear(VerticesByLayer, 0, VerticesByLayer.Length);
        }

        private static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();

        /// <summary>The colour each of its nets was last drawn in, net <see cref="From"/> first.</summary>
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

            int last = (int)System.Math.Min((long)From + Count, Model.Layout.Nets.Count);
            for (int i = From; i < last && i - From < _drawn.Length; i++)
            {
                if (Model.Layout.Nets[i].Signal == signal || Model.Layout.Nets[i].Name == signal)
                {
                    colour = _drawn[i - From];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Draws the mesh and hands it to the renderer, in place of uGUI's way through
        /// <c>OnPopulateMesh</c> and its <c>VertexHelper</c>: the same picture, and a frame of playback
        /// on 300 wires took 22 ms this way where it had taken 35 (<see cref="MeshBuffer"/>).
        /// </summary>
        protected override void UpdateGeometry()
        {
            Clock.Restart();
            Buffer.Clear();
            if (rectTransform.rect.width >= 0f && rectTransform.rect.height >= 0f)
                Draw(Buffer);

            VertexCount = Buffer.VertexCount;
            Rebuilds++;

            // A chunk whose items outgrow a mesh draws as many whole ones as fit (VectorPainter.Fits)
            // and says so, once; anything drawn outside those loops that still outgrew one draws nothing
            // rather than hand a renderer more than a mesh can hold.
            if (VertexCount > MeshLimit)
                Buffer.Clear();
            if ((_painter.Overflowed || VertexCount > MeshLimit) && !_overflowed)
            {
                _overflowed = true;
                Debug.LogError($"[Workbench] {name} ({Draws}, from {From}) had more to draw than one mesh holds, " +
                    $"{MeshLimit} vertices, and left the rest out: its chunk's estimate is too low.");
            }

            double drawn = Clock.Elapsed.TotalMilliseconds;
            if (_scratch == null)
                _scratch = new Mesh { name = "Workbench", hideFlags = HideFlags.HideAndDontSave };
            Buffer.FillMesh(_scratch);
            canvasRenderer.SetMesh(_scratch);
            Clock.Stop();

            DrawingMilliseconds += drawn;
            HandingOverMilliseconds += Clock.Elapsed.TotalMilliseconds - drawn;
            DrawingByLayer[(int)Draws] += drawn;
            VerticesByLayer[(int)Draws] += VertexCount;
        }

        /// <summary>Never asked for: <see cref="UpdateGeometry"/> draws the mesh itself.</summary>
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        private void Draw(MeshBuffer mesh)
        {
            // One screen pixel in this graphic's units: how wide the edges' smoothing is.
            float scale = Mathf.Abs(rectTransform.lossyScale.x);
            _painter.Begin(mesh, scale > 1e-5f ? 1f / scale : 1f);

            SchematicPalette palette = SchematicPalette.Current;

            if (Draws == Layer.Card)
            {
                WorkbenchDrawing.Card(_painter, rectTransform.rect.size.ToStage(), palette);
                return;
            }

            if (Model == null)
                return;

            bool wave = Draws == Layer.Grid || Draws == Layer.Timing || Draws == Layer.Glitches || Draws == Layer.Playhead;
            if (wave && Wave == null)
                return;

            switch (Draws)
            {
                case Layer.Wires:
                    int nets = (int)System.Math.Max(0, System.Math.Min((long)From + Count, Model.Layout.Nets.Count) - From);
                    if (_drawn.Length != nets)
                        _drawn = new Color[nets];
                    WorkbenchDrawing.Wires(_painter, Model, palette, _drawn, Highlighted, From, Count);
                    break;

                case Layer.Changes:
                    WorkbenchDrawing.Changes(_painter, Model, palette, From, Count);
                    break;

                case Layer.Parts:
                    WorkbenchDrawing.Parts(_painter, Model, palette, From, Count);
                    break;

                case Layer.Flashes:
                    WorkbenchDrawing.Parts(_painter, Model, palette, From, Count, flashes: true);
                    break;

                case Layer.Grid:
                    WorkbenchDrawing.Grid(_painter, Model, Wave, palette);
                    break;

                case Layer.Timing:
                    WorkbenchDrawing.Timing(_painter, Model, Wave, palette, From, Count);
                    break;

                case Layer.Glitches:
                    WorkbenchDrawing.Glitches(_painter, Model, Wave, palette, From, Count);
                    break;

                case Layer.Playhead:
                    WorkbenchDrawing.Playhead(_painter, Model, Wave, palette);
                    break;
            }
        }

        /// <summary>The colour net <paramref name="net"/> was drawn in last, by a wires chunk that draws it.</summary>
        public Color DrawnColour(int net) => net >= From && net - From < _drawn.Length ? _drawn[net - From] : default;
    }
}
