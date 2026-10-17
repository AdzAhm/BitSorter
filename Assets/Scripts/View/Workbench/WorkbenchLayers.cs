using System.Collections.Generic;
using BitSorter.ViewModel;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's picture cut into chunks, a UI mesh each, and each redrawn only when what it
    /// shows has changed.
    /// </summary>
    /// <remarks>
    /// **Cut, because a UI mesh holds 65,000 vertices.** Part 8's budget is 300 wires with 100 changes
    /// in flight at 60 frames a second; in one mesh the wires and their changes were over the limit,
    /// and uGUI threw rather than draw. So each layer is cut, by whole items, into runs whose worst
    /// case (<see cref="WorkbenchDrawing.NetVertices"/> and its kind) stays under
    /// <see cref="VertexBudget"/>: the wires by net, the changes on their way by the same nets, the
    /// parts by part, the waveform by row.
    ///
    /// **Redrawn only where it changed.** A wire's chunk is rebuilt when a value arrives on one of its
    /// nets, or the pointer lights one, and not for a ring passing along it: the changes in flight are
    /// chunks of their own, over every wire, rebuilt while they move. Wire chunks are small
    /// (<see cref="WireBudget"/>), so a value arriving redraws few neighbours. The parts are drawn once,
    /// at rest, with an overlay of only those flashing (and the lamps). The waveform's rows are drawn
    /// once, for the whole run, and revealed up to the moment by a mask, as a pen draws; the playhead,
    /// a line, is redrawn with each moment, and a row's glitch marks as each appears -- never revealed
    /// with the rows, since a cross reaches either side of its moment. A lamp's chunk is redrawn when a
    /// lamp in it changes colour. A circuit with nothing moving redraws the playhead and nothing else.
    ///
    /// **A pan of the circuit redraws nothing, and a zoom of it only the schematic**
    /// (<see cref="RefreshSchematic"/>): the edges' smoothing is a pixel wide, so a change of scale
    /// redraws what was scaled, and the waveform is left as it is. Fitting the stage to the window
    /// redraws everything.
    ///
    /// **The waveform has a zoom of its own, in time** (<see cref="Wave"/>, Part 9b-2a), and its rows
    /// are drawn for the window it shows rather than for the whole run, since zoomed in a run can be
    /// millions of pixels long. When the window moves -- a zoom, a pan, or the view paging on after the
    /// playhead -- the rows, the grid and the glitch marks are drawn again, and nothing of the
    /// schematic is. While it stays put, playback still redraws only the playhead.
    ///
    /// **Revealed, a bus's box runs on under the playhead** rather than closing in a point there, as it
    /// did when each row was drawn up to the moment: the box's value has not ended, so it is the truer
    /// picture, and the one change the split made to a reference shot.
    ///
    /// Measured on 300 wires with 200 changes in flight (<c>WorkbenchPerformanceTests</c>): a frame was
    /// 61 ms with every layer one mesh redrawn whole, and 35 ms with the parts drawn once and the
    /// waveform revealed. A canvas for every chunk, to keep uGUI from re-batching them all, made it 50:
    /// tried and taken out. The rest was the drawing itself, and the test's remarks give it step by
    /// step: about 3.3 ms in the editor and 5.5 in a batch run (2026-10-14), against the 6 ms the
    /// canvas is allowed of a 60 Hz frame, at a canvas scale of 1 -- and 4.5 to 5.5 in the editor once
    /// meshes were handed over the way a browser can draw them (<see cref="MeshBuffer.FillMesh"/>,
    /// 2026-10-15).
    ///
    /// **Cut by estimates, held by <see cref="VectorPainter.Fits"/>.** How many changes a wire carries
    /// at once has no bound, so no estimate can promise a chunk fits a mesh; a chunk that outgrows one
    /// draws as many whole items as fit and says so once, rather than drawing nothing.
    ///
    /// The cutting itself is <see cref="Chunks.Cut"/>, in the view-model, where it is tested without
    /// the editor; the budgets are here, since they are uGUI's.
    ///
    /// Built in drawing order -- every wires chunk, then every changes chunk, so no wire is drawn over
    /// a change on another chunk's wire -- and the panel puts the ring digits and the parts after them.
    /// </remarks>
    public sealed class WorkbenchLayers
    {
        /// <summary>What a chunk is cut to by its estimate: under half a mesh, since the estimates are rough.</summary>
        public const int VertexBudget = 30000;

        /// <summary>What a still wires chunk is cut to: small, as a value arriving on any of its nets redraws all of it.</summary>
        public const int WireBudget = 4000;

        private readonly WorkbenchModel _model;
        private readonly List<WorkbenchGraphic> _wires = new List<WorkbenchGraphic>();
        private readonly List<WorkbenchGraphic> _changes = new List<WorkbenchGraphic>();
        private readonly List<WorkbenchGraphic> _parts = new List<WorkbenchGraphic>();
        private readonly List<WorkbenchGraphic> _timing = new List<WorkbenchGraphic>();
        private readonly List<WorkbenchGraphic> _glitches = new List<WorkbenchGraphic>();
        private readonly List<WorkbenchGraphic> _flashes = new List<WorkbenchGraphic>();
        private WorkbenchGraphic _grid, _playhead;
        private RectTransform _reveal, _timingParent;
        private Vector2 _waveSize;
        private WaveView _wave;

        /// <summary>The window of time the waveform's rows were last drawn for: once the view shows another, they are out of date.</summary>
        private double _drawnStart = double.NaN, _drawnPixelsPerUnit = double.NaN;

        /// <summary>How far the run had got when the waveform's rows were last drawn: past it, they are out of date.</summary>
        private double _rowsDrawnTo = double.MaxValue;

        private int[] _changesOfNet = new int[0];
        private bool[] _hadChanges = new bool[0], _hasChanges = new bool[0], _wasFlashing = new bool[0];
        private int[] _glitchesShown = new int[0];
        private Color[] _lampShown = new Color[0];
        private int _highlighted = -1;

        public WorkbenchLayers(WorkbenchModel model, WaveCursors cursors = null)
        {
            _model = model;
            Cursors = cursors;
        }

        /// <summary>The second cursor the playhead's layer draws, if there is one to draw.</summary>
        public WaveCursors Cursors { get; }

        /// <summary>The time the waveform shows (<see cref="BuildTiming"/>): what its zoom and pan move. Null until it is built.</summary>
        public WaveView Wave => _wave;

        /// <summary>Every chunk, in drawing order within each layer.</summary>
        public IEnumerable<WorkbenchGraphic> All
        {
            get
            {
                foreach (WorkbenchGraphic g in _wires) yield return g;
                foreach (WorkbenchGraphic g in _changes) yield return g;
                foreach (WorkbenchGraphic g in _parts) yield return g;
                foreach (WorkbenchGraphic g in _flashes) yield return g;
                if (_grid != null) yield return _grid;
                foreach (WorkbenchGraphic g in _timing) yield return g;
                foreach (WorkbenchGraphic g in _glitches) yield return g;
                if (_playhead != null) yield return _playhead;
            }
        }

        /// <summary>The most vertices any chunk took the last time it drew.</summary>
        public int MostVertices
        {
            get
            {
                int most = 0;
                foreach (WorkbenchGraphic g in All)
                    most = Mathf.Max(most, g.VertexCount);
                return most;
            }
        }

        /// <summary>The wires' chunks and then the changes', under <paramref name="parent"/>.</summary>
        public void BuildWires(RectTransform parent, Vector2 size)
        {
            SchematicLayout layout = _model.Layout;

            foreach ((int Start, int Count) run in Chunks.Cut(layout.Nets.Count, i => WorkbenchDrawing.NetVertices(layout.Nets[i]), WireBudget))
                _wires.Add(Create("wires", parent, WorkbenchGraphic.Layer.Wires, size, run));

            _changesOfNet = new int[layout.Nets.Count];
            foreach ((int Start, int Count) run in Chunks.Cut(layout.Nets.Count, i => WorkbenchDrawing.ChangeVertices(layout.Nets[i]), VertexBudget))
            {
                for (int i = run.Start; i < run.Start + run.Count; i++)
                    _changesOfNet[i] = _changes.Count;
                _changes.Add(Create("changes", parent, WorkbenchGraphic.Layer.Changes, size, run));
            }

            _hadChanges = new bool[_changes.Count];
            _hasChanges = new bool[_changes.Count];
        }

        /// <summary>The parts' chunks at rest and then their flashes', under <paramref name="parent"/>; the first draws the ports as well.</summary>
        public void BuildParts(RectTransform parent, Vector2 size)
        {
            SchematicLayout layout = _model.Layout;
            List<(int Start, int Count)> runs = Chunks.Cut(layout.Parts.Count, i => WorkbenchDrawing.PartVertices(layout.Parts[i]), VertexBudget, first: WorkbenchDrawing.PortVertices(layout));
            foreach ((int Start, int Count) run in runs)
                _parts.Add(Create("parts", parent, WorkbenchGraphic.Layer.Parts, size, run));
            foreach ((int Start, int Count) run in runs)
                _flashes.Add(Create("flashes", parent, WorkbenchGraphic.Layer.Flashes, size, run));
            _wasFlashing = new bool[_flashes.Count];
            _lampShown = new Color[layout.Parts.Count];
        }

        /// <summary>
        /// The waveform under <paramref name="parent"/>: its grid, its rows in chunks behind a mask that
        /// reveals them up to the moment, their glitch marks, and the playhead over them. They show the
        /// time <paramref name="wave"/> does, or the whole run if none is given.
        /// </summary>
        public void BuildTiming(RectTransform parent, Vector2 size, WaveView wave = null)
        {
            _waveSize = size;
            _timingParent = parent;
            _rowsDrawnTo = RowsReach;

            if (wave == null)
            {
                wave = new WaveView(Mathf.Max(1f, WorkbenchDrawing.AxisWidth(size.x)));
                wave.Fit(_model.End);
            }

            _wave = wave;
            _drawnStart = wave.Start;
            _drawnPixelsPerUnit = wave.PixelsPerUnit;
            _grid = Create("grid", parent, WorkbenchGraphic.Layer.Grid, size, (0, 0));

            _reveal = UiTheme.Rect("reveal", parent);
            _reveal.anchorMin = _reveal.anchorMax = new Vector2(0f, 1f);
            _reveal.pivot = new Vector2(0f, 1f);
            _reveal.anchoredPosition = Vector2.zero;
            _reveal.sizeDelta = size;
            _reveal.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            foreach ((int Start, int Count) run in Chunks.Cut(_model.RowCount, row => WorkbenchDrawing.RowVertices(_model, row, _wave.Width), VertexBudget))
                _timing.Add(Create("timing", _reveal, WorkbenchGraphic.Layer.Timing, size, run));

            foreach ((int Start, int Count) run in Chunks.Cut(_model.RowCount, row => WorkbenchDrawing.GlitchVertices(_model, row), VertexBudget))
                _glitches.Add(Create("glitches", parent, WorkbenchGraphic.Layer.Glitches, size, run));
            _glitchesShown = new int[_glitches.Count];

            _playhead = Create("playhead", parent, WorkbenchGraphic.Layer.Playhead, size, (0, 0));
        }

        /// <summary>
        /// The run has grown (Part 9b): the waveform's rows are cut into chunks again when what they now
        /// hold no longer cuts the same way, and drawn again only once the moment shown passes where the
        /// run had got when they were last drawn (<see cref="Refresh"/>).
        /// </summary>
        /// <remarks>
        /// The rows are cut by their estimates when they are built, and a row's estimate grows with
        /// its changes: left as they were cut, the chunks of a run still being made would outgrow their
        /// meshes, and <see cref="VectorPainter.Fits"/> would draw only what fitted.
        ///
        /// **They are not drawn again every time the run grows**, which cost 3 ms a frame on the
        /// performance test's board. Nothing a run adds is earlier than where it had got, the rows are
        /// revealed only up to the moment, and the moment can never pass where the run has got -- so
        /// what the rows show changes only once the moment moves past where they were drawn to. During
        /// playback the run is made far faster than it is played, and that is seldom. The rest of the
        /// picture depends only on the moment shown, not on how far the run has got.
        /// </remarks>
        public void Grew()
        {
            if (_reveal == null)
                return;

            int rows = _model.RowCount;
            if (Recut(_timing, Chunks.Cut(rows, row => WorkbenchDrawing.RowVertices(_model, row, _wave.Width), VertexBudget),
                    "timing", _reveal, WorkbenchGraphic.Layer.Timing, redraw: false))
                _rowsDrawnTo = RowsReach;

            if (Recut(_glitches, Chunks.Cut(rows, row => WorkbenchDrawing.GlitchVertices(_model, row), VertexBudget),
                    "glitches", _timingParent, WorkbenchGraphic.Layer.Glitches, redraw: false))
            {
                _glitchesShown = new int[_glitches.Count];
                _playhead.transform.SetAsLastSibling();   // still over every glitch mark
            }
        }

        /// <summary>How far the rows reach when drawn now: the whole run once it is made.</summary>
        private double RowsReach => _model.Complete ? double.MaxValue : _model.Reached;

        /// <summary>
        /// Puts <paramref name="chunks"/> on <paramref name="runs"/>, made again where they are not cut
        /// the same -- a new chunk draws itself -- and, where they are, marked for drawing again if
        /// <paramref name="redraw"/>. True when they were made again.
        /// </summary>
        private bool Recut(List<WorkbenchGraphic> chunks, List<(int Start, int Count)> runs, string name, RectTransform parent,
            WorkbenchGraphic.Layer draws, bool redraw)
        {
            bool same = runs.Count == chunks.Count;
            for (int i = 0; i < runs.Count && same; i++)
                same = runs[i].Start == chunks[i].From && runs[i].Count == chunks[i].Count;

            if (same)
            {
                if (redraw)
                    foreach (WorkbenchGraphic chunk in chunks)
                        chunk.SetVerticesDirty();
                return false;
            }

            foreach (WorkbenchGraphic chunk in chunks)
            {
                if (Application.isPlaying)
                    Object.Destroy(chunk.gameObject);
                else
                    Object.DestroyImmediate(chunk.gameObject);
            }

            chunks.Clear();
            foreach ((int Start, int Count) run in runs)
                chunks.Add(Create(name, parent, draws, _waveSize, run));
            return true;
        }

        /// <summary>
        /// Marks for rebuilding every chunk whose picture is no longer what it shows: called when the
        /// moment changes. <paramref name="all"/> rebuilds every one -- when the scale changes, since
        /// the edges' smoothing is a pixel wide.
        /// </summary>
        public void Refresh(bool all = false)
        {
            SchematicPalette palette = SchematicPalette.Current;
            SchematicLayout layout = _model.Layout;

            // A wire's chunk, when a value has arrived on one of its nets since it drew.
            foreach (WorkbenchGraphic chunk in _wires)
            {
                bool stale = all;
                int last = Mathf.Min(chunk.From + chunk.Count, layout.Nets.Count);
                for (int i = chunk.From; i < last && !stale; i++)
                    stale = palette.Of(_model.NetValue(i)) != chunk.DrawnColour(i);
                if (stale)
                    chunk.SetVerticesDirty();
            }

            // A changes chunk, while a change is on one of its nets, and once more as the last arrives
            // (none, when only the waveform was built).
            for (int k = 0; k < _hasChanges.Length; k++)
                _hasChanges[k] = false;
            IReadOnlyList<int> nets = _model.MarkerNets;
            for (int m = 0; m < nets.Count && _changes.Count > 0; m++)
                _hasChanges[_changesOfNet[nets[m]]] = true;
            for (int k = 0; k < _changes.Count; k++)
            {
                if (all || _hasChanges[k] || _hadChanges[k])
                    _changes[k].SetVerticesDirty();
                _hadChanges[k] = _hasChanges[k];
            }

            // The parts at rest, only when everything is redrawn.
            if (all)
                foreach (WorkbenchGraphic chunk in _parts)
                    chunk.SetVerticesDirty();

            // A flashes chunk, while a part in it flashes, and once more as the last flash ends; and when
            // a lamp in it shows a value of another colour than it was drawn in.
            for (int k = 0; k < _flashes.Count; k++)
            {
                WorkbenchGraphic chunk = _flashes[k];
                bool moving = false, relit = false;
                int last = Mathf.Min(chunk.From + chunk.Count, layout.Parts.Count);
                for (int i = chunk.From; i < last; i++)
                {
                    if (layout.Parts[i].Symbol == PartSymbol.Lamp)
                        relit |= palette.Of(_model.PartValue(i)) != _lampShown[i];
                    else if (!moving)
                        moving = CanvasMotion.Flash(_model.GateFlash(i, out _)) > 0f;
                }

                if (all || moving || relit || _wasFlashing[k])
                {
                    chunk.SetVerticesDirty();
                    for (int i = chunk.From; i < last; i++)
                        if (layout.Parts[i].Symbol == PartSymbol.Lamp)
                            _lampShown[i] = palette.Of(_model.PartValue(i));
                }

                _wasFlashing[k] = moving;
            }

            // The waveform's rows and grid are drawn for the window of time it shows and revealed up
            // to the moment; the playhead is a line, redrawn with it. They are drawn again when the
            // window moves, and the rows of a run still being made when the moment passes where the
            // run had got when they were last drawn: see Grew.
            bool moved = _wave != null && (_wave.Start != _drawnStart || _wave.PixelsPerUnit != _drawnPixelsPerUnit);
            if (moved)
            {
                _drawnStart = _wave.Start;
                _drawnPixelsPerUnit = _wave.PixelsPerUnit;
            }

            if (all || moved || _model.Time > _rowsDrawnTo)
            {
                foreach (WorkbenchGraphic chunk in _timing)
                    chunk.SetVerticesDirty();
                _rowsDrawnTo = RowsReach;
            }

            if ((all || moved) && _grid != null)
                _grid.SetVerticesDirty();

            // A row's glitch marks, as one appears or goes: drawn up to the moment, never revealed.
            for (int k = 0; k < _glitches.Count; k++)
            {
                WorkbenchGraphic chunk = _glitches[k];
                int shown = 0, last = Mathf.Min(chunk.From + chunk.Count, _model.RowCount);
                for (int row = chunk.From; row < last; row++)
                    shown += _model.RowSwallowedSoFar(row);
                if (all || moved || shown != _glitchesShown[k])
                    chunk.SetVerticesDirty();
                _glitchesShown[k] = shown;
            }

            if (_reveal != null)
            {
                float now = WorkbenchDrawing.WaveX(_model.Time, _wave);
                // Up to the moment and no further: the cut is under the playhead's own line.
                _reveal.sizeDelta = new Vector2(Mathf.Clamp(now, 0f, _waveSize.x), _waveSize.y);
                _playhead.SetVerticesDirty();
            }
        }

        /// <summary>
        /// Redraws everything the schematic's zoom reaches -- its wires, the changes on them and its
        /// parts -- and nothing of the waveform: what a change of zoom needs, since the edges' smoothing
        /// is a pixel wide and a zoom changes how many units a pixel is.
        /// </summary>
        public void RefreshSchematic()
        {
            foreach (WorkbenchGraphic chunk in _wires) chunk.SetVerticesDirty();
            foreach (WorkbenchGraphic chunk in _changes) chunk.SetVerticesDirty();
            foreach (WorkbenchGraphic chunk in _parts) chunk.SetVerticesDirty();
            foreach (WorkbenchGraphic chunk in _flashes) chunk.SetVerticesDirty();
        }

        /// <summary>Lights net <paramref name="net"/> (-1 for none), redrawing the chunks it and the last one lit are in.</summary>
        public void Highlight(int net)
        {
            if (net == _highlighted)
                return;

            foreach (WorkbenchGraphic chunk in _wires)
            {
                if (Holds(chunk, net) || Holds(chunk, _highlighted))
                    chunk.SetVerticesDirty();
                chunk.Highlighted = net;
            }

            _highlighted = net;
        }

        /// <summary>The colour a signal's wire was drawn in last: what the screen shows.</summary>
        public bool TryDrawnColour(string signal, out Color colour)
        {
            foreach (WorkbenchGraphic chunk in _wires)
            {
                if (chunk.TryDrawnColour(signal, out colour))
                    return true;
            }

            colour = default;
            return false;
        }

        private static bool Holds(WorkbenchGraphic chunk, int item) => item >= chunk.From && item < chunk.From + chunk.Count;

        private WorkbenchGraphic Create(string name, RectTransform parent, WorkbenchGraphic.Layer draws, Vector2 size, (int Start, int Count) run)
        {
            RectTransform rect = UiTheme.Rect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            var graphic = rect.gameObject.AddComponent<WorkbenchGraphic>();
            graphic.Draws = draws;
            graphic.Model = _model;
            graphic.Wave = _wave;
            graphic.Cursors = Cursors;
            graphic.From = run.Start;
            graphic.Count = run.Count;
            graphic.raycastTarget = false;
            return graphic;
        }
    }
}
