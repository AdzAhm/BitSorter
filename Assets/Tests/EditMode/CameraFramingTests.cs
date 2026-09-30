using NUnit.Framework;
using BitSorter.View;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Framing the board in the space the interface leaves free.
    /// </summary>
    public class CameraFramingTests
    {
        // The shipped board: nine columns of two-unit cells, plus a margin.
        private const float HalfWidth = 4 * 2f + 1.4f;
        private const float Authored = 5.5f;

        private const float Width = 1200f;
        private const float Height = 750f;

        /// <summary>Where a world x lands on screen, for a given framing.</summary>
        private static float ScreenX(Framing framing, float worldX) =>
            Width * 0.5f + (worldX - framing.CameraX) * Height / (2f * framing.OrthographicSize);

        [Test]
        public void WithNothingInTheWay_ItFitsTheBoardAsBefore()
        {
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, Width, Height, 0f, 0f);

            Assert.AreEqual(0f, framing.CameraX, 1e-4f, "nothing at the sides, so nothing to move away from");
            Assert.AreEqual(HalfWidth * Height / Width, framing.OrthographicSize, 1e-4f,
                "16:10 is narrower than the authored framing, so the width decides");
        }

        /// <summary>
        /// A seven-row board on a screen wide enough that its width never binds is still framed
        /// tall enough to show its bottom row's names.
        /// </summary>
        [Test]
        public void ATallBoard_OnAWideScreen_IsFramedTallEnough()
        {
            const float halfHeight = 3 * 2f + NodeRenderer.LabelReach + 0.2f;

            Framing framing = CameraFraming.Fit(HalfWidth, halfHeight, Authored, 2560f, 1080f, 0f, 0f);

            Assert.AreEqual(halfHeight, framing.OrthographicSize, 1e-4f);
        }

        /// <summary>The standard five rows fit the authored framing, so the height changes nothing for them.</summary>
        [Test]
        public void TheStandardBoard_IsNotMadeSmallerByItsHeight()
        {
            const float halfHeight = 2 * 2f + NodeRenderer.LabelReach + 0.2f;

            Assert.Less(halfHeight, Authored, "sanity: five rows fit the authored framing");

            Framing withHeight = CameraFraming.Fit(HalfWidth, halfHeight, Authored, 2400f, Height, 0f, 0f);
            Framing without = CameraFraming.Fit(HalfWidth, Authored, 2400f, Height, 0f, 0f);

            Assert.AreEqual(without.OrthographicSize, withHeight.OrthographicSize, 1e-5f);
        }

        /// <summary>
        /// A seven-row board on a wide screen shows its top row below a banner covering the top of
        /// the screen, where fitted by its height alone it reached under it.
        /// </summary>
        [Test]
        public void ATallBoard_ClearsTheBanner()
        {
            const float halfHeight = 3 * 2f + NodeRenderer.LabelReach + 0.2f;
            const float top = 3 * 2f + PortGeometry.NodeSize * 0.5f;
            const float banner = 88f;
            const float height = 1080f;

            Framing byHeight = CameraFraming.Fit(HalfWidth, halfHeight, Authored, 2560f, height, 0f, 0f);
            Assert.Greater(top, byHeight.OrthographicSize * (1f - 2f * banner / height),
                "sanity: fitted by its height alone, the top row reaches under the banner");

            Framing framing = CameraFraming.Fit(HalfWidth, halfHeight, top, Authored, 2560f, height, 0f, 0f, banner);

            // Where the top of the row lands, in pixels down from the top of the screen.
            float fromTheTop = (framing.OrthographicSize - top) * height / (2f * framing.OrthographicSize);
            Assert.GreaterOrEqual(fromTheTop, banner - 1e-2f, "the top row is under the banner");
        }

        /// <summary>
        /// Five rows already clear a banner of one or two lines, so the banner changes nothing for
        /// them -- every level on the standard board frames exactly as it did.
        /// </summary>
        [Test]
        public void TheStandardBoard_IsNotMadeSmallerByTheBanner()
        {
            const float halfHeight = 2 * 2f + NodeRenderer.LabelReach + 0.2f;
            const float top = 2 * 2f + PortGeometry.NodeSize * 0.5f;

            foreach (float banner in new[] { 88f, 109f })
            {
                Framing without = CameraFraming.Fit(HalfWidth, halfHeight, Authored, 1920f, 1080f, 180f, 0f);
                Framing with = CameraFraming.Fit(HalfWidth, halfHeight, top, Authored, 1920f, 1080f, 180f, 0f, banner);

                Assert.AreEqual(without.OrthographicSize, with.OrthographicSize, 1e-5f, $"a banner {banner}px deep");
                Assert.AreEqual(without.CameraX, with.CameraX, 1e-5f, $"a banner {banner}px deep");
            }
        }

        /// <summary>A banner covering most of the screen is ignored, as side insets that leave no room are.</summary>
        [Test]
        public void ABannerThatLeavesNoRoom_IsIgnored()
        {
            const float top = 3 * 2f + PortGeometry.NodeSize * 0.5f;

            Framing framing = CameraFraming.Fit(HalfWidth, 0f, top, Authored, 2560f, 1080f, 0f, 0f, 500f);
            Framing plain = CameraFraming.Fit(HalfWidth, 0f, Authored, 2560f, 1080f, 0f, 0f);

            Assert.AreEqual(plain.OrthographicSize, framing.OrthographicSize, 1e-4f);
        }

        [Test]
        public void AWideScreen_KeepsTheAuthoredFraming()
        {
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, 2400f, Height, 0f, 0f);

            Assert.AreEqual(Authored, framing.OrthographicSize, 1e-4f);
        }

        [Test]
        public void ALeftInset_KeepsTheBoardClearOfIt()
        {
            const float Left = 110f;
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, Width, Height, Left, 0f);

            Assert.GreaterOrEqual(ScreenX(framing, -HalfWidth), Left - 1e-3f,
                "the board's left edge is under the left inset");
            Assert.LessOrEqual(ScreenX(framing, HalfWidth), Width + 1e-3f,
                "the board's right edge is off the screen");
            Assert.Less(framing.CameraX, 0f, "the camera should move left, so the board moves right");
        }

        [Test]
        public void ARightInset_KeepsTheBoardClearOfIt()
        {
            const float Right = 300f;
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, Width, Height, 0f, Right);

            Assert.LessOrEqual(ScreenX(framing, HalfWidth), Width - Right + 1e-3f,
                "the board's right edge is under the right inset");
            Assert.GreaterOrEqual(ScreenX(framing, -HalfWidth), -1e-3f,
                "the board's left edge is off the screen");
        }

        [Test]
        public void BothInsets_CentreTheBoardBetweenThem()
        {
            const float Left = 110f;
            const float Right = 300f;
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, Width, Height, Left, Right);

            float left = ScreenX(framing, -HalfWidth);
            float right = ScreenX(framing, HalfWidth);

            Assert.GreaterOrEqual(left, Left - 1e-3f);
            Assert.LessOrEqual(right, Width - Right + 1e-3f);
            Assert.AreEqual((Left + Width - Right) * 0.5f, (left + right) * 0.5f, 1e-2f,
                "the board should sit in the middle of the free span");
        }

        [Test]
        public void InsetsThatLeaveNoRoom_AreIgnored()
        {
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, Width, Height, 700f, 450f);
            Framing plain = CameraFraming.Fit(HalfWidth, Authored, Width, Height, 0f, 0f);

            Assert.AreEqual(plain.OrthographicSize, framing.OrthographicSize, 1e-4f);
            Assert.AreEqual(plain.CameraX, framing.CameraX, 1e-4f);
        }

        [Test]
        public void ANoSizeScreen_FallsBackToTheAuthoredFraming()
        {
            Framing framing = CameraFraming.Fit(HalfWidth, Authored, 0f, 0f, 10f, 10f);

            Assert.AreEqual(Authored, framing.OrthographicSize);
            Assert.AreEqual(0f, framing.CameraX);
        }

        // -----------------------------------------------------------------
        // The timing diagram along the bottom
        // -----------------------------------------------------------------

        /// <summary>Where a world y lands, in pixels up from the bottom of the screen.</summary>
        private static float ScreenY(Framing framing, float worldY, float height) =>
            height * 0.5f + (worldY - framing.CameraY) * height / (2f * framing.OrthographicSize);

        private const float FiveRowHalfHeight = 2 * 2f + NodeRenderer.LabelReach + 0.2f;
        private const float FiveRowTop = 2 * 2f + PortGeometry.NodeSize * 0.5f;
        private const float SevenRowHalfHeight = 3 * 2f + NodeRenderer.LabelReach + 0.2f;
        private const float SevenRowTop = 3 * 2f + PortGeometry.NodeSize * 0.5f;

        /// <summary>
        /// With nothing along the bottom the framing is the one without the argument, to the last bit
        /// -- the camera stays at y = 0 -- because every reference shot is held to it.
        /// </summary>
        [Test]
        public void WithNothingAlongTheBottom_TheFramingIsExactlyAsBefore()
        {
            foreach (float banner in new[] { 0f, 88f, 150f })
            {
                foreach (float right in new[] { 0f, 330f })
                {
                    Framing before = CameraFraming.Fit(
                        HalfWidth, SevenRowHalfHeight, SevenRowTop, Authored, 1920f, 1080f, 164f, right, banner);
                    Framing now = CameraFraming.Fit(
                        HalfWidth, SevenRowHalfHeight, SevenRowTop, Authored, 1920f, 1080f, 164f, right, banner, 0f);

                    Assert.AreEqual(before.OrthographicSize, now.OrthographicSize, $"banner {banner}, right {right}");
                    Assert.AreEqual(before.CameraX, now.CameraX, $"banner {banner}, right {right}");
                    Assert.AreEqual(0f, now.CameraY, $"banner {banner}, right {right}");
                }
            }
        }

        /// <summary>
        /// A strip along the bottom lifts the board clear of it: the names under the bottom row stay
        /// above the strip, the top row stays under the banner, and the width still fits.
        /// </summary>
        [TestCase(1080f, 150f, 450f, false)]
        [TestCase(1080f, 110f, 330f, false)]
        [TestCase(929f, 150f, 420f, true)]
        [TestCase(1080f, 150f, 380f, true)]
        public void AStripAlongTheBottom_LiftsTheBoardClearOfIt(float height, float banner, float strip, bool sevenRows)
        {
            float halfHeight = sevenRows ? SevenRowHalfHeight : FiveRowHalfHeight;
            float top = sevenRows ? SevenRowTop : FiveRowTop;

            Framing framing = CameraFraming.Fit(
                HalfWidth, halfHeight, top, Authored, 1920f, height, 164f, 0f, banner, strip);

            Assert.GreaterOrEqual(ScreenY(framing, -halfHeight, height), strip - 1e-2f,
                "the board's bottom row, or the names under it, is under the strip");
            Assert.LessOrEqual(ScreenY(framing, top, height), height - banner + 1e-2f,
                "the board's top row is under the banner");

            float pixelsPerWorld = height / (2f * framing.OrthographicSize);
            Assert.LessOrEqual(2f * HalfWidth * pixelsPerWorld, 1920f - 164f + 1e-2f,
                "the board no longer fits across");
        }

        /// <summary>Opening the strip can only make the board smaller, never larger.</summary>
        [Test]
        public void AStripAlongTheBottom_NeverMakesTheBoardBigger()
        {
            foreach (float strip in new[] { 20f, 160f, 300f, 450f })
            {
                Framing without = CameraFraming.Fit(
                    HalfWidth, FiveRowHalfHeight, FiveRowTop, Authored, 1920f, 1080f, 164f, 0f, 88f);
                Framing with = CameraFraming.Fit(
                    HalfWidth, FiveRowHalfHeight, FiveRowTop, Authored, 1920f, 1080f, 164f, 0f, 88f, strip);

                Assert.GreaterOrEqual(with.OrthographicSize, without.OrthographicSize, $"a strip {strip}px tall");
            }
        }

        /// <summary>
        /// A strip that leaves under a quarter of the screen is ignored, as a banner or side panels
        /// that leave no room are: the board keeps its framing and the strip covers it.
        /// </summary>
        [Test]
        public void AStripThatLeavesNoRoom_IsIgnored()
        {
            Framing plain = CameraFraming.Fit(
                HalfWidth, FiveRowHalfHeight, FiveRowTop, Authored, 1920f, 1080f, 164f, 0f, 150f);
            Framing framing = CameraFraming.Fit(
                HalfWidth, FiveRowHalfHeight, FiveRowTop, Authored, 1920f, 1080f, 164f, 0f, 150f, 700f);

            Assert.AreEqual(plain.OrthographicSize, framing.OrthographicSize);
            Assert.AreEqual(0f, framing.CameraY);
        }
    }
}
