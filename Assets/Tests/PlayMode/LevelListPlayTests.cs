using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The level list on a window too short for it: it scrolls inside the room between its title and
    /// its help line, and it opens on the level the player is on.
    /// </summary>
    /// <remarks>
    /// It had no scroll view, and a list taller than its room grew out of both ends and printed over
    /// the LEVELS title -- with seventeen levels there were thirty pixels to spare at the reference
    /// resolution, and any window shorter than that shape had none. A short window is made here the
    /// way the game meets one: the canvas scaler matches width and height half and half, so asking it
    /// for a shorter reference leaves fewer canvas units between the title and the help line.
    /// </remarks>
    [TestFixture]
    public class LevelListPlayTests
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

        /// <summary>
        /// Too short for the run, the list scrolls instead of drawing over its title, shows a
        /// scrollbar, and opens with the current level in view.
        /// </summary>
        [UnityTest]
        public IEnumerator OnAShortWindow_TheListScrolls_AndOpensOnTheCurrentLevel()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            // The last level, so the row that has to be revealed is at the far end of the list.
            LevelSession session = Find<LevelSession>();
            string last = session.Catalogue[session.Catalogue.Count - 1].FileName;
            Assert.IsTrue(session.LoadLevel(last), "the level did not load");
            yield return null;

            Transform panel = LevelListRoot();
            panel.GetComponentInParent<CanvasScaler>().referenceResolution = new Vector2(1920f, 420f);
            yield return null;

            Find<LevelSelectPanel>().Open();
            yield return null;
            yield return null;

            ScrollRect scroll = panel.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(scroll, "the list has no scroll view");

            Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height,
                "sanity: the window is not short enough for the list to overflow");

            Assert.IsNotNull(scroll.viewport.GetComponent<RectMask2D>(),
                "rows outside the room are drawn rather than clipped");

            Assert.IsFalse(Overlaps(panel.Find("title") as RectTransform, scroll.viewport),
                "the room the rows scroll in reaches up over the LEVELS title");
            Assert.IsFalse(Overlaps(panel.Find("help") as RectTransform, scroll.viewport),
                "the room the rows scroll in reaches down over the help line");

            Assert.IsTrue(scroll.verticalScrollbar.gameObject.activeInHierarchy,
                "a list that scrolls shows no scrollbar, so nothing says there is more");

            var current = scroll.content.Find($"Level {last}") as RectTransform;
            Assert.IsNotNull(current, "sanity: the last level has a row");
            Assert.IsTrue(Inside(current, scroll.viewport),
                "the list opened with the current level scrolled out of view");

            // And it scrolls back to the top, where the tutorial row is.
            scroll.verticalNormalizedPosition = 1f;
            yield return null;

            Assert.IsTrue(Inside(scroll.content.Find("Tutorial") as RectTransform, scroll.viewport),
                "scrolled to the top, the first row is still out of view");
        }

        /// <summary>
        /// At full height the whole run fits, and nothing about scrolling shows.
        /// </summary>
        /// <remarks>
        /// The pair to the test above: a scrollbar that never hid would satisfy that one.
        /// </remarks>
        [UnityTest]
        public IEnumerator AtFullHeight_TheListShowsNoScrollbar()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            Transform panel = LevelListRoot();
            panel.GetComponentInParent<CanvasScaler>().referenceResolution = UiTheme.ReferenceResolution;

            Find<LevelSelectPanel>().Open();
            yield return null;
            yield return null;

            ScrollRect scroll = panel.GetComponentInChildren<ScrollRect>(true);

            Assert.LessOrEqual(scroll.content.rect.height, scroll.viewport.rect.height,
                "sanity: at full height the run fits");
            Assert.IsFalse(scroll.verticalScrollbar.gameObject.activeInHierarchy,
                "a scrollbar shows beside a list with nothing to scroll");
        }

        /// <summary>
        /// The list has a CLOSE button, clear of its title, and it closes the list.
        /// </summary>
        /// <remarks>
        /// Escape was the only way out, stated in dim caption-sized text at the foot of the screen,
        /// so a list opened with the mouse from the main menu had no way back the mouse could reach.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheList_HasACloseButton_ThatClosesIt()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSelectPanel levels = Find<LevelSelectPanel>();
            levels.Open();
            yield return null;

            Transform panel = LevelListRoot();
            var close = panel.Find("Close") as RectTransform;
            Assert.IsNotNull(close, "the level list has no close button");
            Assert.IsTrue(close.gameObject.activeInHierarchy, "the close button is not drawn");
            Assert.IsFalse(Overlaps(panel.Find("title") as RectTransform, close),
                "the close button sits over the LEVELS title");

            close.GetComponent<Button>().onClick.Invoke();
            yield return null;

            Assert.IsFalse(levels.IsShowing, "the close button did not close the level list");
            Assert.IsFalse(UiModal.AnyOpen, "something is still covering the board after closing the list");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>The level list's full-screen root, found whether or not it is showing.</summary>
        private static Transform LevelListRoot()
        {
            foreach (RectTransform rect in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (rect.name == "Level select" && rect.gameObject.scene.IsValid())
                    return rect;
            }

            Assert.Fail("sanity: the level list was not built");
            return null;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool Overlaps(RectTransform a, RectTransform b)
        {
            Assert.IsNotNull(a, "sanity: the element being checked exists");
            return WorldRect(a).Overlaps(WorldRect(b));
        }

        /// <summary>Whether one rect lies wholly inside another, to within half a unit.</summary>
        private static bool Inside(RectTransform inner, RectTransform outer)
        {
            Assert.IsNotNull(inner, "sanity: the row being checked exists");

            Rect a = WorldRect(inner);
            Rect b = WorldRect(outer);
            const float slack = 0.5f;

            return a.yMin >= b.yMin - slack && a.yMax <= b.yMax + slack;
        }
    }
}
