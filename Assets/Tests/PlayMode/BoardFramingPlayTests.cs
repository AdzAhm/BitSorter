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
