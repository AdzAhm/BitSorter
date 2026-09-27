using System.Collections;
using NUnit.Framework;
using BitSorter.View;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The help panel's tabs: the truth table, and a Karnaugh map for each bin.
    /// </summary>
    /// <remarks>
    /// The maps' contents are <c>KarnaughMapTests</c>' business. These hold the panel to what the
    /// player sees: that a tab changes what is shown and nothing else -- above all not the panel's
    /// size, which the camera frames the board around.
    /// </remarks>
    [TestFixture]
    public class HelpMapPlayTests
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
        public void ClearTheSave()
        {
            SaveGuard.Clear();
        }

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>
        /// Boots the game, past the tutorial and the main menu, onto <paramref name="level"/>.
        /// </summary>
        /// <remarks>
        /// The tutorial is marked done first: a fresh save is exactly what it offers itself on, and
        /// it would adopt its own board the moment the menu closed.
        /// </remarks>
        private static IEnumerator OnTheBoard(string level)
        {
            yield return TestScene.Load();

            Find<ProgressTracker>().Store.MarkMilestone(TutorialLevel.Key);
            yield return null;

            Find<MainMenu>().Show(false);
            yield return null;
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel(level), $"{level} did not load");
            yield return null;
        }

        private static IEnumerator OpenTheHelp()
        {
            Press("Help badge");
            yield return null;

            Assert.IsNotNull(Panel(), "sanity: the badge should open the help panel");
        }

        private static void Press(string name)
        {
            GameObject found = GameObject.Find(name);
            Assert.IsNotNull(found, $"there is no '{name}' on screen");
            found.GetComponent<Button>().onClick.Invoke();
        }

        private static RectTransform Panel()
        {
            GameObject panel = GameObject.Find("Help");
            return panel != null ? (RectTransform)panel.transform : null;
        }

        private static string Shown() =>
            Panel().Find("table").GetComponent<TextMeshProUGUI>().text;

        /// <summary>Only the table has a column rule; no map does.</summary>
        private static bool ShowingTheTable() => Shown().Contains("|");

        [UnityTest]
        public IEnumerator TheMapTab_ShowsTheMap_AndTheTableTab_ShowsTheTable()
        {
            yield return OnTheBoard("four-corners");
            yield return OpenTheHelp();

            Assert.IsTrue(ShowingTheTable(), "the panel should open on the table");

            Press("Help map tab out");
            yield return null;

            StringAssert.Contains("00 01 11 10", Shown(), "the map tab should show OUT's map");
            Assert.IsFalse(ShowingTheTable());

            Press("Help table tab");
            yield return null;

            Assert.IsTrue(ShowingTheTable(), "the table tab should bring the table back");
        }

        [UnityTest]
        public IEnumerator EachBin_HasItsOwnMap()
        {
            yield return OnTheBoard("half-adder");
            yield return OpenTheHelp();

            Press("Help map tab sum");
            yield return null;
            string sum = Shown();

            Press("Help map tab carry");
            yield return null;
            string carry = Shown();

            Assert.AreNotEqual(sum, carry, "SUM and CARRY are different functions");
            Assert.IsFalse(sum.Contains("|") || carry.Contains("|"), "both should be maps");
        }

        /// <summary>
        /// The camera frames the board around the open panel, so a tab that resized it would move the
        /// board every time a player looked at a map.
        /// </summary>
        [UnityTest]
        public IEnumerator SwitchingTabs_NeverResizesThePanel()
        {
            yield return OnTheBoard("half-adder");
            yield return OpenTheHelp();

            Vector2 size = Panel().sizeDelta;

            foreach (string tab in new[] { "Help map tab sum", "Help map tab carry", "Help table tab" })
            {
                Press(tab);
                yield return null;

                Assert.AreEqual(size, Panel().sizeDelta, $"pressing '{tab}' resized the panel");
            }

            var canvas = (RectTransform)Panel().GetComponentInParent<Canvas>().transform;
            var panelCorners = new Vector3[4];
            var canvasCorners = new Vector3[4];
            Panel().GetWorldCorners(panelCorners);
            canvas.GetWorldCorners(canvasCorners);

            Assert.GreaterOrEqual(panelCorners[0].y, canvasCorners[0].y - 0.5f, "the panel runs off the bottom");
            Assert.GreaterOrEqual(panelCorners[0].x, canvasCorners[0].x - 0.5f, "the panel runs off the left");
        }

        [UnityTest]
        public IEnumerator TheChoiceOfTheMap_CarriesToTheNextLevelWithOne()
        {
            yield return OnTheBoard("four-corners");
            yield return OpenTheHelp();

            Press("Help map tab out");
            yield return null;

            Assert.IsTrue(Find<LevelSession>().LoadLevel("half-adder"), "half-adder did not load");
            yield return null;
            yield return OpenTheHelp();

            Assert.IsFalse(ShowingTheTable(), "the player chose maps; the next level should open on one");
            StringAssert.StartsWith("<mspace", Shown());
            Assert.AreEqual(KarnaughMap.Format(Find<LevelSession>().Level, "sum"),
                Shown().Substring(Shown().IndexOf('>') + 1).Replace("</mspace>", string.Empty),
                "the next level's map should be its first bin's");
        }

        /// <summary>
        /// One input has no map, so no tabs. Which levels have a map is <c>KarnaughMapTests</c>'
        /// question, sequential ones included; this is about the panel acting on the answer.
        /// </summary>
        [UnityTest]
        public IEnumerator ALevelWithNoMap_HasNoTabs()
        {
            yield return OnTheBoard("route-the-bit");
            yield return OpenTheHelp();

            Assert.IsNull(GameObject.Find("Help tabs"), "route-the-bit has one input and no map");
            Assert.IsTrue(ShowingTheTable(), "and shows its table as it always has");
        }
    }
}
