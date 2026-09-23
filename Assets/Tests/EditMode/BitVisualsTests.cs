using NUnit.Framework;
using UnityEngine;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The arrival-squash curve. Most of the visual pass is only checkable by eye, but this is
    /// plain maths and a sign error here would invert or collapse every bit on screen.
    /// </summary>
    public class BitVisualsTests
    {
        private const float Size = 0.4f;

        [Test]
        public void NoSquash_ForMostOfTheJourney()
        {
            Assert.AreEqual(0f, BitVisuals.SquashAmount(0f), 0.0001f);
            Assert.AreEqual(0f, BitVisuals.SquashAmount(0.5f), 0.0001f);
            Assert.AreEqual(0f, BitVisuals.SquashAmount(1f - BitVisuals.SquashWindow), 0.0001f);
        }

        [Test]
        public void SquashPeaksExactlyOnArrival()
        {
            Assert.AreEqual(1f, BitVisuals.SquashAmount(1f), 0.0001f);
            Assert.Less(BitVisuals.SquashAmount(0.95f), 1f);
            Assert.Greater(BitVisuals.SquashAmount(0.95f), 0f);
        }

        [Test]
        public void SquashRisesMonotonically()
        {
            float previous = -1f;

            for (float p = 0f; p <= 1.0001f; p += 0.02f)
            {
                float amount = BitVisuals.SquashAmount(p);
                Assert.GreaterOrEqual(amount, previous, $"squash went backwards at {p}");
                previous = amount;
            }
        }

        [Test]
        public void ScaleCompressesAlongTravel_AndBulgesAcross()
        {
            Vector2 travelling = BitVisuals.ScaleAt(0f, Size);
            Vector2 arriving = BitVisuals.ScaleAt(1f, Size);

            Assert.AreEqual(Size, travelling.x, 0.0001f, "no squash while travelling");
            Assert.AreEqual(Size, travelling.y, 0.0001f);

            Assert.Less(arriving.x, travelling.x, "should compress along the wire");
            Assert.Greater(arriving.y, travelling.y, "should bulge across the wire");
        }

        [Test]
        public void ScaleStaysPositive_AcrossAndBeyondTheRange()
        {
            // Progress is clamped inside, so out-of-range values must not flip the sprite.
            foreach (float p in new[] { -0.5f, 0f, 0.5f, 0.9f, 1f, 1.5f })
            {
                Vector2 scale = BitVisuals.ScaleAt(p, Size);

                Assert.Greater(scale.x, 0f, $"non-positive x scale at {p}");
                Assert.Greater(scale.y, 0f, $"non-positive y scale at {p}");
            }
        }

        /// <summary>
        /// A digit cannot turn to face its wire, so its squash is laid onto the screen's axes --
        /// exactly as a turned dot's on a level or plumb wire, and evenly on a diagonal one.
        /// </summary>
        [Test]
        public void AnUprightBit_SquashesAsATurnedOneWould_OnAStraightWire()
        {
            Vector2 arriving = BitVisuals.ScaleAt(1f, Size);

            Vector2 level = BitVisuals.Upright(arriving, Vector2.left * 2f);
            Assert.AreEqual(arriving.x, level.x, 1e-5f, "along a level wire is x");
            Assert.AreEqual(arriving.y, level.y, 1e-5f, "across a level wire is y");

            Vector2 plumb = BitVisuals.Upright(arriving, Vector2.down * 3f);
            Assert.AreEqual(arriving.y, plumb.x, 1e-5f, "across a plumb wire is x");
            Assert.AreEqual(arriving.x, plumb.y, 1e-5f, "along a plumb wire is y");

            Vector2 diagonal = BitVisuals.Upright(arriving, new Vector2(1f, -1f));
            Assert.AreEqual(diagonal.x, diagonal.y, 1e-5f, "a diagonal wire should squash a digit evenly");

            Assert.AreEqual(arriving, BitVisuals.Upright(arriving, Vector2.zero),
                "a wire with no length gives no direction to squash along");
        }
    }
}
