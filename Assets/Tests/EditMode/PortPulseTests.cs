using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="PortPulse"/>: the ring a bit leaves on a block's port starts as the socket, grows,
    /// and ends fully transparent at exactly its length.
    /// </summary>
    public class PortPulseTests
    {
        [Test]
        public void APulse_StartsAsTheSocket_AtItsBrightest()
        {
            Assert.AreEqual(PortPulse.StartScale, PortPulse.ScaleAt(0f));
            Assert.AreEqual(PortPulse.StartAlpha, PortPulse.AlphaAt(0f));
            Assert.IsFalse(PortPulse.IsOver(0f));
        }

        /// <summary>
        /// It ends on its final value, never on its last frame: transparent and at its widest from
        /// the moment it is over, whatever frame lands there.
        /// </summary>
        [Test]
        public void APulse_EndsTransparent_AtExactlyItsLength_AndStaysThere()
        {
            Assert.AreEqual(0f, PortPulse.AlphaAt(PortPulse.Seconds));
            Assert.AreEqual(PortPulse.EndScale, PortPulse.ScaleAt(PortPulse.Seconds), 1e-5f);
            Assert.IsTrue(PortPulse.IsOver(PortPulse.Seconds));

            Assert.AreEqual(0f, PortPulse.AlphaAt(PortPulse.Seconds * 3f), "past its length it came back");
            Assert.AreEqual(PortPulse.EndScale, PortPulse.ScaleAt(PortPulse.Seconds * 3f), 1e-5f);
        }

        /// <summary>
        /// The ring is exactly empty in its middle and its corners, and solid in its band: drawn a
        /// gate wide, anything else showed the square it is drawn on (2026-10-06).
        /// </summary>
        [Test]
        public void ThePulsesRing_IsEmptyInsideAndOutside_AndSolidInItsBand()
        {
            UnityEngine.Texture2D texture = ProceduralSprites.PulseRing().texture;
            int size = texture.width;

            Assert.AreEqual(0f, texture.GetPixel(size / 2, size / 2).a, "the middle is not empty");
            Assert.AreEqual(0f, texture.GetPixel(0, 0).a, "a corner is not empty");
            Assert.AreEqual(0f, texture.GetPixel(size - 1, size / 2).a, "the edge is not empty");

            // The band, three quarters of the way out from the middle.
            int band = size / 2 + UnityEngine.Mathf.RoundToInt(size / 2f * 0.76f);
            Assert.Greater(texture.GetPixel(band, size / 2).a, 0.9f, "the band is not solid");
        }

        [Test]
        public void APulse_GrowsAndFades_AllTheWay()
        {
            float lastScale = PortPulse.ScaleAt(0f);
            float lastAlpha = PortPulse.AlphaAt(0f);

            for (float age = 0.01f; age <= PortPulse.Seconds; age += 0.01f)
            {
                Assert.GreaterOrEqual(PortPulse.ScaleAt(age), lastScale, $"it shrank at {age}");
                Assert.LessOrEqual(PortPulse.AlphaAt(age), lastAlpha, $"it brightened at {age}");

                lastScale = PortPulse.ScaleAt(age);
                lastAlpha = PortPulse.AlphaAt(age);
            }
        }
    }
}
