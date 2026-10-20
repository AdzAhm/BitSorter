using System.Collections.Generic;
using BoardPoint = BitSorter.Board.Point;

namespace BitSorter.ViewModel
{
    /// <summary>What a gesture on the canvas asks the workbench to do.</summary>
    public enum CanvasGesture
    {
        /// <summary>Place the part in hand at <see cref="CanvasCommand.At"/>.</summary>
        Place,

        /// <summary>A click on <see cref="CanvasCommand.Target"/>: select it, with <see cref="CanvasCommand.Extend"/> or <see cref="CanvasCommand.Toggle"/>.</summary>
        Select,

        /// <summary>A drag of the selection began, taken hold of at <see cref="CanvasCommand.From"/>.</summary>
        BeginMove,

        /// <summary>The selection, being dragged, is at <see cref="CanvasCommand.At"/>; placed freely with <see cref="CanvasCommand.Free"/>.</summary>
        MoveTo,

        /// <summary>The drag let go at <see cref="CanvasCommand.At"/>: the move is made.</summary>
        EndMove,

        /// <summary>A box being dragged from <see cref="CanvasCommand.From"/> to <see cref="CanvasCommand.At"/>.</summary>
        Box,

        /// <summary>The box let go: what it touches is selected, added to the selection with <see cref="CanvasCommand.Extend"/>.</summary>
        EndBox,

        /// <summary>Move the view by <see cref="CanvasCommand.Pan"/> window pixels.</summary>
        Pan,

        /// <summary>Add the signal of the wire <see cref="CanvasCommand.Target"/> is to the waveform (Alt and a click).</summary>
        AddWireSignal,

        /// <summary>Put down what is in hand, placing nothing, or let go of the wire being drawn.</summary>
        Cancel,

        /// <summary>A wire starts from <see cref="CanvasCommand.Target"/>: a pin, or a wire to tap (Part 10b).</summary>
        BeginWire,

        /// <summary>The wire being drawn bends at <see cref="CanvasCommand.At"/>, on the grid or, <see cref="CanvasCommand.Free"/>, exactly.</summary>
        WireBend,

        /// <summary>The wire being drawn ends on <see cref="CanvasCommand.Target"/>, a pin or a wire, and is made.</summary>
        EndWire,
    }

    /// <summary>One thing a gesture on the canvas asks for.</summary>
    public readonly struct CanvasCommand
    {
        public CanvasCommand(CanvasGesture gesture, BoardPoint at = default, BoardPoint from = default, CanvasTarget target = default,
            Vec2 pan = default, bool extend = false, bool toggle = false, bool free = false)
        {
            Gesture = gesture;
            At = at;
            From = from;
            Target = target;
            Pan = pan;
            Extend = extend;
            Toggle = toggle;
            Free = free;
        }

        public CanvasGesture Gesture { get; }

        /// <summary>Where, on the board, in cells.</summary>
        public BoardPoint At { get; }

        /// <summary>Where a drag began, on the board.</summary>
        public BoardPoint From { get; }

        public CanvasTarget Target { get; }

        public Vec2 Pan { get; }

        public bool Extend { get; }
        public bool Toggle { get; }
        public bool Free { get; }

        public override string ToString() => Gesture switch
        {
            CanvasGesture.Select => $"Select {Target}{(Extend ? " +" : "")}{(Toggle ? " ^" : "")}",
            CanvasGesture.Place or CanvasGesture.MoveTo or CanvasGesture.EndMove => $"{Gesture} {At.X},{At.Y}{(Free ? " free" : "")}",
            CanvasGesture.BeginMove => $"BeginMove {From.X},{From.Y}",
            CanvasGesture.Box or CanvasGesture.EndBox => $"{Gesture} {From.X},{From.Y}..{At.X},{At.Y}{(Extend ? " +" : "")}",
            CanvasGesture.Pan => $"Pan {Pan.x},{Pan.y}",
            CanvasGesture.AddWireSignal => $"AddWireSignal {Target}",
            CanvasGesture.BeginWire => $"BeginWire {Target}",
            CanvasGesture.WireBend => $"WireBend {At.X},{At.Y}{(Free ? " free" : "")}",
            CanvasGesture.EndWire => $"EndWire {Target}",
            _ => Gesture.ToString(),
        };
    }

    /// <summary>A press on the canvas, as the view sees it: where, with what, and what is under it.</summary>
    public struct CanvasPress
    {
        /// <summary>0 the left button, 1 the right, 2 the middle.</summary>
        public int Button;

        /// <summary>Where, in the window's pixels: what a drag is measured in.</summary>
        public Vec2 Window;

        /// <summary>Where, on the board.</summary>
        public BoardPoint Board;

        /// <summary>What is under it (<see cref="CanvasHit"/>).</summary>
        public CanvasTarget Target;

