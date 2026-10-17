using System;
using System.Collections.Generic;
using BitSorter.SystemVerilog;
using BitSorter.ViewModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    ///
    /// **Rows are what the panel's list says** (Part 9b-2b): their names come in with them, each row is
    /// written in its own radix (<see cref="WorkbenchModel.RowRadix"/>), and a band behind a row says
    /// it is selected, or, fainter, that the pointer is on its wire. The second cursor's time is
    /// written at its top, as the playhead's is, and the time between them over the bracket under the
    /// rows (<see cref="WorkbenchDrawing.Playhead"/>).
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
        private readonly TextMeshProUGUI _playheadLabel, _secondLabel, _spanLabel;
        private readonly Image[] _bands;

        private readonly List<WaveSpan> _spans = new List<WaveSpan>(32);
        private readonly List<double> _swallowed = new List<double>(4);

        // Each row's readout as last written, and the text it was made from: a new string only when the value changes.
        private readonly string[] _readoutOf, _readout;
        private readonly char[] _timeText = new char[TimeText.MaxLength(1, "ns")];

        /// <summary>The waveform's rect, its pivot at its top-left corner, in the stage's units.</summary>
        public RectTransform Rect { get; }

        /// <summary>The time it shows, and how that is laid across its axis.</summary>
        public WaveView View { get; }

        /// <summary>How tall it is: its rows, its numbers and its margin.</summary>
        public float Height { get; }

        /// <summary>
        /// The waveform of <paramref name="model"/>'s rows, named <paramref name="names"/>,
        /// <paramref name="width"/> wide at <paramref name="at"/> on <paramref name="parent"/> (y down),
        /// drawn by <paramref name="layers"/>; showing the time <paramref name="view"/> does, or the whole run.
        /// </summary>
        public WaveformView(WorkbenchModel model, WorkbenchLayers layers, RectTransform parent, Vector2 at, float width,
            SchematicPalette palette, IReadOnlyList<string> names, WaveView view = null)
        {
            _model = model;
            _width = width;
            int rows = model.RowCount;
            Height = WorkbenchDrawing.WaveHeight(rows);

            Rect = UiTheme.Rect("waveform", parent);
            WorkbenchPanel.TopLeft(Rect, at, new Vector2(width, Height));

            if (view == null || view.Width != Mathf.Max(1f, WorkbenchDrawing.AxisWidth(width)))
            {
                view = new WaveView(Mathf.Max(1f, WorkbenchDrawing.AxisWidth(width)));
                view.Fit(model.End);
            }

            View = view;

            // Behind everything the waveform draws, a band a row: shown for a selected row.
            _bands = new Image[rows];
            for (int row = 0; row < rows; row++)
            {
                Image band = UiTheme.Panel_("band", Rect, palette.Selection);
                band.sprite = null;
                band.raycastTarget = false;
                WorkbenchPanel.TopLeft(band.rectTransform, new Vector2(0f, WorkbenchDrawing.WaveTop + row * WorkbenchDrawing.RowHeight),
                    new Vector2(width, WorkbenchDrawing.RowHeight));
                WorkbenchPanel.Show(band, false);
                _bands[row] = band;
            }

            layers.BuildTiming(Rect, new Vector2(width, Height), View);

            _readouts = new TextMeshProUGUI[rows];
            _busLabels = new List<TextMeshProUGUI>[rows];
            _readoutOf = new string[rows];
            _readout = new string[rows];

            for (int row = 0; row < rows; row++)
            {
                float middle = WorkbenchDrawing.RowMiddle(row);
                WorkbenchPanel.StageLabel(Rect, row < names.Count ? names[row] : "", new Vector2(WorkbenchDrawing.NameRight - 120f, middle),
                    UiType.Micro, palette.TextDim, TextAlignmentOptions.MidlineRight, 120f);

                _readouts[row] = WorkbenchPanel.StageLabel(Rect, "", new Vector2(WorkbenchDrawing.ReadoutLeft, middle),
                    UiType.Micro, palette.Text, TextAlignmentOptions.MidlineLeft, 60f);

                if (model.RowIsBus(row))
                    _busLabels[row] = new List<TextMeshProUGUI>();
            }

            _playheadLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.WaveTop + 4f), UiType.Micro, palette.Accent,
                TextAlignmentOptions.MidlineLeft, 80f);
            _secondLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.WaveTop + 4f), UiType.Micro, palette.Cursor,
                TextAlignmentOptions.MidlineLeft, 80f);
            _spanLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.BracketY(rows) + 9f), UiType.Micro, palette.Cursor,
                TextAlignmentOptions.Center, 90f);
            WorkbenchPanel.Show(_secondLabel, false);
            WorkbenchPanel.Show(_spanLabel, false);
        }

        /// <summary>How many rows it shows.</summary>
        public int Rows => _readouts.Length;

        /// <summary>What row <paramref name="row"/>'s readout says now: what a test reads.</summary>
        public string ReadoutOf(int row) => _readouts[row].text;

        /// <summary>What the second cursor's time and the span to it say, null while there is none.</summary>
        public string SecondText => _secondLabel.gameObject.activeSelf ? _secondLabel.text : null;

        /// <inheritdoc cref="SecondText"/>
        public string SpanText => _spanLabel.gameObject.activeSelf ? _spanLabel.text : null;

        /// <summary>Whether row <paramref name="row"/>'s band is up: selected, or its wire pointed at.</summary>
        public bool BandShown(int row) => _bands[row].gameObject.activeSelf;

        /// <summary>
        /// Puts up the band behind every row in <paramref name="selected"/>, and a fainter one behind
        /// every row showing <paramref name="litNet"/>'s wire (-1 for none).
        /// </summary>
        public void SetBands(IReadOnlyList<bool> selected, int litNet)
        {
            SchematicPalette palette = SchematicPalette.Current;
            Color faint = Color.Lerp(palette.Ground, palette.Selection, 0.5f);
            for (int row = 0; row < _bands.Length; row++)
            {
                bool chosen = row < selected.Count && selected[row];
                bool lit = !chosen && litNet >= 0 && _model.RowShowsNet(row, litNet);
                WorkbenchPanel.Show(_bands[row], chosen || lit);
                if (chosen || lit)
                    _bands[row].color = chosen ? palette.Selection : faint;
            }
        }

        // -----------------------------------------------------------------
        // Where the pointer is
        // -----------------------------------------------------------------

        /// <summary>
        /// Which part of the waveform a point on the screen is over -- a row's name, its value, the
        /// waves or the axis's numbers -- the row under it (-1 for none), and the moment there; false
        /// when it is over none of them.
        /// </summary>
        public bool RegionAt(Vector2 screenPoint, out WaveRegion region, out int row, out double time)
        {
            region = WaveRegion.None;
            row = -1;
            time = 0;
            if (Rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local))
                return false;

            float x = local.x, y = -local.y;
            if (x < 0f || x > _width || y < WorkbenchDrawing.WaveTop - 20f || y > Height)
                return false;

            float rowsTop = WorkbenchDrawing.WaveTop, rowsBottom = WorkbenchDrawing.WaveTop + Rows * WorkbenchDrawing.RowHeight;
            if (y >= rowsTop && y < rowsBottom)
                row = (int)((y - rowsTop) / WorkbenchDrawing.RowHeight);

            time = View.TimeAt(x - WorkbenchDrawing.WaveLeft);
            if (x >= WorkbenchDrawing.WaveLeft)
                region = y < rowsTop ? WaveRegion.Axis : WaveRegion.Waves;
            else
                region = x < WorkbenchDrawing.ReadoutLeft - 4f ? WaveRegion.Names : WaveRegion.Values;
            return true;
        }

        /// <summary>The moment under a point on the screen, wherever it is up and down, held to the axis's ends: what a drag scrubs to off the rows.</summary>
        public double TimeAtScreen(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local);
            float x = Mathf.Clamp(local.x - WorkbenchDrawing.WaveLeft, 0f, View.Width);
            return View.TimeAt(x);
        }

        /// <summary>How far a moment is drawn from <paramref name="time"/>, in pixels of the axis.</summary>
        public float PixelsBetween(double time, double other) => (float)System.Math.Abs((time - other) * View.PixelsPerUnit);

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
        /// moment, the glitches' words, the playhead's <paramref name="playheadText"/>, the second
        /// cursor's time and the span to it, and the axis's numbers when the window has moved.
        /// </summary>
        public void Place(string playheadText, WaveCursors cursors = null)
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
                if (_model.RowSignal(row) < 0)
                {
                    WorkbenchPanel.SetText(_readouts[row], "");
                    continue;
                }

                LogicVec value = _model.RowValue(row, out string text);
                bool bus = _model.RowIsBus(row);

                if (!ReferenceEquals(text, _readoutOf[row]))
                {
                    _readoutOf[row] = text;
                    _readout[row] = Readout(text, _model.RowRadix(row), bus);
                }

                WorkbenchPanel.SetText(_readouts[row], _readout[row]);
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

            PlaceCursor(cursors);
        }

        /// <summary>The second cursor's time over its line, and the time between the cursors over the bracket.</summary>
        private void PlaceCursor(WaveCursors cursors)
        {
            double? second = cursors?.Second;
            bool shown = second.HasValue && View.Shows(second.Value);
            WorkbenchPanel.Show(_secondLabel, shown);
            if (shown)
            {
                float x = Mathf.Min(WorkbenchDrawing.WaveX(second.Value, View) + 5f, WorkbenchDrawing.WaveRight(_width) - 52f);
                _secondLabel.rectTransform.anchoredPosition = new Vector2(x, -(WorkbenchDrawing.WaveTop + 4f));
                WriteTime(_secondLabel, second.Value);
            }

            double? delta = cursors?.Delta(_model.Time);
            bool span = delta.HasValue && delta.Value > 0;
            float left = WorkbenchDrawing.WaveLeft, right = WorkbenchDrawing.WaveLeft + View.Width;
            float a = 0f, b = 0f;
            if (span)
            {
                a = Mathf.Clamp(WorkbenchDrawing.WaveX(System.Math.Min(second.Value, _model.Time), View), left, right);
                b = Mathf.Clamp(WorkbenchDrawing.WaveX(System.Math.Max(second.Value, _model.Time), View), left, right);
                span = b - a >= 1f;
            }

            WorkbenchPanel.Show(_spanLabel, span);
            if (span)
            {
                _spanLabel.rectTransform.anchoredPosition = new Vector2((a + b) * 0.5f, -(WorkbenchDrawing.BracketY(Rows) + 9f));
                WriteTime(_spanLabel, delta.Value);
            }
        }

        /// <summary>A time in whole units, or to a tenth when it is not whole, and its unit: <c>19 ns</c>, <c>5.5 ns</c>.</summary>
        private void WriteTime(TextMeshProUGUI label, double time)
        {
            int decimals = System.Math.Abs(time - System.Math.Round(time)) < 1e-9 ? 0 : 1;
            label.SetCharArray(_timeText, 0, TimeText.Write(time, decimals, "ns", _timeText));
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

                string text = _model.RowText(row, span.First);
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
        /// A row's readout: a bus's digits marked with their radix -- <c>3h</c>, <c>1010b</c>, <c>17o</c>,
        /// as ModelSim marks them -- and a value with no digit to read, x or z throughout, or one digit
        /// partly either, left as it is, as are decimals and an enum's names. Made when the value changes.
        /// </summary>
        private static string Readout(string text, WaveRadix radix, bool bus)
        {
            if (!bus || !HasHexDigit(text))
                return text;
            switch (radix)
            {
                case WaveRadix.Hex: return text + "h";
                case WaveRadix.Octal: return text + "o";
                case WaveRadix.Binary: return text + "b";
                default: return text;
            }
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
