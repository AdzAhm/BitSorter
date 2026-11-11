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
    ///
    /// **A group's header is a row of its own**, its name after a <c>-</c> while it is open and a
    /// <c>+</c> while it is shut, with no wave. **The names column is as wide as its longest name**,
    /// from the 58 units the reference's names take up to 260, and a longer name is cut from its start,
    /// where a full path repeats what every row shares: the waves start further right by as much.
    ///
    /// **A bus's value reads without zooming in** (5.1, Ahmad: a 64-bit value read only zoomed far in): a box too narrow
    /// for the whole value shows as much of it as fits and an ellipsis (<see cref="ValueFit.Kept"/>), the pointer on a box
    /// shows a tip with the whole of it (<see cref="ShowTip"/>), and VALUES + at the head of the values column -- or W --
    /// widens it to the widest value any row can hold (<see cref="WholeValues"/>), the waves starting further right by as
    /// much, as they do for the names.
    ///
    /// **Its buttons are the dock's** (Part 11b): + SIGNAL, GROUP, NAMES, UNIT and EXPORT stood over it until the
    /// shell's dock took them into its header. **Its rows scroll in a window that clips them** (the dock's waves), so
    /// a point outside that window is over none of it, whatever is drawn there out of sight.
    /// </remarks>
    public sealed class WaveformView
    {
        private readonly WorkbenchModel _model;
        private readonly float _width;

        private readonly List<TextMeshProUGUI> _ticks = new List<TextMeshProUGUI>();
        private readonly char[] _tickText = new char[TimeText.MaxLength(TimeText.MaxDecimals, null)];
        private double _ticksStart = double.NaN, _ticksPixelsPerUnit = double.NaN;

        private readonly TextMeshProUGUI[] _readouts;
        private readonly List<TextMeshProUGUI>[] _busLabels;

        // The colour each row's readout is written in before it is made legible on what is behind it, the ground or the row's
        // band (SchematicPalette.Legible): kept, so a band put up or taken down writes it again without the value.
        private readonly Color[] _readoutInk;

        // What each readout's colour was last worked out from -- its value's colour and what was under it -- so that a frame
        // changing neither works nothing out: Place runs every frame, and Legible is a WCAG contrast at least.
        private readonly Color[] _inkWorkedOut, _underWorkedOut;
        private readonly List<TextMeshProUGUI> _glitchLabels = new List<TextMeshProUGUI>();
        private readonly TextMeshProUGUI _playheadLabel, _secondLabel, _spanLabel;
        private readonly Image[] _bands, _marks;

        /// <summary>How wide a selected row's mark is, at its band's left end.</summary>
        public const float SelectedMark = 3f;

        private readonly List<WaveSpan> _spans = new List<WaveSpan>(32);
        private readonly List<double> _swallowed = new List<double>(4);

        /// <summary>The room a "glitch swallowed" takes along its row, in pixels: two are never closer.</summary>
        private const float GlitchLabelWidth = 140f;

        // Each row's readout as last written, and the text it was made from: a new string only when the value changes.
        private readonly string[] _readoutOf, _readout;
        // Every unit is two letters at most.
        private readonly char[] _timeText = new char[TimeText.MaxLength(1, "ms")];

        // The moment's words the playhead's label was last handed (MomentWords.Version): handed again only when they change.
        private int _playheadVersion = -1;

        /// <summary>The waveform's rect, its pivot at its top-left corner, in the stage's units.</summary>
        public RectTransform Rect { get; }

        /// <summary>The time it shows, and how that is laid across its axis.</summary>
        public WaveView View { get; }

        /// <summary>How tall it is: its rows, its numbers and its margin.</summary>
        public float Height { get; }

        /// <summary>The most room the names column may take.</summary>
        public const float MostNamesWidth = 260f;

        /// <summary>The waveform's buttons' names, in the dock's header (Part 11b), which the tests look for.</summary>
        public const string AddButton = "Workbench add signal", GroupButton = "Workbench group", NamesButton = "Workbench names",
            UnitButton = "Workbench unit", ExportButton = "Workbench export";

        /// <summary>The window it is seen through, outside which nothing of it is pointed at; null for none.</summary>
        private readonly RectTransform _clip;

        /// <summary>How much wider than the reference's the names column is: how far right the waves start.</summary>
        public float NamesExtra { get; }

        /// <summary>How wide the values column is in the reference's layout, from <see cref="WorkbenchDrawing.ReadoutLeft"/>: each value cut to it.</summary>
        public const float ValuesWidth = 60f;

        /// <summary>The most room the values column takes with every value whole: a 64-bit value in binary, and a little more.</summary>
        public const float MostValuesWidth = 480f;

        /// <summary>Whether the values column shows every value whole (5.1), as wide as the widest a row can hold.</summary>
        public bool WholeValues { get; }

        /// <summary>How much wider than <see cref="ValuesWidth"/> the values column is: how much further right the waves start.</summary>
        public float ValuesExtra { get; }

        /// <summary>The left end of the names column, in the waveform's units: as far left of its rect as the two columns are widened.</summary>
        private float ColumnsLeft => -(NamesExtra + ValuesExtra);

        /// <summary>The words over the values column -- VALUES + or VALUES - -- that widen it and cut it again.</summary>
        private readonly TextMeshProUGUI _valuesHead;

        /// <summary>What the values column's head says while it cuts every value, and while it shows them whole.</summary>
        public const string ValuesCutCaption = "VALUES +", ValuesWholeCaption = "VALUES -";

        /// <summary>What the values column's head says now: what a test reads.</summary>
        public string ValuesHeadText => _valuesHead.text;

        /// <summary>What row <paramref name="row"/>'s name says, as drawn.</summary>
        public string NameOf(int row) => _names[row].text;

        private readonly TextMeshProUGUI[] _names;

        /// <summary>
        /// The waveform of <paramref name="model"/>'s rows, named <paramref name="names"/>,
        /// <paramref name="width"/> wide at <paramref name="at"/> on <paramref name="parent"/> (y down),
        /// drawn by <paramref name="layers"/>; showing the time <paramref name="view"/> does, or the whole run;
        /// seen through <paramref name="clip"/>, when it is.
        /// </summary>
        public WaveformView(WorkbenchModel model, WorkbenchLayers layers, RectTransform parent, Vector2 at, float width,
            SchematicPalette palette, IReadOnlyList<string> names, WaveView view = null, IReadOnlyList<bool> headers = null,
            RectTransform clip = null, bool wholeValues = false)
        {
            _model = model;
            _clip = clip;
            int rows = model.RowCount;
            Height = WorkbenchDrawing.WaveHeight(rows);

            // The names column as wide as its longest name, within reason: the waves move right by as much.
            var shown = new string[rows];
            float longest = 0f;
            for (int row = 0; row < rows; row++)
            {
                shown[row] = FitName(row < names.Count ? names[row] : "", MostNamesWidth - 8f);
                longest = Mathf.Max(longest, UiTheme.TextWidth(shown[row], UiType.Micro));
            }

            NamesExtra = Mathf.Clamp(longest + 8f, WorkbenchDrawing.NameRight, MostNamesWidth) - WorkbenchDrawing.NameRight;
            width -= NamesExtra;

            // With every value whole, the values column as wide as the widest value any row can hold in its radix, its mark
            // included -- worked out, never measured off the run, so it never moves while the run plays (ValueText.MaxChars).
            WholeValues = wholeValues;
            float values = ValuesWidth;
            if (wholeValues)
                for (int row = 0; row < rows; row++)
                {
                    int most = model.RowMostChars(row);
                    if (most <= 0)
                        continue;
                    if (model.RowIsBus(row) && model.RowRadix(row) <= WaveRadix.Hex)
                        most++;   // its b, o or h
                    values = Mathf.Max(values, UiTheme.TextWidth(new string('8', most), UiType.Micro) + 8f);
                }

            ValuesExtra = Mathf.Clamp(Mathf.Min(values, MostValuesWidth, Mathf.Max(ValuesWidth, width * 0.45f)), ValuesWidth, MostValuesWidth)
                - ValuesWidth;
            width -= ValuesExtra;
            _width = width;

            Rect = UiTheme.Rect("waveform", parent);
            WorkbenchPanel.TopLeft(Rect, at + new Vector2(NamesExtra + ValuesExtra, 0f), new Vector2(width, Height));

            if (view == null || view.Width != Mathf.Max(1f, WorkbenchDrawing.AxisWidth(width)))
            {
                view = new WaveView(Mathf.Max(1f, WorkbenchDrawing.AxisWidth(width)));
                view.Fit(model.End);
            }

            View = view;

            // Behind everything the waveform draws, a band a row, the names' column included: shown for a selected row. And a
            // selected row's mark at its left end, in the accent: the band stands a little off the ground and no more -- 1.14:1
            // on paper -- so the mark is what shows a row selected, a shape as well as a colour (the 16d sweep).
            _bands = new Image[rows];
            _marks = new Image[rows];
            for (int row = 0; row < rows; row++)
            {
                var top = new Vector2(ColumnsLeft, WorkbenchDrawing.WaveTop + row * WorkbenchDrawing.RowHeight);
                Image band = UiTheme.Panel_("band", Rect, palette.Selection);
                band.sprite = null;
                band.raycastTarget = false;
                WorkbenchPanel.TopLeft(band.rectTransform, top, new Vector2(width - ColumnsLeft, WorkbenchDrawing.RowHeight));
                WorkbenchPanel.Show(band, false);
                _bands[row] = band;

                Image mark = UiTheme.Panel_("selected mark", Rect, palette.Accent);
                mark.sprite = null;
                mark.raycastTarget = false;
                WorkbenchPanel.TopLeft(mark.rectTransform, top, new Vector2(SelectedMark, WorkbenchDrawing.RowHeight));
                WorkbenchPanel.Show(mark, false);
                _marks[row] = mark;
            }

            layers.BuildTiming(Rect, new Vector2(width, Height), View);

            _readouts = new TextMeshProUGUI[rows];
            _busLabels = new List<TextMeshProUGUI>[rows];
            _busLabelOf = new List<string>[rows];
            _busLabelKept = new List<int>[rows];
            _readoutInk = new Color[rows];
            _inkWorkedOut = new Color[rows];     // clear, which no ground nor band is: every row is worked out the first time
            _underWorkedOut = new Color[rows];
            _readoutOf = new string[rows];
            _readout = new string[rows];
            _names = new TextMeshProUGUI[rows];

            float namesWidth = WorkbenchDrawing.NameRight + NamesExtra;
            for (int row = 0; row < rows; row++)
            {
                float middle = WorkbenchDrawing.RowMiddle(row);
                bool header = headers != null && row < headers.Count && headers[row];
                _names[row] = WorkbenchPanel.StageLabel(Rect, shown[row], new Vector2(WorkbenchDrawing.NameRight - ValuesExtra - namesWidth, middle),
                    UiType.Micro, header ? palette.Text : palette.TextDim, TextAlignmentOptions.MidlineRight, namesWidth);
                if (header)
                    _names[row].fontStyle = FontStyles.Bold;

                _readouts[row] = WorkbenchPanel.StageLabel(Rect, "", new Vector2(WorkbenchDrawing.ReadoutLeft - ValuesExtra, middle),
                    UiType.Micro, palette.Text, TextAlignmentOptions.MidlineLeft, ValuesWidth + ValuesExtra);
                _readoutInk[row] = palette.Text;

                // Cut to its room, never over the waves (Part 15g): a 64-bit value, FFFFFFFFFFFFFFFFh, ran on across them
                // (Ahmad's ALU, 2026-10-28). The whole value is the cursor's and the bus's own labels'.
                _readouts[row].textWrappingMode = TextWrappingModes.NoWrap;
                _readouts[row].overflowMode = TextOverflowModes.Ellipsis;

                if (model.RowIsBus(row))
                {
                    _busLabels[row] = new List<TextMeshProUGUI>();
                    _busLabelOf[row] = new List<string>();
                    _busLabelKept[row] = new List<int>();
                }
            }

            _playheadLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.WaveTop + 4f), UiType.Micro, palette.Accent,
                TextAlignmentOptions.MidlineLeft, 80f);
            _secondLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.WaveTop + 4f), UiType.Micro, palette.Cursor,
                TextAlignmentOptions.MidlineLeft, 80f);
            _spanLabel = WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, WorkbenchDrawing.BracketY(rows) + 9f), UiType.Micro, palette.Cursor,
                TextAlignmentOptions.Center, 90f);
            WorkbenchPanel.Show(_secondLabel, false);
            WorkbenchPanel.Show(_spanLabel, false);

            // Over the values column, the words that widen it to every value whole and cut it again (5.1).
            _valuesHead = WorkbenchPanel.StageLabel(Rect, wholeValues ? ValuesWholeCaption : ValuesCutCaption,
                new Vector2(WorkbenchDrawing.ReadoutLeft - ValuesExtra, WorkbenchDrawing.WaveTop - 14f), UiType.Micro, palette.TextDim,
                TextAlignmentOptions.MidlineLeft, ValuesWidth + ValuesExtra);

            // Where a row dragged by its name would go (5.1): a line across the names and the waves, over everything else.
            _drop = UiTheme.Panel_("drop line", Rect, palette.Accent);
            _drop.sprite = null;
            _drop.raycastTarget = false;
            WorkbenchPanel.Show(_drop, false);

            // The whole of a bus's value where the pointer is (5.1): over everything, the drop line included.
            _tip = WorkbenchPanel.CanvasChip("value tip", Rect, palette);
            _tip.raycastTarget = false;
            _tipText = UiTheme.Label("text", _tip.transform, UiType.Micro, palette.Text, TextAlignmentOptions.TopLeft);
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            UiTheme.Stretch(_tipText.rectTransform, TipPadding);
            WorkbenchPanel.Show(_tip, false);

            // An ellipsis in one character where the face has it, as a viewer writes one; three dots where it has not.
            _ellipsis = _readouts.Length > 0 && _readouts[0].font != null && _readouts[0].font.HasCharacter('\u2026', false, false)
                ? "\u2026" : "...";
        }

        // -----------------------------------------------------------------
        // A row dragged by its name (5.1)
        // -----------------------------------------------------------------

        /// <summary>How thick the line is that shows where a dragged row would go.</summary>
        public const float DropThickness = 2f;

        private readonly Image _drop;
        private int _dropGap = -1;

        /// <summary>The gap the drop line stands in, or -1 while there is none: what a test reads.</summary>
        public int DropShown => _dropGap;

        /// <summary>Puts the line that shows where a dragged row would go in the gap <paramref name="gap"/> -- 0 over the first row -- or takes it down for -1.</summary>
        public void ShowDrop(int gap)
        {
            if (gap > Rows)
                gap = -1;
            if (gap == _dropGap)
                return;
            _dropGap = gap;
            WorkbenchPanel.Show(_drop, gap >= 0);
            if (gap >= 0)
                WorkbenchPanel.TopLeft(_drop.rectTransform,
                    new Vector2(ColumnsLeft, WorkbenchDrawing.WaveTop + gap * WorkbenchDrawing.RowHeight - DropThickness / 2f),
                    new Vector2(_width - ColumnsLeft, DropThickness));
        }

        /// <summary>
        /// The gap between rows nearest a point on the screen, at any height -- 0 over the first row, <see cref="Rows"/> under
        /// the last -- where a row dragged there would go before <c>WaveList.DropGap</c> keeps it to its level.
        /// </summary>
        public int GapAt(Vector2 screenPoint)
        {
            if (Rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local))
                return -1;
            int gap = Mathf.RoundToInt((-local.y - WorkbenchDrawing.WaveTop) / WorkbenchDrawing.RowHeight);
            return Mathf.Clamp(gap, 0, Rows);
        }

        /// <summary>How near the window's top or foot, in pixels on the screen, a dragged row scrolls the rows.</summary>
        public const float ScrollEdgePixels = 16f;

        private static readonly Vector3[] ClipCorners = new Vector3[4];

        /// <summary>
        /// Which way a row dragged to a point on the screen scrolls the rows: -1 at the window's top or above it, 1 at its foot
        /// or below it, within <see cref="ScrollEdgePixels"/>, and 0 between.
        /// </summary>
        public int EdgeAt(Vector2 screenPoint)
        {
            if (_clip == null)
                return 0;
            _clip.GetWorldCorners(ClipCorners);   // the overlay's world is the screen's pixels
            if (screenPoint.y >= ClipCorners[1].y - ScrollEdgePixels)
                return -1;
            if (screenPoint.y <= ClipCorners[0].y + ScrollEdgePixels)
                return 1;
            return 0;
        }

        /// <summary>How many rows it shows.</summary>
        public int Rows => _readouts.Length;

        /// <summary>A name cut from its start, behind "...", until it is no wider than <paramref name="most"/> units (<see cref="TextRows.Tail"/>).</summary>
        private static string FitName(string name, float most) => TextRows.Tail(name, most, MicroWidth);

        /// <summary>How wide a row's name is drawn, as <see cref="TextRows.Tail"/> asks it.</summary>
        private static readonly Func<string, float> MicroWidth = text => UiTheme.TextWidth(text, UiType.Micro);

        /// <summary>What row <paramref name="row"/>'s readout says now: what a test reads.</summary>
        public string ReadoutOf(int row) => _readouts[row].text;

        /// <summary>Whether row <paramref name="row"/>'s readout fits its column whole, not cut by an ellipsis: what a test reads.</summary>
        public bool ReadoutWhole(int row) => _readouts[row].preferredWidth <= _readouts[row].rectTransform.rect.width + 0.5f;

        /// <summary>The colour row <paramref name="row"/>'s readout is written in now: what a test measures.</summary>
        public Color ReadoutColourOf(int row) => _readouts[row].color;

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
                WorkbenchPanel.Show(_marks[row], chosen);
                if (chosen)
                    _marks[row].color = palette.Accent;
                WriteLegibly(row, palette);
            }
        }

        /// <summary>What the words of row <paramref name="row"/> are written on: its band while it is up, else the ground.</summary>
        private Color Under(int row, SchematicPalette palette) =>
            _bands[row].gameObject.activeSelf ? _bands[row].color : palette.Ground;

        /// <summary>
        /// Row <paramref name="row"/>'s readout in its value's colour, made legible on what is under it
        /// (<see cref="SchematicPalette.Legible"/>): a word at 4.5:1 on the ground or a row's band, where the colour alone is
        /// held to 3:1 (the accessibility review, 2026-11-03).
        /// </summary>
        /// <remarks>
        /// Worked out again only when the value's colour or what is under it has changed since the last time, which is
        /// rarely: a frame that changes neither compares two colours a row.
        ///
        /// A bus's labels are not lifted here: a known value's is the text's colour, which reads on either, and an x's is made
        /// legible over its hatched box, on the ground or a band, once for the palette (<see cref="BusLabelColour"/>).
        /// </remarks>
        private void WriteLegibly(int row, SchematicPalette palette)
        {
            Color ink = _readoutInk[row], under = Under(row, palette);
            if (ink == _inkWorkedOut[row] && under == _underWorkedOut[row])
                return;

            _inkWorkedOut[row] = ink;
            _underWorkedOut[row] = under;
            _readouts[row].color = palette.Legible(ink, under);
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
            if (Rect == null || !Seen(screenPoint) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local))
                return false;

            float x = local.x, y = -local.y;
            if (x < ColumnsLeft || x > _width || y < WorkbenchDrawing.WaveTop - 20f || y > Height)
                return false;

            float rowsTop = WorkbenchDrawing.WaveTop, rowsBottom = WorkbenchDrawing.WaveTop + Rows * WorkbenchDrawing.RowHeight;
            if (y >= rowsTop && y < rowsBottom)
                row = (int)((y - rowsTop) / WorkbenchDrawing.RowHeight);

            time = View.TimeAt(x - WorkbenchDrawing.WaveLeft);
            float valuesLeft = WorkbenchDrawing.ReadoutLeft - ValuesExtra - 4f;
            if (x >= WorkbenchDrawing.WaveLeft)
                region = y < rowsTop ? WaveRegion.Axis : WaveRegion.Waves;
            else if (y < rowsTop && x >= valuesLeft)
                region = WaveRegion.ValuesHead;   // VALUES + or VALUES -, over the values column (5.1)
            else
                region = x < valuesLeft ? WaveRegion.Names : WaveRegion.Values;
            return true;
        }

        /// <summary>Whether a point on the screen is inside the window the waveform is seen through.</summary>
        private bool Seen(Vector2 screenPoint) => _clip == null || RectTransformUtility.RectangleContainsScreenPoint(_clip, screenPoint, null);

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
            if (Rect == null || !Seen(screenPoint) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local))
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
        /// moment, the glitches' words, the playhead's from <paramref name="moment"/>, the second
        /// cursor's time and the span to it, and the axis's numbers when the window has moved.
        /// </summary>
        public void Place(MomentWords moment, WaveCursors cursors = null)
        {
            SchematicPalette palette = SchematicPalette.Current;

            // The values column reads at the cursor while there is one, and at the playhead otherwise (Part 15h): a click
            // places the cursor, and Ahmad asked that the values be the ones it moves through (2026-10-28).
            double reading = cursors?.Second ?? _model.Time;

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

                LogicVec value = _model.RowValueAt(row, reading, out string text);
                bool bus = _model.RowIsBus(row);

                if (!ReferenceEquals(text, _readoutOf[row]))
                {
                    _readoutOf[row] = text;
                    _readout[row] = Readout(text, _model.RowRadix(row), bus);
                }

                WorkbenchPanel.SetText(_readouts[row], _readout[row]);
                _readoutInk[row] = palette.Of(value);

                if (bus)
                    PlaceBusLabels(row, middle, palette);
                WriteLegibly(row, palette);

                // Named where its words have room, the first of any closer, as its crosses are drawn a cross apart. Every one in view
                // was named, a label each, kept and moved every frame -- thousands for a fast clock into a slow gate (Part 16d).
                _model.RowSwallowedApart(row, View.Start, View.End, GlitchLabelWidth / View.PixelsPerUnit, _swallowed);
                foreach (double at in _swallowed)
                {
                    if (glitches == _glitchLabels.Count)
                        _glitchLabels.Add(WorkbenchPanel.StageLabel(Rect, "glitch swallowed", Vector2.zero, UiType.Micro, palette.TextDim,
                            TextAlignmentOptions.MidlineLeft, GlitchLabelWidth));

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
                float x = WaveCursorLabels.Alone(WorkbenchDrawing.WaveX(_model.Time, View), WorkbenchDrawing.WaveRight(_width));
                _playheadLabel.rectTransform.anchoredPosition = new Vector2(x, -(WorkbenchDrawing.WaveTop + 4f));
                // Characters, never a string: a frame of playback reaching another tenth made one for this label (the review
                // of Part 16d's sweep). Handed to TextMeshPro only when they change, as SetText hands a string.
                if (moment.Version != _playheadVersion)
                {
                    _playheadVersion = moment.Version;
                    _playheadLabel.SetCharArray(moment.Characters, moment.PlayheadStart, moment.Moment == null ? 0 : moment.PlayheadLength);
                }
            }

            PlaceCursor(cursors, now);
        }

        /// <summary>
        /// The second cursor's time over its line, and the time between the cursors over the bracket. Near the playhead's, the
        /// two are measured and written apart (<see cref="WaveCursorLabels.Apart"/>): a unit apart, or both held at the waves'
        /// right end, they were written over each other (the 16d sweep).
        /// </summary>
        private void PlaceCursor(WaveCursors cursors, bool playheadShown)
        {
            double? second = cursors?.Second;
            bool shown = second.HasValue && View.Shows(second.Value);
            WorkbenchPanel.Show(_secondLabel, shown);
            if (shown)
            {
                float waveEnd = WorkbenchDrawing.WaveRight(_width), line = WorkbenchDrawing.WaveX(second.Value, View);
                float x = WaveCursorLabels.Alone(line, waveEnd);
                WriteTime(_secondLabel, second.Value);
                if (playheadShown)
                {
                    float playheadLine = WorkbenchDrawing.WaveX(_model.Time, View);
                    float p = WaveCursorLabels.Alone(playheadLine, waveEnd);
                    if (Mathf.Abs(p - x) < 2f * WaveCursorLabels.Room)   // near enough to meet: measured, and kept apart
                    {
                        (p, x) = WaveCursorLabels.Apart(playheadLine, _playheadLabel.preferredWidth, line, _secondLabel.preferredWidth,
                            WorkbenchDrawing.WaveLeft, waveEnd);
                        _playheadLabel.rectTransform.anchoredPosition = new Vector2(p, -(WorkbenchDrawing.WaveTop + 4f));
                    }
                }
                _secondLabel.rectTransform.anchoredPosition = new Vector2(x, -(WorkbenchDrawing.WaveTop + 4f));
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
            label.SetCharArray(_timeText, 0, TimeText.Write(time, decimals, _model.Unit, _timeText));
        }

        /// <summary>
        /// The colour a bus's value is written in over its box: the text's for a known value, and for one with an x in it the
        /// x's red made legible over its hatched box (<see cref="SchematicPalette.XBoxWord"/>), as a readout's is on its row.
        /// </summary>
        public static Color BusLabelColour(LogicVec value, SchematicPalette palette) => value.IsKnown ? palette.Text : palette.XBoxWord;

        // Each bus label's text and how much of it it shows, as last handed to it: a label is written again only when either
        // changes, so a frame of playback lays out no text that has not changed.
        private readonly List<string>[] _busLabelOf;
        private readonly List<int>[] _busLabelKept;

        // A cut value's characters and its ellipsis, handed to a label as characters, never a string a frame.
        private char[] _cut = new char[64];
        private readonly string _ellipsis;

        /// <summary>
        /// A bus row's values written over its boxes, up to the moment: whole where it fits, else as much of it as fits and an
        /// ellipsis (<see cref="ValueFit.Kept"/>, 5.1), and nothing in a box with room for fewer than three characters.
        /// </summary>
        private void PlaceBusLabels(int row, float middle, SchematicPalette palette)
        {
            List<TextMeshProUGUI> labels = _busLabels[row];
            List<string> labelOf = _busLabelOf[row];
            List<int> labelKept = _busLabelKept[row];
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
                int kept = ValueFit.Kept(text.Length, x1 - x0, _ellipsis.Length == 1 ? 1 : 2, out bool cut);
                if (kept == 0)
                    continue;

                if (used == labels.Count)
                {
                    labels.Add(WorkbenchPanel.StageLabel(Rect, "", new Vector2(0f, middle), UiType.Micro, palette.Text,
                        TextAlignmentOptions.Center, 60f));
                    labelOf.Add(null);
                    labelKept.Add(-1);
                }

                int at = used++;
                TextMeshProUGUI label = labels[at];
                WorkbenchPanel.Show(label, true);
                if (!ReferenceEquals(labelOf[at], text) || labelKept[at] != kept)
                {
                    labelOf[at] = text;
                    labelKept[at] = kept;
                    if (cut)
                        WriteCut(label, text, kept);
                    else
                        label.text = text;
                }

                label.color = BusLabelColour(value, palette);
                label.rectTransform.anchoredPosition = new Vector2((x0 + x1) * 0.5f, -middle);
            }

            for (int i = used; i < labels.Count; i++)
                WorkbenchPanel.Show(labels[i], false);
        }

        /// <summary>The first <paramref name="kept"/> characters of <paramref name="text"/> and the ellipsis, handed to <paramref name="label"/> as characters.</summary>
        private void WriteCut(TextMeshProUGUI label, string text, int kept)
        {
            int length = kept + _ellipsis.Length;
            if (_cut.Length < length)
                _cut = new char[length * 2];
            text.CopyTo(0, _cut, 0, kept);
            _ellipsis.CopyTo(0, _cut, kept, _ellipsis.Length);
            label.SetCharArray(_cut, 0, length);
        }

        /// <summary>What the labels over a bus row's boxes say now, left to right: what a test reads.</summary>
        public IEnumerable<string> BusLabelsOf(int row)
        {
            if (row < 0 || row >= _busLabels.Length || _busLabels[row] == null)
                yield break;
            foreach (TextMeshProUGUI label in _busLabels[row])
                if (label.gameObject.activeSelf)
                    yield return label.text;
        }

        // -----------------------------------------------------------------
        // The tip: a bus's whole value where the pointer is (5.1)
        // -----------------------------------------------------------------

        /// <summary>How far in from the tip's edge its words start.</summary>
        private const float TipPadding = 8f;

        private readonly Image _tip;
        private readonly TextMeshProUGUI _tipText;

        /// <summary>What the tip says, or null while it is down: what a test reads.</summary>
        public string TipText => _tip.gameObject.activeSelf ? _tipText.text : null;

        /// <summary>
        /// The box of a bus's value under a point on the screen, on the waves: its row, the change it shows -- its index in the
        /// row's track -- and the stretch of time it holds, as drawn. False off the waves, on a one-bit row, and where the
        /// changes come too close together to be drawn apart.
        /// </summary>
        public bool BusBoxAt(Vector2 screenPoint, out int row, out int change, out double from, out double to)
        {
            change = -1;
            from = to = 0;
            if (!RegionAt(screenPoint, out WaveRegion region, out row, out double time) || region != WaveRegion.Waves || row < 0
                || !_model.RowIsBus(row) || _model.RowSignal(row) < 0)
                return false;

            WorkbenchDrawing.RowSpans(_model, View, row, View.Start, Math.Min(View.End, _model.Time), _spans);
            foreach (WaveSpan span in _spans)
            {
                if (span.Busy || time < span.From || time >= span.To)
                    continue;
                change = span.First;
                from = span.From;
                to = span.To;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Puts up the tip saying <paramref name="text"/> by a point on the screen, below and right of it, kept inside the window
        /// the waves are seen through -- above the point where there is no room below -- and as wide as its words, or the
        /// window, a value too long for it wrapping to another line.
        /// </summary>
        public void ShowTip(Vector2 screenPoint, string text)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, null, out Vector2 local))
                return;

            // The window, in the waveform's own units: what the tip stays inside.
            Vector2 low = new Vector2(ColumnsLeft, -Height), high = new Vector2(_width, 0f);
            if (_clip != null)
            {
                _clip.GetWorldCorners(ClipCorners);
                Vector3 a = Rect.InverseTransformPoint(ClipCorners[0]), b = Rect.InverseTransformPoint(ClipCorners[2]);
                low = new Vector2(Mathf.Max(low.x, a.x), Mathf.Max(low.y, a.y));
                high = new Vector2(Mathf.Min(high.x, b.x), Mathf.Min(high.y, b.y));
            }

            if (!ReferenceEquals(_tipText.text, text))
                _tipText.text = text;
            float most = Mathf.Max(80f, high.x - low.x - 8f);
            float width = Mathf.Min(_tipText.GetPreferredValues(text, most - 2f * TipPadding, 0f).x + 2f * TipPadding + 2f, most);
            float height = _tipText.GetPreferredValues(text, width - 2f * TipPadding, 0f).y + 2f * TipPadding;

            float x = Mathf.Clamp(local.x + 14f, low.x + 4f, Mathf.Max(low.x + 4f, high.x - width - 4f));
            float top = local.y - 18f;
            if (top - height < low.y + 4f)
                top = local.y + height + 10f;   // no room below: above the pointer
            top = Mathf.Min(top, high.y - 4f);

            WorkbenchPanel.TopLeft(_tip.rectTransform, new Vector2(x, -top), new Vector2(width, height));
            WorkbenchPanel.Show(_tip, true);
        }

        /// <summary>Takes the tip down.</summary>
        public void HideTip()
        {
            if (_tip.gameObject.activeSelf)
                WorkbenchPanel.Show(_tip, false);
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
        internal static string Readout(string text, WaveRadix radix, bool bus)
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
