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
            if (node is NotGate) return ProceduralSprites.CircleBubble();
            if (node is NandGate) return ProceduralSprites.RoundedSquareBubble();
            if (node is NorGate) return ProceduralSprites.ShieldBubble();
            if (node is XorGate) return ProceduralSprites.ShieldArc();
            if (node is AndGate) return ProceduralSprites.RoundedSquare();
            if (node is OrGate) return ProceduralSprites.Shield();
            // A wide capsule, not a diamond: under bloom a diamond and NOT's circle both blurred
            // into the same round blob. Aspect ratio survives the glow where silhouette detail
            // does not, and no gate is anywhere near this wide.
            if (node is SourceNode) return ProceduralSprites.Capsule();
            if (node is SinkNode) return ProceduralSprites.Hexagon();

            return ProceduralSprites.RoundedSquare();   // pass-through and anything new
        }

        public static Color ColourFor(Node node)
        {
            if (node is SourceNode) return new Color(0.36f, 0.92f, 0.55f);   // green
            if (node is SinkNode) return new Color(0.98f, 0.44f, 0.44f);     // red
            if (node is XorGate) return new Color(0.42f, 0.68f, 1.00f);      // blue
            if (node is AndGate) return new Color(1.00f, 0.78f, 0.32f);      // amber
            if (node is OrGate) return new Color(0.76f, 0.54f, 1.00f);       // violet
            if (node is NandGate) return new Color(0.46f, 0.94f, 0.90f);     // teal
            if (node is NorGate) return new Color(0.90f, 0.88f, 0.48f);      // olive
            if (node is NotGate) return new Color(1.00f, 0.58f, 0.82f);      // pink

            return new Color(0.62f, 0.64f, 0.70f);
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
            switch (kind)
            {
                case GateKind.Not: return ProceduralSprites.CircleBubble();
                case GateKind.Nand: return ProceduralSprites.RoundedSquareBubble();
                case GateKind.Nor: return ProceduralSprites.ShieldBubble();
                case GateKind.Xor: return ProceduralSprites.ShieldArc();
                case GateKind.And: return ProceduralSprites.RoundedSquare();
                case GateKind.Or: return ProceduralSprites.Shield();
                default: return ProceduralSprites.RoundedSquare();
            }
        }

        /// <inheritdoc cref="SpriteFor(GateKind)"/>
        public static Color ColourFor(GateKind kind)
        {
            switch (kind)
            {
                case GateKind.Xor: return new Color(0.42f, 0.68f, 1.00f);
                case GateKind.And: return new Color(1.00f, 0.78f, 0.32f);
                case GateKind.Or: return new Color(0.76f, 0.54f, 1.00f);
                case GateKind.Nand: return new Color(0.46f, 0.94f, 0.90f);
                case GateKind.Nor: return new Color(0.90f, 0.88f, 0.48f);
                case GateKind.Not: return new Color(1.00f, 0.58f, 0.82f);
                default: return new Color(0.62f, 0.64f, 0.70f);
            }
        }
    }
}
