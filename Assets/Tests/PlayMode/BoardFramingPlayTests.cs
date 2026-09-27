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
    }
}
