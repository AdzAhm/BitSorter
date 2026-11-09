using System;
using System.Collections.Generic;
using BitSorter.Sim;
using BitSorter.SystemVerilog.Syntax;

namespace BitSorter.Board
{
    /// <summary>Which way a pin faces: the way a wire leaves it.</summary>
    public enum Facing { Left, Right, Up, Down }

    /// <summary>A pin of a part as drawn: where, and which way a wire leaves it.</summary>
    public readonly struct PinSpot
    {
        public PinSpot(string pin, Point at, Facing facing)
        {
            Pin = pin;
            At = at;
            Facing = facing;
        }

        public readonly string Pin;
        public readonly Point At;
        public readonly Facing Facing;
    }

    /// <summary>A part's extent and its pins, in cells.</summary>
    public sealed class PartOutline
    {
        internal PartOutline(double left, double bottom, double right, double top, IReadOnlyList<PinSpot> pins)
        {
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
            Pins = pins;
        }

        public double Left { get; }
        public double Bottom { get; }
        public double Right { get; }
        public double Top { get; }
        public IReadOnlyList<PinSpot> Pins { get; }

        public bool TryPin(string name, out PinSpot spot)
        {
            for (int i = 0; i < Pins.Count; i++)   // by index: an interface's enumerator would be boxed
            {
                if (Pins[i].Pin == name)
                {
                    spot = Pins[i];
                    return true;
                }
            }

            spot = default;
            return false;
        }
    }

    /// <summary>
    /// Where each kind of part's pins are and how much room it takes, in cells, y up: one geometry for
    /// drawing, routing and (Part 10) picking, so a wire always meets the pin it is drawn to.
    /// </summary>
    /// <remarks>
    /// A part is measured centred on its placement and facing right -- inputs on the left, outputs on
    /// the right, a control (a tri-state's enable, a mux's select, a register's reset) underneath --
    /// and then mirrored (<see cref="Placement.Flipped"/>, about its own middle) and turned
    /// (<see cref="Placement.QuarterTurns"/>, counter-clockwise). Every pin is on the quarter-cell
    /// grid placement snaps to, so a wire between snapped parts runs straight.
    /// </remarks>
    public static class PartGeometry
    {
        /// <summary>The space between neighbouring pins on one side.</summary>
        public const double Pitch = 0.5;

        /// <summary>The most a resized block may be along either side, in cells.</summary>
        public const double MaxSize = 64;

        /// <summary>The step a resized block's width and height take, in cells: half a cell, so every pin stays on the quarter grid.</summary>
        public const double SizeStep = 0.5;

        /// <summary>Whether a part can be resized (Part 16): a block, or a box of a drawing of code.</summary>
        public static bool Resizable(Part part) => part is BlockPart || part is BoxPart;

        /// <summary>A part's natural width and height, unturned, in cells: what it is drawn at with no size of its own.</summary>
        public static (double Width, double Height) NaturalSize(Project project, Part part)
        {
            PartOutline local = Local(project, part);
            return (local.Right - local.Left, local.Top - local.Bottom);
        }

        /// <summary>The part's outline and pins as placed: mirrored, turned and moved to where it stands.</summary>
        public static PartOutline Placed(Project project, Part part, Placement placement)
        {
            PartOutline local = Local(project, part, placement);
            var pins = new List<PinSpot>(local.Pins.Count);
            foreach (PinSpot pin in local.Pins)
                pins.Add(new PinSpot(pin.Pin, Transform(placement, pin.At), Turn(placement, pin.Facing)));

            Point a = Transform(placement, new Point(local.Left, local.Bottom));
            Point b = Transform(placement, new Point(local.Right, local.Top));
            return new PartOutline(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), pins);
        }

        /// <summary>A point of a part's own drawing, where it falls on the board.</summary>
        public static Point Transform(Placement placement, Point local)
        {
            double x = placement.Flipped ? -local.X : local.X, y = local.Y;
            for (int i = 0; i < placement.QuarterTurns; i++)
            {
                double turned = -y;
                y = x;
                x = turned;
            }

            return new Point(placement.Position.X + x, placement.Position.Y + y);
        }

        public static Facing Turn(Placement placement, Facing facing)
        {
            if (placement.Flipped)
                facing = facing == Facing.Left ? Facing.Right : facing == Facing.Right ? Facing.Left : facing;
            for (int i = 0; i < placement.QuarterTurns; i++)
            {
                switch (facing)
                {
                    case Facing.Right: facing = Facing.Up; break;
                    case Facing.Up: facing = Facing.Left; break;
                    case Facing.Left: facing = Facing.Down; break;
                    default: facing = Facing.Right; break;
                }
            }

            return facing;
        }

