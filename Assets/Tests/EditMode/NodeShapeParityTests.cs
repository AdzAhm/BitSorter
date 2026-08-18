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
    /// which is what the palette draws. Two switch statements over the same six cases will drift, and
    /// the drift would be invisible -- an interface quietly promising one shape and delivering
    /// another, with every test still green.
    /// </remarks>
    public class NodeShapeParityTests
    {
        private static readonly GateKind[] EveryKind =
        {
            GateKind.Not, GateKind.And, GateKind.Or, GateKind.Xor, GateKind.Nand, GateKind.Nor,
        };

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

        [Test]
        public void EveryKindHasASprite()
        {
            foreach (GateKind kind in EveryKind)
                Assert.IsNotNull(NodeShapes.SpriteFor(kind), GatePalette.Label(kind));
        }
    }
}
