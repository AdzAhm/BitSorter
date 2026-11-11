using System.Collections.Generic;

namespace BitSorter.ViewModel
{
    /// <summary>Which part of the workbench a press landed on, as the view finds it by testing its rects.</summary>
    public enum WaveRegion
    {
        /// <summary>Nothing the workbench answers.</summary>
        None,

        /// <summary>The rows' names.</summary>
        Names,

        /// <summary>The rows' values now.</summary>
        Values,

        /// <summary>The waves.</summary>
        Waves,

        /// <summary>The time axis's numbers over them.</summary>
        Axis,

        /// <summary>The circuit's window.</summary>
        Schematic,

        /// <summary>A button, the slider, a popup, the add list -- anything with clicks of its own, which win.</summary>
        Control,
    }

    /// <summary>What a gesture asks the workbench to do.</summary>
    public enum WaveGesture
    {
        /// <summary>Move the playhead to <see cref="WaveCommand.Time"/>.</summary>
        Seek,

        /// <summary>Place the second cursor at <see cref="WaveCommand.Time"/>.</summary>
        PlaceSecond,

        /// <summary>Move the second cursor to <see cref="WaveCommand.Time"/>.</summary>
        MoveSecond,

        /// <summary>Select <see cref="WaveCommand.Row"/>, alone or with <see cref="WaveCommand.Extend"/> or <see cref="WaveCommand.Toggle"/>.</summary>
        SelectRow,

        /// <summary>Open or shut the group whose header is <see cref="WaveCommand.Row"/>.</summary>
        ToggleGroup,

        /// <summary>Open <see cref="WaveCommand.Row"/>'s menu.</summary>
        RowMenu,

        /// <summary>Add the signal of the wire under the pointer.</summary>
        AddWireSignal,

        /// <summary>Open the menu for the moment <see cref="WaveCommand.Time"/>: a breakpoint there, set or taken away.</summary>
        TimeMenu,

        /// <summary>
        /// <see cref="WaveCommand.Row"/> is being dragged by its name, over the gap <see cref="WaveCommand.To"/>: show where
        /// it would go (<see cref="WaveList.DropGap"/>).
        /// </summary>
        DragRow,

        /// <summary>Put <see cref="WaveCommand.Row"/> down in the gap <see cref="WaveCommand.To"/> (<see cref="WaveList.MoveTo"/>).</summary>
        DropRow,
    }

    /// <summary>One thing a gesture asks for.</summary>
    public readonly struct WaveCommand
    {
        public WaveCommand(WaveGesture gesture, double time = 0, int row = -1, bool extend = false, bool toggle = false, int to = -1)
        {
            Gesture = gesture;
            Time = time;
            Row = row;
            Extend = extend;
            Toggle = toggle;
            To = to;
        }

        public WaveGesture Gesture { get; }
        public double Time { get; }
        public int Row { get; }
        public bool Extend { get; }
        public bool Toggle { get; }

        /// <summary>The gap a dragged row is over: 0 above the first line, the lines' count under the last.</summary>
        public int To { get; }

        public override string ToString() => $"{Gesture} t={Time} row={Row}{(Extend ? " extend" : "")}{(Toggle ? " toggle" : "")}{(To >= 0 ? " to=" + To : "")}";
    }

    /// <summary>A press as the view sees it: where, with what, and what is under it.</summary>
    public struct WavePress
    {
        public WaveRegion Region;

        /// <summary>0 the left button, 1 the right, 2 the middle.</summary>
        public int Button;

        /// <summary>The moment under the pointer, snapped as a click there means it (<see cref="WaveSnap"/>).</summary>
        public double Time;

        /// <summary>The row under the pointer, or -1.</summary>
        public int Row;

        /// <summary>Whether the row is a group's header, and the press is on its open-or-shut mark.</summary>
        public bool OnGroupMark;

        public bool Shift, Ctrl, Alt;

        /// <summary>How far from the playhead the press is, in pixels; null where the view cannot say.</summary>
        public float? FromPlayhead;

        /// <summary>Which press of a quick run this is, as the view counts them: 2 for the second of a double-click.</summary>
        public int Clicks;

        /// <summary>How high on the screen the press is, in pixels: what a row's drag is measured from (<see cref="WavePointer.MoveRow"/>).</summary>
        public float Y;
    }

