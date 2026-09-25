using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>What a look is, and the one conversion every new look's alphas go through.</summary>
    public class LookTests
    {
        /// <summary>
        /// Classic changes nothing: every knob a look adds is a share of what the scene already
        /// says, and Classic's share is all of it.
        /// </summary>
        [Test]
        public void Classic_LeavesTheSceneAsItIs()
        {
            Look classic = Look.Classic;

            Assert.AreEqual(1f, classic.TrailLength);
            Assert.AreEqual(1f, classic.GateGlow);
            Assert.AreEqual(1f, classic.FixtureGlow);
            Assert.AreEqual(1f, classic.BoardUnits);
            Assert.AreEqual(1f, classic.BoardLineWidth);
            Assert.AreEqual(1f, classic.GridMarkSize);
            Assert.AreSame(Palette.Classic, classic.Colours);
        }

        /// <summary>
        /// In every look, a wire's delay number sits clear of the bits passing along that wire.
        /// </summary>
        /// <remarks>
        /// Where a digit passed under the number, every 0 went by with a 1 printed over its head.
        /// The number was made a third larger after a playtest said it was too small, and its
        /// distance from the wire is now worked out from the bit and the pill; this holds that
        /// working to its purpose. Measured against the bit's box on a straight wire -- a digit's
        /// ink is narrower than its box, which is the slack a diagonal wire uses.
        /// </remarks>
        [Test]
        public void EveryLook_KeepsAWiresNumberClearOfItsBits()
        {
            try
            {
                foreach (Look look in Look.All)
                {
                    Look.Use(look);

                    float halfABit = BitRenderer.DefaultBitSize * look.BitScale * 0.5f;

                    Assert.Greater(EdgeRenderer.LabelInnerEdge, halfABit,
                        $"in {look.Name} the wire's number starts {EdgeRenderer.LabelInnerEdge:F3} from the " +
                        $"wire and a bit reaches {halfABit:F3}, so the bits pass under the number");
                }
            }
            finally
            {
                Look.Use(null);
            }
        }

        /// <summary>Every look can be found by its name, and no two share one.</summary>
        [Test]
        public void EveryLook_IsNamedOnce()
        {
            foreach (Look look in Look.All)
            {
                Assert.IsNotNull(look.Colours, $"{look.Name} has no palette");
                Assert.AreSame(look, Look.Named(look.Name), $"{look.Name} is not the look its name finds");
            }

            Assert.IsNull(Look.Named("no such look"));
        }

        /// <summary>Nothing and everything are the same written as seen.</summary>
        [Test]
        public void Seen_KeepsTheEnds()
        {
            Assert.AreEqual(0f, Palette.Seen(0f), 1e-6f);
            Assert.AreEqual(1f, Palette.Seen(1f), 1e-6f);
        }

        /// <summary>
        /// The measured case: a black scrim written at 0.88 was seen as about 62% opaque, so asking
        /// for 62% has to give 0.88 back.
        /// </summary>
        [Test]
        public void Seen_UndoesTheLinearBlend()
        {
            Assert.AreEqual(0.88f, Palette.Seen(0.62f), 0.005f);
        }

        /// <summary>Asking for more opacity never gives less.</summary>
        [Test]
        public void Seen_Rises()
        {
            float previous = 0f;

            for (int step = 1; step <= 20; step++)
            {
                float alpha = Palette.Seen(step / 20f);
                Assert.GreaterOrEqual(alpha, previous);
                previous = alpha;
            }
        }
    }
}