        /// <summary>Whether what is under it is selected already: a press on it drags the whole selection.</summary>
        public bool OnSelected;

        /// <summary>Whether a part is in hand, from the parts strip.</summary>
        public bool InHand;

        /// <summary>Whether a wire is being drawn: a click then bends it or ends it (Part 10b).</summary>
        public bool Drafting;

        public bool Shift, Ctrl, Alt;

        /// <summary>Whether Space is held: a press with it pans.</summary>
        public bool Space;
    }

    /// <summary>
    /// The canvas's mouse as gestures (Part 10a), as <see cref="WavePointer"/> is the waveform's: a press, the
    /// moves while it is held, and its release become what the workbench is asked to do. Unity-free, so
    /// every gesture is tested from a scripted list of events under dotnet.
    /// </summary>
    /// <remarks>
    /// - **With a part in hand**, a left press places it and a right press puts it down.
    /// - **On a part**, a press selects it -- Shift adds it, Ctrl puts it in or out -- and a drag moves the
    ///   selection, Alt held placing it freely. A press on a part already selected keeps the selection to
    ///   drag; let go without a drag, it selects that part alone.
    /// - **On a pin**, a press starts a wire from it (Part 10b), and a drag draws it out: let go on a pin or a wire
    ///   it ends there, a tap; let go anywhere else it bends there and goes on. A click starts one too, and then
    ///   every click on nothing bends it and a click on a pin or a wire ends it. The right button, or Escape, lets
    ///   it go. Shift or Ctrl on a pin picks its part instead, as on its body.
    /// - **On a wire**, a click selects it, and a drag draws a new wire out of it, tapping it where the drag
    ///   began; Alt and a press adds its signal to the waveform instead.
    /// - **On nothing**, a drag draws a box that selects what it touches, Shift adding to the selection; a
    ///   click without a drag clears the selection.
    /// - **The middle button, or Space with the left, pans**, wherever it is pressed.
    /// - A drag is one only past <see cref="DragPixels"/>: a hand that shakes still clicks.
    /// </remarks>
    public sealed class CanvasPointer
    {
        /// <summary>How far, in window pixels, a press has to move before it is a drag.</summary>
        public const float DragPixels = 4f;

        private enum Hold { None, OnPart, Moving, OnNothing, Boxing, Panning, OnPin, OnWire, Wiring }

        private Hold _hold;
        private CanvasPress _press;
        private Vec2 _last;

        /// <summary>Whether a press is being dragged: moving the selection, drawing a box or a wire, or panning.</summary>
        public bool Dragging => _hold == Hold.Moving || _hold == Hold.Boxing || _hold == Hold.Panning || _hold == Hold.Wiring;

        /// <summary>Whether a press is held at all, dragged yet or not.</summary>
        public bool Held => _hold != Hold.None;

        /// <summary>Whether the selection is being moved.</summary>
        public bool Moving => _hold == Hold.Moving;

        /// <summary>A button pressed: what it asks for, added to <paramref name="into"/>.</summary>
        public void Press(CanvasPress press, List<CanvasCommand> into)
        {
            _hold = Hold.None;
            _press = press;
            _last = press.Window;

            if (press.Button == 2 || press.Button == 0 && press.Space)
            {
                _hold = Hold.Panning;
                return;
            }

            if (press.InHand)
            {
                if (press.Button == 0)
                    into.Add(new CanvasCommand(CanvasGesture.Place, press.Board, free: press.Alt, extend: press.Shift));
                else if (press.Button == 1)
                    into.Add(new CanvasCommand(CanvasGesture.Cancel));
                return;
            }

            if (press.Drafting)
            {
                // A wire being drawn: a click on a pin or a wire ends it, anywhere else bends it.
                if (press.Button == 0)
                    into.Add(EndsOn(press.Target)
                        ? new CanvasCommand(CanvasGesture.EndWire, press.Board, target: press.Target)
                        : new CanvasCommand(CanvasGesture.WireBend, press.Board, free: press.Alt));
                else if (press.Button == 1)
                    into.Add(new CanvasCommand(CanvasGesture.Cancel));
                return;
            }

            if (press.Button != 0)
                return;

            if (press.Target.Kind == CanvasTargetKind.Pin && !press.Ctrl && !press.Shift)
            {
                into.Add(new CanvasCommand(CanvasGesture.BeginWire, press.Board, target: press.Target));
                _hold = Hold.OnPin;
                return;
            }

            switch (press.Target.Kind)
            {
                case CanvasTargetKind.Part:
                case CanvasTargetKind.Pin:
                    // A press on a part already selected keeps the selection, to drag it all; Ctrl takes it out.
                    if (press.Ctrl)
                    {
                        into.Add(new CanvasCommand(CanvasGesture.Select, target: press.Target, toggle: true));
                        return;
                    }

                    if (!press.OnSelected)
                        into.Add(new CanvasCommand(CanvasGesture.Select, target: press.Target, extend: press.Shift));
                    _hold = Hold.OnPart;
                    return;

                case CanvasTargetKind.Wire:
                    if (press.Alt || press.Shift || press.Ctrl)
                    {
                        into.Add(press.Alt
                            ? new CanvasCommand(CanvasGesture.AddWireSignal, target: press.Target)
                            : new CanvasCommand(CanvasGesture.Select, target: press.Target, extend: press.Shift, toggle: press.Ctrl));
                        return;
                    }

                    // A click selects it, on release; a drag draws a wire out of it.
                    _hold = Hold.OnWire;
                    return;

                default:
                    _hold = Hold.OnNothing;
                    return;
            }
        }

