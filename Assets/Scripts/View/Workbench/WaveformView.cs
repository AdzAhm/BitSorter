using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The workbench's waveform, given a rect to fill: the time it shows (<see cref="View"/>), which
    /// the wheel over it zooms and moves, and its words -- the rows' names and values now, the values
    /// over the buses' boxes, the glitch marks' words, the time axis's numbers and the playhead's --
    /// over the layers <see cref="WorkbenchLayers"/> draws it in.
    /// </summary>
    /// <remarks>
    /// **Its own piece of the screen** (Part 9b-2a), out of <see cref="WorkbenchPanel"/>, so the
    /// docked layout of Part 11 can give it a place of its own; the panel says where it is and hands
    /// it the moment.
    ///
    /// **The wheel over its time axis zooms in about the cursor and back out to the whole run; with
    /// Shift held, or scrolled sideways, it moves through time** -- the 4.x timing diagram's gestures
    /// (<see cref="WaveView"/> is its rules, under dotnet). While the run plays, a zoomed view pages
    /// on after the playhead, until it is moved away from it; a replay follows it again.
    ///
    /// **Only what the window shows is written.** The words over a bus's boxes are taken from the
    /// stretches the view draws, so a run zoomed in has no more labels than its window has room for,
    /// pooled per row and reused as it moves. The axis's numbers are written into one buffer with
    /// <see cref="TimeText"/>, in the step's own decimals, and only when the window moves.
    /// </remarks>
    internal sealed class WaveformView
    {
        private readonly WorkbenchModel _model;
        private readonly float _width;

        private readonly List<TextMeshProUGUI> _ticks = new List<TextMeshProUGUI>();
        private readonly char[] _tickText = new char[TimeText.MaxLength(TimeText.MaxDecimals, null)];
        private double _ticksStart = double.NaN, _ticksPixelsPerUnit = double.NaN;

        private readonly TextMeshProUGUI[] _readouts;
        private readonly List<TextMeshProUGUI>[] _busLabels;
        private readonly List<TextMeshProUGUI> _glitchLabels = new List<TextMeshProUGUI>();
        private readonly TextMeshProUGUI _playheadLabel;

        private readonly List<WaveSpan> _spans = new List<WaveSpan>(32);
        private readonly List<double> _swallowed = new List<double>(4);
        private readonly Dictionary<string, string> _hexReadouts = new Dictionary<string, string>();

        /// <summary>The waveform's rect, its pivot at its top-left corner, in the stage's units.</summary>
        public RectTransform Rect { get; }

        /// <summary>The time it shows, and how that is laid across its axis.</summary>
        public WaveView View { get; }

        /// <summary>How tall it is: its rows, its numbers and its margin.</summary>
        public float Height { get; }

        /// <summary>The waveform of <paramref name="model"/>'s rows, <paramref name="width"/> wide at <paramref name="at"/> on <paramref name="parent"/> (y down), drawn by <paramref name="layers"/>.</summary>
        public WaveformView(WorkbenchModel model, WorkbenchLayers layers, RectTransform parent, Vector2 at, float width,
            SchematicPalette palette)
        {
            _model = model;
            _width = width;
            int rows = model.Layout.Rows.Count;
            Height = WorkbenchDrawing.WaveHeight(rows);

            Rect = UiTheme.Rect("waveform", parent);
            WorkbenchPanel.TopLeft(Rect, at, new Vector2(width, Height));

            View = new WaveView(Mathf.Max(1f, WorkbenchDrawing.AxisWidth(width)));
            View.Fit(model.End);
            layers.BuildTiming(Rect, new Vector2(width, Height), View);

            _readouts = new TextMeshProUGUI[rows];
            _busLabels = new List<TextMeshProUGUI>[rows];

            for (int row = 0; row < rows; row++)
            {
                float middle = WorkbenchDrawing.RowMiddle(row);
                WorkbenchPanel.StageLabel(Rect, model.Layout.Rows[row].Label, new Vector2(WorkbenchDrawing.NameRight - 120f, middle),
                    UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineRight, 120f);

                _readouts[row] = WorkbenchPanel.StageLabel(Rect, "", new Vector2(WorkbenchDrawing.ReadoutLeft, middle),
                    UiType.Micro, palette.Text, TextAlignmentOptions.MidlineLeft, 60f);

                if (model.RowIsBus(row))
                    _busLabels[row] = new List<TextMeshProUGUI>();
            }

            _playheadLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.WaveTop + 4f), UiType.Micro, palette.Accent,
                TextAlignmentOptions.MidlineLeft, 80f);
        }

        // -----------------------------------------------------------------
        // The wheel
        // -----------------------------------------------------------------

        /// <summary>
        /// Whether a point on the screen is over the time axis -- its rows, its numbers, its margin --
        /// and where, in pixels of the axis from its left edge.
        /// </summary>
        public bool Over(Vector2 screenPoint, out float x)
        {
            x = 0f;
            if (Rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local))
                return false;

            // The pivot is the top-left corner: local x runs right and y up from there.
            x = local.x - WorkbenchDrawing.WaveLeft;
            return x >= 0f && x <= View.Width && local.y <= 0f && -local.y <= Height;
        }

        /// <summary>
        /// A turn of the wheel at a point on the screen: over the time axis, up zooms in about the time
        /// under the cursor and down zooms back out, as far as the whole run; with Shift held, or
        /// scrolled sideways, it moves through time. True when the view changed. Only the sign is read.
        /// </summary>
        public bool TakeWheel(Vector2 screenPoint, Vector2 scroll, bool shift)
        {
            float across = Mathf.Abs(scroll.x), down = Mathf.Abs(scroll.y);
            if ((across < 0.01f && down < 0.01f) || !Over(screenPoint, out float x))
                return false;

            if (across > down)
                return View.Pan(scroll.x > 0f ? 1 : -1);

            // Shift and the wheel down moves later, as a page scrolls on with it.
            if (shift)
                return View.Pan(scroll.y < 0f ? 1 : -1);

            return View.ZoomAbout(x, scroll.y > 0f ? 1 : -1);
        }

        /// <summary>Where a moment of the run is on the screen, over the middle of row <paramref name="row"/>: somewhere the wheel can be turned.</summary>
        public Vector2 ScreenPointOf(double time, int row = 0)
        {
            var local = new Vector3(WorkbenchDrawing.WaveX(time, View), -WorkbenchDrawing.RowMiddle(row), 0f);
            return RectTransformUtility.WorldToScreenPoint(null, Rect.TransformPoint(local));
        }

        // -----------------------------------------------------------------
        // A moment
        // -----------------------------------------------------------------

        /// <summary>Tells the view where the playhead is, so a zoomed view pages on after it. True when the view moved.</summary>
        public bool Follow(double time) => View.Follow(time);

        /// <summary>The run started again from <paramref name="time"/>: the view follows the playhead again, wherever it had been moved.</summary>
        public bool Restart(double time) => View.Restart(time);

        /// <summary>
        /// Writes the moment's words: each row's value now, the values over the buses' boxes up to the
        /// moment, the glitches' words, the playhead's <paramref name="playheadText"/>, and the axis's
        /// numbers when the window has moved.
        /// </summary>
        public void Place(string playheadText)
        {
            SchematicPalette palette = SchematicPalette.Current;

            if (View.Start != _ticksStart || View.PixelsPerUnit != _ticksPixelsPerUnit)
            {
                _ticksStart = View.Start;
                _ticksPixelsPerUnit = View.PixelsPerUnit;
                PlaceTicks(palette);
            }

            int glitches = 0;
            for (int row = 0; row < _readouts.Length; row++)
            {
                float middle = WorkbenchDrawing.RowMiddle(row);
                LogicVec value = _model.RowValue(row, out string text);
                bool bus = _model.RowIsBus(row);

                WorkbenchPanel.SetText(_readouts[row], bus ? HexReadout(text) : text);
                _readouts[row].color = palette.Of(value);

                if (bus)
                    PlaceBusLabels(row, middle, palette);

                _model.RowSwallowed(row, _swallowed);
                foreach (double at in _swallowed)
                {
                    if (!View.Shows(at))
                        continue;

                    if (glitches == _glitchLabels.Count)
                        _glitchLabels.Add(WorkbenchPanel.StageLabel(Rect, "glitch swallowed", Vector2.zero, UiType.Micro, palette.TextDim,
                            TextAlignmentOptions.MidlineLeft, 140f));

                    TextMeshProUGUI label = _glitchLabels[glitches++];
                    WorkbenchPanel.Show(label, true);
                    label.rectTransform.anchoredPosition = new Vector2(WorkbenchDrawing.WaveX(at, View) + 8f, -(middle - 7f));
                }
            }

            for (int i = glitches; i < _glitchLabels.Count; i++)
                WorkbenchPanel.Show(_glitchLabels[i], false);

            bool now = View.Shows(_model.Time);
            WorkbenchPanel.Show(_playheadLabel, now);
            if (now)
            {
                float x = Mathf.Min(WorkbenchDrawing.WaveX(_model.Time, View) + 5f, WorkbenchDrawing.WaveRight(_width) - 52f);
                _playheadLabel.rectTransform.anchoredPosition = new Vector2(x, -(WorkbenchDrawing.WaveTop + 4f));
                WorkbenchPanel.SetText(_playheadLabel, playheadText);
            }
        }

        /// <summary>A bus row's values written over its boxes, up to the moment: a box too narrow for its value has none.</summary>
        private void PlaceBusLabels(int row, float middle, SchematicPalette palette)
        {
            List<TextMeshProUGUI> labels = _busLabels[row];
            SignalTrack track = _model.RowTrack(row);
            int used = 0;

            WorkbenchDrawing.RowSpans(_model, View, row, View.Start, Math.Min(View.End, _model.Time), _spans);
            foreach (WaveSpan span in _spans)
            {
                if (span.Busy)
                    continue;

                LogicVec value = track[span.First].Value;
                if (value.IsAllZ)
                    continue;

                string text = track.Text(span.First);
                float x0 = WorkbenchDrawing.WaveX(span.From, View), x1 = WorkbenchDrawing.WaveX(span.To, View);
                if (x1 - x0 <= 22f + 7f * (text.Length - 1))
                    continue;

                if (used == labels.Count)
                    labels.Add(WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, middle), UiType.Micro, palette.Text,
                        TextAlignmentOptions.Center, 60f));

                TextMeshProUGUI label = labels[used++];
                WorkbenchPanel.Show(label, true);
                WorkbenchPanel.SetText(label, text);
                label.color = value.IsKnown ? palette.Text : palette.Unknown;
                label.rectTransform.anchoredPosition = new Vector2((x0 + x1) * 0.5f, -middle);
            }

            for (int i = used; i < labels.Count; i++)
                WorkbenchPanel.Show(labels[i], false);
        }

        /// <summary>The time axis's numbers, one over each line of the grid (<see cref="WorkbenchDrawing.Grid"/>).</summary>
        private void PlaceTicks(SchematicPalette palette)
        {
            double step = View.TickStep(WorkbenchDrawing.TickPixels);
            int decimals = TimeText.Decimals(step);
            (long first, long last) = View.TicksInView(step);
            int used = 0;

            for (long k = first; k <= last; k++)
            {
                double t = k * step;
                if (t > _model.End + 1e-9)
                    break;

                if (used == _ticks.Count)
                    _ticks.Add(WorkbenchPanel.StageLabel(Rect, "", Vector2.zero, UiType.Micro, palette.Muted, TextAlignmentOptions.Center, 48f));

                TextMeshProUGUI label = _ticks[used++];
                WorkbenchPanel.Show(label, true);
                label.SetCharArray(_tickText, 0, TimeText.Write(t, decimals, null, _tickText));
                label.rectTransform.anchoredPosition = new Vector2(WorkbenchDrawing.WaveX(t, View), -(WorkbenchDrawing.WaveTop - 14f));
            }

            for (int i = used; i < _ticks.Count; i++)
                WorkbenchPanel.Show(_ticks[i], false);
        }

        /// <summary>The axis's numbers shown now, left to right: what a test reads.</summary>
        public IEnumerable<string> TickTexts
        {
            get
            {
                foreach (TextMeshProUGUI tick in _ticks)
                    if (tick.gameObject.activeSelf)
                        yield return tick.text;
            }
        }

        /// <summary>
        /// A bus's readout: its hex digits marked as hex, "3h", as ModelSim marks them, and a value
        /// with no digit to read -- x or z throughout, or one digit partly either -- left as it is.
        /// Made once per value.
        /// </summary>
        private string HexReadout(string digits)
        {
            if (!_hexReadouts.TryGetValue(digits, out string readout))
                _hexReadouts[digits] = readout = HasHexDigit(digits) ? digits + "h" : digits;

            return readout;
        }

        private static bool HasHexDigit(string text)
        {
            foreach (char c in text)
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))
                    return true;

            return false;
        }
    }
}