    /// <summary>
    /// The waveform's mouse, as gestures: a press, the moves while it is held, and its release become
    /// what the workbench is asked to do. Unity-free, so every gesture is tested from a scripted list
    /// of events under dotnet, and Part 10's canvas can take the same pattern.
    /// </summary>
    /// <remarks>
    /// - **On the waves or the axis:** a press places the second cursor and a drag moves it through the
    ///   waves; with Shift it seeks the playhead and a drag scrubs it, and a press within
    ///   <see cref="GrabPixels"/> of the playhead takes hold of it. A double-click is two clicks: the waves
    ///   change no input since 5.1, which INPUTS sets.
    ///
    ///   **They were the other way round until Part 15h.** The waves are drawn only up to the playhead,
    ///   so a click back in a finished run hid everything after it; the second cursor reads any moment
    ///   and hides nothing. Ahmad asked for the cursor that moves through the waves to be the click's,
    ///   and the one that cuts them Shift's (2026-10-28).
    /// - **On a row's name or value:** a press selects the row -- with Shift every row to it, with Ctrl
    ///   in or out of the selection -- and a press on a group's mark opens or shuts it. The right button
    ///   opens the row's menu.
    /// - **A row's name held and moved more than <see cref="RowDragPixels"/> up or down drags the row** (5.1, asked by Ahmad,
    ///   2026-11-11: rows put in another order by dragging them up or down by their names): each gap it is over is a
    ///   <see cref="WaveGesture.DragRow"/>, and the release puts it down there (<see cref="WaveGesture.DropRow"/>);
    ///   <see cref="Cancel"/> -- Escape -- puts it down nowhere. Only a plain press drags: with Shift or Ctrl it selects, as it
    ///   did. A group's header opens or shuts on the release of a press on its name, not the press, so that the same press
    ///   can carry the group instead.
    /// - **On the circuit with Alt held:** adds the wire's signal. Without Alt the circuit's own drag and
    ///   hover are the panel's, as they were.
    /// - **A press on a control is never a gesture**, nor is anything until its release: the slider
    ///   dragged down over the waves stays a drag of the slider.
    /// </remarks>
    public sealed class WavePointer
    {
        /// <summary>How near the playhead, in pixels, a press takes hold of it.</summary>
        public const float GrabPixels = 6f;

        /// <summary>How far a row's name is moved up or down, in pixels, before the press is a drag of the row and not a click.</summary>
        public const float RowDragPixels = 4f;

        private enum Hold { None, Scrubbing, MovingSecond, RowPressed, MovingRow }

        private Hold _hold;

        // The row held by its name, where on the screen it was pressed, whether its release opens or shuts a group, and the
        // gap it was last over.
        private int _row = -1, _gap = -1;
        private float _pressY;
        private bool _toggleOnRelease;

        /// <summary>Whether a press is being dragged through time: moving the second cursor or scrubbing the playhead.</summary>
        public bool Dragging => _hold == Hold.Scrubbing || _hold == Hold.MovingSecond;

        /// <summary>Whether a row's name is held: pressed, and perhaps being dragged (<see cref="MoveRow"/>).</summary>
        public bool HoldsRow => _hold == Hold.RowPressed || _hold == Hold.MovingRow;

        /// <summary>Whether a row is being dragged by its name, past <see cref="RowDragPixels"/>.</summary>
        public bool MovingRow => _hold == Hold.MovingRow;

        /// <summary>The row being dragged, or -1.</summary>
        public int RowDragged => _hold == Hold.MovingRow ? _row : -1;