        /// <summary>The pointer moved to <paramref name="window"/>, <paramref name="board"/> on the board, with the button still held; <paramref name="alt"/> as it is now.</summary>
        public void Move(Vec2 window, BoardPoint board, bool alt, List<CanvasCommand> into)
        {
            switch (_hold)
            {
                case Hold.Panning:
                    if (window != _last)
                        into.Add(new CanvasCommand(CanvasGesture.Pan, pan: window - _last));
                    break;

                case Hold.OnPart:
                    if (Vec2.Distance(window, _press.Window) < DragPixels)
                        return;
                    _hold = Hold.Moving;
                    into.Add(new CanvasCommand(CanvasGesture.BeginMove, from: _press.Board));
                    into.Add(new CanvasCommand(CanvasGesture.MoveTo, board, _press.Board, free: alt));
                    break;

                case Hold.Moving:
                    into.Add(new CanvasCommand(CanvasGesture.MoveTo, board, _press.Board, free: alt));
                    break;

                case Hold.OnNothing:
                    if (Vec2.Distance(window, _press.Window) < DragPixels)
                        return;
                    _hold = Hold.Boxing;
                    into.Add(new CanvasCommand(CanvasGesture.Box, board, _press.Board, extend: _press.Shift));
                    break;

                case Hold.Boxing:
                    into.Add(new CanvasCommand(CanvasGesture.Box, board, _press.Board, extend: _press.Shift));
                    break;

                case Hold.OnPin:
                    if (Vec2.Distance(window, _press.Window) >= DragPixels)
                        _hold = Hold.Wiring;
                    break;

                case Hold.OnWire:
                    if (Vec2.Distance(window, _press.Window) < DragPixels)
                        return;
                    _hold = Hold.Wiring;
                    into.Add(new CanvasCommand(CanvasGesture.BeginWire, _press.Board, target: _press.Target));
                    break;
            }

            _last = window;
        }

        /// <summary>The button let go at <paramref name="board"/>, over <paramref name="target"/>: a drag ends there, a click is what it was.</summary>
        public void Release(BoardPoint board, CanvasTarget target, bool alt, List<CanvasCommand> into)
        {
            switch (_hold)
            {
                case Hold.Wiring:
                    // A wire drawn out and let go: on a pin or a wire it ends there; anywhere else it bends, and goes on.
                    into.Add(EndsOn(target)
                        ? new CanvasCommand(CanvasGesture.EndWire, board, target: target)
                        : new CanvasCommand(CanvasGesture.WireBend, board, free: alt));
                    break;

                case Hold.OnWire:
                    into.Add(new CanvasCommand(CanvasGesture.Select, target: _press.Target));
                    break;

                case Hold.Moving:
                    into.Add(new CanvasCommand(CanvasGesture.EndMove, board, _press.Board, free: alt));
                    break;

                case Hold.OnPart:
                    // A click on a part already selected, with no drag: it alone.
                    if (_press.OnSelected && !_press.Shift)
                        into.Add(new CanvasCommand(CanvasGesture.Select, target: _press.Target));
                    break;

                case Hold.Boxing:
                    into.Add(new CanvasCommand(CanvasGesture.EndBox, board, _press.Board, extend: _press.Shift));
                    break;

                case Hold.OnNothing:
                    into.Add(new CanvasCommand(CanvasGesture.Select, target: CanvasTarget.Nothing, extend: _press.Shift, toggle: _press.Ctrl));
                    break;
            }

            _hold = Hold.None;
        }

        /// <summary>Lets go of whatever is held without making it: what Escape does mid-drag, and a document changed under it.</summary>
        public void Abandon() => _hold = Hold.None;

        /// <summary>What a wire may end on: a pin, or a wire to tap.</summary>
        private static bool EndsOn(CanvasTarget target) => target.Kind == CanvasTargetKind.Pin || target.Kind == CanvasTargetKind.Wire;
    }
}
