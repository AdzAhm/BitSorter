using System;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// A bit in flight is the brightest thing on the board, and it is the only thing that is.
    /// </summary>
    /// <remarks>
    /// The board renders to an HDR target and bloom keeps whatever is brighter than its threshold,
    /// so "what glows" is decided by nothing more than which colour is the largest number. That
    /// was not a decision anyone had made: five of the nine node colours peak at exactly 1.00,
    /// the same as a one, so gates bloomed exactly as hard as bits and a NOT gate rendered as a
    /// featureless bright disc with its silhouette lost inside the glow.
    ///
    /// The rule pinned here is the design one rather than the renderer's: a bit outshines every
    /// gate. It says nothing about the threshold itself, which lives in a volume profile -- a
    /// constant here mirroring that number would be a second copy of it, and the drift would be
    /// silent in exactly the way this whole arrangement already failed once.
    /// </remarks>
    public class BitEmissionTests
    {
        private static readonly Bit[] BothValues = { Bit.Zero, Bit.One };

        /// <summary>Every colour a node can be drawn in.</summary>
        private static GateKind[] EveryKind => (GateKind[])Enum.GetValues(typeof(GateKind));

        [Test]
        public void ABitInFlight_OutshinesEveryGate()
        {
            foreach (Bit value in BothValues)
            {
                float bit = BitVisuals.Brightness(
                    BitVisuals.Emissive(BitVisuals.ColourFor(value)));

                foreach (GateKind kind in EveryKind)
                {
                    float gate = BitVisuals.Brightness(NodeShapes.ColourFor(kind));

                    Assert.Greater(bit, gate,
                        $"a {value} is drawn at {bit:F2} and a {kind} at {gate:F2}, so the gate " +
                        "blooms at least as hard as the bit crossing it");
                }
            }
        }

        /// <summary>
        /// Every bit in flight crosses the bloom threshold, and no node colour does.
        /// </summary>
        /// <remarks>
        /// The threshold is <see cref="BitVisuals.BloomThreshold"/>, which the scene builder writes
        /// into the volume profile -- so this asks the same number the renderer is given, rather
        /// than a copy of it. A zero clears it by the thinnest margin on the board, which is what
        /// makes it glimmer where a one blazes; lowering the lift by a few percent would drop it
        /// back to the small grey dot a player could lose.
        /// </remarks>
        [Test]
        public void OnlyBitsInFlight_CrossTheBloomThreshold()
        {
            foreach (Bit value in BothValues)
            {
                float bit = BitVisuals.Brightness(BitVisuals.Emissive(BitVisuals.ColourFor(value)));

                Assert.Greater(bit, BitVisuals.BloomThreshold,
                    $"a {value} in flight is drawn at {bit:F3} and does not reach the bloom " +
                    $"threshold of {BitVisuals.BloomThreshold}, so it will not glow");
            }

            foreach (GateKind kind in EveryKind)
            {
                float gate = BitVisuals.Brightness(NodeShapes.ColourFor(kind));

                Assert.LessOrEqual(gate, BitVisuals.BloomThreshold,
                    $"a {kind} is drawn at {gate:F2}, over the bloom threshold, so it blooms like a " +
                    "bit and its silhouette goes");
            }
        }

        /// <summary>
        /// A one outshines a zero, because one of them is the interesting value.
        /// </summary>
        /// <remarks>
        /// Both glow -- a zero is a value travelling along a wire, not the absence of one, and it
        /// used to peak below the threshold and read as a small grey dot that was easy to lose.
        /// But they are not equal, and which is brighter is the one place brightness is allowed to
        /// carry meaning on this board.
        /// </remarks>
        [Test]
        public void AOne_IsBrighterThanAZero()
        {
            float one = BitVisuals.Brightness(BitVisuals.Emissive(BitVisuals.One));
            float zero = BitVisuals.Brightness(BitVisuals.Emissive(BitVisuals.Zero));

            Assert.Greater(one, zero, "a one should be the brighter of the two");
        }

        /// <summary>
        /// The lift is applied to the drawn colour, not folded into the colour itself.
        /// </summary>
        /// <remarks>
        /// A bit is drawn in three places: travelling, sitting in the port it landed in, and
        /// inside a register that is holding it. Only the travelling one is lifted -- the other
        /// two sit on a body, and an HDR disc on a pale body is the blown-out slab the register's
        /// colour was chosen to avoid.
        /// </remarks>
        [Test]
        public void TheBitsOwnColour_IsLeftAlone()
        {
            Assert.LessOrEqual(BitVisuals.Brightness(BitVisuals.One), 1f,
                "the bit's own colour has been lifted, so every place it is drawn will glow");

            Assert.LessOrEqual(BitVisuals.Brightness(BitVisuals.Zero), 1f,
                "the bit's own colour has been lifted, so every place it is drawn will glow");
        }
    }
}
