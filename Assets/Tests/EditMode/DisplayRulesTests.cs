using System.IO;
using System.Text.RegularExpressions;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="DisplayRules"/>: where the fullscreen switch is offered, and the window it comes
    /// back to.
    /// </summary>
    public class DisplayRulesTests
    {
        [Test]
        public void OnAnOrdinaryDisplay_TheWindowIsTheOneTheGameOpensAt()
        {
            Assert.AreEqual(DisplayRules.PreferredWindow, DisplayRules.WindowedSize(1920, 1080));
        }

        [Test]
        public void OnALargeDisplay_TheWindowIsNoLarger()
        {
            // Leaving fullscreen by the mode alone kept the display's resolution: a window the size
            // of the whole screen. Bigger displays get the same window, not a bigger one.
            Assert.AreEqual(DisplayRules.PreferredWindow, DisplayRules.WindowedSize(3840, 2160));
        }

        [TestCase(1366, 768)]
        [TestCase(1280, 720)]
        [TestCase(1024, 768)]
        public void OnASmallDisplay_TheWindowFitsAndKeepsItsShape(int width, int height)
        {
            Vector2Int window = DisplayRules.WindowedSize(width, height);

            Assert.LessOrEqual(window.x, width * DisplayRules.MostOfTheScreen + 1f, "wider than the display allows");
            Assert.LessOrEqual(window.y, height * DisplayRules.MostOfTheScreen + 1f, "taller than the display allows");

            float shape = (float)DisplayRules.PreferredWindow.x / DisplayRules.PreferredWindow.y;
            Assert.AreEqual(shape, (float)window.x / window.y, 0.01f, "the window changed shape");
        }

        [Test]
        public void ADisplayOfNoSize_GivesTheOrdinaryWindow()
        {
            Assert.AreEqual(DisplayRules.PreferredWindow, DisplayRules.WindowedSize(0, 0));
        }

        /// <summary>
        /// The window the switch comes back to is the window the game opens at.
        /// </summary>
        /// <remarks>
        /// The player settings are an editor-only API, so <see cref="DisplayRules"/> states the size
        /// and this reads the project file to hold the two together. Changed in one place only, the
        /// game would open at one size and come out of fullscreen at another.
        /// </remarks>
        [Test]
        public void ThePreferredWindow_IsThePlayerSettingsDefault()
        {
            string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");
            string settings = File.ReadAllText(path);

            Match width = Regex.Match(settings, @"^\s*defaultScreenWidth:\s*(\d+)", RegexOptions.Multiline);
            Match height = Regex.Match(settings, @"^\s*defaultScreenHeight:\s*(\d+)", RegexOptions.Multiline);

            Assert.IsTrue(width.Success && height.Success, "the project settings no longer name a default size");
            Assert.AreEqual(int.Parse(width.Groups[1].Value), DisplayRules.PreferredWindow.x);
            Assert.AreEqual(int.Parse(height.Groups[1].Value), DisplayRules.PreferredWindow.y);
        }

        [Test]
        public void TheSwitchIsOffered_InTheEditorAndOnTheDesktop()
        {
            Assert.IsTrue(DisplayRules.Offered);
        }
    }
}