        /// <summary>A button pressed: what it asks for, added to <paramref name="into"/>.</summary>
        public void Press(WavePress press, List<WaveCommand> into)
        {
            _hold = Hold.None;
            _row = _gap = -1;
            _toggleOnRelease = false;

            switch (press.Region)
            {
                case WaveRegion.Waves:
                case WaveRegion.Axis:
                    if (press.Button == 1)
                    {
                        into.Add(new WaveCommand(WaveGesture.TimeMenu, press.Time));
                        return;
                    }

                    if (press.Button != 0)
                        return;

                    // A double-click is two clicks: since 5.1 the waves never change an input, which INPUTS sets (Ahmad,
                    // 2026-11-11: a double-click on an input's wave once flipped it, or asked its value).
                    if (press.Shift || press.FromPlayhead.HasValue && press.FromPlayhead.Value <= GrabPixels)
                    {
                        into.Add(new WaveCommand(WaveGesture.Seek, press.Time));
                        _hold = Hold.Scrubbing;
                    }
                    else
                    {
                        into.Add(new WaveCommand(WaveGesture.PlaceSecond, press.Time));
                        _hold = Hold.MovingSecond;
                    }

                    return;

                case WaveRegion.Names:
                case WaveRegion.Values:
                    if (press.Row < 0)
                        return;
                    if (press.Button == 1)
                    {
                        into.Add(new WaveCommand(WaveGesture.RowMenu, row: press.Row));
                        return;
                    }

                    if (press.Button != 0)
                        return;

                    // A plain press on a name may become a drag of the row: a header's opening or shutting waits for the
                    // release, so that a drag carries the group instead (5.1).
                    bool mayDrag = press.Region == WaveRegion.Names && !press.Shift && !press.Ctrl;
                    if (mayDrag)
                    {
                        _hold = Hold.RowPressed;
                        _row = press.Row;
                        _pressY = press.Y;
                        _toggleOnRelease = press.OnGroupMark;
                    }

                    if (press.OnGroupMark)
                    {
                        if (!mayDrag)
                            into.Add(new WaveCommand(WaveGesture.ToggleGroup, row: press.Row));
                    }
                    else
                    {
                        into.Add(new WaveCommand(WaveGesture.SelectRow, row: press.Row, extend: press.Shift, toggle: press.Ctrl));
                    }

                    return;

                case WaveRegion.Schematic:
                    if (press.Button == 0 && press.Alt)
                        into.Add(new WaveCommand(WaveGesture.AddWireSignal));
                    return;
            }
        }

        /// <summary>The pointer moved to <paramref name="time"/> with the button still held.</summary>
        public void Move(double time, List<WaveCommand> into)
        {
            if (_hold == Hold.Scrubbing)
                into.Add(new WaveCommand(WaveGesture.Seek, time));
            else if (_hold == Hold.MovingSecond)
                into.Add(new WaveCommand(WaveGesture.MoveSecond, time));
        }

        /// <summary>
        /// The pointer at <paramref name="y"/> on the screen, over the gap <paramref name="gap"/> between the rows, with a
        /// row's name still held: past <see cref="RowDragPixels"/> from the press, a drag of the row, which says each new gap
        /// it is over.
        /// </summary>
        public void MoveRow(float y, int gap, List<WaveCommand> into)
        {
            if (_hold == Hold.RowPressed && System.Math.Abs(y - _pressY) > RowDragPixels)
            {
                _hold = Hold.MovingRow;
                _toggleOnRelease = false;
            }

            if (_hold != Hold.MovingRow || gap == _gap)
                return;
            _gap = gap;
            into.Add(new WaveCommand(WaveGesture.DragRow, row: _row, to: gap));
        }

        /// <summary>The button let go: whatever was held is let go with it, asking nothing.</summary>
        public void Release() => Release(null);

        /// <summary>
        /// The button let go: a row dragged is put down in the gap it was last over, and a group's header pressed by its name
        /// and not dragged opens or shuts -- each added to <paramref name="into"/>; whatever else was held is let go.
        /// </summary>
        public void Release(List<WaveCommand> into)
        {
            if (into != null && _hold == Hold.MovingRow && _gap >= 0)
                into.Add(new WaveCommand(WaveGesture.DropRow, row: _row, to: _gap));
            else if (into != null && _hold == Hold.RowPressed && _toggleOnRelease)
                into.Add(new WaveCommand(WaveGesture.ToggleGroup, row: _row));
            _hold = Hold.None;
            _row = _gap = -1;
            _toggleOnRelease = false;
        }

        /// <summary>Lets go of a row being dragged without putting it anywhere -- Escape -- and says whether one was.</summary>
        public bool Cancel()
        {
            if (_hold != Hold.MovingRow)
                return false;
            _hold = Hold.None;
            _row = _gap = -1;
            return true;
        }
    }
}
