using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The board is framed in the part of the screen the interface leaves free.
    /// </summary>
    /// <remarks>
    /// The camera used to fit the board to the whole screen, and the parts list sits over the
    /// screen's left edge -- which is where the board's leftmost column is. Four shipped levels
    /// put a source in that column, and in Carry the one the parts list covered source B's body
    /// and the delay line printed over its label.
    /// </remarks>
    [TestFixture]
    public class BoardFramingPlayTests
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        // -----------------------------------------------------------------
        // A level's own board
        // -----------------------------------------------------------------

        /// <summary>A level on a 13 by 7 board, with a fixture in each of its outermost corners.</summary>
        private static LevelDefinition AWideLevel()
        {
            LevelLoadResult result = LevelLoader.Parse(@"{
                ""name"": ""Wide"", ""tickLimit"": 40,
                ""board"": { ""columns"": 13, ""rows"": 7 },
                ""fixtures"": [
                    { ""id"": ""a"",   ""kind"": ""Source"", ""cell"": { ""x"": -6, ""y"":  3 }, ""stream"": ""01"" },
                    { ""id"": ""b"",   ""kind"": ""Source"", ""cell"": { ""x"": -6, ""y"": -3 }, ""stream"": ""01"" },
                    { ""id"": ""out"", ""kind"": ""Sink"",   ""cell"": { ""x"":  6, ""y"": -3 } }
                ],
                ""budget"": [ { ""kind"": ""And"", ""count"": 1 } ],
                ""expected"": [ { ""sink"": ""out"", ""values"": ""01"" } ]
            }", new Vector2Int(4, 2));

            Assert.IsTrue(result.IsValid, result.Error);
            return result.Level;
        }

        private static int Dots(PlacementGrid grid)
        {
            Transform container = grid.transform.Find("Grid dots");
            return container != null ? container.childCount : 0;
        }

        /// <summary>
        /// A level that names its board gets that board, and the next level that does not gets the
        /// standard one back.
        /// </summary>
        [UnityTest]
        public IEnumerator ALevelsOwnBoard_ResizesTheGrid_AndTheNextLevelPutsItBack()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSession session = Find<LevelSession>();
            PlacementGrid grid = Find<PlacementGrid>();
            bool marked = Look.Current.Grid != GridStyle.None;

            Assert.IsTrue(session.Adopt(AWideLevel(), "wide-board-test"), "the wide level was not adopted");
            yield return null;

            Assert.AreEqual(new Vector2Int(6, 3), grid.HalfExtents);
            Assert.IsTrue(grid.Contains(new Vector2Int(6, -3)), "the corner cell is off the board");

            if (marked)
                Assert.AreEqual(13 * 7, Dots(grid), "a dot on every cell of the wide board");

            Assert.IsTrue(session.LoadLevel("half-adder"), "the half adder did not load");
            yield return null;

            Assert.AreEqual(new Vector2Int(4, 2), grid.HalfExtents);
            Assert.IsFalse(grid.Contains(new Vector2Int(6, -3)));

            if (marked)
                Assert.AreEqual(9 * 5, Dots(grid), "the standard board's dots, and none of the wide one's");
        }

        /// <summary>
        /// The whole wide board -- corner fixtures, and the name under the bottom row -- is on
        /// screen and clear of the parts list.
        /// </summary>
        [UnityTest]
        public IEnumerator TheWholeWideBoard_IsFramedOnScreen()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().Adopt(AWideLevel(), "wide-board-test"), "the wide level was not adopted");

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            PlacementGrid grid = Find<PlacementGrid>();
            Camera view = Camera.main;

            var corners = new Vector3[4];
            GameObject.Find("Palette").GetComponent<RectTransform>().GetWorldCorners(corners);
            float paletteRight = corners[2].x;

            float half = grid.CellSize * 0.5f;
            Vector3 topLeft = view.WorldToScreenPoint(new Vector3(-6 * grid.CellSize - half, 3 * grid.CellSize, 0f));
            Vector3 bottomRight = view.WorldToScreenPoint(
                new Vector3(6 * grid.CellSize + half, -3 * grid.CellSize - NodeRenderer.LabelReach, 0f));

            Assert.GreaterOrEqual(topLeft.x, paletteRight, "the leftmost column runs under the parts list");
            Assert.LessOrEqual(bottomRight.x, Screen.width, "the rightmost column runs off the screen");
            Assert.GreaterOrEqual(bottomRight.y, 0f, "the bottom row's names run off the screen");
            Assert.LessOrEqual(topLeft.y, Screen.height, "the top row runs off the screen");
        }

        /// <summary>
        /// The top row of a seven-row board is clear of the banner, so a part placed on it is not
        /// drawn under the level's title.
        /// </summary>
        /// <remarks>
        /// A seven-row board on a wide screen is fitted by its height, and the height was measured
        /// against the whole screen. In Four lanes the top row came out under the banner: the AND
        /// on it was cut off at the top, along with the delays on the wires beside it.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTopRow_OfASevenRowBoard_IsClearOfTheBanner()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel("four-lanes"), "the level did not load");

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            PlacementGrid grid = Find<PlacementGrid>();
            Assert.AreEqual(new Vector2Int(5, 3), grid.HalfExtents, "sanity: not on Four lanes' own board");

            GameObject banner = GameObject.Find("Status");
            Assert.IsNotNull(banner, "sanity: no banner on screen");

            var corners = new Vector3[4];
            banner.GetComponent<RectTransform>().GetWorldCorners(corners);
            float bannerBottom = corners[0].y;

            // The top of a part on the top row: the row's centre, and half a part above it.
            float rowTop = grid.HalfExtents.y * grid.CellSize + PortGeometry.NodeSize * 0.5f;
            float rowTopOnScreen = Camera.main.WorldToScreenPoint(new Vector3(0f, rowTop, 0f)).y;

            Assert.LessOrEqual(rowTopOnScreen, bannerBottom, "the board's top row runs under the banner");
        }

        [UnityTest]
        public IEnumerator TheLeftmostColumn_IsClearOfThePartsList()
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel("carry-the-one"), "the level did not load");

            // The framing follows the interface, so give both a few frames to settle.
            for (int frame = 0; frame < 4; frame++)
                yield return null;

            GameObject paletteObject = GameObject.Find("Palette");
            Assert.IsNotNull(paletteObject, "sanity: no parts list on screen");

            // A screen-space overlay canvas: world corners are screen pixels.
            var corners = new Vector3[4];
            paletteObject.GetComponent<RectTransform>().GetWorldCorners(corners);
            float paletteRight = corners[2].x;

            PlacementGrid grid = Find<PlacementGrid>();
            Camera view = Camera.main;

            // The left edge of the leftmost column: its centre, less half a cell.
            float columnLeft = (-grid.HalfExtents.x - 0.5f) * grid.CellSize;
            float columnLeftOnScreen = view.WorldToScreenPoint(new Vector3(columnLeft, 0f, 0f)).x;

            Assert.GreaterOrEqual(columnLeftOnScreen, paletteRight,
                "the parts list covers the board's leftmost column, where four levels keep a source");
        }

        /// <summary>
        /// Opening the help panel keeps the rightmost column -- the bins -- clear of it.
        /// </summary>
        /// <remarks>
        /// The help panel was the one thing down the right the board was not framed around, so
        /// opening it to read a level's truth table covered the bins the table describes. Carry the
        /// one, for the widest table: five fixtures' columns.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheRightmostColumn_IsClearOfTheOpenHelpPanel()
        {
            yield return TestScene.Load();

            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel("carry-the-one"), "the level did not load");

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            GameObject badge = GameObject.Find("Help badge");
            Assert.IsNotNull(badge, "sanity: no help badge on screen");
            badge.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            GameObject help = GameObject.Find("Help");
            Assert.IsNotNull(help, "sanity: the help panel did not open");

            var corners = new Vector3[4];
            help.GetComponent<RectTransform>().GetWorldCorners(corners);
            float helpLeft = corners[0].x;

            PlacementGrid grid = Find<PlacementGrid>();
            float columnRight = (grid.HalfExtents.x + 0.5f) * grid.CellSize;
            float columnRightOnScreen = Camera.main.WorldToScreenPoint(new Vector3(columnRight, 0f, 0f)).x;

            Assert.LessOrEqual(columnRightOnScreen, helpLeft,
                "the open help panel covers the board's rightmost column, where the bins are");
        }

        // -----------------------------------------------------------------
        // The top-right corner
        // -----------------------------------------------------------------

        /// <summary>
        /// On a 13 by 7 board with nothing docked on the right, the bin in the top-right cell is
        /// clear of the badges in the top-right corner and the keys under them.
        /// </summary>
        /// <remarks>
        /// A board framed to fill the width the interface leaves reaches into the corner, where
        /// nothing down the right was counted while no panel was open there. Five levels keep a bin
        /// in the top-right cell -- One of four, Highest wins, Which is bigger, Pass it on and Carry
        /// it further -- and Carry it further is 13 by 7. Found on a render of free play
        /// (2026-09-30): the timing badge's key was printed across the bin.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheTopRightBin_IsClearOfTheCornerBadges()
        {
            yield return TestScene.Load();
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel("carry-it-further"), "the level did not load");

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            Assert.AreEqual(new Vector2Int(6, 3), Find<PlacementGrid>().HalfExtents, "sanity: not on the 13 by 7 board");

            Rect bin = TopRightPart();
            AssertClear(bin, "Timing badge");
            AssertClear(bin, "Help badge");
        }

        /// <summary>
        /// On the standard 9 by 5 board the bin in the top-right cell is clear of the corner badges
        /// too.
        /// </summary>
        /// <remarks>
        /// Not only a big board's problem. At 16:9 the top-right part of a 9 by 5 board is drawn from
        /// about 110 pixels down at 1080, and the timing badge's key runs to 132 -- so on the four
        /// 9 by 5 levels with a bin in that cell, the key was printed across it. Pass it on stands
        /// for the four.
        /// </remarks>
        [UnityTest]
        public IEnumerator OnTheStandardBoard_TheTopRightBinIsClearOfTheCornerBadges()
        {
            yield return TestScene.Load();
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            Find<MainMenu>().Show(false);
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel("pass-it-on"), "the level did not load");

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            Assert.AreEqual(new Vector2Int(4, 2), Find<PlacementGrid>().HalfExtents, "sanity: not on the 9 by 5 board");
            Assert.IsNotNull(Find<LevelSession>().Level.FixtureAt(new Vector2Int(4, 2)),
                "sanity: Pass it on no longer keeps a bin in the top-right cell");

            Rect bin = TopRightPart();
            AssertClear(bin, "Timing badge");
            AssertClear(bin, "Help badge");
        }

        /// <summary>
        /// In free play with the setup panel folded to its tab, the bin in the top-right cell is
        /// clear of the tab, and of the badges.
        /// </summary>
        /// <remarks>
        /// Folding the panel hands its width back to the board, and the tab it leaves sits on the
        /// panels' row in the top-right corner -- which on the full-width 13 by 7 board is over
        /// OUT 1. Older than the badges: the tab covered the bin from the day free play's board
        /// became 13 by 7.
        /// </remarks>
        [UnityTest]
        public IEnumerator InFreePlay_WithTheSetupFolded_TheTopRightBinIsClearOfTheTab()
        {
            yield return TestScene.Load();
            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            Find<MainMenu>().Show(false);
            yield return null;

            Find<SandboxPanel>().Open();

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            GameObject fold = GameObject.Find("collapse");
            Assert.IsNotNull(fold, "sanity: the setup panel has no button to fold it");
            fold.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            for (int frame = 0; frame < 4; frame++)
                yield return null;

            Assert.AreEqual(new Vector2Int(6, 3), Find<PlacementGrid>().HalfExtents, "sanity: not on free play's board");
            Assert.IsNotNull(GameObject.Find("Setup tab"), "sanity: the folded panel left no tab");

            Rect bin = TopRightPart();
            AssertClear(bin, "Setup tab");
            AssertClear(bin, "Timing badge");
            AssertClear(bin, "Help badge");
        }

        /// <summary>The square the part in the board's top-right cell is drawn in, in screen pixels.</summary>
        private static Rect TopRightPart()
        {
            PlacementGrid grid = Find<PlacementGrid>();
            Camera view = Camera.main;

            float half = PortGeometry.NodeSize * 0.5f;
            float x = grid.HalfExtents.x * grid.CellSize;
            float y = grid.HalfExtents.y * grid.CellSize;

            Vector3 low = view.WorldToScreenPoint(new Vector3(x - half, y - half, 0f));
            Vector3 high = view.WorldToScreenPoint(new Vector3(x + half, y + half, 0f));
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }

        /// <summary>
        /// Fails when the named piece of the interface -- itself or anything under it, such as a
        /// badge's key -- overlaps <paramref name="part"/>.
        /// </summary>
        private static void AssertClear(Rect part, string name)
        {
            GameObject target = GameObject.Find(name);
            Assert.IsNotNull(target, $"sanity: there is no '{name}' on screen");

            var corners = new Vector3[4];

            foreach (RectTransform piece in target.GetComponentsInChildren<RectTransform>())
            {
                piece.GetWorldCorners(corners);
                Rect drawn = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);

                Assert.IsFalse(drawn.Overlaps(part),
                    $"'{piece.name}' of '{name}' is drawn over the part in the board's top-right cell " +
                    $"({drawn} over {part})");
            }
        }
    }
}
