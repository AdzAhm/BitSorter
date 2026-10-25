using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// <see cref="ShellLayout"/> (Part 11b): the docked panels tile the window under the toolbar without overlapping,
    /// keep their widths in a small window while the circuit and the waves take what is left, and the dock's height is
    /// the student's within what the window has room for. And <see cref="SchematicView.Resize"/>: a window made another
    /// size keeps the zoom, or, fitted, fits again.
    /// </summary>
    public class ShellLayoutTests
    {
        private const float Toolbar = 236f;
        private static readonly Vec2 Full = new Vec2(1920f, 1080f), Browser = new Vec2(2230f, 930f);

        private static bool Overlap(Rect2 a, Rect2 b) =>
            a.xMin < b.xMax - 1e-3f && b.xMin < a.xMax - 1e-3f && a.yMin < b.yMax - 1e-3f && b.yMin < a.yMax - 1e-3f;

        private static Rect2[] Tiles(ShellRegions r) => new[] { r.Parts, r.Circuit, r.Side, r.Splitter, r.Dock, r.Status };

        [Test]
        public void ThePanels_TileTheWindowUnderTheToolbar_AndNoTwoOverlap()
        {
            foreach (Vec2 window in new[] { Full, Browser, new Vec2(1681f, 1261f) })
            {
                ShellRegions r = ShellLayout.Of(window, Toolbar, ShellLayout.DefaultDock);
                Rect2[] tiles = Tiles(r);
                for (int i = 0; i < tiles.Length; i++)
                {
                    Assert.GreaterOrEqual(tiles[i].yMin, Toolbar - 1e-3f, $"tile {i} reaches up under the toolbar in {window.x}x{window.y}");
                    Assert.LessOrEqual(tiles[i].yMax, window.y + 1e-3f, $"tile {i} runs off the bottom");
                    Assert.GreaterOrEqual(tiles[i].xMin, ShellLayout.Margin - 1e-3f);
                    Assert.LessOrEqual(tiles[i].xMax, window.x - ShellLayout.Margin + 1e-3f, $"tile {i} runs off the right");
                    for (int j = i + 1; j < tiles.Length; j++)
                        Assert.IsFalse(Overlap(tiles[i], tiles[j]), $"tiles {i} and {j} overlap in {window.x}x{window.y}");
                }

                Assert.AreEqual(window.y, r.Status.yMax, 1e-3f, "the status line is along the bottom");
                Assert.AreEqual(r.Dock.yMax, r.Status.yMin, 1e-3f, "the dock stands on it");
                Assert.AreEqual(r.Splitter.yMax, r.Dock.yMin, 1e-3f, "the splitter is right over the dock");
                Assert.AreEqual(r.Circuit.yMax, r.Splitter.yMin, 1e-3f, "and right under the circuit");
            }
        }

        [Test]
        public void TheDock_IsItsHeaderTheWavesAndTheLegend_TheWindowsWholeWidth()
        {
            ShellRegions r = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock);

            Assert.AreEqual(ShellLayout.DefaultDock, r.Dock.height, 1e-3f);
            Assert.AreEqual(Full.x - 2f * ShellLayout.Margin, r.Dock.width, 1e-3f, "the waves run under the side panels too");
            Assert.AreEqual(r.Dock.yMin, r.DockHeader.yMin, 1e-3f);
            Assert.AreEqual(r.DockHeader.yMax, r.Waves.yMin, 1e-3f);
            Assert.AreEqual(r.Waves.yMax, r.Legend.yMin, 1e-3f);
            Assert.AreEqual(r.Dock.yMax, r.Legend.yMax, 1e-3f, "the legend at its foot");
        }

        [Test]
        public void InASmallWindow_ThePanelsKeepTheirWidths_AndTheCircuitTakesWhatIsLeft()
        {
            ShellRegions wide = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock);
            ShellRegions narrow = ShellLayout.Of(new Vec2(1200f, 1080f), Toolbar, ShellLayout.DefaultDock);

            Assert.AreEqual(ShellLayout.PartsWidth, narrow.Parts.width, 1e-3f);
            Assert.AreEqual(ShellLayout.SideWidth, narrow.Side.width, 1e-3f);
            Assert.AreEqual(wide.Circuit.width - 720f, narrow.Circuit.width, 1e-3f, "every unit the window lost, the circuit gave");

            ShellRegions tiny = ShellLayout.Of(new Vec2(400f, 300f), Toolbar, ShellLayout.DefaultDock);
            foreach (Rect2 tile in Tiles(tiny))
                Assert.IsTrue(tile.width >= 0f && tile.height >= 0f, "a window too small for any of it still has no tile inside out");
        }

        [Test]
        public void TheDocksHeight_IsTheStudents_WithinWhatTheWindowHasRoomFor()
        {
            Assert.AreEqual(500f, ShellLayout.Of(Full, Toolbar, 500f).Dock.height, 1e-3f, "a height that fits is kept");
            Assert.AreEqual(ShellLayout.MinDock, ShellLayout.Of(Full, Toolbar, 10f).Dock.height, 1e-3f, "never less than a row or two of waves");

            ShellRegions tallest = ShellLayout.Of(Full, Toolbar, 5000f);
            Assert.AreEqual(ShellLayout.MinCircuit, tallest.Circuit.height, 1e-3f, "never so tall that the circuit is less than its least");

            ShellRegions shortest = ShellLayout.Of(new Vec2(1920f, 500f), Toolbar, 5000f);
            Assert.AreEqual(ShellLayout.MinDock, shortest.Dock.height, 1e-3f, "too short a window for both: the dock keeps its least");
            Assert.Less(shortest.Circuit.height, ShellLayout.MinCircuit, "and the circuit gives way");
        }

        // -----------------------------------------------------------------
        // The circuit's window made another size
        // -----------------------------------------------------------------

        [Test]
        public void AFittedView_FitsItsNewWindow_CentredInIt()
        {
            var view = new SchematicView(new Vec2(800f, 400f), new Vec2(16f, 10f), new Vec2(300f, 200f));
            Assert.AreEqual(new Vec2(250f, 100f), view.Offset, "the fit centres a small drawing in its window");

            view.Resize(new Vec2(1000f, 600f));
            Assert.IsTrue(view.IsFitted);
            Assert.AreEqual(new Vec2(350f, 200f), view.Offset, "centred in the new window");

            view.Resize(new Vec2(200f, 150f));
            Assert.IsTrue(view.IsFitted);
            Assert.Less(view.Zoom, 1f, "a window smaller than the drawing fits it smaller");
        }

        [Test]
        public void ABlankDrawing_StartsAtTheWindowsCorner_AsAPageDoes_AndOnceItHasPartsTheFitCentresIt()
        {
            var view = new SchematicView(new Vec2(800f, 400f), new Vec2(16f, 10f), new Vec2(60f, 60f), blank: true);
            Assert.IsTrue(view.IsFitted);
            Assert.AreEqual(new Vec2(16f, 10f), view.Offset, "at home, the top-left corner: what is built from it goes down and right in sight");

            view.Resize(new Vec2(1000f, 600f));
            Assert.AreEqual(new Vec2(16f, 10f), view.Offset, "a new window size keeps it there");

            view.Blank = false;
            Assert.AreEqual(new Vec2(16f, 10f), view.Offset, "the first part moves nothing");
            view.Fit();
            Assert.AreEqual(new Vec2(470f, 270f), view.Offset, "and the fit then centres the drawing");
        }

        [Test]
        public void AZoomedView_KeepsItsZoomAndItsCorner_WhenItsWindowIsResized()
        {
            var view = new SchematicView(new Vec2(800f, 400f), new Vec2(16f, 10f), new Vec2(760f, 380f));
            view.ZoomAbout(new Vec2(400f, 200f), 3);
            float zoom = view.Zoom;
            Vec2 corner = view.Offset;

            view.Resize(new Vec2(900f, 300f));
            Assert.IsFalse(view.IsFitted);
            Assert.AreEqual(zoom, view.Zoom, "the zoom is the student's");
            Assert.AreEqual(corner, view.Offset, "and what was at the window's corner is still there");
        }
    }
}
