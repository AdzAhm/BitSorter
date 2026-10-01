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

        // -----------------------------------------------------------------
        // The top-right corner
        // -----------------------------------------------------------------

        private const float ThirteenHalfWidth = 6 * 2f + 1.4f;
        private const float NineRight = 4 * 2f + PortGeometry.NodeSize * 0.5f;
        private const float ThirteenRight = 6 * 2f + PortGeometry.NodeSize * 0.5f;

        /// <summary>The gap CameraFit adds to the corner's width, as to every side inset.</summary>
        private const float Gap = 8f;

        /// <summary>Where a world x lands on a 1920-wide screen 1080 tall.</summary>
        private static float ScreenXAt1080(Framing framing, float worldX) =>
            1920f * 0.5f + (worldX - framing.CameraX) * 1080f / (2f * framing.OrthographicSize);

        /// <summary>
        /// The corner takes in both badges with the keys under them, and free play's folded tab.
        /// </summary>
        [Test]
        public void TheTopRightCorner_TakesInTheBadgesAndTheTab()
        {
            var badges = UiRows.TopRightCorner(false);
            var tab = UiRows.TopRightCorner(true);

            Assert.GreaterOrEqual(badges.x, UiTheme.Margin + 2f * UiTheme.BadgeSize + UiTheme.BadgeGap,
                "the timing badge, the further in of the two, is outside the corner");
            Assert.GreaterOrEqual(badges.y, UiRows.BadgeKey.Offset + UiRows.BadgeKey.Height,
                "the keys under the badges are below the corner");
            Assert.GreaterOrEqual(tab.x, UiTheme.Margin + UiTheme.SetupTabSize.x, "the tab is wider than the corner");
            Assert.GreaterOrEqual(tab.y, UiRows.Panels.Offset + UiTheme.SetupTabSize.y, "the tab is below the corner");
        }

        /// <summary>
        /// A bin in the top-right cell that the framing would put under the badges, or under free
        /// play's folded tab, is framed clear of them -- and the board stays clear of the parts list
        /// and the banner.
        /// </summary>
        /// <remarks>
        /// At 1920 by 1080 the canvas is at scale 1, so the corner is its canvas size in pixels.
        /// The last case is a 13 by 7 board under a three-line banner, which already sets the size:
        /// the board moves left rather than shrinking.
        /// </remarks>
        [TestCase(false, 88f, false)]
        [TestCase(false, 120f, false)]
        [TestCase(true, 88f, false)]
        [TestCase(true, 88f, true)]
        [TestCase(true, 150f, true)]
        public void ABinUnderTheCorner_IsFramedClearOfIt(bool thirteen, float banner, bool tab)
        {
            float halfWidth = thirteen ? ThirteenHalfWidth : HalfWidth;
            float halfHeight = thirteen ? SevenRowHalfHeight : FiveRowHalfHeight;
            float top = thirteen ? SevenRowTop : FiveRowTop;
            float right = thirteen ? ThirteenRight : NineRight;

            float cornerWidth = UiRows.TopRightCorner(tab).x + Gap;
            float cornerHeight = UiRows.TopRightCorner(tab).y;

            Framing before = CameraFraming.Fit(
                halfWidth, halfHeight, top, Authored, 1920f, 1080f, 164f, 0f, banner, 0f);

            Assert.IsTrue(CameraFraming.Reaches(before, right, top, 1920f, 1080f, cornerWidth, cornerHeight),
                "sanity: framed without the corner, the top-right part is clear of it already");

            Framing framing = CameraFraming.Fit(
                halfWidth, halfHeight, top, Authored, 1920f, 1080f, 164f, 0f, banner, 0f,
                right, cornerWidth, cornerHeight);

            Assert.IsFalse(CameraFraming.Reaches(framing, right, top, 1920f, 1080f, cornerWidth, cornerHeight),
                "the top-right part is still drawn into the corner");
            Assert.GreaterOrEqual(ScreenXAt1080(framing, -halfWidth), 164f - 1e-2f,
                "the board's left edge went under the parts list");
            Assert.LessOrEqual(ScreenY(framing, top, 1080f), 1080f - banner + 1e-2f,
                "the board's top row went under the banner");
            Assert.GreaterOrEqual(framing.OrthographicSize, before.OrthographicSize,
                "keeping clear of the corner made the board bigger");
        }

        /// <summary>
        /// Wherever the top-right part does not reach into the corner, the framing is the one
        /// without it, to the last bit -- which is what holds every reference shot still.
        /// </summary>
        [Test]
        public void WhereTheCornerIsNotReached_TheFramingIsExactlyAsBefore()
        {
            float width = UiRows.TopRightCorner(false).x + Gap;
            float depth = UiRows.TopRightCorner(false).y;

            AssertUnchanged(1920f, 0f, 0f, 0f, 0f, "no bin in the cell, so no corner");
            AssertUnchanged(1920f, 0f, 0f, width, 60f, "a corner that stops above the top row");
            AssertUnchanged(2560f, 0f, 0f, width, depth, "a 21:9 screen, which keeps the authored framing");
            AssertUnchanged(1920f, 330f, 0f, width, depth, "a panel down the right that reaches further in");
            AssertUnchanged(1920f, 0f, 380f, width, depth, "the timing diagram open, which shrinks the board clear");
            AssertUnchanged(1920f, 0f, 0f, 1500f, depth, "a corner that would leave no room");
        }

        /// <summary>A 9 by 5 board 1080 tall under a one-line banner, with and without the corner.</summary>
        private static void AssertUnchanged(
            float screenWidth, float rightInset, float strip, float cornerWidth, float cornerHeight, string what)
        {
            Framing before = CameraFraming.Fit(
                HalfWidth, FiveRowHalfHeight, FiveRowTop, Authored, screenWidth, 1080f, 164f, rightInset, 88f, strip);
            Framing now = CameraFraming.Fit(
                HalfWidth, FiveRowHalfHeight, FiveRowTop, Authored, screenWidth, 1080f, 164f, rightInset, 88f, strip,
                NineRight, cornerWidth, cornerHeight);

            Assert.AreEqual(before.OrthographicSize, now.OrthographicSize, what);
            Assert.AreEqual(before.CameraX, now.CameraX, what);
            Assert.AreEqual(before.CameraY, now.CameraY, what);
        }
    }
}
