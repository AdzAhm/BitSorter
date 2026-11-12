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

        private static Rect2[] Tiles(ShellRegions r) => new[] { r.Inspector, r.Parts, r.Circuit, r.Side, r.Splitter, r.Dock, r.Status };

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

                Assert.AreEqual(Toolbar, r.Inspector.yMin, 1e-3f, "the inspector bar is right under the toolbar");
                Assert.AreEqual(window.x - 2f * ShellLayout.Margin, r.Inspector.width, 1e-3f, "the window's whole width");
                Assert.Greater(r.Circuit.yMin, r.Inspector.yMax, "and the panels under it");
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
        public void UntilItIsDragged_TheDockIsHalfTheRoom_AndNoTallerThanTheReferencesWaves()
        {
            Assert.AreEqual(ShellLayout.DefaultDock, ShellLayout.DefaultDockFor(new Vec2(1920f, 1400f), Toolbar), 1e-3f,
                "a tall window: the reference's nine rows and no more");

            float room = ShellLayout.Of(Browser, Toolbar, 0f).Status.yMin - ShellLayout.Of(Browser, Toolbar, 0f).Circuit.yMin
                - ShellLayout.SplitterHeight;
            Assert.AreEqual(0.5f * room, ShellLayout.DefaultDockFor(Browser, Toolbar), 1e-3f, "a browser tab: half the room");

            Assert.AreEqual(ShellLayout.MinDock, ShellLayout.DefaultDockFor(new Vec2(1920f, 500f), Toolbar), 1e-3f,
                "and never less than the dock's least");
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
        // The panels folded away (Part 16d)
        // -----------------------------------------------------------------

        private static readonly ShellFold[] EveryPanel = { ShellFold.Parts, ShellFold.Side, ShellFold.Dock };

        /// <summary>Every way the three can be folded, none folded first.</summary>
        private static System.Collections.Generic.IEnumerable<ShellFolds> EveryFold()
        {
            for (int bits = 0; bits < 8; bits++)
                yield return ShellFolds.FromBits(bits);
        }

        [Test]
        public void TheLayoutWithNoFolds_IsTheOpenOne_AsItWasBefore()
        {
            foreach (Vec2 window in new[] { Full, Browser, new Vec2(400f, 300f) })
                Assert.AreEqual(ShellLayout.Of(window, Toolbar, 300f), ShellLayout.Of(window, Toolbar, 300f, default));
        }

        [Test]
        public void FoldedOrNot_ThePanelsTileTheWindow_AndNoTwoOverlap()
        {
            foreach (Vec2 window in new[] { Full, Browser })
                foreach (ShellFolds folds in EveryFold())
                {
                    ShellRegions r = ShellLayout.Of(window, Toolbar, ShellLayout.DefaultDock, folds);
                    Rect2[] tiles = Tiles(r);
                    for (int i = 0; i < tiles.Length; i++)
                    {
                        Assert.GreaterOrEqual(tiles[i].xMin, ShellLayout.Margin - 1e-3f);
                        Assert.LessOrEqual(tiles[i].xMax, window.x - ShellLayout.Margin + 1e-3f, $"tile {i} runs off the right, folds {folds.Bits}");
                        Assert.LessOrEqual(tiles[i].yMax, window.y + 1e-3f, $"tile {i} runs off the bottom, folds {folds.Bits}");
                        for (int j = i + 1; j < tiles.Length; j++)
                            Assert.IsFalse(Overlap(tiles[i], tiles[j]), $"tiles {i} and {j} overlap, folds {folds.Bits}");
                    }
                    Assert.AreEqual(folds, r.Folds);
                }
        }

        [Test]
        public void AFoldedPanel_IsAStripAtTheWindowsEdge_AndTheCircuitTakesItsRoom()
        {
            ShellRegions open = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock);

            ShellRegions parts = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock, new ShellFolds(true, false, false));
            Assert.AreEqual(ShellLayout.Folded, parts.Parts.width, 1e-3f);
            Assert.AreEqual(ShellLayout.Margin, parts.Parts.xMin, 1e-3f, "at the window's left edge");
            Assert.AreEqual(open.Circuit.width + ShellLayout.PartsWidth - ShellLayout.Folded, parts.Circuit.width, 1e-3f, "the circuit wider by what folded");

            ShellRegions side = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock, new ShellFolds(false, true, false));
            Assert.AreEqual(ShellLayout.Folded, side.Side.width, 1e-3f);
            Assert.AreEqual(Full.x - ShellLayout.Margin, side.Side.xMax, 1e-3f, "at the window's right edge");
            Assert.AreEqual(open.Circuit.width + ShellLayout.SideWidth - ShellLayout.Folded, side.Circuit.width, 1e-3f);

            ShellRegions dock = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock, new ShellFolds(false, false, true));
            Assert.AreEqual(ShellLayout.Folded, dock.Dock.height, 1e-3f, "a strip over the status line");
            Assert.AreEqual(dock.Status.yMin, dock.Dock.yMax, 1e-3f);
            Assert.AreEqual(open.Circuit.height + ShellLayout.DefaultDock - ShellLayout.Folded, dock.Circuit.height, 1e-3f, "the circuit taller by what folded");
            Assert.AreEqual(0f, dock.DockHeader.height, 1e-3f, "nothing of the dock's inside shows");
            Assert.AreEqual(0f, dock.Waves.height, 1e-3f);
            Assert.AreEqual(0f, dock.Legend.height, 1e-3f);

            ShellRegions all = ShellLayout.Of(Full, Toolbar, 5000f, new ShellFolds(true, true, true));
            Assert.AreEqual(ShellLayout.Folded, all.Dock.height, 1e-3f, "folded, the dock's dragged height is beside the point");
            Assert.AreEqual(parts.Circuit.width + ShellLayout.SideWidth - ShellLayout.Folded, all.Circuit.width, 1e-3f);
        }

        [Test]
        public void EachArrow_IsOnTheEdgeFacingTheCircuit_OrInItsStripWhenFolded_AndClearOfTheGrip()
        {
            foreach (ShellFolds folds in EveryFold())
            {
                ShellRegions r = ShellLayout.Of(Full, Toolbar, ShellLayout.DefaultDock, folds);
                foreach (ShellFold panel in EveryPanel)
                {
                    Rect2 arrow = r.FoldOf(panel);
                    Assert.Greater(arrow.width * arrow.height, 0f, $"{panel}'s arrow has a size");
                    Assert.IsFalse(Overlap(arrow, r.Inspector), $"{panel}'s arrow is under the inspector");
                }

                Vec2 parts = r.PartsFold.center, side = r.SideFold.center, dock = r.DockFold.center;
                if (folds.Parts)
                    Assert.AreEqual(r.Parts.center.x, parts.x, 1e-3f, "folded, PARTS' arrow is in its strip");
                else
                    Assert.IsTrue(parts.x > r.Parts.xMax && parts.x < r.Circuit.xMin, "PARTS' arrow in the gap facing the circuit");
                if (folds.Side)
                    Assert.AreEqual(r.Side.center.x, side.x, 1e-3f, "folded, the side panel's arrow is in its strip");
                else
                    Assert.IsTrue(side.x < r.Side.xMin && side.x > r.Circuit.xMax, "the side panel's arrow in the gap facing the circuit");
                Assert.AreEqual(r.Circuit.center.y, parts.y, 1e-3f, "half way down its edge");
                Assert.AreEqual(r.Circuit.center.y, side.y, 1e-3f);

                if (folds.Dock)
                    Assert.AreEqual(r.Dock.center.y, dock.y, 1e-3f, "folded, the dock's arrow is in its strip");
                else
                    Assert.AreEqual(r.Splitter.center.y, dock.y, 1e-3f, "open, on the splitter");
                Assert.GreaterOrEqual(dock.x - r.DockFold.width / 2f, r.Splitter.center.x + 28f, "clear of the grip in the splitter's middle");
            }
        }

        [Test]
        public void H_FoldsEveryPanelWhileAnyIsOpen_AndOpensThemAllOnceAllAreFolded()
        {
            foreach (ShellFolds folds in EveryFold())
            {
                ShellFolds after = folds.AllToggled();
                if (folds.All)
                    Assert.AreEqual(default(ShellFolds), after, "all folded: H opens them");
                else
                    Assert.IsTrue(after.All, $"folds {folds.Bits}: H folds every one");
            }

            ShellFolds one = default(ShellFolds).Toggled(ShellFold.Side);
            Assert.IsTrue(one.Side && !one.Parts && !one.Dock, "an arrow folds its own panel alone");
            Assert.AreEqual(default(ShellFolds), one.Toggled(ShellFold.Side), "and opens it again");
        }

        [Test]
        public void TheFolds_AreKeptAsOneNumber_AndNoneIsWhatASettingNeverMadeReads()
        {
            foreach (ShellFolds folds in EveryFold())
                Assert.AreEqual(folds, ShellFolds.FromBits(folds.Bits));
            Assert.AreEqual(default(ShellFolds), ShellFolds.FromBits(-1), "a machine that never folded one");
            Assert.AreEqual(7, new ShellFolds(true, true, true).Bits);
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

        /// <summary>
        /// QUIT (5.1) stands at the status line's right end, inside its row and in its middle, and the status text keeps clear
        /// of it on both sides, so the text is still in the window's middle; without QUIT, a browser's, the text has the row.
        /// </summary>
        [Test]
        public void Quit_StandsAtTheStatusLinesRightEnd_AndTheTextKeepsClear_StillCentred()
        {
            foreach (Vec2 window in new[] { Full, Browser, new Vec2(1681f, 1261f) })
            {
                Rect2 status = ShellLayout.Of(window, Toolbar, ShellLayout.DefaultDock).Status;
                Rect2 quit = ShellLayout.QuitIn(status), text = ShellLayout.StatusTextIn(status, quit: true);

                Assert.AreEqual(status.xMax, quit.xMax, 1e-3f, "at the row's right end, the window's margin from its edge");
                Assert.AreEqual(ShellLayout.QuitWidth, quit.width, 1e-3f);
                Assert.AreEqual(status.center.y, quit.center.y, 1e-3f, "in the row's middle");
                Assert.IsTrue(quit.yMin >= status.yMin && quit.yMax <= status.yMax, "inside the row");

                Assert.LessOrEqual(text.xMax, quit.xMin - ShellLayout.Gap + 1e-3f, "the text keeps a gap from QUIT");
                Assert.AreEqual(status.center.x, text.center.x, 1e-3f, "and stays in the middle");
                Assert.AreEqual(status, ShellLayout.StatusTextIn(status, quit: false), "with no QUIT the text has the row");
            }
        }

        /// <summary>In a window too narrow for both, QUIT keeps to the row and the text gives up its room rather than go under 0.</summary>
        [Test]
        public void InATinyWindow_QuitKeepsToTheRow_AndTheTextHasNoLessThanNothing()
        {
            var status = new Rect2(16f, 500f, 100f, ShellLayout.StatusHeight);
            Rect2 quit = ShellLayout.QuitIn(status), text = ShellLayout.StatusTextIn(status, quit: true);
            Assert.GreaterOrEqual(quit.xMin, status.xMin - 1e-3f);
            Assert.LessOrEqual(quit.xMax, status.xMax + 1e-3f);
            Assert.AreEqual(0f, text.width, 1e-3f);
        }
    }
}
