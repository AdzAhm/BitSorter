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
    }
}
