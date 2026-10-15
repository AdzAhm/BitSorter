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
                    return (x, y);

                case PartSymbol.Splitter:
                case PartSymbol.Joiner:
                    return (0, y);

                case PartSymbol.Lamp:
                    return (-0.3, y);

                case PartSymbol.Block:
                    return (left ? -1.25 : 1.25, y);

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