        /// <summary>The unit step a wire takes leaving a pin that faces this way.</summary>
        public static Point Step(Facing facing)
        {
            switch (facing)
            {
                case Facing.Left: return new Point(-1, 0);
                case Facing.Right: return new Point(1, 0);
                case Facing.Up: return new Point(0, 1);
                default: return new Point(0, -1);
            }
        }

        /// <summary>The part centred on the origin, facing right, at its natural size.</summary>
        public static PartOutline Local(Project project, Part part) => Local(project, part, default);

        /// <summary>
        /// The part centred on the origin, facing right, at the size <paramref name="placement"/> gives it: a block's or a box's,
        /// never smaller than its natural size nor larger than <see cref="MaxSize"/> (Part 16); every other part at its own.
        /// </summary>
        /// <remarks>
        /// A resized block's pins stand on its sides, at x = ±width/2, and down them at one pitch for both sides, centred: half a
        /// cell, as at natural size, and as the box grows taller the largest half-cell step its side with the most pins fits in
        /// it -- so a natural block is drawn as it always was, a side with fewer pins never jumps apart, and every pin stays on
        /// the quarter grid placement snaps to.
        /// </remarks>
        public static PartOutline Local(Project project, Part part, Placement placement)
        {
            var pins = new List<PinSpot>();
            void Left(string pin, double y, double x) => pins.Add(new PinSpot(pin, new Point(x, y), Facing.Left));
            void Right(string pin, double y, double x) => pins.Add(new PinSpot(pin, new Point(x, y), Facing.Right));
            void Down(string pin, double x, double y) => pins.Add(new PinSpot(pin, new Point(x, y), Facing.Down));
            void Up(string pin, double x, double y) => pins.Add(new PinSpot(pin, new Point(x, y), Facing.Up));

            switch (part)
            {
                case GatePart gate when gate.IsTriState:
                {
                    // A triangle with its control underneath, as the textbook draws a tri-state buffer.
                    Left("a", 0, -1.0);
                    Down("en", 0, -0.75);
                    Right("y", 0, 1.0);
                    return new PartOutline(-1.0, -0.75, 1.0, 0.5, pins);
                }

                case GatePart gate:
                {
                    // As many inputs as the gate has pins for: none for a count no gate can have, which the rules say in words
                    // (Part.Counted; the review of the 16d fixes: a gate saved with a million inputs drew a million pins).
                    int inputs = Part.Counted(gate.Inputs);
                    double half = Half(inputs);
                    for (int i = 0; i < inputs; i++)
                        Left(GatePart.InputPin(gate.Kind, i), Row(i, inputs), -1.0);
                    Right("y", 0, 1.0);
                    return new PartOutline(-1.0, -half, 1.0, half, pins);
                }

                case PortPart port:
                {
                    double width = PortWidth(port);
                    if (port.Direction == PortDirection.Output)
                    {
                        Left("in", 0, -0.5);
                        return new PartOutline(-0.5, -0.3, width - 0.5, 0.3, pins);
                    }

                    Right("out", 0, 0.5);
                    return new PartOutline(0.5 - width, -0.3, 0.5, 0.3, pins);
                }

                case ConstantPart constant:
                {
                    // Room for the value as a literal, rounded up to the quarter-cell grid, and half a cell more for the lead
                    // its box steps back from its pin by (Part 15g): its pin stays put, and the box keeps the literal's room.
                    int characters = constant.Literal.Length;
                    double width = Math.Ceiling(Math.Max(1.0, 0.3 + 0.12 * characters) * 4) / 4 + 0.5;
                    Right("y", 0, 0.5);
                    return new PartOutline(0.5 - width, -0.3, 0.5, 0.3, pins);
                }

                case RegisterPart register:
                {
                    // A box with the clock's wedge on its left, q on the right at d's height.
                    Left("d", 0.25, -1.0);
                    Left("clk", -0.25, -1.0);
                    if (register.HasReset)
                        Down("rst", 0, -1.0);
                    Right("q", 0.25, 1.0);
                    return new PartOutline(-1.0, register.HasReset ? -1.0 : -0.75, 1.0, 0.75, pins);
                }

                case MuxPart mux:
                {
                    // A trapezoid, the data on its long left side and the select underneath.
                    int inputs = Part.Counted(mux.Inputs);
                    double half = Half(inputs) + 0.25;
                    for (int i = 0; i < inputs; i++)
                        Left("d" + i, Row(i, inputs), -1.0);
                    Down("s", 0, -half - 0.25);
                    Right("y", 0, 1.0);
                    return new PartOutline(-1.0, -half - 0.25, 1.0, half, pins);
                }

                case SplitterPart splitter:
                {
                    // A bar: the bus in on the left, bit 0 lowest on the right.
                    int bits = Part.Counted(splitter.Width);
                    double half = Half(bits);
                    Left("in", 0, -0.5);
                    for (int i = 0; i < bits; i++)
                        Right("o" + i, Row(bits - 1 - i, bits), 0.5);
                    return new PartOutline(-0.5, -half, 0.5, half, pins);
                }

                case JoinerPart joiner:
                {
                    int bits = Part.Counted(joiner.Width);
                    double half = Half(bits);
                    for (int i = 0; i < bits; i++)
                        Left("i" + i, Row(bits - 1 - i, bits), -0.5);
                    Right("out", 0, 0.5);
                    return new PartOutline(-0.5, -half, 0.5, half, pins);
                }

                case OperatorPart op:
                {
                    // A box with its symbol, as a block's: a and b down the left, y on the right; a shift's amount on top, as an
                    // ALU's control is drawn (Part 15h-3).
                    if (Operators.IsUnary(op.Op) || Operators.IsShift(op.Op))
                        Left("a", 0, -1.5);
                    else
                    {
                        Left("a", Row(0, 2), -1.5);
                        Left("b", Row(1, 2), -1.5);
                    }
                    Right("y", 0, 1.5);
                    if (Operators.IsShift(op.Op))
                    {
                        Up("b", 0, 1.0);
                        return new PartOutline(-1.5, -0.5, 1.5, 1.0, pins);
                    }
                    return new PartOutline(-1.5, -0.5, 1.5, 0.5, pins);
                }

                case LampPart _:
                    Left("in", 0, -0.75);
                    return new PartOutline(-0.75, -0.35, 0.35, 0.35, pins);

                case BlockPart _:
                case BoxPart _:
                {
                    // A box with its ports' names inside, inputs down the left and outputs down the right, its inouts under
                    // them (Part 14g); a drawing of code's box the same (Part 15h-4).
                    var inputs = new List<Pin>();
                    var outputs = new List<Pin>();
                    var inouts = new List<Pin>();
                    foreach (Pin pin in part.Pins(project))
                        (pin.Direction == PinDirection.In ? inputs : pin.Direction == PinDirection.InOut ? inouts : outputs).Add(pin);
                    outputs.AddRange(inouts);

                    int most = Math.Max(1, Math.Max(inputs.Count, outputs.Count));
                    double half = Half(most), wide = BlockHalfWidth;
                    if (placement.Width > 0)
                        wide = Math.Max(wide, Math.Min(MaxSize, placement.Width) / 2);
                    if (placement.Height > 0)
                        half = Math.Max(half, Math.Min(MaxSize, placement.Height) / 2);
                    double pitch = SidePitch(2 * half, most);
                    for (int i = 0; i < inputs.Count; i++)
                        Left(inputs[i].Name, Row(i, inputs.Count, pitch), -wide);
                    for (int i = 0; i < outputs.Count; i++)
                        Right(outputs[i].Name, Row(i, outputs.Count, pitch), wide);
                    return new PartOutline(-wide, -half, wide, half, pins);
                }

                default:
                    return new PartOutline(-0.5, -0.5, 0.5, 0.5, pins);
            }
        }

