using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// Which silhouette and colour each node type gets.
    /// </summary>
    /// <remarks>
    /// Shape carries the meaning and colour is decoration, deliberately. Bloom washes colour
    /// towards white where it is strongest, so a scheme that relied on hue would be least readable
    /// exactly where the glow is brightest. Silhouettes survive that.
    /// </remarks>
    public static class NodeShapes
    {
        public static Sprite SpriteFor(Node node)
        {
            BodyStyle style = Look.Current.Bodies;

            if (node is NotGate) return ProceduralSprites.CircleBubble(style);
            if (node is NandGate) return ProceduralSprites.DShapeBubble(style);
            if (node is NorGate) return ProceduralSprites.ShieldBubble(style);
            if (node is XorGate) return ProceduralSprites.ShieldArc(style);
            if (node is AndGate) return ProceduralSprites.DShape(style);
            if (node is OrGate) return ProceduralSprites.Shield(style);
            // A wide capsule, not a diamond: under bloom a diamond and NOT's circle both blurred
            // into the same round blob. Aspect ratio survives the glow where silhouette detail
            // does not, and no gate is anywhere near this wide.
            if (node is SourceNode) return ProceduralSprites.Capsule(style);
            if (node is SinkNode) return ProceduralSprites.Hexagon(style);
            // Taller than it is wide, and the only shape that is: a register is not a gate, and the
            // silhouette has to say so before the colour or the label can.
            if (node is RegisterNode) return ProceduralSprites.FlipFlop(style);

            return ProceduralSprites.RoundedSquare(style);   // pass-through and anything new
        }

        public static Color ColourFor(Node node)
        {
            Palette p = Palette.Current;

            if (node is SourceNode) return p.Source;
            if (node is SinkNode) return p.Sink;
            if (node is XorGate) return p.Xor;
            if (node is AndGate) return p.And;
            if (node is OrGate) return p.Or;
            if (node is NandGate) return p.Nand;
            if (node is NorGate) return p.Nor;
            if (node is NotGate) return p.Not;

            // A cool slate, and deliberately the least saturated thing on the board: what is worth
            // looking at is the bit it holds, drawn inside it in that bit's own colour. Near-white
            // was tried first and was wrong for the reason the stalled-gate glow was wrong -- a
            // bright slab with the bit lost inside it, the largest bright area on the node
            // outshouting the small thing inside it that carries the meaning.
            if (node is RegisterNode) return p.Register;

            return p.OtherNode;
        }

        /// <summary>The silhouette a palette entry shows, matching the gate it places.</summary>
        /// <remarks>
        /// Keyed off <see cref="GateKind"/> rather than off a node, so a palette button needs no
        /// throwaway <see cref="Node"/> just to ask what it looks like. Parity with the node overload
        /// is pinned by a test: an icon that stopped matching the gate it places would be a quietly
        /// misleading interface, and nothing else would catch it.
        /// </remarks>
        public static Sprite SpriteFor(GateKind kind)
        {
            BodyStyle style = Look.Current.Bodies;

            switch (kind)
            {
                case GateKind.Not: return ProceduralSprites.CircleBubble(style);
                case GateKind.Nand: return ProceduralSprites.DShapeBubble(style);
                case GateKind.Nor: return ProceduralSprites.ShieldBubble(style);
                case GateKind.Xor: return ProceduralSprites.ShieldArc(style);
                case GateKind.And: return ProceduralSprites.DShape(style);
                case GateKind.Or: return ProceduralSprites.Shield(style);
                case GateKind.Register: return ProceduralSprites.FlipFlop(style);
                default: return ProceduralSprites.RoundedSquare(style);
            }
        }

        /// <inheritdoc cref="SpriteFor(GateKind)"/>
        public static Color ColourFor(GateKind kind)
        {
            Palette p = Palette.Current;

            switch (kind)
            {
                case GateKind.Xor: return p.Xor;
                case GateKind.And: return p.And;
                case GateKind.Or: return p.Or;
                case GateKind.Nand: return p.Nand;
                case GateKind.Nor: return p.Nor;
                case GateKind.Not: return p.Not;
                case GateKind.Register: return p.Register;
                default: return p.OtherNode;
            }
        }
    }
}
