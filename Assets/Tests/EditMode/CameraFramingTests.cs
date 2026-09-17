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
    }
}
