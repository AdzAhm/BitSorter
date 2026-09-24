using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A palette icon must look exactly like the gate it places.
    /// </summary>
    /// <remarks>
    /// <see cref="NodeShapes"/> now answers the same question twice: once for a live
    /// <see cref="Node"/>, which is what the board draws, and once for a <see cref="GateKind"/>,
    /// which is what the palette draws. Two switch statements over the same cases will drift, and
    /// the drift would be invisible -- an interface quietly promising one shape and delivering
    /// another, with every test still green.
    ///
    /// The list of kinds is taken from the enum rather than written out. It used to be written out,
    /// and when the register was added it was the one part these tests did not cover -- the check
    /// that exists to catch a part being forgotten, quietly forgetting a part.
    /// </remarks>
    public class NodeShapeParityTests
    {
        private static readonly GateKind[] EveryKind =
            (GateKind[])System.Enum.GetValues(typeof(GateKind));

        [Test]
        public void EveryPaletteIcon_MatchesTheGateItPlaces()
        {
            foreach (GateKind kind in EveryKind)
            {
                Node placed = GatePalette.Create(kind);

                Assert.AreSame(NodeShapes.SpriteFor(placed), NodeShapes.SpriteFor(kind),
                    $"{GatePalette.Label(kind)} draws a different silhouette in the palette");
            }
        }

        [Test]
        public void EveryPaletteColour_MatchesTheGateItPlaces()
        {
            foreach (GateKind kind in EveryKind)
            {
                Node placed = GatePalette.Create(kind);

                Assert.AreEqual(NodeShapes.ColourFor(placed), NodeShapes.ColourFor(kind),
                    $"{GatePalette.Label(kind)} is a different colour in the palette");
            }
        }

        [Test]
        public void NoTwoGateKinds_ShareASilhouette()
        {
            // Shape carries the meaning here -- NodeShapes says so outright, because bloom washes
            // colour towards white exactly where the glow is strongest. Two gates with one silhouette
            // would be indistinguishable at the moment they matter most.
            for (int i = 0; i < EveryKind.Length; i++)
            {
                for (int j = i + 1; j < EveryKind.Length; j++)
                {
                    Assert.AreNotSame(
                        NodeShapes.SpriteFor(EveryKind[i]), NodeShapes.SpriteFor(EveryKind[j]),
                        $"{GatePalette.Label(EveryKind[i])} and {GatePalette.Label(EveryKind[j])} " +
                        "share a shape");
                }
            }
        }

        /// <summary>
        /// The AND gate is the textbook D -- square at the back, round at the front -- and NAND is
        /// the same D with its bubble.
        /// </summary>
        /// <remarks>
        /// Asked for by name: a student reads these symbols in every lecture, and a rounded square
        /// was a shape the game had made up. Checked at the corners, where the D differs from both
        /// the square it replaced and the OR family's pointed front.
        /// </remarks>
        [Test]
        public void TheAndGate_IsTheTextbookD()
        {
            // The gates draw these, in whatever style the look fills bodies with.
            Assert.AreSame(ProceduralSprites.DShape(Look.Current.Bodies), NodeShapes.SpriteFor(GateKind.And),
                "the AND gate is not drawn as the D");
            Assert.AreSame(ProceduralSprites.DShapeBubble(Look.Current.Bodies), NodeShapes.SpriteFor(GateKind.Nand),
                "the NAND gate is not drawn as the D with a bubble");

            // The silhouettes, read off the filled bodies: a glass style thins the middle, never the edge.
            Sprite and = ProceduralSprites.DShape(BodyStyle.Filled);
            Sprite nand = ProceduralSprites.DShapeBubble(BodyStyle.Filled);

            Assert.Greater(AlphaAt(and, -0.8f, 0.8f), 0.5f, "the AND's back corners should be square");
            Assert.Greater(AlphaAt(and, -0.8f, -0.8f), 0.5f, "the AND's back corners should be square");
            Assert.Less(AlphaAt(and, 0.8f, 0.8f), 0.5f, "the AND's front should be round, not square");
            Assert.Less(AlphaAt(and, 0.8f, -0.8f), 0.5f, "the AND's front should be round, not square");
            Assert.Greater(AlphaAt(and, 0.84f, 0f), 0.5f, "the AND's round front should reach its full width");

            Assert.Greater(AlphaAt(nand, -0.6f, 0.6f), 0.5f, "the NAND's back corners should be square");
            Assert.Greater(AlphaAt(nand, 0.9f, 0f), 0.5f, "the NAND has no bubble at its output");
        }

        /// <summary>A sprite's coverage at a point in the -1..1 space its shape was drawn in.</summary>
        private static float AlphaAt(Sprite sprite, float x, float y)
        {
            Texture2D texture = sprite.texture;
            int px = Mathf.Clamp((int)((x + 1f) * 0.5f * texture.width), 0, texture.width - 1);
            int py = Mathf.Clamp((int)((y + 1f) * 0.5f * texture.height), 0, texture.height - 1);
            return texture.GetPixel(px, py).a;
        }

        [Test]
        public void EveryKindHasASprite()
        {
            foreach (GateKind kind in EveryKind)
                Assert.IsNotNull(NodeShapes.SpriteFor(kind), GatePalette.Label(kind));
        }
    }
}
