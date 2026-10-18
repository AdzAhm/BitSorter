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
    }

    /// <summary>One thing a gesture asks for.</summary>
    public readonly struct WaveCommand
    {
        public WaveCommand(WaveGesture gesture, double time = 0, int row = -1, bool extend = false, bool toggle = false)
        {
            Gesture = gesture;
            Time = time;
            Row = row;
            Extend = extend;
            Toggle = toggle;
        }

        public WaveGesture Gesture { get; }
        public double Time { get; }
        public int Row { get; }
        public bool Extend { get; }
        public bool Toggle { get; }

        public override string ToString() => $"{Gesture} t={Time} row={Row}{(Extend ? " extend" : "")}{(Toggle ? " toggle" : "")}";
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

        /// <summary>How far from the second cursor the press is, in pixels; null while there is none.</summary>
        public float? FromSecond;
    }

    /// <summary>
    /// The waveform's mouse, as gestures: a press, the moves while it is held, and its release become
    /// what the workbench is asked to do. Unity-free, so every gesture is tested from a scripted list
    /// of events under dotnet, and Part 10's canvas can take the same pattern.
    /// </summary>
    /// <remarks>
    /// - **On the waves or the axis:** a press seeks the playhead and a drag scrubs it; with Shift it
    ///   places the second cursor, and a press within <see cref="GrabPixels"/> of the second cursor
    ///   takes hold of it, and a drag moves it.
    /// - **On a row's name or value:** a press selects the row -- with Shift every row to it, with Ctrl
    ///   in or out of the selection -- and a press on a group's mark opens or shuts it. The right button
    ///   opens the row's menu.
    /// - **On the circuit with Alt held:** adds the wire's signal. Without Alt the circuit's own drag and
    ///   hover are the panel's, as they were.
    /// - **A press on a control is never a gesture**, nor is anything until its release: the slider
    ///   dragged down over the waves stays a drag of the slider.
    /// </remarks>
    public sealed class WavePointer
    {
        /// <summary>How near the second cursor, in pixels, a press takes hold of it.</summary>
        public const float GrabPixels = 6f;

        private enum Hold { None, Scrubbing, MovingSecond }

        private Hold _hold;

        /// <summary>Whether a press is being dragged: scrubbing the playhead or moving the second cursor.</summary>
        public bool Dragging => _hold != Hold.None;

        /// <summary>A button pressed: what it asks for, added to <paramref name="into"/>.</summary>
        public void Press(WavePress press, List<WaveCommand> into)
        {
            _hold = Hold.None;

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
                    if (press.Shift)
                    {
                        into.Add(new WaveCommand(WaveGesture.PlaceSecond, press.Time));
                        _hold = Hold.MovingSecond;
                    }
                    else if (press.FromSecond.HasValue && press.FromSecond.Value <= GrabPixels)
                    {
                        into.Add(new WaveCommand(WaveGesture.MoveSecond, press.Time));
                        _hold = Hold.MovingSecond;
                    }
                    else
                    {
                        into.Add(new WaveCommand(WaveGesture.Seek, press.Time));
                        _hold = Hold.Scrubbing;
                    }

                    return;

                case WaveRegion.Names:
                case WaveRegion.Values:
                    if (press.Row < 0)
                        return;
                    if (press.Button == 1)
                        into.Add(new WaveCommand(WaveGesture.RowMenu, row: press.Row));
                    else if (press.Button == 0 && press.OnGroupMark)
                        into.Add(new WaveCommand(WaveGesture.ToggleGroup, row: press.Row));
                    else if (press.Button == 0)
                        into.Add(new WaveCommand(WaveGesture.SelectRow, row: press.Row, extend: press.Shift, toggle: press.Ctrl));
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

        /// <summary>The button let go: whatever was held is let go with it.</summary>
        public void Release() => _hold = Hold.None;
    }
}