        /// <summary>How far above and below the middle a side of <paramref name="pins"/> pins reaches.</summary>
        public static double Half(int pins) => Math.Max(0.5, (pins - 1) * Pitch / 2 + 0.25);

        /// <summary>Pin <paramref name="index"/> of <paramref name="count"/> on one side, the first at the top.</summary>
        public static double Row(int index, int count) => Row(index, count, Pitch);

        /// <summary>Pin <paramref name="index"/> of <paramref name="count"/> on one side, <paramref name="pitch"/> apart, the first at the top.</summary>
        public static double Row(int index, int count, double pitch) => ((count - 1) / 2.0 - index) * pitch;

        /// <summary>Half a block's natural width, in cells: its pins a cell and a half out from its middle.</summary>
        public const double BlockHalfWidth = 1.5;

        /// <summary>
        /// The pitch a block <paramref name="height"/> cells tall spreads its pins at, <paramref name="most"/> on its fuller side:
        /// <see cref="Pitch"/> at natural height, and the largest half-cell step that fits, its outermost pins a quarter cell in.
        /// </summary>
        public static double SidePitch(double height, int most)
        {
            if (most <= 1)
                return Pitch;
            double fits = (height - 0.5) / (most - 1);
            return Math.Max(Pitch, Math.Floor(fits / SizeStep + 1e-9) * SizeStep);
        }

        /// <summary>How wide a port's box is: room for its name, and its width when it is a bus.</summary>
        public static double PortWidth(PortPart port)
        {
            int characters = (port.Name ?? "").Length + (port.Width > 1 ? 3 + (port.Width - 1).ToString().Length + 1 : 0);
            return Math.Ceiling(Math.Max(1.0, 0.5 + characters * 0.17) * 4) / 4;
        }
    }
}
