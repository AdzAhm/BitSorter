using BitSorter.Board;

namespace BitSorter.ViewModel
{
    /// <summary>
    /// Where a symbol's parts are, in the part's own cells: how far its front reaches, whether it
    /// carries a bubble, where the OR family's hollow back is, and where each pin's lead meets the body.
    /// </summary>
    /// <remarks>
    /// Shared by the layout, which runs every wire on to the body (<see cref="DocumentSchematic"/>),
    /// and the drawing, which draws the bodies these describe -- so a wire's end and the outline it
    /// meets cannot disagree. They lived with the drawing until the view-model left Unity (Part 9).
    /// </remarks>
    public static class SymbolGeometry
    {
        /// <summary>
        /// How far a pin's lead reaches from the pin to a body that steps back from its pins to make one, in cells: a port's
        /// box, a constant's and a block's (Part 15g). A gate's front and back already sit half a cell in from its pins. The
        /// pins stay where they were, so no wire, route or saved circuit moves; the bodies are drawn further in.
        /// </summary>
        /// <remarks>
        /// Ahmad asked for "a small tiny line that we can wire from" on every part (2026-10-28): a port's box and a
        /// constant's stood on their pins, and a block's a quarter of a cell from them, so nothing on those said where a
        /// wire could start.
        /// </remarks>
        public const double Lead = 0.5;

        /// <summary>Half a block's box, in cells: its pins, a cell and a half out, have a <see cref="Lead"/> each.</summary>
        public const double BlockHalf = 1.0;

        /// <summary>Where a pin's lead meets the body, in the part's own cells: where its symbol's outline is, as the drawing draws it.</summary>
        public static (double X, double Y) LeadEnd(PartLayout part, PinSpot pin)
        {
            double x = pin.At.X, y = pin.At.Y;
            bool left = pin.Facing == Facing.Left, down = pin.Facing == Facing.Down;

            switch (part.Symbol)
            {
                case PartSymbol.Register:
                    return down ? (x, -0.75) : (left ? -0.6 : 0.6, y);

                case PartSymbol.Mux:
                {
                    double half = PartGeometry.Half(part.Local.Pins.Count - 2) + 0.25;
                    return down ? (x, -(half - 0.15)) : (left ? -0.5 : 0.5, y);
                }

                case PartSymbol.Constant:
                    return (x - Lead, y);

                case PartSymbol.Splitter:
                case PartSymbol.Joiner:
                    return (0, y);

                case PartSymbol.Lamp:
                    return (-0.3, y);

                case PartSymbol.Block:
                    return (left ? -BlockHalf : BlockHalf, y);

                case PartSymbol.TriBuffer:
                case PartSymbol.TriInverter:
                    // The control meets the triangle's lower edge where it crosses the middle.
                    if (down)
                        return (x, -0.32 + 0.32 * (x + 0.5) / (Tip(part.Symbol) + 0.5));
                    return left ? (-0.5, y) : (Tip(part.Symbol) + (Inverts(part.Symbol) ? 2 * Bubble(part) : 0), y);

                default:
                    if (left)
                        return (Shield(part.Symbol) ? BackX(-0.5, y, PartGeometry.Half(part.Local.Pins.Count - 1)) : -0.5, y);
                    return (Tip(part.Symbol) + (Inverts(part.Symbol) ? 2 * Bubble(part) : 0), y);
            }
        }

        /// <summary>Where a port's lead meets its box, in the port's own cells: a <see cref="Lead"/> in from its pin.</summary>
        public static (double X, double Y) PortLeadEnd(PinSpot pin) =>
            (pin.Facing == Facing.Left ? pin.At.X + Lead : pin.At.X - Lead, pin.At.Y);

        /// <summary>
        /// A port's box in its own cells: its outline -- what routes keep clear of, unchanged -- with the lead taken off the
        /// side its pin is on. <see cref="PartGeometry.PortWidth"/> gives a name 0.17 of a cell a character and half a cell
        /// more, and the type draws one in about 0.12: the box gives up the half cell and the name still fits.
        /// </summary>
        public static (double Left, double Bottom, double Right, double Top) PortBox(PartOutline local)
        {
            bool pinLeft = local.Pins.Count > 0 && local.Pins[0].Facing == Facing.Left;
            return pinLeft
                ? (local.Left + Lead, local.Bottom, local.Right, local.Top)
                : (local.Left, local.Bottom, local.Right - Lead, local.Top);
        }

        public static bool Inverts(PartSymbol symbol) =>
            symbol == PartSymbol.Not || symbol == PartSymbol.Nand || symbol == PartSymbol.Nor ||
            symbol == PartSymbol.Xnor || symbol == PartSymbol.TriInverter;

        public static bool Shield(PartSymbol symbol) =>
            symbol == PartSymbol.Or || symbol == PartSymbol.Nor || symbol == PartSymbol.Xor || symbol == PartSymbol.Xnor;

        /// <summary>Where a gate's body ends at the front, in cells: a bubble, if any, sits just past it.</summary>
        public static double Tip(PartSymbol symbol) =>
            symbol == PartSymbol.Not || symbol == PartSymbol.Buffer || symbol == PartSymbol.TriBuffer || symbol == PartSymbol.TriInverter
                ? 0.25 : 0.5;

        /// <summary>How far past the tip a bubble's middle is, in cells.</summary>
        public static double Bubble(PartLayout part) => SchematicStyle.BubbleRadius / part.Cell;

        /// <summary>The OR family's hollow back, <paramref name="at"/> cells from the middle at its ends, bowed in by a seventh of a cell.</summary>
        public static double BackX(double at, double v, double half) => at + 0.15 * (1 - (v * v) / (half * half));
    }
}
